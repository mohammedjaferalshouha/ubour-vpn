package com.ubour.vpn.warp

import org.junit.Assert.*
import org.junit.Test

class WarpNetworkSelectorTest {

    @Test
    fun testSelectPrimaryEmptyReturnsNull() {
        assertNull(WarpNetworkSelector.selectPrimary(emptyList()))
    }

    @Test
    fun testSelectPrimarySingleWifi() {
        val wifi = PhysicalNetworkCandidate(handle = 100L, type = WarpNetworkType.WIFI, isValidated = true)
        assertEquals(wifi, WarpNetworkSelector.selectPrimary(listOf(wifi)))
    }

    @Test
    fun testSelectPrimaryPrefersWifiOverCellularWhenBothValidated() {
        val cellular = PhysicalNetworkCandidate(handle = 200L, type = WarpNetworkType.CELLULAR, isValidated = true)
        val wifi = PhysicalNetworkCandidate(handle = 100L, type = WarpNetworkType.WIFI, isValidated = true)
        assertEquals(wifi, WarpNetworkSelector.selectPrimary(listOf(cellular, wifi)))
    }

    @Test
    fun testSelectPrimaryKeepsValidatedCellularUntilNewWifiValidates() {
        val cellular = PhysicalNetworkCandidate(handle = 200L, type = WarpNetworkType.CELLULAR, isValidated = true)
        val unvalidatedWifi = PhysicalNetworkCandidate(handle = 300L, type = WarpNetworkType.WIFI, isValidated = false)
        assertEquals(cellular, WarpNetworkSelector.selectPrimary(listOf(cellular, unvalidatedWifi)))

        val validatedWifi = unvalidatedWifi.copy(isValidated = true)
        assertEquals(validatedWifi, WarpNetworkSelector.selectPrimary(listOf(cellular, validatedWifi)))
    }

    @Test
    fun testSelectPrimaryFallsBackToCellularWhenWifiLost() {
        val cellular = PhysicalNetworkCandidate(handle = 200L, type = WarpNetworkType.CELLULAR, isValidated = false)
        assertEquals(cellular, WarpNetworkSelector.selectPrimary(listOf(cellular)))
    }

    @Test
    fun testSelectPrimaryPrefersEthernetAndNewerHandleTieBreaker() {
        val wifiOld = PhysicalNetworkCandidate(handle = 100L, type = WarpNetworkType.WIFI, isValidated = true)
        val wifiNew = PhysicalNetworkCandidate(handle = 150L, type = WarpNetworkType.WIFI, isValidated = true)
        assertEquals(wifiNew, WarpNetworkSelector.selectPrimary(listOf(wifiOld, wifiNew)))

        val eth = PhysicalNetworkCandidate(handle = 50L, type = WarpNetworkType.ETHERNET, isValidated = true)
        assertEquals(eth, WarpNetworkSelector.selectPrimary(listOf(wifiNew, eth)))
    }

    @Test
    fun testDecideTransitionAction() {
        val epA = "162.159.195.1" to 4500
        val epB = "188.114.96.1" to 4500

        assertEquals(
            EndpointTransitionAction.RECOVER_FAILED,
            WarpNetworkSelector.decideTransitionAction(-1L, epA, epB)
        )
        assertEquals(
            EndpointTransitionAction.RECOVER_FAILED,
            WarpNetworkSelector.decideTransitionAction(0L, epA, null)
        )
        assertEquals(
            EndpointTransitionAction.OPTIMIZE_DEGRADED,
            WarpNetworkSelector.decideTransitionAction(600L, epA, null)
        )
        assertEquals(
            EndpointTransitionAction.CHECK_REMEMBERED_ENDPOINT,
            WarpNetworkSelector.decideTransitionAction(250L, epA, epB)
        )
        assertEquals(
            EndpointTransitionAction.KEEP_CURRENT,
            WarpNetworkSelector.decideTransitionAction(250L, epA, epA)
        )
        assertEquals(
            EndpointTransitionAction.KEEP_CURRENT,
            WarpNetworkSelector.decideTransitionAction(250L, epA, null)
        )
        assertEquals(
            EndpointTransitionAction.KEEP_CURRENT,
            WarpNetworkSelector.decideTransitionAction(90L, epA, epB)
        )
    }

    @Test
    fun testIsWorthSwitching() {
        assertFalse(WarpNetworkSelector.isWorthSwitching(200L, -1L))
        assertFalse(WarpNetworkSelector.isWorthSwitching(200L, 0L))
        assertTrue(WarpNetworkSelector.isWorthSwitching(-1L, 180L))

        // Degraded current (> 450ms)
        assertTrue(WarpNetworkSelector.isWorthSwitching(600L, 290L))
        assertTrue(WarpNetworkSelector.isWorthSwitching(600L, 500L))
        assertFalse(WarpNetworkSelector.isWorthSwitching(600L, 550L))

        // Acceptable current (<= 450ms) requires > 60ms improvement
        assertTrue(WarpNetworkSelector.isWorthSwitching(250L, 150L))
        assertFalse(WarpNetworkSelector.isWorthSwitching(250L, 210L))
    }
}
