package com.ubour.vpn.service

import android.content.Context
import android.content.Intent
import android.net.Uri
import android.util.Log
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import okhttp3.OkHttpClient
import okhttp3.Request
import org.json.JSONArray
import org.json.JSONObject
import java.util.concurrent.TimeUnit

data class UpdateInfo(
    val hasUpdate: Boolean,
    val latestVersion: String?,
    val downloadUrl: String?,
    val releaseNotes: String?,
    val releasePageUrl: String?
)

object UpdateService {
    private const val TAG = "UpdateService"
    private const val CURRENT_VERSION = "1.0.0"
    private const val GITHUB_API_RELEASES = "https://api.github.com/repos/mohammedjaferalshouha/ubour-vpn/releases"

    private val client = OkHttpClient.Builder()
        .connectTimeout(10, TimeUnit.SECONDS)
        .readTimeout(10, TimeUnit.SECONDS)
        .build()

    suspend fun checkForAppUpdate(context: Context? = null): UpdateInfo = withContext(Dispatchers.IO) {
        try {
            val request = Request.Builder()
                .url(GITHUB_API_RELEASES)
                .header("User-Agent", "Ubour-Android-App/$CURRENT_VERSION")
                .header("Accept", "application/vnd.github.v3+json")
                .build()

            client.newCall(request).execute().use { response ->
                if (!response.isSuccessful) {
                    Log.w(TAG, "Failed to check update: HTTP ${response.code}")
                    return@withContext UpdateInfo(false, null, null, null, null)
                }

                val body = response.body?.string() ?: return@withContext UpdateInfo(false, null, null, null, null)
                val jsonArray = JSONArray(body)

                if (jsonArray.length() == 0) {
                    return@withContext UpdateInfo(false, null, null, null, null)
                }

                val latest = jsonArray.getJSONObject(0)
                val tagName = latest.optString("tag_name", "").trimStart('v', 'V')
                val htmlUrl = latest.optString("html_url", "")
                val bodyText = latest.optString("body", "")
                
                // Find apk download url if attached
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

                val hasUpdate = isNewerVersion(tagName, CURRENT_VERSION)

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
            UpdateInfo(false, null, null, null, null)
        }
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
