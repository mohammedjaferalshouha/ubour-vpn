package com.ubour.vpn.service

import com.ubour.vpn.BuildConfig
import android.content.Context
import android.util.Log
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.async
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.withContext
import okhttp3.OkHttpClient
import okhttp3.Request
import org.json.JSONArray
import java.util.concurrent.TimeUnit

data class UpdateInfo(
    val hasUpdate: Boolean,
    val latestVersion: String?,
    val downloadUrl: String?,
    val releaseNotes: String?,
    val releasePageUrl: String?
)

data class UpstreamComponent(
    val name: String,
    val repo: String,
    val currentVersion: String,
    val latestVersion: String?,
    val isUpToDate: Boolean
)

data class FullSystemUpdateStatus(
    val appUpdate: UpdateInfo,
    val upstreams: List<UpstreamComponent>
)

object UpdateService {
    private const val TAG = "UpdateService"
    val CURRENT_APP_VERSION: String
        get() = BuildConfig.VERSION_NAME

    private const val CURRENT_SINGBOX_VERSION = "1.14.0"
    private const val CURRENT_BYEDPI_VERSION = "0.17.3"
    private const val CURRENT_GOODBYEDPI_VERSION = "0.2.2"

    fun getAppVersion(context: Context? = null): String {
        return try {
            if (context != null) {
                val pInfo = context.packageManager.getPackageInfo(context.packageName, 0)
                pInfo.versionName ?: BuildConfig.VERSION_NAME
            } else {
                BuildConfig.VERSION_NAME
            }
        } catch (_: Exception) {
            BuildConfig.VERSION_NAME
        }
    }

    private val client = OkHttpClient.Builder()
        .connectTimeout(10, TimeUnit.SECONDS)
        .readTimeout(10, TimeUnit.SECONDS)
        .build()

    suspend fun checkForAppUpdate(context: Context? = null): UpdateInfo = withContext(Dispatchers.IO) {
        val currentVer = getAppVersion(context)
        val apiUrl = "https://api.github.com/repos/mohammedjaferalshouha/ubour-vpn/releases"
        try {
            val request = Request.Builder()
                .url(apiUrl)
                .header("User-Agent", "Ubour-Android-App/$currentVer")
                .header("Accept", "application/vnd.github.v3+json")
                .build()

            client.newCall(request).execute().use { response ->
                if (!response.isSuccessful) {
                    return@withContext UpdateInfo(false, currentVer, null, null, null)
                }

                val body = response.body?.string() ?: return@withContext UpdateInfo(false, currentVer, null, null, null)
                val jsonArray = JSONArray(body)

                if (jsonArray.length() == 0) {
                    return@withContext UpdateInfo(false, currentVer, null, null, null)
                }

                val latest = jsonArray.getJSONObject(0)
                val tagName = latest.optString("tag_name", "").trimStart('v', 'V')
                val htmlUrl = latest.optString("html_url", "")
                val bodyText = latest.optString("body", "")
                
                var apkUrl: String? = null
                val assets = latest.optJSONArray("assets")
                if (assets != null) {
                    for (i in 0 until assets.length()) {
                        val asset = assets.getJSONObject(i)
                        val name = asset.optString("name", "")
                        if (name.endsWith(".apk", ignoreCase = true)) {
                            apkUrl = asset.optString("browser_download_url", "")
                            break
                        }
                    }
                }

                val hasUpdate = isNewerVersion(tagName, currentVer)

                UpdateInfo(
                    hasUpdate = hasUpdate,
                    latestVersion = tagName,
                    downloadUrl = apkUrl ?: htmlUrl,
                    releaseNotes = bodyText,
                    releasePageUrl = htmlUrl
                )
            }
        } catch (e: Exception) {
            Log.e(TAG, "Error checking update: ${e.message}")
            UpdateInfo(false, currentVer, null, null, null)
        }
    }

    suspend fun checkAllSystemUpstreams(context: Context? = null): FullSystemUpdateStatus = coroutineScope {
        val appUpdateDeferred = async { checkForAppUpdate(context) }
        val singboxDeferred = async { fetchLatestReleaseTag("SagerNet/sing-box") }
        val byedpiDeferred = async { fetchLatestReleaseTag("hufrea/byedpi") }
        val goodbyedpiDeferred = async { fetchLatestReleaseTag("ValdikSS/GoodbyeDPI") }

        val appUpdate = appUpdateDeferred.await()
        val latestSingbox = singboxDeferred.await() ?: CURRENT_SINGBOX_VERSION
        val latestByedpi = byedpiDeferred.await() ?: CURRENT_BYEDPI_VERSION
        val latestGoodbyeDpi = goodbyedpiDeferred.await() ?: CURRENT_GOODBYEDPI_VERSION

        val list = listOf(
            UpstreamComponent(
                name = "محرك Sing-box & WARP",
                repo = "SagerNet/sing-box",
                currentVersion = CURRENT_SINGBOX_VERSION,
                latestVersion = latestSingbox,
                isUpToDate = !isNewerVersion(latestSingbox, CURRENT_SINGBOX_VERSION)
            ),
            UpstreamComponent(
                name = "محرك ByeDPI الأساسي",
                repo = "hufrea/byedpi",
                currentVersion = CURRENT_BYEDPI_VERSION,
                latestVersion = latestByedpi,
                isUpToDate = !isNewerVersion(latestByedpi, CURRENT_BYEDPI_VERSION)
            ),
            UpstreamComponent(
                name = "محرك GoodbyeDPI لويندوز",
                repo = "ValdikSS/GoodbyeDPI",
                currentVersion = CURRENT_GOODBYEDPI_VERSION,
                latestVersion = latestGoodbyeDpi,
                isUpToDate = !isNewerVersion(latestGoodbyeDpi, CURRENT_GOODBYEDPI_VERSION)
            )
        )

        FullSystemUpdateStatus(appUpdate, list)
    }

    private suspend fun fetchLatestReleaseTag(repo: String): String? = withContext(Dispatchers.IO) {
        try {
            val url = "https://api.github.com/repos/$repo/releases/latest"
            val request = Request.Builder()
                .url(url)
                .header("User-Agent", "Ubour-Android-App/$CURRENT_APP_VERSION")
                .header("Accept", "application/vnd.github.v3+json")
                .build()

            client.newCall(request).execute().use { response ->
                if (response.isSuccessful) {
                    val body = response.body?.string() ?: return@withContext null
                    val json = org.json.JSONObject(body)
                    return@withContext json.optString("tag_name", "").trimStart('v', 'V')
                }
            }
        } catch (_: Exception) {}
        null
    }

    private fun isNewerVersion(latest: String, current: String): Boolean {
        if (latest.isBlank() || current.isBlank()) return false
        try {
            val latestParts = latest.split(".").map { it.filter { ch -> ch.isDigit() }.toIntOrNull() ?: 0 }
            val currentParts = current.split(".").map { it.filter { ch -> ch.isDigit() }.toIntOrNull() ?: 0 }
            val maxLen = maxOf(latestParts.size, currentParts.size)
            for (i in 0 until maxLen) {
                val l = latestParts.getOrElse(i) { 0 }
                val c = currentParts.getOrElse(i) { 0 }
                if (l > c) return true
                if (l < c) return false
            }
        } catch (e: Exception) {
            return latest != current
        }
        return false
    }
}
