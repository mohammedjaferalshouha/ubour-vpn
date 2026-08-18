package com.ubour.vpn.warp

import android.content.Context
import android.util.Base64
import android.util.Log
import com.ubour.vpn.core.SingboxManager
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.RequestBody.Companion.toRequestBody
import org.json.JSONObject
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale
import java.util.TimeZone
import java.util.concurrent.TimeUnit

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
                        .putString(KEY_HOST, hostToUse)
                        .putInt(KEY_PORT, portToUse)
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
                }
            }
        } catch (e: Exception) {
            Log.e(TAG, "Registration failed, using fallback config: ${e.message}")
        }

        WarpConfig(
            privateKey = "QFb39ooaBYVDqSwZuwmnXJfmZQh5y2GaSM6yv3rV7kE=",
            publicKey = "SGQoI1GOzPThfSGIyxMks6TL7B2T2x+fvE4JqMv1ThQ=",
            endpointHost = hostToUse,
            endpointPort = portToUse,
            localIpv4 = "172.16.0.2/32",
            localIpv6 = "2606:4700:110:8ee3:bc9f:edcc:8ad8:5252/128",
            reserved = listOf(122, 15, 67)
        )
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
        val singboxFile = SingboxManager.getExecutableBinary(context)
        if (singboxFile != null && singboxFile.exists()) {
            try {
                val proc = ProcessBuilder(singboxFile.absolutePath, "generate", "wg-keypair")
                    .redirectErrorStream(true)
                    .start()
                val output = proc.inputStream.bufferedReader().readText()
                proc.waitFor()
                
                var priv: String? = null
                var pub: String? = null
                output.lines().forEach { line ->
                    if (line.startsWith("PrivateKey:", ignoreCase = true)) {
                        priv = line.substringAfter(":").trim()
                    } else if (line.startsWith("PublicKey:", ignoreCase = true)) {
                        pub = line.substringAfter(":").trim()
                    }
                }
                if (!priv.isNullOrBlank() && !pub.isNullOrBlank()) {
                    return Pair(priv!!, pub!!)
                }
            } catch (e: Exception) {
                Log.w(TAG, "Failed to run singbox generate: ${e.message}")
            }
        }

        return Pair(
            "QFb39ooaBYVDqSwZuwmnXJfmZQh5y2GaSM6yv3rV7kE=",
            "SGQoI1GOzPThfSGIyxMks6TL7B2T2x+fvE4JqMv1ThQ="
        )
    }
}
