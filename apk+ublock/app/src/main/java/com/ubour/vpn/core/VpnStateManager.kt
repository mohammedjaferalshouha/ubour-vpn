package com.ubour.vpn.core

import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

enum class VpnState {
    DISCONNECTED,
    CONNECTING,
    CONNECTED,
    DISCONNECTING
}

enum class AppOperationMode {
    WARP_AND_ADBLOCK, // Cloudflare WARP + AdBlock (Global Cloud Tunnel)
    VPN_AND_ADBLOCK,  // Direct ByeDPI Bypass + AdBlock (Direct 100% Line Speed)
    ADBLOCK_ONLY,     // AdBlock Only (Low power DNS filtering)
    VPN_ONLY,         // Direct ByeDPI Bypass Only
    CUSTOM_VLESS      // VLESS Reality / Custom Server
}

data class TrafficStats(
    val rxBytes: Long = 0,
    val txBytes: Long = 0,
    val connectedSince: Long = 0,
    val blockedAds: Long = 0,
    val blockedTrackers: Long = 0,
    val totalRules: Int = 0
)

object VpnStateManager {
    private val _state = MutableStateFlow(VpnState.DISCONNECTED)
    val state: StateFlow<VpnState> = _state.asStateFlow()

    private val _stats = MutableStateFlow(TrafficStats())
    val stats: StateFlow<TrafficStats> = _stats.asStateFlow()

    private val _currentMode = MutableStateFlow(AppOperationMode.WARP_AND_ADBLOCK)
    val currentMode: StateFlow<AppOperationMode> = _currentMode.asStateFlow()

    fun setOperationMode(mode: AppOperationMode) {
        _currentMode.value = mode
    }

    fun updateState(newState: VpnState) {
        _state.value = newState
        if (newState == VpnState.DISCONNECTED) {
            _stats.value = TrafficStats()
        }
    }

    fun updateStats(
        rx: Long,
        tx: Long,
        connectedSince: Long,
        blockedAds: Long = 0,
        blockedTrackers: Long = 0,
        totalRules: Int = 0
    ) {
        _stats.value = TrafficStats(rx, tx, connectedSince, blockedAds, blockedTrackers, totalRules)
    }
}
