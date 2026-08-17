package com.ubour.vpn.service

import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import okhttp3.OkHttpClient
import okhttp3.Request
import org.json.JSONArray
import java.util.concurrent.TimeUnit

data class UpdateInfo(
    val hasUpdate: Boolean,
    val latestVersion: String?,
    val releaseUrl: String?,
    val releaseNotes: String?
)

object UpdateService {
    private const val ENGINE_RELEASE_API = "https://api.github.com/repos/ValdikSS/GoodbyeDPI/releases"
    private const val CURRENT_ENGINE_VERSION = "0.2.3rc3"

    private val client = OkHttpClient.Builder()
        .connectTimeout(10, TimeUnit.SECONDS)
        .readTimeout(10, TimeUnit.SECONDS)
        .build()

    suspend fun checkEngineUpdate(): UpdateInfo = withContext(Dispatchers.IO) {
        try {
            val request = Request.Builder()
                .url(ENGINE_RELEASE_API)
                .header("User-Agent", "Ubour-Android-App/1.0.0")
                .header("Accept", "application/vnd.github.v3+json")
                .build()

            client.newCall(request).execute().use { response ->
                if (!response.isSuccessful) {
                    return@withContext UpdateInfo(false, null, null, null)
                }

                val body = response.body?.string() ?: return@withContext UpdateInfo(false, null, null, null)
                val jsonArray = JSONArray(body)

                if (jsonArray.length() == 0) {
                    return@withContext UpdateInfo(false, null, null, null)
                }

                val latest = jsonArray.getJSONObject(0)
                val tagName = latest.optString("tag_name", "")
                val htmlUrl = latest.optString("html_url", "")
                val bodyText = latest.optString("body", "")

                val hasUpdate = !tagName.contains(CURRENT_ENGINE_VERSION, ignoreCase = true) && tagName.isNotEmpty()

                UpdateInfo(
                    hasUpdate = hasUpdate,
                    latestVersion = tagName,
                    releaseUrl = htmlUrl,
                    releaseNotes = bodyText
                )
            }
        } catch (e: Exception) {
            UpdateInfo(false, null, null, null)
        }
    }
}
