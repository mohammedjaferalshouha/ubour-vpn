package com.ubour.vpn.warp

import org.junit.Assert.*
import org.junit.Test

class CountryManagerTest {

    @Test
    fun testAvailableLocationsNotEmpty() {
        val locations = CountryManager.AVAILABLE_LOCATIONS
        assertTrue(locations.isNotEmpty())
        assertTrue(locations.size >= 10)
    }

    @Test
    fun testGetLocationByIndex() {
        val first = CountryManager.getLocationByIndex(0)
        assertEquals("auto", first.id)

        val invalidIndex = CountryManager.getLocationByIndex(9999)
        assertEquals(CountryManager.AVAILABLE_LOCATIONS[0].id, invalidIndex.id)

        val negativeIndex = CountryManager.getLocationByIndex(-1)
        assertEquals(CountryManager.AVAILABLE_LOCATIONS[0].id, negativeIndex.id)
    }

    @Test
    fun testServerLocationProperties() {
        val loc = ServerLocation(
            id = "test_loc",
            nameArabic = "موقع تجريبي",
            flag = "🏳️",
            endpointHost = "1.2.3.4",
            endpointPort = 2408
        )
        assertEquals("test_loc", loc.id)
        assertEquals("1.2.3.4", loc.endpointHost)
        assertEquals(2408, loc.endpointPort)
        assertFalse(loc.isCustomVless)
        assertEquals(-1, loc.pingMs)
    }

    @Test
    fun testWarpConfigDefaults() {
        val config = WarpConfig(
            privateKey = "priv123",
            publicKey = "pub123"
        )
        assertEquals("priv123", config.privateKey)
        assertEquals("pub123", config.publicKey)
        assertEquals("162.159.192.1", config.endpointHost)
        assertEquals(2408, config.endpointPort)
        assertEquals(3, config.reserved.size)
    }

    @Test
    fun testWarpCandidateEndpointsPrioritizationAndDeduplication() {
        val defaultList = WarpManager.buildCandidateEndpoints(null, 0)
        assertTrue(defaultList.size >= 10)
        assertEquals("162.159.195.1" to 4500, defaultList.first())
        assertTrue(defaultList.contains("188.114.96.1" to 4500))
        assertTrue(defaultList.contains("162.159.192.1" to 854))
        assertTrue(defaultList.contains("162.159.192.1" to 2408))

        val withLast = WarpManager.buildCandidateEndpoints("188.114.96.1", 4500)
        assertEquals("188.114.96.1" to 4500, withLast.first())
        assertEquals(defaultList.size, withLast.size)

        val withCustom = WarpManager.buildCandidateEndpoints("1.2.3.4", 4500)
        assertEquals("1.2.3.4" to 4500, withCustom.first())
        assertEquals(defaultList.size + 1, withCustom.size)
    }

    @Test
    fun testExpandedWarpEndpointPoolCoverage() {
        assertEquals(7, WarpManager.CANDIDATE_IPS.size)
        assertEquals(43, WarpManager.CANDIDATE_PORTS.size)

        val pool = WarpManager.DEFAULT_WARP_ENDPOINTS
        assertTrue(pool.size >= 70)
        assertEquals(pool.size, pool.distinct().size)

        val hostsInPool = pool.map { it.first }.toSet()
        val portsInPool = pool.map { it.second }.toSet()
        assertTrue(hostsInPool.containsAll(WarpManager.CANDIDATE_IPS))
        assertTrue(portsInPool.containsAll(WarpManager.CANDIDATE_PORTS))

        // First parallel batch of 8 must include all 7 Cloudflare Anycast IPv4 prefixes
        val firstBatchHosts = pool.take(8).map { it.first }.toSet()
        assertTrue(firstBatchHosts.containsAll(WarpManager.CANDIDATE_IPS))
    }

    @Test
    fun testResolveCandidatesForAutoAndManualAndRecovery() {
        val manualEp = "188.114.97.1" to 4500

        val manualStart = WarpManager.resolveCandidatesForMode(
            mode = WarpSelectionMode.MANUAL,
            manualEndpoint = manualEp,
            lastWorkingEndpoint = "162.159.195.1" to 4500,
            isRecovery = false
        )
        assertEquals(listOf(manualEp), manualStart)

        val manualRecovery = WarpManager.resolveCandidatesForMode(
            mode = WarpSelectionMode.MANUAL,
            manualEndpoint = manualEp,
            lastWorkingEndpoint = "162.159.195.1" to 4500,
            deprioritizeEndpoint = manualEp,
            isRecovery = true
        )
        assertTrue(manualRecovery.isEmpty())

        val failedAuto = "162.159.195.1" to 4500
        val autoRecovery = WarpManager.resolveCandidatesForMode(
            mode = WarpSelectionMode.AUTO,
            manualEndpoint = manualEp,
            lastWorkingEndpoint = failedAuto,
            deprioritizeEndpoint = failedAuto,
            isRecovery = true
        )
        assertTrue(autoRecovery.size >= 70)
        assertNotEquals(failedAuto, autoRecovery.first())
        assertEquals(failedAuto, autoRecovery.last())
    }

    @Test
    fun testPortNoteArabicDescriptions() {
        assertTrue(WarpManager.getPortNoteArabic(4500).isNotBlank())
        assertTrue(WarpManager.getPortNoteArabic(854).isNotBlank())
        assertTrue(WarpManager.getPortNoteArabic(2408).isNotBlank())
        assertTrue(WarpManager.getPortNoteArabic(51820).isNotBlank())
        assertTrue(WarpManager.getPortNoteArabic(9999).isNotBlank())
    }
}
