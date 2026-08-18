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
}
