package com.ubour.vpn.adblock

import android.content.Context
import android.util.Log
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import okhttp3.OkHttpClient
import okhttp3.Request
import java.io.File
import java.util.concurrent.TimeUnit

object FilterUpdateService {
    private const val TAG = "FilterUpdateService"
    
    // Comprehensive Official AdGuard, uBlock Origin, EasyList & EasyPrivacy filter lists
    private val FILTER_URLS = listOf(
        "https://adguardteam.github.io/HostlistsRegistry/assets/filter_1.txt", // AdGuard DNS Filter (Comprehensive)
        "https://raw.githubusercontent.com/AdguardTeam/FiltersRegistry/master/filters/filter_15_DnsFilter/filter.txt",
        "https://raw.githubusercontent.com/uBlockOrigin/uAssets/master/filters/privacy.txt", // uBlock Privacy & Tracking
        "https://raw.githubusercontent.com/uBlockOrigin/uAssets/master/filters/badware.txt"  // uBlock Malicious & Popups
    )

    private val httpClient = OkHttpClient.Builder()
        .connectTimeout(15, TimeUnit.SECONDS)
        .readTimeout(30, TimeUnit.SECONDS)
        .build()

    data class UpdateResult(
        val success: Boolean,
        val rulesCount: Int = 0,
        val message: String = ""
    )

    suspend fun updateFiltersOnline(context: Context): UpdateResult = withContext(Dispatchers.IO) {
        val targetFile = File(context.filesDir, "filters_cached.txt")
        val combinedRules = StringBuilder()
        var totalLoaded = 0

        for (url in FILTER_URLS) {
            try {
                Log.i(TAG, "Fetching comprehensive rules from: $url")
                val request = Request.Builder()
                    .url(url)
                    .header("User-Agent", "Ubour-AdBlock/2.0")
                    .build()

                httpClient.newCall(request).execute().use { response ->
                    if (response.isSuccessful) {
                        val body = response.body?.string()
                        if (!body.isNullOrBlank()) {
                            combinedRules.append("\n").append(body)
                            val count = body.lines().count { it.isNotBlank() && !it.startsWith("!") && !it.startsWith("#") }
                            totalLoaded += count
                            Log.i(TAG, "Fetched $count rules from $url")
                        }
                    }
                }
            } catch (e: Exception) {
                Log.w(TAG, "Failed to download from $url: ${e.message}")
            }
        }

        if (totalLoaded > 0) {
            targetFile.writeText(combinedRules.toString())
            Log.i(TAG, "Total combined rules written to cache: $totalLoaded")

            // Reload rules into memory
            AdBlockEngine.reloadFromStorage(context)

            val prefs = context.getSharedPreferences("ubour_settings", Context.MODE_PRIVATE)
            prefs.edit().putLong("last_rules_update", System.currentTimeMillis()).apply()

            return@withContext UpdateResult(
                success = true,
                rulesCount = AdBlockEngine.totalRules,
                message = "تم تحديث وتفعيل الحماية القصوى بنجاح (${AdBlockEngine.totalRules} نطاق وقاعدة)"
            )
        }

        UpdateResult(
            success = false,
            rulesCount = AdBlockEngine.totalRules,
            message = "تعذر الاتصال، تم استخدام القواعد المخزنة مسبقاً"
        )
    }

    fun isUpdateNeeded(context: Context): Boolean {
        val prefs = context.getSharedPreferences("ubour_settings", Context.MODE_PRIVATE)
        val lastUpdate = prefs.getLong("last_rules_update", 0L)
        val twentyFourHours = 24 * 60 * 60 * 1000L
        return (System.currentTimeMillis() - lastUpdate) > twentyFourHours
    }
}
