package com.ubour.vpn.adblock

import android.util.Log
import kotlinx.coroutines.*
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetAddress
import java.nio.ByteBuffer
import java.util.concurrent.atomic.AtomicBoolean

class DnsFilterServer(
    private var upstreamDns: String = "8.8.8.8",
    private val upstreamPort: Int = 53,
    private val localPort: Int = 5353,
    private val socketProtector: ((DatagramSocket) -> Boolean)? = null,
    private val enableFiltering: Boolean = true
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
            Log.i(TAG, "DNS Filter Server started on 127.0.0.1:$localPort (Upstream: $upstreamDns, Filtering: $enableFiltering)")

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
            if (enableFiltering) {
                val qname = parseQName(data)
                if (qname != null && AdBlockEngine.isDomainBlocked(qname)) {
                    Log.d(TAG, "🚫 Blocked DNS query for: $qname")
                    val blockedResponse = createBlockedResponse(data)
                    sendPacket(blockedResponse, clientAddress, clientPort)
                    return
                }
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

        // Find end of Question section & parse QTYPE
        var offset = 12
        while (offset < query.size) {
            val len = query[offset].toInt() and 0xFF
            if (len == 0) {
                offset += 1
                break
            }
            offset += len + 1
        }

        if (offset + 4 > query.size) return query

        val qtype = ((query[offset].toInt() and 0xFF) shl 8) or (query[offset + 1].toInt() and 0xFF)
        val questionLength = offset + 4 // QNAME + 0x00 + QTYPE (2) + QCLASS (2)

        return when (qtype) {
            1 -> { // Type A (IPv4) -> 0.0.0.0
                val response = ByteBuffer.allocate(questionLength + 16)
                response.put(query[0])
                response.put(query[1])
                response.put(0x81.toByte())
                response.put(0x80.toByte()) // Flags: standard response, NOERROR
                response.putShort(1) // QDCOUNT = 1
                response.putShort(1) // ANCOUNT = 1
                response.putShort(0) // NSCOUNT = 0
                response.putShort(0) // ARCOUNT = 0
                response.put(query, 12, questionLength - 12) // Question section
                // Answer
                response.put(0xC0.toByte())
                response.put(0x0C.toByte()) // Pointer to QNAME
                response.putShort(1) // Type A
                response.putShort(1) // Class IN
                response.putInt(300) // TTL 300
                response.putShort(4) // RDLENGTH 4
                response.put(0.toByte())
                response.put(0.toByte())
                response.put(0.toByte())
                response.put(0.toByte())
                response.array()
            }
            28 -> { // Type AAAA (IPv6) -> ::0
                val response = ByteBuffer.allocate(questionLength + 28)
                response.put(query[0])
                response.put(query[1])
                response.put(0x81.toByte())
                response.put(0x80.toByte()) // Flags: standard response, NOERROR
                response.putShort(1) // QDCOUNT = 1
                response.putShort(1) // ANCOUNT = 1
                response.putShort(0)
                response.putShort(0)
                response.put(query, 12, questionLength - 12) // Question section
                // Answer
                response.put(0xC0.toByte())
                response.put(0x0C.toByte()) // Pointer to QNAME
                response.putShort(28) // Type AAAA
                response.putShort(1) // Class IN
                response.putInt(300) // TTL 300
                response.putShort(16) // RDLENGTH 16
                for (i in 0 until 16) response.put(0.toByte())
                response.array()
            }
            else -> { // HTTPS (65), CNAME, TXT, etc. -> NOERROR with 0 answers (NODATA)
                val response = ByteBuffer.allocate(questionLength)
                response.put(query[0])
                response.put(query[1])
                response.put(0x81.toByte())
                response.put(0x80.toByte()) // Flags: standard response, NOERROR
                response.putShort(1) // QDCOUNT = 1
                response.putShort(0) // ANCOUNT = 0
                response.putShort(0)
                response.putShort(0)
                response.put(query, 12, questionLength - 12)
                response.array()
            }
        }
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
