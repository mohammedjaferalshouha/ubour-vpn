package com.ubour.vpn.warp

import android.content.Context
import android.util.Base64
import android.util.Log
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.RequestBody.Companion.toRequestBody
import org.json.JSONObject
import java.io.File
import java.security.SecureRandom
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale
import java.util.TimeZone
import java.util.concurrent.TimeUnit

data class WarpConfig(
    val privateKey: String,
    val publicKey: String,
    val peerPublicKey: String = "bmXOC+F1FxEMF9dyiK2H5/1SUtzH0JuVo51h2wPfgyo=",
    val endpointHost: String = "162.159.192.1",
    val endpointPort: Int = 2408,
    val localIpv4: String = "172.16.0.2/32",
    val localIpv6: String? = null,
    val reserved: List<Int> = listOf(0, 0, 0)
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

    private val httpClient = OkHttpClient.Builder()
        .connectTimeout(15, TimeUnit.SECONDS)
        .readTimeout(15, TimeUnit.SECONDS)
        .build()

    suspend fun getOrRegisterConfig(context: Context): WarpConfig = withContext(Dispatchers.IO) {
        val prefs = context.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)
        val savedPriv = prefs.getString(KEY_PRIVATE, null)
        val savedIpv4 = prefs.getString(KEY_IPV4, null)

        if (!savedPriv.isNullOrBlank() && !savedIpv4.isNullOrBlank()) {
            return@withContext WarpConfig(
                privateKey = savedPriv,
                publicKey = prefs.getString(KEY_PUBLIC, "") ?: "",
                peerPublicKey = prefs.getString(KEY_PEER_KEY, "bmXOC+F1FxEMF9dyiK2H5/1SUtzH0JuVo51h2wPfgyo=") ?: "bmXOC+F1FxEMF9dyiK2H5/1SUtzH0JuVo51h2wPfgyo=",
                endpointHost = prefs.getString(KEY_HOST, "162.159.192.1") ?: "162.159.192.1",
                endpointPort = prefs.getInt(KEY_PORT, 2408),
                localIpv4 = savedIpv4,
                localIpv6 = prefs.getString(KEY_IPV6, null)
            )
        }

        // Generate new keypair using Sing-box binary or Curve25519
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
                    val configObj = root.optJSONObject("config")
                    val peersArr = configObj?.optJSONArray("peers")
                    val firstPeer = peersArr?.optJSONObject(0)
                    val peerPub = firstPeer?.optString("public_key", "bmXOC+F1FxEMF9dyiK2H5/1SUtzH0JuVo51h2wPfgyo=") ?: "bmXOC+F1FxEMF9dyiK2H5/1SUtzH0JuVo51h2wPfgyo="
                    
                    val iface = configObj?.optJSONObject("interface")
                    val addrs = iface?.optJSONObject("addresses")
                    val v4 = addrs?.optString("v4", "172.16.0.2") ?: "172.16.0.2"
                    val v6 = addrs?.optString("v6", null)

                    val v4Cidr = if (v4.contains("/")) v4 else "$v4/32"
                    val v6Cidr = if (v6 != null && !v6.contains("/")) "$v6/128" else v6

                    prefs.edit()
                        .putString(KEY_PRIVATE, privKey)
                        .putString(KEY_PUBLIC, pubKey)
                        .putString(KEY_PEER_KEY, peerPub)
                        .putString(KEY_IPV4, v4Cidr)
                        .putString(KEY_IPV6, v6Cidr)
                        .putString(KEY_HOST, "162.159.192.1")
                        .putInt(KEY_PORT, 2408)
                        .apply()

                    Log.i(TAG, "Cloudflare WARP successfully registered: $v4Cidr")
                    return@withContext WarpConfig(
                        privateKey = privKey,
                        publicKey = pubKey,
                        peerPublicKey = peerPub,
                        endpointHost = "162.159.192.1",
                        endpointPort = 2408,
                        localIpv4 = v4Cidr,
                        localIpv6 = v6Cidr
                    )
                }
            }
        } catch (e: Exception) {
            Log.e(TAG, "Registration failed, fallback to default config: ${e.message}")
        }

        // Fallback default config
        WarpConfig(
            privateKey = privKey,
            publicKey = pubKey,
            localIpv4 = "172.16.0.2/32"
        )
    }

    private fun generateWireguardKeyPair(context: Context): Pair<String, String> {
        val nativeDir = context.applicationInfo.nativeLibraryDir
        val singboxFile = File(nativeDir, "libsingbox.so")
        if (singboxFile.exists()) {
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

        // Fallback random 32 bytes
        val random = SecureRandom()
        val privBytes = ByteArray(32)
        random.nextBytes(privBytes)
        privBytes[0] = (privBytes[0].toInt() and 248).toByte()
        privBytes[31] = (privBytes[31].toInt() and 127).toByte()
        privBytes[31] = (privBytes[31].toInt() or 64).toByte()
        val priv = Base64.encodeToString(privBytes, Base64.NO_WRAP)
        return Pair(priv, "ONuQmVS+1MNym5/1iVkt38VKBTZAMP443y0GN/ymq1Y=")
    }
}
