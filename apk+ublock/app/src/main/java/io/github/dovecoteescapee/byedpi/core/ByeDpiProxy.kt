package io.github.dovecoteescapee.byedpi.core

import android.util.Log
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock

class ByeDpiProxy {
    companion object {
        init {
            System.loadLibrary("byedpi")
        }
        private const val TAG = "UbourVPN_Proxy"
    }

    private val mutex = Mutex()
    private var fd = -1

    suspend fun startProxy(
        mode: Int = 1, // 0 = Direct (No DPI), 1 = Standard Mode 9 (Disorder), 2 = Fast Split, 3 = Fake SNI
        ip: String = "127.0.0.1",
        port: Int = 1080,
    ): Int {
        val socketFd = mutex.withLock {
            if (fd >= 0) {
                Log.w(TAG, "Proxy already running on fd $fd")
                return -1
            }

            val (desyncMethod, splitPos, splitAtHost, _, fakeSni, desyncHttp, desyncHttps) = when (mode) {
                0 -> Tuple7(0, 0, false, false, "", true, true) // Mode 0 = Direct clean SOCKS5 with AdBlock inspection
                2 -> Tuple7(1, 2, false, false, "www.google.com", true, true) // Split pos 2
                3 -> Tuple7(2, 2, true, false, "www.google.com", true, true)  // Advanced Disorder + Host Split (Ultra stable & high bypass)
                else -> Tuple7(2, 1, true, false, "www.google.com", true, true) // Disorder (Mode 9)
            }

            Log.i(TAG, "Creating proxy socket on $ip:$port with mode=$mode, desync=$desyncMethod, http=$desyncHttp, https=$desyncHttps")
            val created = jniCreateSocket(
                ip = ip,
                port = port,
                maxConnections = 512,
                bufferSize = 16384,
                defaultTtl = 0,
                customTtl = false,
                noDomain = false,
                desyncHttp = desyncHttp,
                desyncHttps = desyncHttps,
                desyncUdp = false,
                desyncMethod = desyncMethod,
                splitPosition = splitPos,
                splitAtHost = splitAtHost,
                fakeTtl = 8,
                fakeSni = fakeSni,
                oobChar = 'a'.code.toByte(),
                hostMixedCase = false,
                domainMixedCase = false,
                hostRemoveSpaces = true,
                tlsRecordSplit = (mode != 0),
                tlsRecordSplitPosition = 1,
                tlsRecordSplitAtSni = (mode != 0),
                hostsMode = 0,
                hosts = null,
                tcpFastOpen = false,
                udpFakeCount = 0,
                dropSack = false,
                fakeOffset = 0
            )

            if (created < 0) {
                Log.e(TAG, "Failed to create proxy socket: $created")
                return -1
            }
            this.fd = created
            created
        }

        Log.i(TAG, "Starting proxy event loop on fd $socketFd")
        return jniStartProxy(socketFd)
    }

    suspend fun stopProxy(): Int {
        return mutex.withLock {
            if (fd < 0) return 0
            Log.i(TAG, "Stopping proxy fd $fd")
            val res = jniStopProxy(fd)
            fd = -1
            res
        }
    }

    private data class Tuple7<A, B, C, D, E, F, G>(
        val a: A, val b: B, val c: C, val d: D, val e: E, val f: F, val g: G
    )

    private external fun jniCreateSocket(
        ip: String,
        port: Int,
        maxConnections: Int,
        bufferSize: Int,
        defaultTtl: Int,
        customTtl: Boolean,
        noDomain: Boolean,
        desyncHttp: Boolean,
        desyncHttps: Boolean,
        desyncUdp: Boolean,
        desyncMethod: Int,
        splitPosition: Int,
        splitAtHost: Boolean,
        fakeTtl: Int,
        fakeSni: String,
        oobChar: Byte,
        hostMixedCase: Boolean,
        domainMixedCase: Boolean,
        hostRemoveSpaces: Boolean,
        tlsRecordSplit: Boolean,
        tlsRecordSplitPosition: Int,
        tlsRecordSplitAtSni: Boolean,
        hostsMode: Int,
        hosts: String?,
        tcpFastOpen: Boolean,
        udpFakeCount: Int,
        dropSack: Boolean,
        fakeOffset: Int,
    ): Int

    private external fun jniStartProxy(fd: Int): Int
    private external fun jniStopProxy(fd: Int): Int
}
