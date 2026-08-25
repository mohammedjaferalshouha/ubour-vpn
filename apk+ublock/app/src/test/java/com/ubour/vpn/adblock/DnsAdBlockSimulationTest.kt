package com.ubour.vpn.adblock

import org.junit.Assert.*
import org.junit.BeforeClass
import org.junit.Test
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetAddress
import java.nio.ByteBuffer

class DnsAdBlockSimulationTest {

    companion object {
        private lateinit var dnsServer: DnsFilterServer
        private const val TEST_PORT = 15353

        @BeforeClass
        @JvmStatic
        fun setUpSimulationEnvironment() {
            AdBlockEngine.loadRulesForTesting(
                listOf(
                    "||doubleclick.net^",
                    "||googleads.g.doubleclick.net^",
                    "||pagead2.googlesyndication.com^",
                    "||partner.googleadservices.com^",
                    "||adservice.google.com^",
                    "||ads.google.com^",
                    "||admob.com^",
                    "||app-measurement.com^",
                    "||google-analytics.com^",
                    "||analytics.google.com^",
                    "||sentry.io^",
                    "||browser.sentry-cdn.com^",
                    "||bugsnag.com^",
                    "||notify.bugsnag.com^",
                    "||criteo.com^",
                    "||taboola.com^",
                    "||trc.taboola.com^",
                    "||outbrain.com^",
                    "||amazon-adsystem.com^",
                    "||an.facebook.com^",
                    "||pixel.facebook.com^",
                    "0.0.0.0 tracking-server.com",
                    "127.0.0.1 bad-analytics.net",
                    "@@||whitelist-example.com^"
                )
            )

            dnsServer = DnsFilterServer(
                upstreamDns = "1.1.1.1",
                upstreamPort = 53,
                localPort = TEST_PORT
            )
            dnsServer.start()
            Thread.sleep(200)
        }
    }

    private fun sendRawDnsQuery(domain: String): ByteArray {
        val socket = DatagramSocket()
        socket.soTimeout = 3000

        val queryPacket = buildDnsQuery(domain)
        val packet = DatagramPacket(queryPacket, queryPacket.size, InetAddress.getByName("127.0.0.1"), TEST_PORT)
        socket.send(packet)

        val buffer = ByteArray(4096)
        val responsePacket = DatagramPacket(buffer, buffer.size)
        socket.receive(responsePacket)
        socket.close()

        return responsePacket.data.copyOf(responsePacket.length)
    }

    private fun buildDnsQuery(domain: String): ByteArray {
        val buffer = ByteBuffer.allocate(512)
        buffer.putShort(0x1234.toShort())
        buffer.putShort(0x0100.toShort())
        buffer.putShort(1)
        buffer.putShort(0)
        buffer.putShort(0)
        buffer.putShort(0)

        for (part in domain.split('.')) {
            val bytes = part.toByteArray(Charsets.US_ASCII)
            buffer.put(bytes.size.toByte())
            buffer.put(bytes)
        }
        buffer.put(0.toByte())

        buffer.putShort(1)
        buffer.putShort(1)

        val result = ByteArray(buffer.position())
        buffer.flip()
        buffer.get(result)
        return result
    }

    @Test
    fun testAdDomainsBlockedThroughDnsServer() {
        val blockedDomains = listOf(
            "doubleclick.net",
            "googleads.g.doubleclick.net",
            "adservice.google.com",
            "ads.google.com",
            "pagead2.googlesyndication.com",
            "sentry.io",
            "browser.sentry-cdn.com",
            "bugsnag.com",
            "notify.bugsnag.com",
            "criteo.com",
            "taboola.com",
            "outbrain.com",
            "amazon-adsystem.com",
            "tracking-server.com",
            "bad-analytics.net",
            "dns.google",
            "cloudflare-dns.com"
        )

        for (domain in blockedDomains) {
            val response = sendRawDnsQuery(domain)
            assertTrue("Response for $domain should not be empty", response.size >= 16)
            
            val flags = ((response[2].toInt() and 0xFF) shl 8) or (response[3].toInt() and 0xFF)
            assertEquals("Response flags should indicate response", 0x8180, flags)

            val ancount = ((response[6].toInt() and 0xFF) shl 8) or (response[7].toInt() and 0xFF)
            assertEquals("Answer count should be 1 for blocked response", 1, ancount)

            val len = response.size
            assertEquals(0.toByte(), response[len - 4])
            assertEquals(0.toByte(), response[len - 3])
            assertEquals(0.toByte(), response[len - 2])
            assertEquals(0.toByte(), response[len - 1])
        }
    }

    @Test
    fun testSubdomainHierarchicalBlocking() {
        val subdomains = listOf(
            "sub.doubleclick.net",
            "ads.partner.googleadservices.com",
            "tracker.sentry.io",
            "events.bugsnag.com",
            "cdn.criteo.com"
        )

        for (sub in subdomains) {
            assertTrue("Subdomain $sub must be blocked hierarchically", AdBlockEngine.isDomainBlocked(sub))
        }
    }

    @Test
    fun testCleanDomainsNotBlocked() {
        val cleanDomains = listOf(
            "google.com",
            "github.com",
            "wikipedia.org",
            "microsoft.com",
            "whitelist-example.com"
        )

        for (domain in cleanDomains) {
            assertFalse("Clean domain $domain must NOT be blocked", AdBlockEngine.isDomainBlocked(domain))
        }
    }
}
