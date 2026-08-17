package com.ubour.vpn.adblock

import android.content.Context
import android.util.Log
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import java.io.BufferedReader
import java.io.File
import java.io.InputStream
import java.io.InputStreamReader
import java.util.concurrent.ConcurrentHashMap
import java.util.concurrent.atomic.AtomicLong

object AdBlockEngine {
    private const val TAG = "AdBlockEngine"

    private val blockedDomains = ConcurrentHashMap.newKeySet<String>()
    private val whiteListDomains = ConcurrentHashMap.newKeySet<String>()
    
    // Built-in DoH providers to prevent Chrome and browsers from bypassing local DNS
    private val dohEndpoints = setOf(
        "dns.google",
        "dns.google.com",
        "cloudflare-dns.com",
        "mozilla.cloudflare-dns.com",
        "dns.quad9.net",
        "doh.opendns.com",
        "dns.nextdns.io",
        "doh.cleanbrowsing.org",
        "dns.alidns.com",
        "doh.pub",
        "sm2.doh.pub"
    )

    private val _totalQueries = AtomicLong(0)
    private val _blockedAds = AtomicLong(0)
    private val _blockedTrackers = AtomicLong(0)

    val totalQueries: Long get() = _totalQueries.get()
    val blockedAds: Long get() = _blockedAds.get()
    val blockedTrackers: Long get() = _blockedTrackers.get()
    val totalRules: Int get() = blockedDomains.size

    @Volatile
    private var isInitialized = false

    suspend fun initialize(context: Context) = withContext(Dispatchers.IO) {
        if (isInitialized) return@withContext
        reloadFromStorage(context)
    }

    suspend fun reloadFromStorage(context: Context) = withContext(Dispatchers.IO) {
        try {
            blockedDomains.clear()
            whiteListDomains.clear()

            // 1. Add DoH endpoints to force browsers into system DNS
            blockedDomains.addAll(dohEndpoints)

            // 2. Check if cached update exists in internal storage
            val cachedFile = File(context.filesDir, "filters_cached.txt")
            val inputStream: InputStream = if (cachedFile.exists() && cachedFile.length() > 0) {
                Log.i(TAG, "Loading rules from cached storage (${cachedFile.length()} bytes)...")
                cachedFile.inputStream()
            } else {
                Log.i(TAG, "Loading baseline rules from assets...")
                context.assets.open("filters/adblock_rules.txt")
            }

            val reader = BufferedReader(InputStreamReader(inputStream))
            var line: String?
            var count = 0
            while (reader.readLine().also { line = it } != null) {
                val trimmed = line?.trim() ?: continue
                if (trimmed.isEmpty() || trimmed.startsWith("#") || trimmed.startsWith("!")) {
                    continue
                }
                
                parseAndAddRule(trimmed)
                count++
            }
            reader.close()
            isInitialized = true
            Log.i(TAG, "Successfully loaded $count rules into AdBlock Engine (Total unique domains: ${blockedDomains.size})")
        } catch (e: Exception) {
            Log.e(TAG, "Error initializing AdBlock Engine", e)
        }
    }

    private fun parseAndAddRule(rule: String) {
        var cleanRule = rule.trim()

        // 1. Whitelist rule: @@||domain^
        if (cleanRule.startsWith("@@||") || cleanRule.startsWith("@@")) {
            val domain = cleanRule.removePrefix("@@||").removePrefix("@@").removeSuffix("^").trim()
            if (domain.isNotEmpty()) {
                whiteListDomains.add(domain.lowercase())
            }
            return
        }

        // 2. uBlock / AdGuard syntax: ||domain.com^
        if (cleanRule.startsWith("||")) {
            val domain = cleanRule.removePrefix("||").removeSuffix("^").trim()
            if (domain.isNotEmpty()) {
                blockedDomains.add(domain.lowercase())
            }
            return
        }

        // 3. Hosts format: 0.0.0.0 domain.com or 127.0.0.1 domain.com
        if (cleanRule.startsWith("0.0.0.0 ") || cleanRule.startsWith("127.0.0.1 ")) {
            val parts = cleanRule.split(Regex("\\s+"))
            if (parts.size >= 2) {
                val domain = parts[1].trim()
                if (domain.isNotEmpty() && domain != "localhost" && domain != "broadcasthost") {
                    blockedDomains.add(domain.lowercase())
                }
            }
            return
        }

        // 4. Plain domain
        val domain = cleanRule.removeSuffix("^").trim()
        if (domain.isNotEmpty() && !domain.contains(" ")) {
            blockedDomains.add(domain.lowercase())
        }
    }

    /**
     * Checks if a domain is blocked by uBlock/AdGuard filter rules.
     * Supports exact match and subdomain hierarchical matching (e.g., sub.ads.google.com -> ads.google.com).
     */
    fun isDomainBlocked(rawDomain: String): Boolean {
        _totalQueries.incrementAndGet()
        val domain = rawDomain.trim().lowercase().removeSuffix(".")
        if (domain.isEmpty()) return false

        // Check Whitelist first
        if (whiteListDomains.contains(domain)) {
            return false
        }

        // Exact match
        if (blockedDomains.contains(domain)) {
            recordBlock(domain)
            return true
        }

        // Hierarchical suffix matching for subdomains
        var current = domain
        while (current.contains(".")) {
            val dotIndex = current.indexOf('.')
            if (dotIndex == -1 || dotIndex == current.length - 1) break
            current = current.substring(dotIndex + 1)
            
            if (blockedDomains.contains(current)) {
                recordBlock(domain)
                return true
            }
        }

        return false
    }

    private fun recordBlock(domain: String) {
        if (domain.contains("track") || domain.contains("analytics") || domain.contains("metric") || domain.contains("telemetry") || domain.contains("adjust") || domain.contains("appsflyer")) {
            _blockedTrackers.incrementAndGet()
        } else {
            _blockedAds.incrementAndGet()
        }
    }

    @JvmStatic
    fun checkDomainFromNative(rawDomain: String): Int {
        val domain = rawDomain.trim().lowercase().removeSuffix(".")
        if (domain.isEmpty()) return 0
        val isBlocked = isDomainBlocked(domain)
        if (isBlocked) {
            val isTracker = domain.contains("track") || domain.contains("analytics") || domain.contains("metric") || domain.contains("telemetry") || domain.contains("adjust") || domain.contains("appsflyer")
            val res = if (isTracker) 2 else 1
            Log.i(TAG, "🚫 Blocked from native: $domain (type=$res, blockedAds=$_blockedAds, blockedTrackers=$_blockedTrackers)")
            return res
        }
        return 0
    }

    fun resetStats() {
        _totalQueries.set(0)
        _blockedAds.set(0)
        _blockedTrackers.set(0)
    }
}
