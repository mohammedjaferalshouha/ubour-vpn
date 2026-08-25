package com.ubour.vpn.service

import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import android.content.pm.ServiceInfo
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
import com.ubour.vpn.warp.WarpManager
import io.github.dovecoteescapee.byedpi.core.ByeDpiProxy
import io.github.dovecoteescapee.byedpi.core.TProxyService
import kotlinx.coroutines.*
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import java.io.File

class UbourVpnService : VpnService() {

    private val byeDpiProxy = ByeDpiProxy()
    private var dnsFilterServer: DnsFilterServer? = null
    private var proxyJob: Job? = null
    private var statsJob: Job? = null
    private var vpnInterface: ParcelFileDescriptor? = null
    private val mutex = Mutex()
    private val serviceScope = CoroutineScope(Dispatchers.IO + SupervisorJob())
    private var startTime: Long = 0

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        val action = intent?.action
        if (action == ACTION_STOP) {
            serviceScope.launch { stopVpn() }
            return START_NOT_STICKY
        }

        if (VpnStateManager.state.value == VpnState.CONNECTED) {
            return START_STICKY
        }

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

                // 1. Initialize AdBlock Engine & DNS Filter Server if enabled
                val needsAdBlock = (opMode == AppOperationMode.WARP_AND_ADBLOCK || 
                                   opMode == AppOperationMode.VPN_AND_ADBLOCK || 
                                   opMode == AppOperationMode.ADBLOCK_ONLY) && isAdBlockEnabled

                if (needsAdBlock) {
                    AdBlockEngine.initialize(applicationContext)
                    dnsFilterServer = DnsFilterServer(
                        upstreamDns = effectiveDns,
                        socketProtector = { s -> protect(s) }
                    ).apply {
                        start()
                    }
                    Log.i(TAG, "DnsFilterServer started with upstream: $effectiveDns")
                } else {
                    dnsFilterServer?.stop()
                    dnsFilterServer = null
                }

                // 2. Start proxy core based on operation mode
                var socksPort = 1080
                var useSingbox = false

                when (opMode) {
                    AppOperationMode.WARP_AND_ADBLOCK -> {
                        // Cloudflare WARP with Sing-box core on port 10809 (Automatic fastest endpoint: 162.159.192.1:2408)
                        try {
                            val warpConfig = WarpManager.getOrRegisterConfig(
                                context = applicationContext,
                                targetHost = "162.159.192.1",
                                targetPort = 2408
                            )
                            if (SingboxManager.startWarp(applicationContext, warpConfig, enableAdBlock = isAdBlockEnabled)) {
                                delay(400)
                                if (SingboxManager.isRunning()) {
                                    socksPort = SingboxManager.SOCKS_PORT
                                    useSingbox = true
                                    Log.i(TAG, "Sing-box WARP running on port $socksPort (AdBlock=$isAdBlockEnabled)")
                                }
                            }
                        } catch (e: Exception) {
                            Log.e(TAG, "Failed to start Sing-box WARP: ${e.message}", e)
                        }

                        // Fallback to ByeDPI if Singbox fails
                        if (!useSingbox) {
                            Log.w(TAG, "Sing-box WARP failed. Falling back to ByeDPI on port 1080")
                            socksPort = 1080
                            proxyJob = serviceScope.launch(Dispatchers.IO) {
                                byeDpiProxy.startProxy(mode = bypassMode, ip = "127.0.0.1", port = 1080)
                            }
                            delay(300)
                        }
                    }

                    AppOperationMode.VPN_AND_ADBLOCK -> {
                        // Direct ByeDPI DPI bypass on port 1080 + Sing-box DNS interceptor on port 10809
                        proxyJob = serviceScope.launch(Dispatchers.IO) {
                            byeDpiProxy.startProxy(mode = bypassMode, ip = "127.0.0.1", port = 1080)
                        }
                        delay(300)

                        if (isAdBlockEnabled && SingboxManager.startVpnAndAdBlock(applicationContext, byedpiPort = 1080)) {
                            delay(300)
                            if (SingboxManager.isRunning()) {
                                socksPort = SingboxManager.SOCKS_PORT
                                useSingbox = true
                                Log.i(TAG, "Sing-box VPN+AdBlock dispatcher running on port $socksPort")
                            }
                        }
                        if (!useSingbox) {
                            socksPort = 1080
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
                            socksPort = 1080
                            proxyJob = serviceScope.launch(Dispatchers.IO) {
                                byeDpiProxy.startProxy(mode = 0, ip = "127.0.0.1", port = 1080)
                            }
                            delay(300)
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
                            socksPort = 1080
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
                val udpMode = if (socksPort == SingboxManager.SOCKS_PORT) "'udp'" else "'none'"
                val tun2socksConfig = """
                tunnel:
                  mtu: 1500
                socks5:
                  port: $socksPort
                  address: 127.0.0.1
                  udp: $udpMode
                misc:
                  task-stack-size: 81920
                """.trimIndent()

                val configFile = File(cacheDir, "tun2socks.tmp").apply {
                    writeText(tun2socksConfig)
                }

                // 4. Build VPN Interface
                val builder = Builder().apply {
                    setSession(getString(R.string.app_name))
                    setMtu(1500)
                    addAddress("10.10.10.10", 32)
                    addRoute("0.0.0.0", 0)

                    addDnsServer("10.10.10.10")
                    if (effectiveDns != "10.10.10.10") {
                        addDnsServer(effectiveDns)
                    }
                    val secondaryDns = if (opMode == AppOperationMode.WARP_AND_ADBLOCK) {
                        "1.0.0.1"
                    } else {
                        SECONDARY_DNS_MAP[effectiveDns] ?: (if (effectiveDns != "8.8.4.4") "8.8.4.4" else "8.8.8.8")
                    }
                    if (secondaryDns != effectiveDns && secondaryDns != "10.10.10.10") {
                        addDnsServer(secondaryDns)
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

                startStatsMonitoring()

            } catch (e: Exception) {
                Log.e(TAG, "Failed to start VPN", e)
                stopVpn()
            }
        }
    }

    private suspend fun stopVpn() {
        mutex.withLock {
            Log.i(TAG, "Stopping VPN service...")
            VpnStateManager.updateState(VpnState.DISCONNECTING)
            statsJob?.cancel()
            statsJob = null

            try {
                dnsFilterServer?.stop()
                dnsFilterServer = null
            } catch (e: Exception) {
                Log.e(TAG, "Error stopping DNS filter server", e)
            }

            try {
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
    }

    private fun startStatsMonitoring() {
        statsJob?.cancel()
        statsJob = serviceScope.launch {
            while (isActive && VpnStateManager.state.value == VpnState.CONNECTED) {
                try {
                    val stats = TProxyService.TProxyGetStats()
                    val rx = if (stats.size >= 4) stats[3] else 0L
                    val tx = if (stats.size >= 4) stats[1] else 0L
                    
                    VpnStateManager.updateStats(
                        rx = rx,
                        tx = tx,
                        connectedSince = startTime,
                        blockedAds = AdBlockEngine.blockedAds,
                        blockedTrackers = AdBlockEngine.blockedTrackers,
                        totalRules = AdBlockEngine.totalRules
                    )
                } catch (e: Exception) {
                    // Ignore stats error
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
        serviceScope.launch { stopVpn() }
    }

    override fun onRevoke() {
        super.onRevoke()
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
