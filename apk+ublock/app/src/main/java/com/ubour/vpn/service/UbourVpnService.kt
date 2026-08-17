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
        val dnsServer = intent?.getStringExtra(EXTRA_DNS) ?: "94.140.14.14" // Default to AdGuard DNS
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

                // 1. Initialize AdBlock Engine if enabled
                val needsAdBlock = isAdBlockEnabled || opMode == AppOperationMode.ADBLOCK_ONLY || 
                                  opMode == AppOperationMode.WARP_AND_ADBLOCK || opMode == AppOperationMode.VPN_AND_ADBLOCK

                if (needsAdBlock) {
                    AdBlockEngine.initialize(applicationContext)
                    dnsFilterServer = DnsFilterServer(upstreamDns = dnsServer).apply {
                        start()
                    }
                }

                // 2. Determine target proxy port and start appropriate engine
                var socksPort = 1080

                when (opMode) {
                    AppOperationMode.WARP_AND_ADBLOCK -> {
                        socksPort = SingboxManager.SOCKS_PORT
                        val warpConfig = WarpManager.getOrRegisterConfig(applicationContext)
                        val started = SingboxManager.startWarp(applicationContext, warpConfig)
                        if (!started) {
                            Log.e(TAG, "Failed to start Sing-box WARP, fallback to ByeDPI")
                            socksPort = 1080
                            proxyJob = serviceScope.launch(Dispatchers.IO) {
                                byeDpiProxy.startProxy(mode = bypassMode, ip = "127.0.0.1", port = 1080)
                            }
                        }
                    }
                    AppOperationMode.CUSTOM_VLESS -> {
                        socksPort = SingboxManager.SOCKS_PORT
                        val prefs = getSharedPreferences("ubour_settings", Context.MODE_PRIVATE)
                        val vlessUrl = prefs.getString("custom_vless_url", "") ?: ""
                        val started = SingboxManager.startVless(applicationContext, vlessUrl)
                        if (!started) {
                            Log.e(TAG, "Failed to start Sing-box VLESS, fallback to ByeDPI")
                            socksPort = 1080
                            proxyJob = serviceScope.launch(Dispatchers.IO) {
                                byeDpiProxy.startProxy(mode = bypassMode, ip = "127.0.0.1", port = 1080)
                            }
                        }
                    }
                    AppOperationMode.VPN_AND_ADBLOCK, AppOperationMode.VPN_ONLY -> {
                        socksPort = 1080
                        proxyJob = serviceScope.launch(Dispatchers.IO) {
                            byeDpiProxy.startProxy(mode = bypassMode, ip = "127.0.0.1", port = 1080)
                        }
                    }
                    AppOperationMode.ADBLOCK_ONLY -> {
                        socksPort = 1080
                        proxyJob = serviceScope.launch(Dispatchers.IO) {
                            byeDpiProxy.startProxy(mode = 0, ip = "127.0.0.1", port = 1080)
                        }
                    }
                }
                delay(300)

                // 3. Prepare YAML config for hev-socks5-tunnel
                val tun2socksConfig = """
                tunnel:
                  mtu: 1500
                socks5:
                  port: $socksPort
                  address: 127.0.0.1
                  udp: 'tcp'
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

                    // Set DNS server
                    if (needsAdBlock) {
                        addDnsServer("94.140.14.14") // AdGuard DNS Primary
                        addDnsServer("94.140.14.15") // AdGuard DNS Secondary
                        if (dnsServer.isNotBlank() && dnsServer != "94.140.14.14" && dnsServer != "1.1.1.1") {
                            addDnsServer(dnsServer)
                        }
                    } else {
                        if (dnsServer.isNotBlank()) {
                            addDnsServer(dnsServer)
                        } else {
                            addDnsServer("1.1.1.1")
                        }
                        addDnsServer("8.8.8.8")
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

        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
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

    companion object {
        private const val TAG = "UbourVPN_Service"
        const val ACTION_START = "com.ubour.vpn.START"
        const val ACTION_STOP = "com.ubour.vpn.STOP"
        const val EXTRA_MODE = "extra_mode"
        const val EXTRA_DNS = "extra_dns"
        const val EXTRA_OP_MODE = "extra_op_mode"
        const val EXTRA_ADBLOCK_ENABLED = "extra_adblock_enabled"
    }
}
