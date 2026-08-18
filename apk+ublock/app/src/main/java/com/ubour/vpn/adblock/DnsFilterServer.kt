package com.ubour.vpn.adblock

import android.util.Log
import kotlinx.coroutines.*
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetAddress
import java.nio.ByteBuffer
import java.util.concurrent.atomic.AtomicBoolean

class DnsFilterServer(
    private var upstreamDns: String = "1.1.1.1",
    private val upstreamPort: Int = 53,
    private val localPort: Int = 5353,
    private val socketProtector: ((DatagramSocket) -> Boolean)? = null
) {
    private val isRunning = AtomicBoolean(false)
    private var serverSocket: DatagramSocket? = null
    private var serverJob: Job? = null
    private val scope = CoroutineScope(Dispatchers.IO + SupervisorJob())

    fun setUpstreamDns(dns: String) {
        upstreamDns = dns
    }

    fun start() {
        if (isRunning.getAndSet(true)) return

        try {
            serverSocket = DatagramSocket(localPort, InetAddress.getByName("127.0.0.1")).apply {
                soTimeout = 2000
            }
            Log.i(TAG, "DNS Filter Server started on 127.0.0.1:$localPort (Upstream: $upstreamDns)")

            serverJob = scope.launch {
                val buffer = ByteArray(4096)
                while (isActive && isRunning.get()) {
                    try {
                        val packet = DatagramPacket(buffer, buffer.size)
                        serverSocket?.receive(packet)

                        val data = packet.data.copyOf(packet.length)
                        val clientAddress = packet.address
                        val clientPort = packet.port

                        launch(Dispatchers.IO) {
                            handleDnsQuery(data, clientAddress, clientPort)
                        }
                    } catch (e: Exception) {
                        // Socket timeout or closed
                    }
                }
            }
        } catch (e: Exception) {
            Log.e(TAG, "Failed to start DNS Filter Server", e)
            isRunning.set(false)
        }
    }

    private fun handleDnsQuery(data: ByteArray, clientAddress: InetAddress, clientPort: Int) {
        try {
            val qname = parseQName(data)
            if (qname != null && AdBlockEngine.isDomainBlocked(qname)) {
                Log.d(TAG, "🚫 Blocked DNS query for: $qname")
                val blockedResponse = createBlockedResponse(data)
                sendPacket(blockedResponse, clientAddress, clientPort)
                return
            }

            // Forward to upstream DNS
            val upstreamSocket = DatagramSocket().apply {
                socketProtector?.invoke(this)
                soTimeout = 3000
            }
            val upstreamPacket = DatagramPacket(data, data.size, InetAddress.getByName(upstreamDns), upstreamPort)
            upstreamSocket.send(upstreamPacket)

            val responseBuffer = ByteArray(4096)
            val responsePacket = DatagramPacket(responseBuffer, responseBuffer.size)
            upstreamSocket.receive(responsePacket)
            upstreamSocket.close()

            val responseData = responsePacket.data.copyOf(responsePacket.length)
            sendPacket(responseData, clientAddress, clientPort)

        } catch (e: Exception) {
            Log.w(TAG, "Error handling DNS query: ${e.message}")
        }
    }

    private fun sendPacket(data: ByteArray, address: InetAddress, port: Int) {
        try {
            val packet = DatagramPacket(data, data.size, address, port)
            serverSocket?.send(packet)
        } catch (e: Exception) {
            Log.w(TAG, "Error sending DNS response: ${e.message}")
        }
    }

    private fun parseQName(data: ByteArray): String? {
        if (data.size < 12) return null
        var offset = 12
        val sb = StringBuilder()

        while (offset < data.size) {
            val len = data[offset].toInt() and 0xFF
            if (len == 0) break
            if (len > 63) return null // Compression in question section is invalid
            offset++

            if (offset + len > data.size) return null
            if (sb.isNotEmpty()) sb.append('.')
            sb.append(String(data, offset, len, Charsets.US_ASCII))
            offset += len
        }

        return if (sb.isNotEmpty()) sb.toString() else null
    }

    private fun createBlockedResponse(query: ByteArray): ByteArray {
        if (query.size < 12) return query

        // Find end of Question section
        var offset = 12
        while (offset < query.size) {
            val len = query[offset].toInt() and 0xFF
            if (len == 0) {
                offset += 5 // Skip 0x00 + QTYPE (2) + QCLASS (2)
                break
            }
            offset += len + 1
        }

        val questionLength = offset.coerceAtMost(query.size)
        val response = ByteBuffer.allocate(questionLength + 16)

        // 1. Transaction ID
        response.put(query[0])
        response.put(query[1])

        // 2. Flags: Standard query response, No error (0x8180)
        response.put(0x81.toByte())
        response.put(0x80.toByte())

        // 3. QDCOUNT (1)
        response.put(0x00.toByte())
        response.put(0x01.toByte())

        // 4. ANCOUNT (1)
        response.put(0x00.toByte())
        response.put(0x01.toByte())

        // 5. NSCOUNT (0), ARCOUNT (0)
        response.putShort(0)
        response.putShort(0)

        // 6. Copy Question Section
        response.put(query, 12, questionLength - 12)

        // 7. Answer Section (Pointer to question name, Type A, Class IN, TTL 300, 0.0.0.0)
        response.put(0xC0.toByte())
        response.put(0x0C.toByte()) // Offset 12 (QNAME pointer)
        response.putShort(0x0001)   // Type A
        response.putShort(0x0001)   // Class IN
        response.putInt(300)        // TTL
        response.putShort(4)        // RDLENGTH (4 bytes)
        response.put(0x00.toByte()) // 0.0.0.0
        response.put(0x00.toByte())
        response.put(0x00.toByte())
        response.put(0x00.toByte())

        return response.array()
    }

    fun stop() {
        isRunning.set(false)
        serverJob?.cancel()
        serverJob = null
        try {
            serverSocket?.close()
        } catch (e: Exception) {
            // Ignore
        }
        serverSocket = null
        Log.i(TAG, "DNS Filter Server stopped")
    }

    companion object {
        private const val TAG = "DnsFilterServer"
    }
}
