package com.ubour.vpn.core

object NativeEngine {
    init {
        System.loadLibrary("ubour_engine")
    }

    external fun startEngine(params: String, port: Int): Int
    external fun stopEngine()
    external fun startTunnel(tunFd: Int, socksHost: String, socksPort: Int, dnsServer: String): Int
    external fun stopTunnel()
    external fun getTrafficStats(): LongArray?
}
