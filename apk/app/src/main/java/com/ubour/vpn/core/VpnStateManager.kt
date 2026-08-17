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

data class TrafficStats(
    val rxBytes: Long = 0,
    val txBytes: Long = 0,
    val connectedSince: Long = 0
)

object VpnStateManager {
    private val _state = MutableStateFlow(VpnState.DISCONNECTED)
    val state: StateFlow<VpnState> = _state.asStateFlow()

    private val _stats = MutableStateFlow(TrafficStats())
    val stats: StateFlow<TrafficStats> = _stats.asStateFlow()

    fun updateState(newState: VpnState) {
        _state.value = newState
        if (newState == VpnState.DISCONNECTED) {
            _stats.value = TrafficStats()
        }
    }

    fun updateStats(rx: Long, tx: Long, connectedSince: Long) {
        _stats.value = TrafficStats(rx, tx, connectedSince)
    }
}
