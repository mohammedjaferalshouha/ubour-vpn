package com.ubour.vpn.warp

import android.content.Context
import android.util.Base64
import android.util.Log
import com.ubour.vpn.core.SingboxManager
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.ensureActive
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.withContext
import okhttp3.ConnectionPool
import okhttp3.Dns
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.RequestBody.Companion.toRequestBody
import org.json.JSONObject
import java.net.InetAddress
import java.net.InetSocketAddress
import java.net.Proxy
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale
import java.util.TimeZone
import java.util.concurrent.TimeUnit
import kotlin.coroutines.coroutineContext

data class ServerLocation(
    val id: String,
    val nameArabic: String,
    val flag: String,
    val endpointHost: String,
    val endpointPort: Int = 2408,
    val isCustomVless: Boolean = false,
    var pingMs: Int = -1
)

object CountryManager {
    val AVAILABLE_LOCATIONS = listOf(
        ServerLocation("auto", "تلقائي (الأسرع استجابة)", "⚡", "162.159.192.1", 2408),
        ServerLocation("jo", "الأردن (عَمّان)", "🇯🇴", "188.114.96.1", 2408),
        ServerLocation("ae", "الإمارات العربية المتحدة", "🇦🇪", "188.114.96.1", 2408),
        ServerLocation("tr", "تركيا (إسطنبول)", "🇹🇷", "188.114.97.1", 2408),
        ServerLocation("de", "ألمانيا (فرانكفورت)", "🇩🇪", "162.159.192.1", 2408),
        ServerLocation("nl", "هولندا (أمستردام)", "🇳🇱", "188.114.96.1", 2408),
        ServerLocation("fr", "فرنسا (باريس)", "🇫🇷", "162.159.192.1", 2408),
        ServerLocation("gb", "المملكة المتحدة (بريطانيا)", "🇬🇧", "188.114.97.1", 2408),
        ServerLocation("us", "الولايات المتحدة الأمريكية", "🇺🇸", "188.114.96.1", 2408),
        ServerLocation("ca", "كندا (تورونتو)", "🇨🇦", "188.114.97.1", 2408),
        ServerLocation("sg", "سنغافورة", "🇸🇬", "162.159.192.1", 2408),
        ServerLocation("jp", "اليابان (طوكيو)", "🇯🇵", "188.114.97.1", 2408),
        ServerLocation("custom_vless", "خادم VLESS Reality مخصص", "🌐", "", 443, isCustomVless = true)
    )

    fun getLocationByIndex(index: Int): ServerLocation {
        return if (index in AVAILABLE_LOCATIONS.indices) AVAILABLE_LOCATIONS[index] else AVAILABLE_LOCATIONS[0]
    }

    suspend fun measurePing(location: ServerLocation): Int = withContext(Dispatchers.IO) {
        if (location.isCustomVless) {
            location.pingMs = -1
            return@withContext -1
        }
        val host = location.endpointHost
        if (host.isEmpty()) {
            location.pingMs = -1
            return@withContext -1
        }
        try {
            val start = System.currentTimeMillis()
            java.net.Socket().use { socket ->
                socket.connect(java.net.InetSocketAddress(host, 443), 1500)
            }
            val elapsed = (System.currentTimeMillis() - start).toInt()
            val result = if (elapsed <= 0) 1 else elapsed
            location.pingMs = result
            result
        } catch (e: Exception) {
            location.pingMs = -2
            -2
        }
    }
}

data class WarpConfig(
    val privateKey: String,
    val publicKey: String,
    val peerPublicKey: String = "bmXOC+F1FxEMF9dyiK2H5/1SUtzH0JuVo51h2wPfgyo=",
    val endpointHost: String = "162.159.192.1",
    val endpointPort: Int = 2408,
    val localIpv4: String = "172.16.0.2/32",
    val localIpv6: String? = null,
    val reserved: List<Int> = listOf(122, 15, 67)
)

enum class WarpSelectionMode {
    AUTO,
    MANUAL
}

enum class WarpNetworkType(val prefSuffix: String) {
    WIFI("wifi"),
    CELLULAR("cellular"),
    ETHERNET("ethernet"),
    OTHER("other")
}

data class PhysicalNetworkCandidate(
    val handle: Long,
    val type: WarpNetworkType,
    val isValidated: Boolean
)

enum class EndpointTransitionAction {
    KEEP_CURRENT,
    CHECK_REMEMBERED_ENDPOINT,
    OPTIMIZE_DEGRADED,
    RECOVER_FAILED
}

object WarpNetworkSelector {
    const val FAST_ACCEPTABLE_LATENCY_MS = 150L
    const val MAX_ACCEPTABLE_LATENCY_MS = 450L
    const val MIN_IMPROVEMENT_OVER_GOOD_MS = 60L
    const val MIN_IMPROVEMENT_OVER_DEGRADED_MS = 80L
    const val GOOD_REPLACEMENT_LATENCY_MS = 300L

    fun transportPriority(type: WarpNetworkType): Int = when (type) {
        WarpNetworkType.ETHERNET -> 3
        WarpNetworkType.WIFI -> 2
        WarpNetworkType.CELLULAR -> 1
        WarpNetworkType.OTHER -> 0
    }

    fun selectPrimary(candidates: Collection<PhysicalNetworkCandidate>): PhysicalNetworkCandidate? {
        if (candidates.isEmpty()) return null
        return candidates.maxWithOrNull(
            compareBy<PhysicalNetworkCandidate> { if (it.isValidated) 1 else 0 }
                .thenBy { transportPriority(it.type) }
                .thenBy { it.handle }
        )
    }

    fun decideTransitionAction(
        currentLatencyMs: Long,
        currentEndpoint: Pair<String, Int>?,
        rememberedForTarget: Pair<String, Int>?
    ): EndpointTransitionAction {
        return when {
            currentLatencyMs <= 0L -> EndpointTransitionAction.RECOVER_FAILED
            currentLatencyMs > MAX_ACCEPTABLE_LATENCY_MS -> EndpointTransitionAction.OPTIMIZE_DEGRADED
            currentLatencyMs > FAST_ACCEPTABLE_LATENCY_MS &&
                rememberedForTarget != null &&
                rememberedForTarget != currentEndpoint -> EndpointTransitionAction.CHECK_REMEMBERED_ENDPOINT
            else -> EndpointTransitionAction.KEEP_CURRENT
        }
    }

    fun isWorthSwitching(currentLatencyMs: Long, candidateLatencyMs: Long): Boolean {
        if (candidateLatencyMs <= 0L) return false
        if (currentLatencyMs <= 0L) return true
        return if (currentLatencyMs > MAX_ACCEPTABLE_LATENCY_MS) {
            candidateLatencyMs + MIN_IMPROVEMENT_OVER_DEGRADED_MS < currentLatencyMs ||
                candidateLatencyMs <= GOOD_REPLACEMENT_LATENCY_MS
        } else {
            candidateLatencyMs + MIN_IMPROVEMENT_OVER_GOOD_MS < currentLatencyMs
        }
    }
}

enum class EndpointCheckState {
    UNTESTED,
    TESTING,
    WORKING,
    FAILED
}

data class WarpEndpointStatus(
    val host: String,
    val port: Int,
    val noteArabic: String,
    var checkState: EndpointCheckState = EndpointCheckState.UNTESTED,
    var latencyMs: Long = -1L,
    var speedMbps: Double? = null,
    var isSpeedTesting: Boolean = false
) {
    val key: String get() = "$host:$port"
}

data class WarpProbeResult(
    val host: String,
    val port: Int,
    val working: Boolean,
    val latencyMs: Long = -1L,
    val speedMbps: Double? = null
)

object WarpManager {
    private const val TAG = "WarpManager"
    private const val PREFS_NAME = "ubour_warp_prefs"
    private const val KEY_PRIVATE = "warp_privkey"
    private const val KEY_PUBLIC = "warp_pubkey"
    private const val KEY_PEER_KEY = "warp_peerkey"
    private const val KEY_IPV4 = "warp_ipv4"
    private const val KEY_IPV6 = "warp_ipv6"
    private const val KEY_HOST = "warp_host"
    private const val KEY_PORT = "warp_port"
    private const val KEY_RESERVED = "warp_reserved"
    private const val KEY_SELECTION_MODE = "warp_selection_mode"
    private const val KEY_MANUAL_HOST = "warp_manual_host"
    private const val KEY_MANUAL_PORT = "warp_manual_port"

    private val probeMutex = Mutex()
    private val interactiveProbeActive = java.util.concurrent.atomic.AtomicBoolean(false)
    private val probeBaseClient = OkHttpClient.Builder()
        .connectionPool(ConnectionPool(0, 1, TimeUnit.MILLISECONDS))
        .build()

    fun stopInteractiveProbe() {
        if (interactiveProbeActive.get()) {
            SingboxManager.stopProbe()
        }
    }

    val CANDIDATE_IPS: List<String> = listOf(
        "162.159.195.1",
        "188.114.96.1",
        "188.114.97.1",
        "188.114.98.1",
        "188.114.99.1",
        "162.159.192.1",
        "162.159.193.1"
    )

    val CANDIDATE_PORTS: List<Int> = listOf(
        4500, 500, 1701, 854, 859, 864, 878, 880, 890, 891, 894, 903, 908, 928,
        934, 939, 942, 943, 945, 946, 955, 968, 987, 988, 1002, 1010, 1014, 1018,
        1070, 1074, 1180, 1387, 2147, 2187, 2222, 3128, 3389, 3478, 3556, 8080,
        8443, 51820, 2408
    )

    /**
     * Expanded prioritized Cloudflare WARP endpoints across all 7 Anycast prefixes
     * and all 43 official UDP ports so restricted carriers connect reliably worldwide.
     */
    val DEFAULT_WARP_ENDPOINTS: List<Pair<String, Int>> = buildExpandedEndpointPool()

    private fun buildExpandedEndpointPool(): List<Pair<String, Int>> {
        val list = mutableListOf<Pair<String, Int>>(
            "162.159.195.1" to 4500,
            "188.114.98.1" to 4500,
            "188.114.97.1" to 4500,
            "188.114.99.1" to 4500,
            "188.114.96.1" to 4500,
            "162.159.192.1" to 4500,
            "162.159.192.1" to 854,
            "162.159.193.1" to 4500,
            "162.159.192.1" to 890,
            "188.114.98.1" to 2408,
            "188.114.99.1" to 2408,
            "162.159.195.1" to 2408,
            "188.114.96.1" to 2408,
            "162.159.192.1" to 500,
            "162.159.192.1" to 1701,
            "162.159.192.1" to 2408,
            "162.159.193.1" to 2408,
            "162.159.192.1" to 51820
        )

        val corePorts = listOf(4500, 854, 500, 1701, 890, 8443, 8080, 3478, 51820, 2408)
        for (port in corePorts) {
            for (ip in CANDIDATE_IPS) {
                list.add(ip to port)
            }
        }

        CANDIDATE_PORTS.forEachIndexed { idx, port ->
            val primaryIp = if (idx % 2 == 0) "162.159.195.1" else "188.114.96.1"
            val secondaryIp = if (idx % 2 == 0) "188.114.97.1" else "162.159.192.1"
            list.add(primaryIp to port)
            if (idx < 16) {
                list.add(secondaryIp to port)
            }
        }

        return list.distinct()
    }

    fun getPortNoteArabic(port: Int): String = when (port) {
        4500 -> "موصى به لتجاوز الحجب (NAT-T)"
        500 -> "منفذ شبكات افتراضية قياسي (IKE)"
        1701 -> "منفذ أنفاق قياسي (L2TP)"
        2408 -> "المنفذ الافتراضي القياسي"
        51820 -> "منفذ واير غارد القياسي"
        8080, 8443 -> "منفذ بديل لحركة الويب"
        3478 -> "منفذ اتصالات فورية بديل"
        854, 859, 864, 878, 880, 890, 891, 894 -> "منفذ سحابي مقاوم للتقييد"
        else -> "منفذ سحابي بديل مخصص"
    }

    fun buildCandidateEndpoints(lastHost: String?, lastPort: Int): List<Pair<String, Int>> {
        val ordered = mutableListOf<Pair<String, Int>>()
        if (!lastHost.isNullOrBlank() && lastPort > 0) {
            ordered.add(lastHost to lastPort)
        }
        ordered.addAll(DEFAULT_WARP_ENDPOINTS)
        return ordered.distinct()
    }

    fun resolveCandidatesForMode(
        mode: WarpSelectionMode,
        manualEndpoint: Pair<String, Int>?,
        lastWorkingEndpoint: Pair<String, Int>?,
        deprioritizeEndpoint: Pair<String, Int>? = null,
        isRecovery: Boolean = false
    ): List<Pair<String, Int>> {
        if (mode == WarpSelectionMode.MANUAL) {
            if (isRecovery) {
                return emptyList()
            }
            if (manualEndpoint != null && manualEndpoint.first.isNotBlank() && manualEndpoint.second > 0) {
                return listOf(manualEndpoint)
            }
        }
        val base = buildCandidateEndpoints(lastWorkingEndpoint?.first, lastWorkingEndpoint?.second ?: 0)
        return if (deprioritizeEndpoint != null) {
            base.filter { it != deprioritizeEndpoint } + base.filter { it == deprioritizeEndpoint }
        } else {
            base
        }
    }

    fun getSelectionMode(context: Context): WarpSelectionMode {
        val prefs = context.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)
        val raw = prefs.getString(KEY_SELECTION_MODE, "auto")
        return if (raw == "manual" && getManualEndpoint(context) != null) {
            WarpSelectionMode.MANUAL
        } else {
            WarpSelectionMode.AUTO
        }
    }

    fun setAutoSelectionMode(context: Context) {
        context.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)
            .edit()
            .putString(KEY_SELECTION_MODE, "auto")
            .apply()
    }

    fun setManualEndpoint(context: Context, host: String, port: Int) {
        if (host.isBlank() || port <= 0) return
        context.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)
            .edit()
            .putString(KEY_SELECTION_MODE, "manual")
            .putString(KEY_MANUAL_HOST, host)
            .putInt(KEY_MANUAL_PORT, port)
            .apply()
    }

    fun getManualEndpoint(context: Context): Pair<String, Int>? {
        val prefs = context.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)
        val host = prefs.getString(KEY_MANUAL_HOST, null)
        val port = prefs.getInt(KEY_MANUAL_PORT, 0)
        return if (!host.isNullOrBlank() && port > 0) (host to port) else null
    }

    fun getLastWorkingEndpoint(context: Context): Pair<String, Int>? {
        val prefs = context.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)
        val host = prefs.getString(KEY_HOST, null)
        val port = prefs.getInt(KEY_PORT, 0)
        return if (!host.isNullOrBlank() && port > 0) (host to port) else null
    }

    fun getCandidateEndpoints(context: Context): List<Pair<String, Int>> {
        val last = getLastWorkingEndpoint(context)
        return buildCandidateEndpoints(last?.first, last?.second ?: 0)
    }

    fun saveLastWorkingEndpoint(context: Context, host: String, port: Int) {
        if (host.isBlank() || port <= 0) return
        context.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)
            .edit()
            .putString(KEY_HOST, host)
            .putInt(KEY_PORT, port)
            .apply()
    }

    fun getLastWorkingEndpointForNetwork(context: Context, networkType: WarpNetworkType): Pair<String, Int>? {
        val prefs = context.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)
        val host = prefs.getString("warp_host_${networkType.prefSuffix}", null)
        val port = prefs.getInt("warp_port_${networkType.prefSuffix}", 0)
        return if (!host.isNullOrBlank() && port > 0) (host to port) else null
    }

    fun saveLastWorkingEndpointForNetwork(context: Context, networkType: WarpNetworkType, host: String, port: Int) {
        if (host.isBlank() || port <= 0) return
        context.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)
            .edit()
            .putString("warp_host_${networkType.prefSuffix}", host)
            .putInt("warp_port_${networkType.prefSuffix}", port)
            .putString(KEY_HOST, host)
            .putInt(KEY_PORT, port)
            .apply()
    }

    private fun buildTraceClientForPort(socksPort: Int, timeoutMs: Long): OkHttpClient {
        return probeBaseClient.newBuilder()
            .proxy(Proxy(Proxy.Type.SOCKS, InetSocketAddress("127.0.0.1", socksPort)))
            .dns(object : Dns {
                override fun lookup(hostname: String): List<InetAddress> {
                    return listOf(InetAddress.getByAddress(byteArrayOf(1, 1, 1, 1)))
                }
            })
            .connectTimeout(timeoutMs, TimeUnit.MILLISECONDS)
            .readTimeout(timeoutMs, TimeUnit.MILLISECONDS)
            .callTimeout(timeoutMs + 800L, TimeUnit.MILLISECONDS)
            .build()
    }

    private fun probeSinglePortBlocking(socksPort: Int, timeoutMs: Long): Pair<Boolean, Long> {
        val client = buildTraceClientForPort(socksPort, timeoutMs)
        val req = Request.Builder()
            .url("https://1.1.1.1/cdn-cgi/trace")
            .header("Connection", "close")
            .get()
            .build()
        val startNs = System.nanoTime()
        return try {
            val firstResult = client.newCall(req).execute().use { resp ->
                val body = resp.body?.string().orEmpty()
                val ok = resp.isSuccessful && (body.contains("warp=on") || body.contains("warp=plus"))
                val elapsedMs = ((System.nanoTime() - startNs) / 1_000_000L).coerceAtLeast(1L)
                if (ok) (true to elapsedMs) else (false to -1L)
            }
            if (!firstResult.first) {
                return false to -1L
            }
            // Measure warm-tunnel round-trip latency without initial WireGuard handshake jitter
            val warmStartNs = System.nanoTime()
            val warmMs = try {
                client.newCall(req).execute().use { resp ->
                    val body = resp.body?.string().orEmpty()
                    val ok = resp.isSuccessful && (body.contains("warp=on") || body.contains("warp=plus"))
                    if (ok) ((System.nanoTime() - warmStartNs) / 1_000_000L).coerceAtLeast(1L) else firstResult.second
                }
            } catch (_: Exception) {
                firstResult.second
            }
            true to minOf(firstResult.second, warmMs)
        } catch (_: Exception) {
            false to -1L
        }
    }

    private const val FAST_POP_LATENCY_MS = 100L

    /**
     * Probes endpoints within a single Sing-box batch sequentially rather than firing
     * simultaneous WireGuard handshakes with the same client privateKey (which causes
     * peer session clobbering and handshake rate-limit drops when multiple Anycast IPs
     * route to the same local Cloudflare PoP).
     */
    suspend fun probeEndpointBatch(
        context: Context,
        baseConfig: WarpConfig,
        batch: List<Pair<String, Int>>,
        timeoutMs: Long = 1500L,
        earlyExitLatencyMs: Long = -1L,
        tightenTimeoutAfterHit: Boolean = false,
        shouldCancel: () -> Boolean = { false }
    ): List<WarpProbeResult> = withContext(Dispatchers.IO) {
        if (batch.isEmpty()) return@withContext emptyList()
        probeMutex.withLock {
            try {
                if (!SingboxManager.startWarpProbeBatch(context, baseConfig, batch, SingboxManager.PROBE_BASE_PORT)) {
                    return@withLock batch.map { (h, p) -> WarpProbeResult(h, p, working = false) }
                }
                val results = ArrayList<WarpProbeResult>(batch.size)
                var bestLatencyMs = Long.MAX_VALUE
                val failedCountByPort = HashMap<Int, Int>()

                for ((idx, pair) in batch.withIndex()) {
                    coroutineContext.ensureActive()
                    if (shouldCancel()) {
                        for (rem in idx until batch.size) {
                            val (rh, rp) = batch[rem]
                            results.add(WarpProbeResult(rh, rp, working = false))
                        }
                        break
                    }

                    val (host, port) = pair
                    val portFailures = failedCountByPort[port] ?: 0
                    val effectiveTimeout = when {
                        tightenTimeoutAfterHit && bestLatencyMs < Long.MAX_VALUE -> {
                            minOf(timeoutMs, (bestLatencyMs * 2L + 300L).coerceIn(550L, 1100L))
                        }
                        portFailures >= 2 -> minOf(timeoutMs, 650L)
                        else -> timeoutMs
                    }

                    val (ok, latMs) = probeSinglePortBlocking(
                        socksPort = SingboxManager.PROBE_BASE_PORT + idx,
                        timeoutMs = effectiveTimeout
                    )
                    results.add(WarpProbeResult(host = host, port = port, working = ok, latencyMs = latMs))

                    if (ok) {
                        if (latMs in 1 until bestLatencyMs) {
                            bestLatencyMs = latMs
                        }
                        if (earlyExitLatencyMs > 0L && latMs in 1..earlyExitLatencyMs) {
                            for (rem in (idx + 1) until batch.size) {
                                val (rh, rp) = batch[rem]
                                results.add(WarpProbeResult(rh, rp, working = false))
                            }
                            break
                        }
                        if (idx + 1 < batch.size) {
                            delay(45L)
                        }
                    } else {
                        failedCountByPort[port] = portFailures + 1
                    }
                }
                results
            } finally {
                SingboxManager.stopProbe()
            }
        }
    }

    suspend fun findBestWorkingEndpoint(
        context: Context,
        baseConfig: WarpConfig,
        candidates: List<Pair<String, Int>>,
        preferQuickFirst: Boolean = false,
        batchSize: Int = 8,
        timeoutMs: Long = 1800L,
        shouldCancel: () -> Boolean = { false }
    ): WarpProbeResult? = withContext(Dispatchers.IO) {
        if (candidates.isEmpty() || shouldCancel()) return@withContext null

        if (candidates.size == 1) {
            val single = candidates.first()
            val firstTry = probeEndpointBatch(context, baseConfig, listOf(single), timeoutMs = 3000L, shouldCancel = shouldCancel).firstOrNull()
            if (firstTry?.working == true) return@withContext firstTry
            if (shouldCancel()) return@withContext null
            val secondTry = probeEndpointBatch(context, baseConfig, listOf(single), timeoutMs = 3500L, shouldCancel = shouldCancel).firstOrNull()
            return@withContext secondTry?.takeIf { it.working }
        }

        var remaining = candidates
        var fallbackCandidate: WarpProbeResult? = null
        if (preferQuickFirst) {
            val firstEp = candidates.first()
            Log.i(TAG, "Quick-checking preferred WARP endpoint: ${firstEp.first}:${firstEp.second}")
            val quickRes = probeEndpointBatch(context, baseConfig, listOf(firstEp), timeoutMs = 1500L, shouldCancel = shouldCancel).firstOrNull()
            if (quickRes?.working == true) {
                if (quickRes.latencyMs in 1..FAST_POP_LATENCY_MS) {
                    return@withContext quickRes
                }
                fallbackCandidate = quickRes
            }
            remaining = candidates.drop(1)
        }

        val batches = remaining.chunked(batchSize.coerceAtLeast(1))
        for ((bIdx, batch) in batches.withIndex()) {
            if (shouldCancel()) return@withContext null
            val effectiveBatchTimeout = if (bIdx == 0) minOf(timeoutMs, 1500L) else timeoutMs
            Log.i(TAG, "Probing WARP batch ${bIdx + 1}/${batches.size} (${batch.size} endpoints, timeout=${effectiveBatchTimeout}ms)")
            val results = probeEndpointBatch(
                context = context,
                baseConfig = baseConfig,
                batch = batch,
                timeoutMs = effectiveBatchTimeout,
                earlyExitLatencyMs = FAST_POP_LATENCY_MS,
                tightenTimeoutAfterHit = true,
                shouldCancel = shouldCancel
            )
            val workingInBatch = results.filter { it.working }.toMutableList()
            if (bIdx == 0 && fallbackCandidate != null) {
                workingInBatch.add(fallbackCandidate)
            }
            val best = workingInBatch.minByOrNull { it.latencyMs }
            if (best != null) {
                Log.i(TAG, "Selected fastest WARP endpoint from batch: ${best.host}:${best.port} (${best.latencyMs}ms)")
                return@withContext best
            }
        }
        fallbackCandidate
    }

    suspend fun scanEndpointsInteractive(
        context: Context,
        endpoints: List<Pair<String, Int>> = DEFAULT_WARP_ENDPOINTS,
        batchSize: Int = 8,
        timeoutMs: Long = 1350L,
        onBatchStarting: suspend (List<Pair<String, Int>>) -> Unit,
        onBatchResult: suspend (List<WarpProbeResult>) -> Unit
    ) {
        val baseConfig = getOrRegisterConfig(context)
        val perEndpointTimeout = timeoutMs.coerceIn(900L, 1500L)
        val batches = endpoints.chunked(batchSize.coerceAtLeast(1))
        interactiveProbeActive.set(true)
        try {
            for (batch in batches) {
                coroutineContext.ensureActive()
                onBatchStarting(batch)
                val results = probeEndpointBatch(
                    context = context,
                    baseConfig = baseConfig,
                    batch = batch,
                    timeoutMs = perEndpointTimeout,
                    earlyExitLatencyMs = -1L,
                    tightenTimeoutAfterHit = false
                )
                coroutineContext.ensureActive()
                onBatchResult(results)
            }
        } finally {
            interactiveProbeActive.set(false)
        }
    }

    private fun resolveHostViaWarpDoh(socksPort: Int, domain: String): List<InetAddress> {
        val client = buildTraceClientForPort(socksPort, 3500L)
        val req = Request.Builder()
            .url("https://1.1.1.1/dns-query?name=$domain&type=A")
            .header("Accept", "application/dns-json")
            .header("Connection", "close")
            .get()
            .build()
        return try {
            client.newCall(req).execute().use { resp ->
                if (!resp.isSuccessful) return emptyList()
                val json = JSONObject(resp.body?.string().orEmpty())
                val answers = json.optJSONArray("Answer") ?: return emptyList()
                val out = mutableListOf<InetAddress>()
                for (i in 0 until answers.length()) {
                    val item = answers.optJSONObject(i) ?: continue
                    if (item.optInt("type") == 1) {
                        val ipStr = item.optString("data", "")
                        if (ipStr.isNotBlank()) {
                            out.add(InetAddress.getByName(ipStr))
                        }
                    }
                }
                out
            }
        } catch (_: Exception) {
            emptyList()
        }
    }

    suspend fun measureEndpointSpeedMbps(
        context: Context,
        host: String,
        port: Int,
        durationLimitMs: Long = 5000L
    ): WarpProbeResult = withContext(Dispatchers.IO) {
        val baseConfig = getOrRegisterConfig(context)
        probeMutex.withLock {
            interactiveProbeActive.set(true)
            try {
                val socksPort = SingboxManager.PROBE_BASE_PORT
                if (!SingboxManager.startWarpProbeBatch(context, baseConfig, listOf(host to port), socksPort)) {
                    return@withLock WarpProbeResult(host, port, working = false)
                }

                var probe = probeSinglePortBlocking(socksPort, 3200L)
                if (!probe.first) {
                    probe = probeSinglePortBlocking(socksPort, 3500L)
                }
                if (!probe.first) {
                    return@withLock WarpProbeResult(host, port, working = false)
                }
                val latencyMs = probe.second

                val speedClient = probeBaseClient.newBuilder()
                    .proxy(Proxy(Proxy.Type.SOCKS, InetSocketAddress("127.0.0.1", socksPort)))
                    .connectTimeout(4500L, TimeUnit.MILLISECONDS)
                    .readTimeout(6000L, TimeUnit.MILLISECONDS)
                    .callTimeout(durationLimitMs + 5500L, TimeUnit.MILLISECONDS)
                    .build()

                val speedReq = Request.Builder()
                    .url("https://speed.cloudflare.com/__down?bytes=25000000")
                    .header("Accept-Encoding", "identity")
                    .header("Connection", "close")
                    .get()
                    .build()

                var totalBytes = 0L
                var steadyBytes = 0L
                var measureStartNs = 0L
                var steadyStartNs = 0L
                var measureEndNs = 0L
                val warmupNs = 150_000_000L
                val deadlineNs = System.nanoTime() + (durationLimitMs * 1_000_000L)

                val call = speedClient.newCall(speedReq)
                try {
                    call.execute().use { resp ->
                        if (resp.isSuccessful) {
                            val stream = resp.body?.byteStream()
                            if (stream != null) {
                                val buf = ByteArray(65536)
                                while (System.nanoTime() < deadlineNs) {
                                    val read = stream.read(buf)
                                    if (read <= 0) break
                                    val now = System.nanoTime()
                                    if (measureStartNs == 0L) {
                                        measureStartNs = now
                                    }
                                    totalBytes += read
                                    if (now - measureStartNs >= warmupNs) {
                                        if (steadyStartNs == 0L) {
                                            steadyStartNs = now
                                        } else {
                                            steadyBytes += read
                                        }
                                    }
                                    measureEndNs = now
                                }
                                call.cancel()
                            }
                        }
                    }
                } catch (e: Exception) {
                    if (totalBytes == 0L) {
                        Log.w(TAG, "Speed stream interrupted on $host:$port -> ${e.message}")
                    }
                }

                val speedMbps = if (totalBytes > 0L && measureStartNs > 0L) {
                    val endNs = if (measureEndNs > measureStartNs) measureEndNs else System.nanoTime()
                    val rawMbps = if (steadyStartNs > 0L && endNs - steadyStartNs >= 200_000_000L && steadyBytes > 0L) {
                        val steadySec = (endNs - steadyStartNs) / 1_000_000_000.0
                        (steadyBytes * 8.0) / (steadySec * 1_000_000.0)
                    } else {
                        val elapsedSec = ((endNs - measureStartNs) / 1_000_000_000.0).coerceAtLeast(0.08)
                        (totalBytes * 8.0) / (elapsedSec * 1_000_000.0)
                    }
                    Math.round(rawMbps * 10.0) / 10.0
                } else {
                    null
                }

                WarpProbeResult(
                    host = host,
                    port = port,
                    working = true,
                    latencyMs = latencyMs,
                    speedMbps = speedMbps
                )
            } catch (e: Exception) {
                Log.w(TAG, "Speed test error on $host:$port -> ${e.message}")
                WarpProbeResult(host, port, working = false)
            } finally {
                interactiveProbeActive.set(false)
                SingboxManager.stopProbe()
            }
        }
    }

    private val httpClient = OkHttpClient.Builder()
        .connectTimeout(15, TimeUnit.SECONDS)
        .readTimeout(15, TimeUnit.SECONDS)
        .build()

    suspend fun getOrRegisterConfig(
        context: Context,
        targetHost: String = "162.159.192.1",
        targetPort: Int = 2408
    ): WarpConfig = withContext(Dispatchers.IO) {
        val prefs = context.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)
        val savedPriv = prefs.getString(KEY_PRIVATE, null)
        val savedIpv4 = prefs.getString(KEY_IPV4, null)
        val savedReservedStr = prefs.getString(KEY_RESERVED, null)

        val hostToUse = if (targetHost.isNotBlank()) targetHost else (prefs.getString(KEY_HOST, "162.159.192.1") ?: "162.159.192.1")
        val portToUse = if (targetPort > 0) targetPort else prefs.getInt(KEY_PORT, 2408)

        if (!savedPriv.isNullOrBlank() && !savedIpv4.isNullOrBlank() && !savedReservedStr.isNullOrBlank()) {
            val reservedList = savedReservedStr.split(",").mapNotNull { it.toIntOrNull() }
            return@withContext WarpConfig(
                privateKey = savedPriv,
                publicKey = prefs.getString(KEY_PUBLIC, "") ?: "",
                peerPublicKey = prefs.getString(KEY_PEER_KEY, "bmXOC+F1FxEMF9dyiK2H5/1SUtzH0JuVo51h2wPfgyo=") ?: "bmXOC+F1FxEMF9dyiK2H5/1SUtzH0JuVo51h2wPfgyo=",
                endpointHost = hostToUse,
                endpointPort = portToUse,
                localIpv4 = savedIpv4,
                localIpv6 = prefs.getString(KEY_IPV6, null),
                reserved = if (reservedList.size == 3) reservedList else listOf(122, 15, 67)
            )
        }

        // Generate keypair
        val (privKey, pubKey) = generateWireguardKeyPair(context)
        Log.i(TAG, "Generated keypair, registering with Cloudflare WARP API...")

        try {
            val regUrl = "https://api.cloudflareclient.com/v0a2158/reg"
            val sdf = SimpleDateFormat("yyyy-MM-dd'T'HH:mm:ss.SSS'Z'", Locale.US)
            sdf.timeZone = TimeZone.getTimeZone("UTC")
            val isoDate = sdf.format(Date())

            val jsonBody = JSONObject().apply {
                put("install_id", "")
                put("tos", isoDate)
                put("key", pubKey)
                put("fcm_token", "")
                put("type", "Android")
                put("locale", "en_US")
            }

            val request = Request.Builder()
                .url(regUrl)
                .header("User-Agent", "okhttp/3.12.1")
                .header("Content-Type", "application/json")
                .post(jsonBody.toString().toRequestBody("application/json".toMediaType()))
                .build()

            httpClient.newCall(request).execute().use { response ->
                if (response.isSuccessful) {
                    val bodyStr = response.body?.string() ?: ""
                    val root = JSONObject(bodyStr)
                    val regId = root.optString("id", "")
                    val token = root.optString("token", "")
                    val configObj = root.optJSONObject("config")
                    
                    val clientIdB64 = configObj?.optString("client_id", "") ?: ""
                    val reservedBytes = decodeReserved(clientIdB64)

                    val peersArr = configObj?.optJSONArray("peers")
                    val firstPeer = peersArr?.optJSONObject(0)
                    val peerPub = firstPeer?.optString("public_key", "bmXOC+F1FxEMF9dyiK2H5/1SUtzH0JuVo51h2wPfgyo=") ?: "bmXOC+F1FxEMF9dyiK2H5/1SUtzH0JuVo51h2wPfgyo="
                    
                    val iface = configObj?.optJSONObject("interface")
                    val addrs = iface?.optJSONObject("addresses")
                    val v4 = addrs?.optString("v4", "172.16.0.2") ?: "172.16.0.2"
                    val v6 = addrs?.optString("v6", "2606:4700:110:8ee3:bc9f:edcc:8ad8:5252") ?: "2606:4700:110:8ee3:bc9f:edcc:8ad8:5252"

                    val v4Cidr = if (v4.contains("/")) v4 else "$v4/32"
                    val v6Cidr = if (v6.contains("/")) v6 else "$v6/128"

                    if (regId.isNotBlank() && token.isNotBlank()) {
                        enableWarp(regId, token)
                    }

                    val reservedStr = reservedBytes.joinToString(",")
                    prefs.edit()
                        .putString(KEY_PRIVATE, privKey)
                        .putString(KEY_PUBLIC, pubKey)
                        .putString(KEY_PEER_KEY, peerPub)
                        .putString(KEY_IPV4, v4Cidr)
                        .putString(KEY_IPV6, v6Cidr)
                        .putString(KEY_RESERVED, reservedStr)
                        .apply()

                    Log.i(TAG, "Cloudflare WARP registered successfully: $v4Cidr with reserved=$reservedBytes")
                    return@withContext WarpConfig(
                        privateKey = privKey,
                        publicKey = pubKey,
                        peerPublicKey = peerPub,
                        endpointHost = hostToUse,
                        endpointPort = portToUse,
                        localIpv4 = v4Cidr,
                        localIpv6 = v6Cidr,
                        reserved = reservedBytes
                    )
                } else {
                    Log.e(TAG, "Registration HTTP failed: code=${response.code}")
                }
            }
        } catch (e: Exception) {
            Log.e(TAG, "Registration failed: ${e.message}")
        }

        throw IllegalStateException("Failed to register with Cloudflare WARP API")
    }

    private fun enableWarp(regId: String, token: String) {
        try {
            val patchUrl = "https://api.cloudflareclient.com/v0a2158/reg/$regId"
            val jsonBody = JSONObject().apply {
                put("warp_enabled", true)
            }
            val request = Request.Builder()
                .url(patchUrl)
                .header("Authorization", "Bearer $token")
                .header("User-Agent", "okhttp/3.12.1")
                .patch(jsonBody.toString().toRequestBody("application/json".toMediaType()))
                .build()
            httpClient.newCall(request).execute().close()
            Log.i(TAG, "Successfully enabled warp_enabled=true on Cloudflare API")
        } catch (e: Exception) {
            Log.w(TAG, "Failed to patch warp_enabled: ${e.message}")
        }
    }

    private fun decodeReserved(b64: String): List<Int> {
        if (b64.isBlank()) return listOf(122, 15, 67)
        return try {
            val bytes = Base64.decode(b64, Base64.DEFAULT)
            if (bytes.size >= 3) {
                listOf(bytes[0].toInt() and 0xFF, bytes[1].toInt() and 0xFF, bytes[2].toInt() and 0xFF)
            } else {
                listOf(122, 15, 67)
            }
        } catch (_: Exception) {
            listOf(122, 15, 67)
        }
    }

    private fun generateWireguardKeyPair(context: Context): Pair<String, String> {
        return try {
            val random = java.security.SecureRandom()
            val rawPriv = ByteArray(32)
            random.nextBytes(rawPriv)
            val clamped = Curve25519KeyGen.clamp(rawPriv)
            val pub = Curve25519KeyGen.generatePublicKey(clamped)
            val privB64 = Base64.encodeToString(clamped, Base64.NO_WRAP)
            val pubB64 = Base64.encodeToString(pub, Base64.NO_WRAP)
            Pair(privB64, pubB64)
        } catch (e: Exception) {
            Log.e(TAG, "Error generating keypair: ${e.message}", e)
            throw e
        }
    }
}

private object Curve25519KeyGen {
    private val P = java.math.BigInteger.valueOf(2).pow(255).subtract(java.math.BigInteger.valueOf(19))
    private val A24 = java.math.BigInteger.valueOf(121665)

    fun clamp(key: ByteArray): ByteArray {
        val k = key.clone()
        k[0] = (k[0].toInt() and 248).toByte()
        k[31] = ((k[31].toInt() and 127) or 64).toByte()
        return k
    }

    fun generatePublicKey(privateKeyClamped: ByteArray): ByteArray {
        val x1 = java.math.BigInteger.valueOf(9)
        var x2 = java.math.BigInteger.ONE
        var z2 = java.math.BigInteger.ZERO
        var x3 = x1
        var z3 = java.math.BigInteger.ONE
        var swap = 0

        for (t in 254 downTo 0) {
            val byteIndex = t / 8
            val bitIndex = t % 8
            val bit = (privateKeyClamped[byteIndex].toInt() shr bitIndex) and 1

            if (bit != swap) {
                var tmp = x2; x2 = x3; x3 = tmp
                tmp = z2; z2 = z3; z3 = tmp
                swap = bit
            }

            val a = x2.add(z2).mod(P)
            val aa = a.multiply(a).mod(P)
            val b = x2.subtract(z2).mod(P)
            val bb = b.multiply(b).mod(P)
            val e = aa.subtract(bb).mod(P)
            val c = x3.add(z3).mod(P)
            val d = x3.subtract(z3).mod(P)
            val da = d.multiply(a).mod(P)
            val cb = c.multiply(b).mod(P)

            val daPlusCb = da.add(cb).mod(P)
            val daMinusCb = da.subtract(cb).mod(P)

            x3 = daPlusCb.multiply(daPlusCb).mod(P)
            z3 = x1.multiply(daMinusCb.multiply(daMinusCb).mod(P)).mod(P)
            x2 = aa.multiply(bb).mod(P)
            z2 = e.multiply(aa.add(A24.multiply(e).mod(P))).mod(P)
        }

        if (swap == 1) {
            val tmp = x2; x2 = x3; x3 = tmp
            val tmpZ = z2; z2 = z3; z3 = tmpZ
        }

        val result = x2.multiply(z2.modInverse(P)).mod(P)
        val raw = result.toByteArray()
        val pub = ByteArray(32)
        for (i in 0 until raw.size.coerceAtMost(32)) {
            pub[i] = raw[raw.size - 1 - i]
        }
        return pub
    }
}
