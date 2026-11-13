package com.ubour.vpn.adblock

import android.content.Context
import android.util.Log
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import java.io.BufferedReader
import java.io.File
import java.io.InputStream
import java.io.InputStreamReader
import java.util.Arrays
import java.util.concurrent.atomic.AtomicLong

object AdBlockEngine {
    private const val TAG = "AdBlockEngine"

    // 64-bit FNV-1a Hash Constants
    private const val FNV_OFFSET_BASIS = -3750763034362895579L // 0xcbf29ce484222325L
    private const val FNV_PRIME = 1099511628211L // 0x100000001b3L

    // Flat sorted primitive LongArray storing 64-bit domain hashes (~17MB for 2.2M rules!)
    @Volatile
    private var blockedHashes: LongArray = LongArray(0)

    @Volatile
    private var whiteListHashes: LongArray = LongArray(0)

    @Volatile
    private var dohHashes: LongArray = LongArray(0)

    // Built-in DoH providers to prevent Chrome and browsers from bypassing local DNS
    private val dohEndpoints = listOf(
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
    val totalRules: Int get() = blockedHashes.size

    @Volatile
    var isAdBlockActive = false

    @Volatile
    var isInitialized = false
        private set

    init {
        val dohList = dohEndpoints.map { hashDomain(it) }.sorted().toLongArray()
        dohHashes = dohList
    }

    /**
     * Computes 64-bit FNV-1a hash of a lowercase domain string with zero allocations.
     */
    fun hashDomain(domain: String): Long {
        var hash = FNV_OFFSET_BASIS
        for (i in 0 until domain.length) {
            val c = domain[i].code.toLong()
            hash = hash xor c
            hash = hash * FNV_PRIME
        }
        return hash
    }

    fun loadRulesForTesting(rules: List<String> = emptyList()) {
        val blockedList = ArrayList<Long>(rules.size + dohEndpoints.size)
        val whiteList = ArrayList<Long>()

        for (d in dohEndpoints) {
            blockedList.add(hashDomain(d))
        }

        for (r in rules) {
            parseRule(r, blockedList, whiteList)
        }

        blockedHashes = prepareSortedArray(blockedList)
        whiteListHashes = prepareSortedArray(whiteList)
        isInitialized = true
        isAdBlockActive = true
    }

    suspend fun initialize(context: Context) = withContext(Dispatchers.IO) {
        if (isInitialized && blockedHashes.isNotEmpty()) return@withContext
        reloadFromStorage(context)
    }

    suspend fun reloadFromStorage(context: Context) = withContext(Dispatchers.IO) {
        try {
            val blockedList = ArrayList<Long>(500_000)
            val whiteList = ArrayList<Long>(10_000)

            // 1. Add DoH endpoints
            for (d in dohEndpoints) {
                blockedList.add(hashDomain(d))
            }

            // 2. Check if cached update exists in internal storage
            val cachedFile = File(context.filesDir, "filters_cached.txt")
            val inputStream: InputStream = if (cachedFile.exists() && cachedFile.length() > 0) {
                Log.i(TAG, "Loading rules from cached storage (${cachedFile.length()} bytes)...")
                cachedFile.inputStream()
            } else {
                Log.i(TAG, "Loading baseline rules from assets...")
                context.assets.open("filters/adblock_rules.txt")
            }

            val reader = BufferedReader(InputStreamReader(inputStream), 65536)
            var line: String?
            var count = 0
            while (reader.readLine().also { line = it } != null) {
                val trimmed = line?.trim() ?: continue
                if (trimmed.isEmpty() || trimmed.startsWith("#") || trimmed.startsWith("!")) {
                    continue
                }
                parseRule(trimmed, blockedList, whiteList)
                count++
            }
            reader.close()

            blockedHashes = prepareSortedArray(blockedList)
            whiteListHashes = prepareSortedArray(whiteList)
            isInitialized = true
            Log.i(TAG, "Successfully loaded $count rules into AdBlock Engine (Total unique domains: ${blockedHashes.size})")
        } catch (e: Exception) {
            Log.e(TAG, "Error initializing AdBlock Engine", e)
        }
    }

    private fun parseRule(rule: String, blockedList: ArrayList<Long>, whiteList: ArrayList<Long>) {
        val cleanRule = rule.trim()

        // 1. Whitelist rule: @@||domain^
        if (cleanRule.startsWith("@@||") || cleanRule.startsWith("@@")) {
            val domain = cleanRule.removePrefix("@@||").removePrefix("@@").removeSuffix("^").trim()
            if (domain.isNotEmpty()) {
                whiteList.add(hashDomain(domain.lowercase()))
            }
            return
        }

        // 2. uBlock / AdGuard syntax: ||domain.com^
        if (cleanRule.startsWith("||")) {
            val domain = cleanRule.removePrefix("||").removeSuffix("^").trim()
            if (domain.isNotEmpty()) {
                blockedList.add(hashDomain(domain.lowercase()))
            }
            return
        }

        // 3. Hosts format: 0.0.0.0 domain.com or 127.0.0.1 domain.com
        if (cleanRule.startsWith("0.0.0.0 ") || cleanRule.startsWith("127.0.0.1 ")) {
            val parts = cleanRule.split(Regex("\\s+"))
            if (parts.size >= 2) {
                val domain = parts[1].trim()
                if (domain.isNotEmpty() && domain != "localhost" && domain != "broadcasthost") {
                    blockedList.add(hashDomain(domain.lowercase()))
                }
            }
            return
        }

        // 4. Plain domain
        val domain = cleanRule.removeSuffix("^").trim()
        if (domain.isNotEmpty() && !domain.contains(" ") && !domain.contains("/")) {
            blockedList.add(hashDomain(domain.lowercase()))
        }
    }

    private fun prepareSortedArray(list: ArrayList<Long>): LongArray {
        if (list.isEmpty()) return LongArray(0)
        val arr = list.toLongArray()
        Arrays.sort(arr)
        
        // Deduplicate in-place
        var uniqueCount = 1
        for (i in 1 until arr.size) {
            if (arr[i] != arr[i - 1]) {
                arr[uniqueCount++] = arr[i]
            }
        }
        return arr.copyOf(uniqueCount)
    }

    /**
     * Checks if a domain is blocked by uBlock/AdGuard filter rules.
     * Supports exact match and subdomain hierarchical matching in O(log N) nanosecond time.
     */
    fun isDomainBlocked(rawDomain: String): Boolean {
        if (!isAdBlockActive) return false
        _totalQueries.incrementAndGet()
        val domain = rawDomain.trim().lowercase().removeSuffix(".")
        if (domain.isEmpty()) return false

        val localBlocked = blockedHashes
        val localWhiteList = whiteListHashes

        // Check Whitelist first
        if (localWhiteList.isNotEmpty()) {
            val domainHash = hashDomain(domain)
            if (Arrays.binarySearch(localWhiteList, domainHash) >= 0) {
                return false
            }
        }

        // Essential service domains and user content (Avatars, Profile images, CDN streams)
        if (domain.contains("googleusercontent.com") || 
            domain.contains("gstatic.com") || 
            domain.contains("ggpht.com") || 
            domain.contains("ytimg.com") ||
            domain.contains("tiktokcdn.com") ||
            domain.contains("byteoversea.com") ||
            domain.contains("ibytedtos.com")) {
            return false
        }

        if (localBlocked.isEmpty()) return false

        // Exact match
        val domainHash = hashDomain(domain)
        if (Arrays.binarySearch(localBlocked, domainHash) >= 0) {
            recordBlock(domain)
            return true
        }

        // Hierarchical suffix matching for subdomains
        var current = domain
        while (current.contains(".")) {
            val dotIndex = current.indexOf('.')
            if (dotIndex == -1 || dotIndex == current.length - 1) break
            current = current.substring(dotIndex + 1)
            
            val subHash = hashDomain(current)
            if (Arrays.binarySearch(localBlocked, subHash) >= 0) {
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
        if (!isAdBlockActive) return 0
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
