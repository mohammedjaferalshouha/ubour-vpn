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
