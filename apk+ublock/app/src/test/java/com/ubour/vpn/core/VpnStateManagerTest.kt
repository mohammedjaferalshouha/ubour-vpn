package com.ubour.vpn.core

import org.junit.Assert.*
import org.junit.Before
import org.junit.Test

class VpnStateManagerTest {

    @Before
    fun setUp() {
        VpnStateManager.updateState(VpnState.DISCONNECTED)
    }

    @Test
    fun testInitialState() {
        assertEquals(VpnState.DISCONNECTED, VpnStateManager.state.value)
        assertEquals(0L, VpnStateManager.stats.value.rxBytes)
        assertEquals(0L, VpnStateManager.stats.value.txBytes)
    }

    @Test
    fun testStateTransitions() {
        VpnStateManager.updateState(VpnState.CONNECTING)
        assertEquals(VpnState.CONNECTING, VpnStateManager.state.value)

        VpnStateManager.updateState(VpnState.CONNECTED)
        assertEquals(VpnState.CONNECTED, VpnStateManager.state.value)

        VpnStateManager.updateState(VpnState.DISCONNECTING)
        assertEquals(VpnState.DISCONNECTING, VpnStateManager.state.value)

        VpnStateManager.updateState(VpnState.DISCONNECTED)
        assertEquals(VpnState.DISCONNECTED, VpnStateManager.state.value)
    }

    @Test
    fun testOperationModes() {
        VpnStateManager.setOperationMode(AppOperationMode.WARP_AND_ADBLOCK)
        assertEquals(AppOperationMode.WARP_AND_ADBLOCK, VpnStateManager.currentMode.value)

        VpnStateManager.setOperationMode(AppOperationMode.VPN_AND_ADBLOCK)
        assertEquals(AppOperationMode.VPN_AND_ADBLOCK, VpnStateManager.currentMode.value)

        VpnStateManager.setOperationMode(AppOperationMode.ADBLOCK_ONLY)
        assertEquals(AppOperationMode.ADBLOCK_ONLY, VpnStateManager.currentMode.value)

        VpnStateManager.setOperationMode(AppOperationMode.VPN_ONLY)
        assertEquals(AppOperationMode.VPN_ONLY, VpnStateManager.currentMode.value)

        VpnStateManager.setOperationMode(AppOperationMode.CUSTOM_VLESS)
        assertEquals(AppOperationMode.CUSTOM_VLESS, VpnStateManager.currentMode.value)
    }

    @Test
    fun testUpdateStatsAndResetOnDisconnect() {
        val now = System.currentTimeMillis()
        VpnStateManager.updateState(VpnState.CONNECTED)
        VpnStateManager.updateStats(
            rx = 1024L,
            tx = 2048L,
            connectedSince = now,
            blockedAds = 15,
            blockedTrackers = 5,
            totalRules = 3500
        )

        val stats = VpnStateManager.stats.value
        assertEquals(1024L, stats.rxBytes)
        assertEquals(2048L, stats.txBytes)
        assertEquals(now, stats.connectedSince)
        assertEquals(15L, stats.blockedAds)
        assertEquals(5L, stats.blockedTrackers)
        assertEquals(3500, stats.totalRules)

        // On disconnect, stats should reset
        VpnStateManager.updateState(VpnState.DISCONNECTED)
        val resetStats = VpnStateManager.stats.value
        assertEquals(0L, resetStats.rxBytes)
        assertEquals(0L, resetStats.txBytes)
    }
}
