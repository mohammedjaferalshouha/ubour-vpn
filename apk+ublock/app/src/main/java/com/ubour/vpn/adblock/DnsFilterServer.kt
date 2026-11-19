package com.ubour.vpn.adblock

import android.util.Log
import kotlinx.coroutines.*
import java.io.DataInputStream
import java.io.DataOutputStream
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetAddress
import java.net.InetSocketAddress
import java.net.ServerSocket
import java.net.Socket
import java.nio.ByteBuffer
import java.util.concurrent.ConcurrentHashMap
import java.util.concurrent.atomic.AtomicBoolean

class DnsFilterServer(
    private var upstreamDns: String = "8.8.8.8",
    private val upstreamPort: Int = 53,
    private val localPort: Int = 5355,
    private val socketProtector: ((DatagramSocket) -> Boolean)? = null,
    private val enableFiltering: Boolean = true,
    private val tcpSocketProtector: ((Socket) -> Boolean)? = null
) {
    private data class CachedDnsEntry(
        val responseBytes: ByteArray,
        val expiresAtMs: Long
    )

    private val isRunning = AtomicBoolean(false)
    private var serverSocket: DatagramSocket? = null
    private var tcpServerSocket: ServerSocket? = null
    private var serverJob: Job? = null
    private var tcpServerJob: Job? = null
    private val scope = CoroutineScope(Dispatchers.IO + SupervisorJob())
    private val dnsCache = ConcurrentHashMap<String, CachedDnsEntry>(512)

    fun setUpstreamDns(dns: String) {
        upstreamDns = dns
        dnsCache.clear()
    }

    fun clearCache() {
        dnsCache.clear()
    }

    fun start() {
        if (isRunning.getAndSet(true)) return

        try {
            val bindAddress = InetSocketAddress(InetAddress.getByName("127.0.0.1"), localPort)
            serverSocket = DatagramSocket(null).apply {
                reuseAddress = true
                soTimeout = 2000
                bind(bindAddress)
            }
            tcpServerSocket = ServerSocket().apply {
                reuseAddress = true
                soTimeout = 2000
                bind(bindAddress, 50)
            }
            Log.i(TAG, "DNS Filter Server started on 127.0.0.1:$localPort UDP+TCP (Upstream: $upstreamDns:$upstreamPort, Filtering: $enableFiltering)")

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

            tcpServerJob = scope.launch {
                while (isActive && isRunning.get()) {
                    try {
                        val client = tcpServerSocket?.accept() ?: break
                        launch(Dispatchers.IO) {
                            handleTcpDnsQuery(client)
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

    private fun resolveBlockedResponse(data: ByteArray): ByteArray? {
        if (!enableFiltering) return null
        val qname = parseQName(data) ?: return null
        if (!AdBlockEngine.isDomainBlocked(qname)) return null
        return createBlockedResponse(data)
    }

    internal fun getCachedResponseForQuery(data: ByteArray): ByteArray? {
        if (data.size < 16) return null
        val key = parseCacheKey(data) ?: return null
        val entry = dnsCache[key] ?: return null
        if (System.currentTimeMillis() >= entry.expiresAtMs) {
            dnsCache.remove(key, entry)
            return null
        }
        val copy = entry.responseBytes.copyOf()
        copy[0] = data[0]
        copy[1] = data[1]
        return copy
    }

    internal fun cacheUpstreamResponse(query: ByteArray, response: ByteArray) {
        if (query.size < 16 || response.size < 12) return
        val key = parseCacheKey(query) ?: return
        val ttlSec = extractCacheableTtlSeconds(response)
        if (ttlSec <= 0L) return

        val now = System.currentTimeMillis()
        if (dnsCache.size >= MAX_CACHE_ENTRIES) {
            val iter = dnsCache.entries.iterator()
            while (iter.hasNext()) {
                if (now >= iter.next().value.expiresAtMs) {
                    iter.remove()
                }
            }
            if (dnsCache.size >= MAX_CACHE_ENTRIES) {
                dnsCache.clear()
            }
        }
        dnsCache[key] = CachedDnsEntry(
            responseBytes = response.copyOf(),
            expiresAtMs = now + (ttlSec * 1000L)
        )
    }

    private fun handleDnsQuery(data: ByteArray, clientAddress: InetAddress, clientPort: Int) {
        try {
            val blockedResponse = resolveBlockedResponse(data)
            if (blockedResponse != null) {
                sendPacket(blockedResponse, clientAddress, clientPort)
                return
            }

            val cachedResponse = getCachedResponseForQuery(data)
            if (cachedResponse != null) {
                sendPacket(cachedResponse, clientAddress, clientPort)
                return
            }

            // Forward to upstream DNS
            DatagramSocket().use { upstreamSocket ->
                socketProtector?.invoke(upstreamSocket)
                upstreamSocket.soTimeout = 3000
                val upstreamPacket = DatagramPacket(data, data.size, InetAddress.getByName(upstreamDns), upstreamPort)
                upstreamSocket.send(upstreamPacket)

                val responseBuffer = ByteArray(4096)
                val responsePacket = DatagramPacket(responseBuffer, responseBuffer.size)
                upstreamSocket.receive(responsePacket)

                val responseData = responsePacket.data.copyOf(responsePacket.length)
                cacheUpstreamResponse(data, responseData)
                sendPacket(responseData, clientAddress, clientPort)
            }

        } catch (e: Exception) {
            Log.w(TAG, "Error handling DNS query: ${e.message}")
        }
    }

    private fun handleTcpDnsQuery(client: Socket) {
        try {
            client.use { sock ->
                sock.soTimeout = 3000
                val input = DataInputStream(sock.getInputStream())
                val output = DataOutputStream(sock.getOutputStream())
                val queryLen = input.readUnsignedShort()
                if (queryLen <= 0 || queryLen > 4096) return

                val data = ByteArray(queryLen)
                input.readFully(data)

                val blockedResponse = resolveBlockedResponse(data)
                if (blockedResponse != null) {
                    output.writeShort(blockedResponse.size)
                    output.write(blockedResponse)
                    output.flush()
                    return
                }

                val cachedResponse = getCachedResponseForQuery(data)
                if (cachedResponse != null) {
                    output.writeShort(cachedResponse.size)
                    output.write(cachedResponse)
                    output.flush()
                    return
                }

                Socket().use { upstreamSocket ->
                    tcpSocketProtector?.invoke(upstreamSocket)
                    upstreamSocket.soTimeout = 3000
                    upstreamSocket.connect(InetSocketAddress(InetAddress.getByName(upstreamDns), upstreamPort), 3000)

                    val upOut = DataOutputStream(upstreamSocket.getOutputStream())
                    val upIn = DataInputStream(upstreamSocket.getInputStream())
                    upOut.writeShort(data.size)
                    upOut.write(data)
                    upOut.flush()

                    val respLen = upIn.readUnsignedShort()
                    if (respLen <= 0 || respLen > 65535) return
                    val responseData = ByteArray(respLen)
                    upIn.readFully(responseData)

                    cacheUpstreamResponse(data, responseData)
                    output.writeShort(responseData.size)
                    output.write(responseData)
                    output.flush()
                }
            }
        } catch (e: Exception) {
            Log.w(TAG, "Error handling TCP DNS query: ${e.message}")
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

    private fun parseCacheKey(data: ByteArray): String? {
        if (data.size < 16) return null
        val qdCount = ((data[4].toInt() and 0xFF) shl 8) or (data[5].toInt() and 0xFF)
        if (qdCount != 1) return null

        var offset = 12
        val sb = StringBuilder()
        while (offset < data.size) {
            val len = data[offset].toInt() and 0xFF
            if (len == 0) {
                offset++
                break
            }
            if (len > 63 || offset + 1 + len > data.size) return null
            if (sb.isNotEmpty()) sb.append('.')
            for (i in 0 until len) {
                val ch = data[offset + 1 + i].toInt() and 0xFF
                val lower = if (ch in 65..90) (ch + 32).toChar() else ch.toChar()
                sb.append(lower)
            }
            offset += len + 1
        }
        if (sb.isEmpty() || offset + 4 > data.size) return null
        val qtype = ((data[offset].toInt() and 0xFF) shl 8) or (data[offset + 1].toInt() and 0xFF)
        val qclass = ((data[offset + 2].toInt() and 0xFF) shl 8) or (data[offset + 3].toInt() and 0xFF)
        return "$sb|$qtype|$qclass"
    }

    private fun skipDnsName(packet: ByteArray, startOffset: Int): Int {
        var offset = startOffset
        var steps = 0
        while (offset < packet.size && steps < 128) {
            val len = packet[offset].toInt() and 0xFF
            if (len == 0) {
                return offset + 1
            }
            if ((len and 0xC0) == 0xC0) {
                return if (offset + 2 <= packet.size) offset + 2 else -1
            }
            if (len > 63 || offset + 1 + len > packet.size) {
                return -1
            }
            offset += len + 1
            steps++
        }
        return -1
    }

    private fun extractCacheableTtlSeconds(resp: ByteArray): Long {
        if (resp.size < 12) return 0L
        val isResponse = (resp[2].toInt() and 0x80) != 0
        val isTruncated = (resp[2].toInt() and 0x02) != 0
        if (!isResponse || isTruncated) return 0L

        val rcode = resp[3].toInt() and 0x0F
        if (rcode == 3) return 30L
        if (rcode != 0) return 0L

        val qdCount = ((resp[4].toInt() and 0xFF) shl 8) or (resp[5].toInt() and 0xFF)
        val anCount = ((resp[6].toInt() and 0xFF) shl 8) or (resp[7].toInt() and 0xFF)
        if (anCount == 0) return 60L

        var offset = 12
        for (q in 0 until qdCount) {
            offset = skipDnsName(resp, offset)
            if (offset < 0 || offset + 4 > resp.size) return 60L
            offset += 4
        }

        var minTtl = Long.MAX_VALUE
        for (a in 0 until anCount) {
            offset = skipDnsName(resp, offset)
            if (offset < 0 || offset + 10 > resp.size) break
            val ttl = ((resp[offset + 4].toLong() and 0xFFL) shl 24) or
                ((resp[offset + 5].toLong() and 0xFFL) shl 16) or
                ((resp[offset + 6].toLong() and 0xFFL) shl 8) or
                (resp[offset + 7].toLong() and 0xFFL)
            val rdLength = ((resp[offset + 8].toInt() and 0xFF) shl 8) or (resp[offset + 9].toInt() and 0xFF)
            offset += 10 + rdLength
            if (offset > resp.size) break
            if (ttl < minTtl) {
                minTtl = ttl
            }
        }

        if (minTtl == Long.MAX_VALUE) return 120L
        if (minTtl <= 0L) return 0L
        return minTtl.coerceIn(20L, 600L)
    }

    fun stop() {
        isRunning.set(false)
        serverJob?.cancel()
        serverJob = null
        tcpServerJob?.cancel()
        tcpServerJob = null
        dnsCache.clear()
        try {
            serverSocket?.close()
        } catch (e: Exception) {
            // Ignore
        }
        try {
            tcpServerSocket?.close()
        } catch (e: Exception) {
            // Ignore
        }
        serverSocket = null
        tcpServerSocket = null
        Log.i(TAG, "DNS Filter Server stopped")
    }

    companion object {
        private const val TAG = "DnsFilterServer"
        private const val MAX_CACHE_ENTRIES = 2048
    }
}
