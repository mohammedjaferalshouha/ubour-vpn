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
    
    // 18 Official AdGuard Home, AdGuard, uBlock Origin, OISD, HaGeZi & Steven Black Filter Lists (2.2M+ Comprehensive Rules)
    private val FILTER_URLS = listOf(
        "https://adguardteam.github.io/HostlistsRegistry/assets/filter_1.txt", // 1. AdGuard DNS Filter (Official Comprehensive)
        "https://raw.githubusercontent.com/AdguardTeam/FiltersRegistry/master/filters/filter_15_DnsFilter/filter.txt", // 2. AdGuard Base DNS Filter
        "https://raw.githubusercontent.com/AdguardTeam/FiltersRegistry/master/filters/filter_3_Spyware/filter.txt", // 3. AdGuard Tracking & Spyware Protection
        "https://raw.githubusercontent.com/AdguardTeam/FiltersRegistry/master/filters/filter_17_TrackParam/filter.txt", // 4. AdGuard URL Tracking Parameters
        "https://raw.githubusercontent.com/AdguardTeam/FiltersRegistry/master/filters/filter_11_Mobile/filter.txt", // 5. AdGuard Mobile Ads Filter
        "https://raw.githubusercontent.com/uBlockOrigin/uAssets/master/filters/privacy.txt", // 6. uBlock Origin Privacy
        "https://raw.githubusercontent.com/uBlockOrigin/uAssets/master/filters/badware.txt", // 7. uBlock Origin Badware & Malware
        "https://raw.githubusercontent.com/uBlockOrigin/uAssets/master/filters/quick-fixes.txt", // 8. uBlock Origin Quick Fixes
        "https://raw.githubusercontent.com/uBlockOrigin/uAssets/master/filters/unbreak.txt", // 9. uBlock Origin Unbreak
        "https://adguardteam.github.io/HostlistsRegistry/assets/filter_3.txt", // 10. Peter Lowe's Ad & Tracking Server List
        "https://adguardteam.github.io/HostlistsRegistry/assets/filter_4.txt", // 11. Dan Pollock's Hosts List
        "https://adguardteam.github.io/HostlistsRegistry/assets/filter_8.txt", // 12. NoCoin Filter List (Crypto Miner Protection)
        "https://big.oisd.nl", // 13. OISD Blocklist (Big / Full)
        "https://raw.githubusercontent.com/hagezi/dns-blocklists/main/adblock/pro.plus.txt", // 14. HaGeZi Multi PRO++ DNS Blocklist
        "https://o0.pages.dev/Lite/adblock.txt", // 15. 1Hosts (Lite/Pro) Blocklist
        "https://raw.githubusercontent.com/StevenBlack/hosts/master/hosts", // 16. Steven Black Unified Hosts
        "https://raw.githubusercontent.com/DandelionSprout/adfilt/master/GameConsoleAdblockList.txt", // 17. Dandelion Sprout's Game Console & Smart TV Ads
        "https://adguardteam.github.io/HostlistsRegistry/assets/filter_11.txt" // 18. URLHaus Malicious Domains Filter
    )

    private val httpClient = OkHttpClient.Builder()
        .connectTimeout(15, TimeUnit.SECONDS)
        .readTimeout(45, TimeUnit.SECONDS)
        .build()

    data class UpdateResult(
        val success: Boolean,
        val rulesCount: Int = 0,
        val message: String = ""
    )

    suspend fun updateFiltersOnline(context: Context): UpdateResult = withContext(Dispatchers.IO) {
        val targetFile = File(context.filesDir, "filters_cached.txt")
        val tempFile = File(context.filesDir, "filters_cached.tmp")
        var totalLoaded = 0

        try {
            tempFile.bufferedWriter().use { writer ->
                for (url in FILTER_URLS) {
                    try {
                        Log.i(TAG, "Fetching rules from: $url")
                        val request = Request.Builder()
                            .url(url)
                            .header("User-Agent", "Ubour-AdBlock/2.0")
                            .build()

                        httpClient.newCall(request).execute().use { response ->
                            if (response.isSuccessful) {
                                response.body?.byteStream()?.bufferedReader()?.use { reader ->
                                    var line: String?
                                    var count = 0
                                    while (reader.readLine().also { line = it } != null) {
                                        val trimmed = line?.trim() ?: continue
                                        if (trimmed.isNotBlank() && !trimmed.startsWith("!") && !trimmed.startsWith("#")) {
                                            writer.write(trimmed)
                                            writer.newLine()
                                            count++
                                        }
                                    }
                                    totalLoaded += count
                                    Log.i(TAG, "Fetched $count rules from $url")
                                }
                            }
                        }
                    } catch (e: Exception) {
                        Log.w(TAG, "Failed to download from $url: ${e.message}")
                    }
                }
            }

            if (totalLoaded > 0) {
                if (targetFile.exists()) targetFile.delete()
                tempFile.renameTo(targetFile)
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
            } else {
                tempFile.delete()
            }
        } catch (e: Exception) {
            Log.e(TAG, "Error updating filters online", e)
            tempFile.delete()
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
