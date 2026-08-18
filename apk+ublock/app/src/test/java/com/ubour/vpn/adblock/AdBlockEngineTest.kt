package com.ubour.vpn.adblock

import org.junit.Assert.*
import org.junit.Before
import org.junit.Test

class AdBlockEngineTest {

    @Before
    fun setUp() {
        AdBlockEngine.loadRulesForTesting(
            listOf(
                "||doubleclick.net^",
                "||ads.example.com^",
                "0.0.0.0 tracking.com",
                "@@||whitelist-ads.com^"
            )
        )
        AdBlockEngine.resetStats()
    }

    @Test
    fun testDoHEndpointsAreBlockedByDefault() {
        // Built-in DoH providers should be blocked to prevent bypassing local DNS
        assertTrue(AdBlockEngine.isDomainBlocked("dns.google"))
        assertTrue(AdBlockEngine.isDomainBlocked("cloudflare-dns.com"))
        assertTrue(AdBlockEngine.isDomainBlocked("dns.quad9.net"))
        assertTrue(AdBlockEngine.isDomainBlocked("sub.dns.google"))
    }

    @Test
    fun testCleanDomainChecks() {
        assertFalse(AdBlockEngine.isDomainBlocked(""))
        assertFalse(AdBlockEngine.isDomainBlocked("   "))
        assertFalse(AdBlockEngine.isDomainBlocked("google.com"))
        assertFalse(AdBlockEngine.isDomainBlocked("github.com"))
        assertFalse(AdBlockEngine.isDomainBlocked("wikipedia.org"))
    }

    @Test
    fun testSubdomainHierarchicalMatching() {
        // dns.google is in blocked list
        assertTrue(AdBlockEngine.isDomainBlocked("dns.google"))
        assertTrue(AdBlockEngine.isDomainBlocked("api.dns.google"))
        assertTrue(AdBlockEngine.isDomainBlocked("v1.api.dns.google"))
        
        // Non-blocked base domains should not match
        assertFalse(AdBlockEngine.isDomainBlocked("google.com"))
        assertFalse(AdBlockEngine.isDomainBlocked("mygoogle.com"))
    }

    @Test
    fun testCheckDomainFromNativeReturnsCorrectCode() {
        // Non-blocked
        assertEquals(0, AdBlockEngine.checkDomainFromNative("example.com"))
        assertEquals(0, AdBlockEngine.checkDomainFromNative(""))

        // Blocked standard ad/doh domain (type = 1)
        val res = AdBlockEngine.checkDomainFromNative("dns.google")
        assertTrue(res == 1 || res == 2)
    }

    @Test
    fun testStatsTrackingAndReset() {
        AdBlockEngine.resetStats()
        assertEquals(0L, AdBlockEngine.totalQueries)

        AdBlockEngine.isDomainBlocked("example.com")
        assertEquals(1L, AdBlockEngine.totalQueries)

        AdBlockEngine.isDomainBlocked("dns.google")
        AdBlockEngine.resetStats()
        assertEquals(0L, AdBlockEngine.totalQueries)
        assertEquals(0L, AdBlockEngine.blockedAds)
        assertEquals(0L, AdBlockEngine.blockedTrackers)
    }

    @Test
    fun testCustomRulesAndWhitelist() {
        assertTrue(AdBlockEngine.isDomainBlocked("doubleclick.net"))
        assertTrue(AdBlockEngine.isDomainBlocked("sub.doubleclick.net"))
        assertTrue(AdBlockEngine.isDomainBlocked("ads.example.com"))
        assertTrue(AdBlockEngine.isDomainBlocked("tracking.com"))

        // Whitelisted domain
        assertFalse(AdBlockEngine.isDomainBlocked("whitelist-ads.com"))
    }
}
