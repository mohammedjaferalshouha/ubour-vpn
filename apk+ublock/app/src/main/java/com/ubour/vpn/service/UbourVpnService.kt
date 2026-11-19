package com.ubour.vpn.service

import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import android.content.pm.ServiceInfo
import android.net.ConnectivityManager
import android.net.Network
import android.net.NetworkCapabilities
import android.net.NetworkRequest
import android.net.VpnService
import android.os.Build
import android.os.ParcelFileDescriptor
import android.util.Log
import androidx.core.app.NotificationCompat
import com.ubour.vpn.R
import com.ubour.vpn.UbourApplication
import com.ubour.vpn.adblock.AdBlockEngine
import com.ubour.vpn.adblock.DnsFilterServer
import com.ubour.vpn.core.AppOperationMode
import com.ubour.vpn.core.SingboxManager
import com.ubour.vpn.core.VpnState
import com.ubour.vpn.core.VpnStateManager
import com.ubour.vpn.ui.MainActivity
import com.ubour.vpn.warp.PhysicalNetworkCandidate
import com.ubour.vpn.warp.WarpManager
import com.ubour.vpn.warp.WarpNetworkSelector
import com.ubour.vpn.warp.WarpNetworkType
import com.ubour.vpn.warp.WarpSelectionMode
import io.github.dovecoteescapee.byedpi.core.ByeDpiProxy
import io.github.dovecoteescapee.byedpi.core.TProxyService
import kotlinx.coroutines.*
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import okhttp3.ConnectionPool
import okhttp3.Dns
import okhttp3.OkHttpClient
import okhttp3.Request
import java.io.File
import java.net.InetAddress
import java.net.InetSocketAddress
import java.net.Proxy
import java.util.concurrent.ConcurrentHashMap
import java.util.concurrent.TimeUnit

class UbourVpnService : VpnService() {

    private val byeDpiProxy = ByeDpiProxy()
    private var dnsFilterServer: DnsFilterServer? = null
    private var proxyJob: Job? = null
    private var statsJob: Job? = null
    private var vpnInterface: ParcelFileDescriptor? = null
    private val mutex = Mutex()
    private val serviceScope = CoroutineScope(Dispatchers.IO + SupervisorJob())
    private var startTime: Long = 0
    @Volatile
    private var userStopRequested: Boolean = false
    private var currentWarpHost: String? = null
    private var currentWarpPort: Int = 0
    private var currentWarpAdBlock: Boolean = true
    private var networkCallback: ConnectivityManager.NetworkCallback? = null
    private val physicalNetworks = ConcurrentHashMap<Long, NetworkCapabilities>()
    @Volatile
    private var activePhysicalNetwork: PhysicalNetworkCandidate? = null
    @Volatile
    private var pendingNetworkTransition: PhysicalNetworkCandidate? = null
    @Volatile
    private var lastNetworkEventTimeMs: Long = 0L
    @Volatile
    private var sawPhysicalNetworkLoss: Boolean = false
    private val warpProbeBaseClient by lazy {
        OkHttpClient.Builder()
            .connectionPool(ConnectionPool(0, 1, TimeUnit.MILLISECONDS))
            .build()
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        val action = intent?.action
        if (action == ACTION_STOP) {
            userStopRequested = true
            SingboxManager.stopProbe()
            serviceScope.launch { stopVpn() }
            return START_NOT_STICKY
        }

        if (VpnStateManager.state.value == VpnState.CONNECTED) {
            return START_STICKY
        }

        userStopRequested = false

        val mode = intent?.getIntExtra(EXTRA_MODE, 1) ?: 1
        val dnsServer = intent?.getStringExtra(EXTRA_DNS) ?: DEFAULT_DNS
        val opModeOrdinal = intent?.getIntExtra(EXTRA_OP_MODE, AppOperationMode.WARP_AND_ADBLOCK.ordinal) ?: AppOperationMode.WARP_AND_ADBLOCK.ordinal
        val opMode = AppOperationMode.values().getOrElse(opModeOrdinal) { AppOperationMode.WARP_AND_ADBLOCK }
        val isAdBlockEnabled = intent?.getBooleanExtra(EXTRA_ADBLOCK_ENABLED, true) ?: true

        serviceScope.launch {
            startVpn(mode, dnsServer, opMode, isAdBlockEnabled)
        }

        return START_STICKY
    }

    private suspend fun startVpn(
        bypassMode: Int,
        dnsServer: String,
        opMode: AppOperationMode,
        isAdBlockEnabled: Boolean
    ) {
        mutex.withLock {
            try {
                Log.i(TAG, "Starting VPN service (OpMode=$opMode, BypassMode=$bypassMode, DNS=$dnsServer, AdBlock=$isAdBlockEnabled)")
                VpnStateManager.updateState(VpnState.CONNECTING)
                VpnStateManager.setOperationMode(opMode)
                startForegroundNotification(getString(R.string.status_connecting))

                // Determine effective DNS:
                // In WARP_AND_ADBLOCK mode, 1.1.1.1 is used for WARP.
                // In other modes, use the configured DNS server or fallback to standard DEFAULT_DNS (8.8.8.8)
                val effectiveDns = if (opMode == AppOperationMode.WARP_AND_ADBLOCK) {
                    "1.1.1.1"
                } else {
                    if (dnsServer.isNotBlank()) dnsServer else DEFAULT_DNS
                }

                // 1. Initialize AdBlock Engine & DNS Filter Server
                val isShieldActive = (opMode == AppOperationMode.WARP_AND_ADBLOCK || 
                                     opMode == AppOperationMode.VPN_AND_ADBLOCK || 
                                     opMode == AppOperationMode.ADBLOCK_ONLY) && isAdBlockEnabled

                AdBlockEngine.isAdBlockActive = isShieldActive

                if (isShieldActive) {
                    AdBlockEngine.initialize(applicationContext)
                }
                val isWarpMode = opMode == AppOperationMode.WARP_AND_ADBLOCK
                dnsFilterServer = DnsFilterServer(
                    upstreamDns = if (isWarpMode) "127.0.0.1" else effectiveDns,
                    upstreamPort = if (isWarpMode) 5354 else 53,
                    socketProtector = if (isWarpMode) null else { s -> protect(s) },
                    enableFiltering = isShieldActive,
                    tcpSocketProtector = if (isWarpMode) null else { s -> protect(s) }
                ).apply {
                    start()
                }
                Log.i(TAG, "DnsFilterServer started with upstream: ${if (isWarpMode) "127.0.0.1:5354" else effectiveDns} (ShieldActive=$isShieldActive)")

                // 2. Start proxy core based on operation mode
                var socksPort = 1080
                var useSingbox = false

                when (opMode) {
                    AppOperationMode.WARP_AND_ADBLOCK -> {
                        if (!hasActivePhysicalNetwork()) {
                            throw IllegalStateException("No active internet network for WARP")
                        }
                        try {
                            val initialNetType = detectCurrentPhysicalNetwork()?.type ?: WarpNetworkType.OTHER
                            val selectionMode = WarpManager.getSelectionMode(applicationContext)
                            val manualEp = WarpManager.getManualEndpoint(applicationContext)
                            val lastEp = WarpManager.getLastWorkingEndpointForNetwork(applicationContext, initialNetType)
                                ?: WarpManager.getLastWorkingEndpoint(applicationContext)
                            val candidates = WarpManager.resolveCandidatesForMode(
                                mode = selectionMode,
                                manualEndpoint = manualEp,
                                lastWorkingEndpoint = lastEp,
                                isRecovery = false
                            )
                            val baseConfig = WarpManager.getOrRegisterConfig(context = applicationContext)
                            val chosen = WarpManager.findBestWorkingEndpoint(
                                context = applicationContext,
                                baseConfig = baseConfig,
                                candidates = candidates,
                                preferQuickFirst = false,
                                shouldCancel = { userStopRequested }
                            )
                            if (chosen != null && chosen.working && !userStopRequested) {
                                val candidateConfig = baseConfig.copy(
                                    endpointHost = chosen.host,
                                    endpointPort = chosen.port
                                )
                                if (SingboxManager.startWarp(applicationContext, candidateConfig, enableAdBlock = isAdBlockEnabled)) {
                                    delay(150)
                                    if (SingboxManager.isRunning() &&
                                        verifyWarpTunnelConnectivity(
                                            socksPort = SingboxManager.SOCKS_PORT,
                                            timeoutMs = 3500L,
                                            attempts = 2
                                        )
                                    ) {
                                        if (selectionMode == WarpSelectionMode.AUTO) {
                                            WarpManager.saveLastWorkingEndpointForNetwork(
                                                applicationContext,
                                                initialNetType,
                                                chosen.host,
                                                chosen.port
                                            )
                                        }
                                        currentWarpHost = chosen.host
                                        currentWarpPort = chosen.port
                                        currentWarpAdBlock = isAdBlockEnabled
                                        VpnStateManager.setActiveWarpEndpoint("${chosen.host}:${chosen.port}")
                                        socksPort = SingboxManager.SOCKS_PORT
                                        useSingbox = true
                                        Log.i(TAG, "Sing-box WARP verified on ${chosen.host}:${chosen.port} (net=${initialNetType.prefSuffix}, mode=$selectionMode, latency=${chosen.latencyMs}ms, socks=$socksPort, AdBlock=$isAdBlockEnabled)")
                                    } else {
                                        SingboxManager.stop()
                                    }
                                }
                            }
                        } catch (e: CancellationException) {
                            throw e
                        } catch (e: Exception) {
                            Log.e(TAG, "Failed to start Sing-box WARP: ${e.message}", e)
                        }

                        if (!useSingbox) {
                            throw IllegalStateException("Sing-box WARP failed to start")
                        }
                    }

                    AppOperationMode.VPN_AND_ADBLOCK -> {
                        // Direct ByeDPI DPI bypass on port 1080 + Sing-box dispatcher on port 10809
                        proxyJob = serviceScope.launch(Dispatchers.IO) {
                            byeDpiProxy.startProxy(mode = bypassMode, ip = "127.0.0.1", port = 1080)
                        }
                        delay(300)

                        val started = if (isAdBlockEnabled) {
                            SingboxManager.startVpnAndAdBlock(applicationContext, byedpiPort = 1080)
                        } else {
                            SingboxManager.startVpnOnly(applicationContext, byedpiPort = 1080)
                        }
                        if (started) {
                            delay(300)
                            if (SingboxManager.isRunning()) {
                                socksPort = SingboxManager.SOCKS_PORT
                                useSingbox = true
                                Log.i(TAG, "Sing-box VPN+AdBlock dispatcher running on port $socksPort (AdBlock=$isAdBlockEnabled)")
                            }
                        }
                        if (!useSingbox) {
                            throw IllegalStateException("Sing-box VPN+AdBlock failed to start")
                        }
                    }

                    AppOperationMode.ADBLOCK_ONLY -> {
                        // AdBlock Only (Sing-box Direct routing + DNS filter on port 10809)
                        if (SingboxManager.startAdBlockOnly(applicationContext)) {
                            delay(300)
                            if (SingboxManager.isRunning()) {
                                socksPort = SingboxManager.SOCKS_PORT
                                useSingbox = true
                                Log.i(TAG, "Sing-box AdBlock Only dispatcher running on port $socksPort")
                            }
                        }
                        if (!useSingbox) {
                            throw IllegalStateException("Sing-box AdBlock Only failed to start")
                        }
                    }

                    AppOperationMode.VPN_ONLY -> {
                        // Direct ByeDPI DPI bypass with Singbox dispatcher for reliable UDP & DNS
                        proxyJob = serviceScope.launch(Dispatchers.IO) {
                            byeDpiProxy.startProxy(mode = bypassMode, ip = "127.0.0.1", port = 1080)
                        }
                        delay(300)
                        if (SingboxManager.startVpnOnly(applicationContext, byedpiPort = 1080)) {
                            delay(300)
                            if (SingboxManager.isRunning()) {
                                socksPort = SingboxManager.SOCKS_PORT
                                useSingbox = true
                                Log.i(TAG, "Sing-box VPN Only dispatcher running on port $socksPort")
                            }
                        }
                        if (!useSingbox) {
                            throw IllegalStateException("Sing-box VPN Only failed to start")
                        }
                    }

                    AppOperationMode.CUSTOM_VLESS -> {
                        // Custom VLESS Reality server on port 10809
                        val prefs = getSharedPreferences("ubour_settings", Context.MODE_PRIVATE)
                        val vlessUrl = prefs.getString("custom_vless_url", "") ?: ""
                        if (vlessUrl.isNotBlank() && SingboxManager.startVless(applicationContext, vlessUrl, enableAdBlock = isAdBlockEnabled)) {
                            delay(400)
                            if (SingboxManager.isRunning()) {
                                socksPort = SingboxManager.SOCKS_PORT
                                useSingbox = true
                                Log.i(TAG, "Sing-box VLESS proxy running on port $socksPort (AdBlock=$isAdBlockEnabled)")
                            }
                        }
                        if (!useSingbox) {
                            Log.w(TAG, "Sing-box VLESS failed. Falling back to ByeDPI on port 1080")
                            socksPort = 1080
                            proxyJob = serviceScope.launch(Dispatchers.IO) {
                                byeDpiProxy.startProxy(mode = bypassMode, ip = "127.0.0.1", port = 1080)
                            }
                            delay(300)
                        }
                    }
                }

                // 3. Prepare YAML config for hev-socks5-tunnel
                val effectiveMtu = if (opMode == AppOperationMode.WARP_AND_ADBLOCK) 1280 else 1500
                val udpMode = if (socksPort == SingboxManager.SOCKS_PORT) "'udp'" else "'none'"
                val tun2socksConfig = """
                tunnel:
                  mtu: $effectiveMtu
                socks5:
                  port: $socksPort
                  address: 127.0.0.1
                  udp: $udpMode
                misc:
                  task-stack-size: 262144
                """.trimIndent()

                val configFile = File(cacheDir, "tun2socks.tmp").apply {
                    writeText(tun2socksConfig)
                }

                // 4. Build VPN Interface
                val builder = Builder().apply {
                    setSession(getString(R.string.app_name))
                    setMtu(effectiveMtu)
                    addAddress("10.10.10.10", 32)
                    addAddress("fd00:10:10::10", 128)
                    addRoute("0.0.0.0", 0)
                    addRoute("::", 0)

                    if (opMode != AppOperationMode.CUSTOM_VLESS) {
                        addDnsServer("10.10.10.11")
                        addDnsServer("fd00:10:10::11")
                    } else {
                        addDnsServer("10.10.10.10")
                        if (effectiveDns != "10.10.10.10") {
                            addDnsServer(effectiveDns)
                        }
                        val secondaryDns = SECONDARY_DNS_MAP[effectiveDns] ?: (if (effectiveDns != "8.8.4.4") "8.8.4.4" else "8.8.8.8")
                        if (secondaryDns != effectiveDns && secondaryDns != "10.10.10.10") {
                            addDnsServer(secondaryDns)
                        }
                    }

                    if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
                        setMetered(false)
                    }
                    addDisallowedApplication(applicationContext.packageName)

                    // Add user-selected excluded apps (Split Tunneling)
                    val prefs = this@UbourVpnService.getSharedPreferences("ubour_settings", Context.MODE_PRIVATE)
                    val excludedApps = prefs.getStringSet("excluded_apps", null) ?: emptySet<String>()
                    for (pkg in excludedApps) {
                        try {
                            addDisallowedApplication(pkg)
                            Log.i(TAG, "Excluded app from VPN tunnel: $pkg")
                        } catch (e: Exception) {
                            Log.w(TAG, "Failed to exclude app: $pkg", e)
                        }
                    }
                }

                val pfd = builder.establish() ?: throw IllegalStateException("Could not establish VPN interface")
                vpnInterface = pfd
                Log.i(TAG, "VPN Interface established, fd: ${pfd.fd}")

                // 5. Start TProxyService (hev-socks5-tunnel)
                TProxyService.TProxyStartService(configFile.absolutePath, pfd.fd)
                Log.i(TAG, "TProxyService started on port $socksPort")

                startTime = System.currentTimeMillis()
                VpnStateManager.updateState(VpnState.CONNECTED)
                
                val statusMsg = when (opMode) {
                    AppOperationMode.WARP_AND_ADBLOCK -> getString(R.string.status_connected_warp)
                    AppOperationMode.VPN_AND_ADBLOCK -> getString(R.string.status_connected_full)
                    AppOperationMode.ADBLOCK_ONLY -> getString(R.string.status_connected_adblock_only)
                    AppOperationMode.VPN_ONLY -> getString(R.string.status_connected)
                    AppOperationMode.CUSTOM_VLESS -> getString(R.string.status_connected_vless)
                }
                startForegroundNotification(statusMsg)

                if (opMode == AppOperationMode.WARP_AND_ADBLOCK) {
                    registerUnderlyingNetworkCallback()
                }

                startStatsMonitoring()

            } catch (e: CancellationException) {
                throw e
            } catch (e: Exception) {
                Log.e(TAG, "Failed to start VPN", e)
                stopVpnLocked()
                if (opMode == AppOperationMode.WARP_AND_ADBLOCK && !userStopRequested) {
                    val mode = WarpManager.getSelectionMode(applicationContext)
                    val manualEp = WarpManager.getManualEndpoint(applicationContext)
                    if (mode == WarpSelectionMode.MANUAL && manualEp != null) {
                        VpnStateManager.reportError(
                            getString(R.string.warp_manual_connection_failed, manualEp.first, manualEp.second)
                        )
                    } else {
                        VpnStateManager.reportError(getString(R.string.warp_connection_failed))
                    }
                }
            }
        }
    }

    private suspend fun stopVpn() {
        mutex.withLock {
            stopVpnLocked()
        }
    }

    private suspend fun stopVpnLocked() {
            Log.i(TAG, "Stopping VPN service...")
            VpnStateManager.updateState(VpnState.DISCONNECTING)
            AdBlockEngine.isAdBlockActive = false
            unregisterUnderlyingNetworkCallback()
            currentWarpHost = null
            currentWarpPort = 0
            statsJob?.cancel()
            statsJob = null

            try {
                dnsFilterServer?.stop()
                dnsFilterServer = null
            } catch (e: Exception) {
                Log.e(TAG, "Error stopping DNS filter server", e)
            }

            try {
                SingboxManager.stopProbe()
                SingboxManager.stop()
            } catch (e: Exception) {
                Log.e(TAG, "Error stopping Sing-box", e)
            }

            try {
                TProxyService.TProxyStopService()
                Log.i(TAG, "TProxyService stopped")
            } catch (e: Exception) {
                Log.e(TAG, "Error stopping TProxy", e)
            }

            try {
                File(cacheDir, "tun2socks.tmp").delete()
            } catch (e: Exception) {
                // Ignore
            }

            try {
                vpnInterface?.close()
                vpnInterface = null
                Log.i(TAG, "VPN interface closed")
            } catch (e: Exception) {
                Log.e(TAG, "Error closing VPN interface", e)
            }

            try {
                byeDpiProxy.stopProxy()
                proxyJob?.cancel()
                proxyJob = null
                Log.i(TAG, "ByeDpi proxy stopped")
            } catch (e: Exception) {
                Log.e(TAG, "Error stopping proxy", e)
            }

            VpnStateManager.updateState(VpnState.DISCONNECTED)
            stopForeground(STOP_FOREGROUND_REMOVE)
            stopSelf()
    }

    private fun hasActivePhysicalNetwork(): Boolean {
        val cm = getSystemService(Context.CONNECTIVITY_SERVICE) as? ConnectivityManager ?: return true
        return try {
            cm.allNetworks.any { net ->
                val caps = cm.getNetworkCapabilities(net)
                caps != null &&
                    !caps.hasTransport(NetworkCapabilities.TRANSPORT_VPN) &&
                    caps.hasCapability(NetworkCapabilities.NET_CAPABILITY_INTERNET)
            }
        } catch (_: Exception) {
            val activeNetwork = cm.activeNetwork ?: return false
            val caps = cm.getNetworkCapabilities(activeNetwork) ?: return false
            !caps.hasTransport(NetworkCapabilities.TRANSPORT_VPN) &&
                caps.hasCapability(NetworkCapabilities.NET_CAPABILITY_INTERNET)
        }
    }

    private fun classifyPhysicalNetwork(network: Network, caps: NetworkCapabilities): PhysicalNetworkCandidate? {
        if (caps.hasTransport(NetworkCapabilities.TRANSPORT_VPN)) return null
        if (!caps.hasCapability(NetworkCapabilities.NET_CAPABILITY_INTERNET)) return null
        val type = when {
            caps.hasTransport(NetworkCapabilities.TRANSPORT_ETHERNET) -> WarpNetworkType.ETHERNET
            caps.hasTransport(NetworkCapabilities.TRANSPORT_WIFI) -> WarpNetworkType.WIFI
            caps.hasTransport(NetworkCapabilities.TRANSPORT_CELLULAR) -> WarpNetworkType.CELLULAR
            else -> WarpNetworkType.OTHER
        }
        val isValidated = caps.hasCapability(NetworkCapabilities.NET_CAPABILITY_VALIDATED)
        return PhysicalNetworkCandidate(
            handle = network.networkHandle,
            type = type,
            isValidated = isValidated
        )
    }

    private fun detectCurrentPhysicalNetwork(): PhysicalNetworkCandidate? {
        val cm = getSystemService(Context.CONNECTIVITY_SERVICE) as? ConnectivityManager ?: return null
        return try {
            val candidates = cm.allNetworks.mapNotNull { net ->
                val caps = cm.getNetworkCapabilities(net) ?: return@mapNotNull null
                classifyPhysicalNetwork(net, caps)
            }
            WarpNetworkSelector.selectPrimary(candidates)
        } catch (_: Exception) {
            val activeNet = cm.activeNetwork ?: return null
            val caps = cm.getNetworkCapabilities(activeNet) ?: return null
            classifyPhysicalNetwork(activeNet, caps)
        }
    }

    private fun buildCurrentPhysicalCandidates(): List<PhysicalNetworkCandidate> {
        return physicalNetworks.entries.mapNotNull { (handle, caps) ->
            if (caps.hasTransport(NetworkCapabilities.TRANSPORT_VPN)) return@mapNotNull null
            if (!caps.hasCapability(NetworkCapabilities.NET_CAPABILITY_INTERNET)) return@mapNotNull null
            val type = when {
                caps.hasTransport(NetworkCapabilities.TRANSPORT_ETHERNET) -> WarpNetworkType.ETHERNET
                caps.hasTransport(NetworkCapabilities.TRANSPORT_WIFI) -> WarpNetworkType.WIFI
                caps.hasTransport(NetworkCapabilities.TRANSPORT_CELLULAR) -> WarpNetworkType.CELLULAR
                else -> WarpNetworkType.OTHER
            }
            val isValidated = caps.hasCapability(NetworkCapabilities.NET_CAPABILITY_VALIDATED)
            PhysicalNetworkCandidate(handle = handle, type = type, isValidated = isValidated)
        }
    }

    @Synchronized
    private fun evaluatePhysicalNetworkChange(trigger: String) {
        val candidates = buildCurrentPhysicalCandidates()
        val next = WarpNetworkSelector.selectPrimary(candidates)
        if (next == null) {
            sawPhysicalNetworkLoss = true
            return
        }
        val prev = activePhysicalNetwork
        if (prev == null) {
            activePhysicalNetwork = next
            sawPhysicalNetworkLoss = false
            return
        }
        val changed = (prev.handle != next.handle) || (prev.type != next.type) || sawPhysicalNetworkLoss
        activePhysicalNetwork = next
        if (changed) {
            sawPhysicalNetworkLoss = false
            pendingNetworkTransition = next
            lastNetworkEventTimeMs = System.currentTimeMillis()
            Log.i(
                TAG,
                "Physical network changed ($trigger): ${prev.type.prefSuffix}(${prev.handle}) -> ${next.type.prefSuffix}(${next.handle}); scheduling WARP transition check"
            )
        }
    }

    private fun registerUnderlyingNetworkCallback() {
        unregisterUnderlyingNetworkCallback()
        val cm = getSystemService(Context.CONNECTIVITY_SERVICE) as? ConnectivityManager ?: return
        try {
            cm.allNetworks.forEach { net ->
                val caps = cm.getNetworkCapabilities(net)
                if (caps != null && classifyPhysicalNetwork(net, caps) != null) {
                    physicalNetworks[net.networkHandle] = caps
                }
            }
        } catch (_: Exception) {
        }
        activePhysicalNetwork = WarpNetworkSelector.selectPrimary(buildCurrentPhysicalCandidates())
        pendingNetworkTransition = null
        sawPhysicalNetworkLoss = false

        val request = NetworkRequest.Builder()
            .addCapability(NetworkCapabilities.NET_CAPABILITY_INTERNET)
            .addCapability(NetworkCapabilities.NET_CAPABILITY_NOT_VPN)
            .build()

        val cb = object : ConnectivityManager.NetworkCallback() {
            override fun onAvailable(network: Network) {
                val caps = try {
                    cm.getNetworkCapabilities(network)
                } catch (_: Exception) {
                    null
                } ?: return
                if (classifyPhysicalNetwork(network, caps) != null) {
                    physicalNetworks[network.networkHandle] = caps
                    evaluatePhysicalNetworkChange("onAvailable")
                }
            }

            override fun onCapabilitiesChanged(network: Network, networkCapabilities: NetworkCapabilities) {
                if (classifyPhysicalNetwork(network, networkCapabilities) != null) {
                    physicalNetworks[network.networkHandle] = networkCapabilities
                } else {
                    physicalNetworks.remove(network.networkHandle)
                }
                evaluatePhysicalNetworkChange("onCapabilitiesChanged")
            }

            override fun onLost(network: Network) {
                physicalNetworks.remove(network.networkHandle)
                evaluatePhysicalNetworkChange("onLost")
            }
        }
        try {
            cm.registerNetworkCallback(request, cb)
            networkCallback = cb
            Log.i(
                TAG,
                "Registered physical network callback (initial=${activePhysicalNetwork?.type?.prefSuffix}:${activePhysicalNetwork?.handle})"
            )
        } catch (e: Exception) {
            Log.w(TAG, "Could not register network callback: ${e.message}")
        }
    }

    private fun unregisterUnderlyingNetworkCallback() {
        val cb = networkCallback
        networkCallback = null
        if (cb != null) {
            try {
                val cm = getSystemService(Context.CONNECTIVITY_SERVICE) as? ConnectivityManager
                cm?.unregisterNetworkCallback(cb)
            } catch (_: Exception) {
            }
        }
        physicalNetworks.clear()
        activePhysicalNetwork = null
        pendingNetworkTransition = null
        sawPhysicalNetworkLoss = false
    }

    private suspend fun handlePhysicalNetworkTransition(targetNet: PhysicalNetworkCandidate): Boolean {
        if (userStopRequested || VpnStateManager.state.value != VpnState.CONNECTED) {
            return false
        }

        for (step in 1..8) {
            if (userStopRequested || hasActivePhysicalNetwork()) break
            delay(500)
        }

        dnsFilterServer?.clearCache()

        val currentPair = currentWarpHost?.let { h ->
            if (currentWarpPort > 0) (h to currentWarpPort) else null
        }
        val rememberedForTarget = WarpManager.getLastWorkingEndpointForNetwork(applicationContext, targetNet.type)
        Log.i(
            TAG,
            "Handling physical network transition to ${targetNet.type.prefSuffix}(${targetNet.handle}): current=${currentPair?.first}:${currentPair?.second}, remembered=${rememberedForTarget?.first}:${rememberedForTarget?.second}"
        )

        return attemptMidSessionWarpRecovery(
            reason = "network_changed_${targetNet.type.prefSuffix}",
            targetNetworkType = targetNet.type,
            deprioritizeFailedEndpoint = true
        )
    }

    private suspend fun attemptMidSessionWarpRecovery(
        reason: String,
        targetNetworkType: WarpNetworkType? = null,
        deprioritizeFailedEndpoint: Boolean = true
    ): Boolean {
        if (userStopRequested || VpnStateManager.state.value != VpnState.CONNECTED) {
            return false
        }

        val effectiveNetType = targetNetworkType ?: detectCurrentPhysicalNetwork()?.type ?: WarpNetworkType.OTHER
        val selectionMode = WarpManager.getSelectionMode(applicationContext)
        if (selectionMode == WarpSelectionMode.MANUAL) {
            val lostHost = currentWarpHost ?: WarpManager.getManualEndpoint(applicationContext)?.first.orEmpty()
            val lostPort = if (currentWarpPort > 0) currentWarpPort else (WarpManager.getManualEndpoint(applicationContext)?.second ?: 0)
            if (reason.startsWith("network_changed") && lostHost.isNotBlank() && lostPort > 0) {
                val rebound = mutex.withLock {
                    if (userStopRequested || VpnStateManager.state.value != VpnState.CONNECTED) {
                        return@withLock false
                    }
                    try {
                        val baseConfig = WarpManager.getOrRegisterConfig(applicationContext)
                        val manualCfg = baseConfig.copy(endpointHost = lostHost, endpointPort = lostPort)
                        if (SingboxManager.startWarp(applicationContext, manualCfg, enableAdBlock = currentWarpAdBlock)) {
                            delay(150)
                            if (SingboxManager.isRunning() &&
                                verifyWarpTunnelConnectivity(
                                    socksPort = SingboxManager.SOCKS_PORT,
                                    timeoutMs = 3000L,
                                    attempts = 2
                                )
                            ) {
                                dnsFilterServer?.clearCache()
                                startForegroundNotification(getString(R.string.status_connected_warp))
                                Log.i(TAG, "Manual WARP endpoint ($lostHost:$lostPort) rebound after network change")
                                return@withLock true
                            }
                        }
                    } catch (e: CancellationException) {
                        throw e
                    } catch (_: Exception) {
                    }
                    false
                }
                if (rebound) return true
            }
            Log.w(TAG, "Manual WARP endpoint ($lostHost:$lostPort) lost ($reason); stopping per manual selection policy")
            stopVpn()
            if (!userStopRequested) {
                VpnStateManager.reportError(
                    getString(R.string.warp_manual_connection_lost, lostHost, lostPort)
                )
            }
            return false
        }

        return mutex.withLock {
            if (userStopRequested || VpnStateManager.state.value != VpnState.CONNECTED) {
                return@withLock false
            }

            val failedPair = currentWarpHost?.let { h ->
                if (currentWarpPort > 0) (h to currentWarpPort) else null
            }
            Log.w(TAG, "Starting mid-session WARP auto-recovery (reason=$reason, net=${effectiveNetType.prefSuffix}, failed=${failedPair?.first}:${failedPair?.second})")
            VpnStateManager.setRecoveringWarp(true)
            startForegroundNotification(getString(R.string.status_recovering_warp))

            for (waitStep in 1..10) {
                if (userStopRequested || hasActivePhysicalNetwork()) break
                delay(1000)
            }

            if (userStopRequested || !hasActivePhysicalNetwork()) {
                Log.e(TAG, "No physical network available during WARP recovery; stopping VPN")
                VpnStateManager.setRecoveringWarp(false)
                stopVpnLocked()
                if (!userStopRequested) {
                    VpnStateManager.reportError(getString(R.string.warp_connection_lost))
                }
                return@withLock false
            }

            try {
                val baseConfig = WarpManager.getOrRegisterConfig(applicationContext)
                val rememberedForTarget = WarpManager.getLastWorkingEndpointForNetwork(applicationContext, effectiveNetType)
                val deprioritize = if (deprioritizeFailedEndpoint && rememberedForTarget != failedPair) {
                    failedPair
                } else {
                    null
                }
                val preferredLast = rememberedForTarget
                    ?: if (targetNetworkType == null) WarpManager.getLastWorkingEndpoint(applicationContext) else null
                val candidates = WarpManager.resolveCandidatesForMode(
                    mode = WarpSelectionMode.AUTO,
                    manualEndpoint = null,
                    lastWorkingEndpoint = preferredLast,
                    deprioritizeEndpoint = deprioritize,
                    isRecovery = true
                )
                val replacement = WarpManager.findBestWorkingEndpoint(
                    context = applicationContext,
                    baseConfig = baseConfig,
                    candidates = candidates,
                    preferQuickFirst = (rememberedForTarget != null && rememberedForTarget != deprioritize),
                    shouldCancel = { userStopRequested }
                )
                if (replacement != null && replacement.working && !userStopRequested) {
                    val newConfig = baseConfig.copy(
                        endpointHost = replacement.host,
                        endpointPort = replacement.port
                    )
                    if (SingboxManager.startWarp(applicationContext, newConfig, enableAdBlock = currentWarpAdBlock)) {
                        delay(150)
                        if (SingboxManager.isRunning() &&
                            verifyWarpTunnelConnectivity(
                                socksPort = SingboxManager.SOCKS_PORT,
                                timeoutMs = 4000L,
                                attempts = 2
                            )
                        ) {
                            WarpManager.saveLastWorkingEndpointForNetwork(
                                applicationContext,
                                effectiveNetType,
                                replacement.host,
                                replacement.port
                            )
                            currentWarpHost = replacement.host
                            currentWarpPort = replacement.port
                            dnsFilterServer?.clearCache()
                            VpnStateManager.setActiveWarpEndpoint("${replacement.host}:${replacement.port}")
                            VpnStateManager.setRecoveringWarp(false)
                            startForegroundNotification(getString(R.string.status_connected_warp))
                            Log.i(TAG, "WARP tunnel recovered seamlessly on ${replacement.host}:${replacement.port} (net=${effectiveNetType.prefSuffix}, ${replacement.latencyMs}ms)")
                            return@withLock true
                        }
                    }
                }
            } catch (e: CancellationException) {
                throw e
            } catch (e: Exception) {
                Log.e(TAG, "Error during WARP auto-recovery: ${e.message}", e)
            }

            Log.e(TAG, "Mid-session WARP recovery failed on all candidates; stopping VPN without fallback")
            VpnStateManager.setRecoveringWarp(false)
            stopVpnLocked()
            if (!userStopRequested) {
                VpnStateManager.reportError(getString(R.string.warp_connection_lost))
            }
            false
        }
    }

    /**
     * Verifies end-to-end tunnel connectivity through the local Sing-box WARP SOCKS5 proxy
     * without falling back to any other engine.
     */
    private suspend fun verifyWarpTunnelConnectivity(
        socksPort: Int,
        timeoutMs: Long = 3500L,
        attempts: Int = 2
    ): Boolean {
        return verifyWarpTunnelConnectivityWithLatency(
            socksPort = socksPort,
            timeoutMs = timeoutMs,
            attempts = attempts
        ) > 0L
    }

    private suspend fun verifyWarpTunnelConnectivityWithLatency(
        socksPort: Int,
        timeoutMs: Long = 3500L,
        attempts: Int = 2
    ): Long = withContext(Dispatchers.IO) {
        val probeClient = warpProbeBaseClient.newBuilder()
            .proxy(Proxy(Proxy.Type.SOCKS, InetSocketAddress("127.0.0.1", socksPort)))
            .dns(object : Dns {
                override fun lookup(hostname: String): List<InetAddress> {
                    return listOf(InetAddress.getByAddress(byteArrayOf(1, 1, 1, 1)))
                }
            })
            .connectTimeout(timeoutMs, TimeUnit.MILLISECONDS)
            .readTimeout(timeoutMs, TimeUnit.MILLISECONDS)
            .callTimeout(timeoutMs + 1000L, TimeUnit.MILLISECONDS)
            .build()

        val request = Request.Builder()
            .url("https://1.1.1.1/cdn-cgi/trace")
            .header("Connection", "close")
            .get()
            .build()

        for (attempt in 1..attempts) {
            if (!isActive || userStopRequested || !SingboxManager.isRunning()) {
                return@withContext -1L
            }
            val startNs = System.nanoTime()
            try {
                probeClient.newCall(request).execute().use { response ->
                    val body = response.body?.string().orEmpty()
                    val ok = response.isSuccessful && (body.contains("warp=on") || body.contains("warp=plus"))
                    val elapsedMs = ((System.nanoTime() - startNs) / 1_000_000L).coerceAtLeast(1L)
                    Log.i(TAG, "WARP connectivity probe attempt=$attempt code=${response.code} ok=$ok latency=${elapsedMs}ms")
                    if (ok) return@withContext elapsedMs
                }
            } catch (e: CancellationException) {
                throw e
            } catch (e: Exception) {
                Log.w(TAG, "WARP connectivity probe attempt=$attempt failed: ${e.message}")
            }
            if (attempt < attempts) {
                delay(250)
            }
        }
        -1L
    }

    private fun startStatsMonitoring() {
        statsJob?.cancel()
        statsJob = serviceScope.launch {
            var warpCheckCounter = 0
            var consecutiveWarpFailures = 0
            var lastRxBytes = 0L

            while (isActive && VpnStateManager.state.value == VpnState.CONNECTED) {
                var rxDelta = 0L
                try {
                    val stats = TProxyService.TProxyGetStats()
                    val rx = if (stats.size >= 4) stats[3] else 0L
                    val tx = if (stats.size >= 4) stats[1] else 0L
                    rxDelta = (rx - lastRxBytes).coerceAtLeast(0L)
                    lastRxBytes = rx

                    VpnStateManager.updateStats(
                        rx = rx,
                        tx = tx,
                        connectedSince = startTime,
                        blockedAds = AdBlockEngine.blockedAds,
                        blockedTrackers = AdBlockEngine.blockedTrackers,
                        totalRules = AdBlockEngine.totalRules
                    )
                } catch (_: Exception) {
                    // Ignore stats error
                }

                if (VpnStateManager.currentMode.value == AppOperationMode.WARP_AND_ADBLOCK) {
                    if (!SingboxManager.isRunning()) {
                        if (!userStopRequested) {
                            Log.e(TAG, "Sing-box WARP process terminated unexpectedly")
                            pendingNetworkTransition = null
                            val recovered = withContext(NonCancellable) {
                                attemptMidSessionWarpRecovery("singbox_terminated")
                            }
                            if (!recovered) break
                            consecutiveWarpFailures = 0
                            warpCheckCounter = 0
                        } else {
                            break
                        }
                    } else {
                        val pendingNet = pendingNetworkTransition
                        if (pendingNet != null) {
                            val elapsedSinceChange = System.currentTimeMillis() - lastNetworkEventTimeMs
                            if (elapsedSinceChange >= NETWORK_TRANSITION_DEBOUNCE_MS) {
                                pendingNetworkTransition = null
                                val transitionOk = withContext(NonCancellable) {
                                    handlePhysicalNetworkTransition(pendingNet)
                                }
                                if (!transitionOk) break
                                consecutiveWarpFailures = 0
                                warpCheckCounter = 0
                            } else {
                                delay(400)
                                continue
                            }
                        } else {
                            if (rxDelta > 2048L) {
                                consecutiveWarpFailures = 0
                            } else {
                                warpCheckCounter++
                            }

                            if (warpCheckCounter >= 18) {
                                warpCheckCounter = 0
                                val healthy = verifyWarpTunnelConnectivity(
                                    socksPort = SingboxManager.SOCKS_PORT,
                                    timeoutMs = 5500L,
                                    attempts = 2
                                )
                                if (healthy) {
                                    consecutiveWarpFailures = 0
                                } else {
                                    consecutiveWarpFailures++
                                    Log.w(TAG, "WARP runtime health check failed ($consecutiveWarpFailures/2)")
                                    if (consecutiveWarpFailures >= 2) {
                                        val recovered = withContext(NonCancellable) {
                                            attemptMidSessionWarpRecovery("health_check_failed")
                                        }
                                        if (!recovered) break
                                        consecutiveWarpFailures = 0
                                        warpCheckCounter = 0
                                    } else {
                                        warpCheckCounter = 14
                                    }
                                }
                            }
                        }
                    }
                }

                delay(1000)
            }
        }
    }

    private fun startForegroundNotification(statusText: String) {
        val openAppIntent = Intent(this, MainActivity::class.java).apply {
            flags = Intent.FLAG_ACTIVITY_SINGLE_TOP or Intent.FLAG_ACTIVITY_CLEAR_TOP
        }
        val pendingIntent = PendingIntent.getActivity(
            this,
            0,
            openAppIntent,
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
        )

        val notification = NotificationCompat.Builder(this, UbourApplication.CHANNEL_ID)
            .setContentTitle(getString(R.string.app_name))
            .setContentText(statusText)
            .setSmallIcon(R.drawable.ic_ubour_logo)
            .setContentIntent(pendingIntent)
            .setOngoing(true)
            .setPriority(NotificationCompat.PRIORITY_LOW)
            .setCategory(NotificationCompat.CATEGORY_SERVICE)
            .setVisibility(NotificationCompat.VISIBILITY_PUBLIC)
            .build()

        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.UPSIDE_DOWN_CAKE) {
            startForeground(
                UbourApplication.NOTIFICATION_ID,
                notification,
                ServiceInfo.FOREGROUND_SERVICE_TYPE_SYSTEM_EXEMPTED
            )
        } else {
            startForeground(UbourApplication.NOTIFICATION_ID, notification)
        }
    }

    override fun onDestroy() {
        super.onDestroy()
        userStopRequested = true
        serviceScope.launch { stopVpn() }
    }

    override fun onRevoke() {
        super.onRevoke()
        userStopRequested = true
        serviceScope.launch { stopVpn() }
    }

    private data class CidrNode(val start: Long, val prefix: Int) {
        val len: Long = (1L shl (32 - prefix))

        fun overlaps(exStart: Long, exLen: Long): Boolean {
            return start < (exStart + exLen) && (start + len) > exStart
        }

        fun inside(exStart: Long, exLen: Long): Boolean {
            return start >= exStart && (start + len) <= (exStart + exLen)
        }

        fun ipString(): String {
            val b1 = (start ushr 24) and 0xFF
            val b2 = (start ushr 16) and 0xFF
            val b3 = (start ushr 8) and 0xFF
            val b4 = start and 0xFF
            return "$b1.$b2.$b3.$b4"
        }
    }

    private fun addRoutesExcludingSubnets(builder: Builder, effectiveDns: String) {
        val exclusions = mutableListOf(
            ipToLong("1.1.1.1") to 1L,
            ipToLong("1.0.0.1") to 1L,
            ipToLong("8.8.8.8") to 1L,
            ipToLong("8.8.4.4") to 1L,
            ipToLong("94.140.14.14") to 1L,
            ipToLong("94.140.15.15") to 1L,
            ipToLong("9.9.9.9") to 1L,
            ipToLong("149.112.112.112") to 1L,
            ipToLong("162.159.0.0") to 65536L, // 162.159.0.0/16
            ipToLong("188.114.0.0") to 65536L  // 188.114.0.0/16
        )

        try {
            if (effectiveDns.isNotBlank() && effectiveDns.contains(".")) {
                val dnsLong = ipToLong(effectiveDns)
                if (exclusions.none { it.first == dnsLong && it.second == 1L }) {
                    exclusions.add(dnsLong to 1L)
                }
            }
        } catch (e: Exception) {
            Log.w(TAG, "Could not parse effective DNS for routing exclusion: $effectiveDns")
        }

        var tree = listOf(CidrNode(0L, 0))
        for ((exStart, exLen) in exclusions) {
            tree = excludeCidr(tree, exStart, exLen)
        }

        for (node in tree) {
            try {
                builder.addRoute(node.ipString(), node.prefix)
            } catch (e: Exception) {
                Log.w(TAG, "Failed to add route ${node.ipString()}/${node.prefix}: ${e.message}")
            }
        }
    }

    private fun excludeCidr(roots: List<CidrNode>, exStart: Long, exLen: Long): List<CidrNode> {
        val result = mutableListOf<CidrNode>()
        for (node in roots) {
            if (node.inside(exStart, exLen)) {
                continue
            }
            if (node.overlaps(exStart, exLen) && node.prefix < 32) {
                val halfLen = node.len / 2
                val left = CidrNode(node.start, node.prefix + 1)
                val right = CidrNode(node.start + halfLen, node.prefix + 1)
                result.addAll(excludeCidr(listOf(left, right), exStart, exLen))
            } else {
                result.add(node)
            }
        }
        return result
    }

    private fun ipToLong(ip: String): Long {
        val parts = ip.split(".")
        return ((parts[0].toLong() and 0xFF) shl 24) or
               ((parts[1].toLong() and 0xFF) shl 16) or
               ((parts[2].toLong() and 0xFF) shl 8) or
               (parts[3].toLong() and 0xFF)
    }

    companion object {
        private const val TAG = "UbourVPN_Service"
        private const val NETWORK_TRANSITION_DEBOUNCE_MS = 800L
        const val ACTION_START = "com.ubour.vpn.START"
        const val ACTION_STOP = "com.ubour.vpn.STOP"
        const val EXTRA_MODE = "extra_mode"
        const val EXTRA_DNS = "extra_dns"
        const val EXTRA_OP_MODE = "extra_op_mode"
        const val EXTRA_ADBLOCK_ENABLED = "extra_adblock_enabled"
        const val DEFAULT_DNS = "8.8.8.8"

        val SECONDARY_DNS_MAP = mapOf(
            "8.8.8.8" to "8.8.4.4",
            "94.140.14.14" to "94.140.15.15",
            "9.9.9.9" to "149.112.112.112",
            "1.1.1.1" to "1.0.0.1"
        )
    }
}
