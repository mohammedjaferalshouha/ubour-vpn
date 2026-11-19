package com.ubour.vpn.core

import android.content.Context
import android.util.Log
import com.ubour.vpn.warp.WarpConfig
import org.json.JSONArray
import org.json.JSONObject
import java.io.File

object SingboxManager {
    private const val TAG = "SingboxManager"
    private var process: Process? = null
    private var probeProcess: Process? = null
    const val SOCKS_PORT = 10809
    const val PROBE_BASE_PORT = 10820
    const val DNS_FILTER_PORT = 5355

    private fun buildLocalDnsConfig(): JSONObject {
        return JSONObject().apply {
            put("servers", JSONArray().apply {
                put(JSONObject().apply {
                    put("tag", "dns-filter")
                    put("type", "udp")
                    put("server", "127.0.0.1")
                    put("server_port", DNS_FILTER_PORT)
                })
            })
            put("final", "dns-filter")
        }
    }

    fun isRunning(): Boolean {
        val p = process ?: return false
        return if (android.os.Build.VERSION.SDK_INT >= android.os.Build.VERSION_CODES.O) {
            p.isAlive
        } else {
            try {
                p.exitValue()
                false
            } catch (e: IllegalThreadStateException) {
                true
            }
        }
    }

    fun startWarp(context: Context, warpConfig: WarpConfig, enableAdBlock: Boolean = true): Boolean {
        stop()
        val configFile = generateWarpConfig(context, warpConfig, enableAdBlock)
        return startProcess(context, configFile)
    }

    fun startAdBlockOnly(context: Context): Boolean {
        stop()
        val configFile = generateAdBlockOnlyConfig(context)
        return startProcess(context, configFile)
    }

    fun startVpnAndAdBlock(context: Context, byedpiPort: Int = 1080): Boolean {
        stop()
        val configFile = generateVpnAndAdBlockConfig(context, byedpiPort)
        return startProcess(context, configFile)
    }

    fun startVpnOnly(context: Context, byedpiPort: Int = 1080): Boolean {
        stop()
        val configFile = generateVpnOnlyConfig(context, byedpiPort)
        return startProcess(context, configFile)
    }

    fun startVless(context: Context, vlessUrl: String, enableAdBlock: Boolean = true): Boolean {
        stop()
        val configFile = generateVlessConfig(context, vlessUrl, enableAdBlock) ?: return false
        return startProcess(context, configFile)
    }

    fun stop() {
        try {
            val p = process
            p?.destroy()
            if (p != null && android.os.Build.VERSION.SDK_INT >= android.os.Build.VERSION_CODES.O && p.isAlive) {
                p.destroyForcibly()
            }
            process = null
            Log.i(TAG, "Sing-box process stopped")
        } catch (e: Exception) {
            Log.e(TAG, "Error stopping Sing-box: ${e.message}")
        }
    }

    fun isProbeRunning(): Boolean {
        val p = probeProcess ?: return false
        return if (android.os.Build.VERSION.SDK_INT >= android.os.Build.VERSION_CODES.O) {
            p.isAlive
        } else {
            try {
                p.exitValue()
                false
            } catch (_: IllegalThreadStateException) {
                true
            }
        }
    }

    fun stopProbe() {
        try {
            val p = probeProcess
            p?.destroy()
            if (p != null && android.os.Build.VERSION.SDK_INT >= android.os.Build.VERSION_CODES.O && p.isAlive) {
                p.destroyForcibly()
            }
            probeProcess = null
        } catch (e: Exception) {
            Log.w(TAG, "Error stopping Sing-box probe: ${e.message}")
        }
    }

    fun startWarpProbeBatch(
        context: Context,
        warp: WarpConfig,
        endpoints: List<Pair<String, Int>>,
        basePort: Int = PROBE_BASE_PORT
    ): Boolean {
        if (endpoints.isEmpty()) return false
        stopProbe()
        val binaryToRun = getExecutableBinary(context) ?: return false
        if (!binaryToRun.exists()) return false

        val addrs = JSONArray().apply {
            put(warp.localIpv4)
            if (!warp.localIpv6.isNullOrBlank()) {
                put(warp.localIpv6)
            }
        }

        val endpointsArr = JSONArray()
        val inboundsArr = JSONArray()
        val rulesArr = JSONArray()

        endpoints.forEachIndexed { idx, (host, port) ->
            val epTag = "warp-probe-$idx"
            val inTag = "socks-probe-$idx"
            val peerObj = JSONObject().apply {
                put("address", host)
                put("port", port)
                put("public_key", warp.peerPublicKey)
                put("allowed_ips", JSONArray().apply {
                    put("0.0.0.0/0")
                    put("::/0")
                })
                if (warp.reserved.size == 3) {
                    put("reserved", JSONArray().apply {
                        warp.reserved.forEach { put(it) }
                    })
                }
            }
            endpointsArr.put(JSONObject().apply {
                put("type", "wireguard")
                put("tag", epTag)
                put("address", addrs)
                put("private_key", warp.privateKey)
                put("peers", JSONArray().apply { put(peerObj) })
                put("mtu", 1280)
            })
            inboundsArr.put(JSONObject().apply {
                put("type", "socks")
                put("tag", inTag)
                put("listen", "127.0.0.1")
                put("listen_port", basePort + idx)
            })
            rulesArr.put(JSONObject().apply {
                put("inbound", JSONArray().apply { put(inTag) })
                put("outbound", epTag)
            })
        }

        val root = JSONObject().apply {
            put("log", JSONObject().apply { put("level", "warn") })
            if (endpoints.size == 1) {
                put("dns", JSONObject().apply {
                    put("servers", JSONArray().apply {
                        put(JSONObject().apply {
                            put("tag", "probe-dns")
                            put("type", "udp")
                            put("server", "1.1.1.1")
                            put("server_port", 53)
                            put("detour", "warp-probe-0")
                        })
                    })
                    put("final", "probe-dns")
                })
            }
            put("endpoints", endpointsArr)
            put("inbounds", inboundsArr)
            put("outbounds", JSONArray().apply {
                put(JSONObject().apply {
                    put("type", "direct")
                    put("tag", "direct")
                })
            })
            put("route", JSONObject().apply {
                put("rules", rulesArr)
                put("final", "direct")
            })
        }

        val configFile = File(context.filesDir, "singbox_probe.json")
        configFile.writeText(root.toString())

        return try {
            val pb = ProcessBuilder(
                binaryToRun.absolutePath,
                "run",
                "-c",
                configFile.absolutePath
            ).directory(context.filesDir)
             .redirectErrorStream(true)

            val startedProc = pb.start()
            probeProcess = startedProc

            Thread {
                try {
                    val reader = startedProc.inputStream.bufferedReader()
                    while (startedProc.isAlive) {
                        reader.readLine() ?: break
                    }
                } catch (_: Exception) {}
            }.apply { isDaemon = true }.start()

            Thread.sleep(220)
            isProbeRunning()
        } catch (e: Exception) {
            Log.e(TAG, "Failed to start Sing-box probe process: ${e.message}", e)
            false
        }
    }

    fun getExecutableBinary(context: Context): File? {
        val nativeDir = context.applicationInfo.nativeLibraryDir
        val nativeBinary = File(nativeDir, "libsingbox.so")
        if (nativeBinary.exists()) {
            if (!nativeBinary.canExecute()) {
                nativeBinary.setExecutable(true, true)
            }
            return nativeBinary
        }
        val fallbackBinary = File(context.filesDir, "libsingbox.so")
        if (fallbackBinary.exists()) {
            if (!fallbackBinary.canExecute()) {
                fallbackBinary.setExecutable(true, true)
            }
            return fallbackBinary
        }
        Log.e(TAG, "libsingbox.so not found in nativeLibraryDir: $nativeDir")
        return null
    }

    private fun startProcess(context: Context, configFile: File): Boolean {
        val binaryToRun = getExecutableBinary(context)
        if (binaryToRun == null || !binaryToRun.exists()) {
            Log.e(TAG, "No valid singbox binary found in nativeLibraryDir!")
            return false
        }

        return try {
            val pb = ProcessBuilder(
                binaryToRun.absolutePath,
                "run",
                "-c",
                configFile.absolutePath
            ).directory(context.filesDir)
             .redirectErrorStream(true)

            process = pb.start()

            // Start log consumer thread
            Thread {
                try {
                    val reader = process?.inputStream?.bufferedReader()
                    while (isRunning()) {
                        val line = reader?.readLine() ?: break
                        Log.w("SingboxCore", line)
                    }
                } catch (_: Exception) {}
            }.apply { isDaemon = true }.start()

            Thread.sleep(400)
            val alive = isRunning()
            Log.i(TAG, "Sing-box process status: isAlive=$alive (Path: ${binaryToRun.absolutePath})")
            alive
        } catch (e: Exception) {
            Log.e(TAG, "Failed to start Sing-box process: ${e.message}", e)
            false
        }
    }

    private fun generateWarpConfig(context: Context, warp: WarpConfig, enableAdBlock: Boolean = true): File {
        val addrs = JSONArray().apply {
            put(warp.localIpv4)
            if (!warp.localIpv6.isNullOrBlank()) {
                put(warp.localIpv6)
            }
        }

        val peerObj = JSONObject().apply {
            put("address", warp.endpointHost)
            put("port", warp.endpointPort)
            put("public_key", warp.peerPublicKey)
            put("allowed_ips", JSONArray().apply {
                put("0.0.0.0/0")
                put("::/0")
            })
            if (warp.reserved.size == 3) {
                put("reserved", JSONArray().apply {
                    warp.reserved.forEach { put(it) }
                })
            }
        }

        val workerCount = Runtime.getRuntime().availableProcessors().coerceIn(2, 4)
        val warpEndpoint = JSONObject().apply {
            put("type", "wireguard")
            put("tag", "warp-ep")
            put("address", addrs)
            put("private_key", warp.privateKey)
            put("peers", JSONArray().apply {
                put(peerObj)
            })
            put("mtu", 1280)
            put("workers", workerCount)
        }

        val root = JSONObject().apply {
            put("log", JSONObject().apply {
                put("level", "warn")
            })
            put("endpoints", JSONArray().apply {
                put(warpEndpoint)
            })
            put("dns", buildLocalDnsConfig())
            put("inbounds", JSONArray().apply {
                put(JSONObject().apply {
                    put("type", "socks")
                    put("tag", "socks-in")
                    put("listen", "127.0.0.1")
                    put("listen_port", SOCKS_PORT)
                })
                put(JSONObject().apply {
                    put("type", "direct")
                    put("tag", "dns-upstream-in")
                    put("listen", "127.0.0.1")
                    put("listen_port", 5354)
                    put("override_address", "1.1.1.1")
                    put("override_port", 53)
                })
            })
            put("outbounds", JSONArray().apply {
                put(JSONObject().apply {
                    put("type", "direct")
                    put("tag", "direct")
                })
            })
            val warpRules = JSONArray().apply {
                put(JSONObject().apply {
                    put("inbound", JSONArray().apply { put("dns-upstream-in") })
                    put("outbound", "warp-ep")
                })
                if (!enableAdBlock) {
                    put(JSONObject().apply {
                        put("port", JSONArray().apply { put(53) })
                        put("action", "hijack-dns")
                    })
                }
                val sharedRules = buildRouteRules(
                    enableAdBlock = enableAdBlock,
                    outboundTag = "warp-ep",
                    rejectQuic = false
                )
                for (i in 0 until sharedRules.length()) {
                    put(sharedRules.get(i))
                }
            }
            put("route", JSONObject().apply {
                put("rules", warpRules)
                put("final", "warp-ep")
            })
        }

        val configFile = File(context.filesDir, "singbox_warp.json")
        configFile.writeText(root.toString(2))
        return configFile
    }

    private fun generateAdBlockOnlyConfig(context: Context): File {
        val root = JSONObject().apply {
            put("log", JSONObject().apply {
                put("level", "warn")
            })
            put("dns", buildLocalDnsConfig())
            put("inbounds", JSONArray().apply {
                put(JSONObject().apply {
                    put("type", "socks")
                    put("tag", "socks-in")
                    put("listen", "127.0.0.1")
                    put("listen_port", SOCKS_PORT)
                })
            })
            put("outbounds", JSONArray().apply {
                put(JSONObject().apply {
                    put("type", "direct")
                    put("tag", "direct")
                })
            })
            put("route", JSONObject().apply {
                put("rules", buildRouteRules(enableAdBlock = true, "direct"))
                put("final", "direct")
            })
        }

        val configFile = File(context.filesDir, "singbox_adblock.json")
        configFile.writeText(root.toString(2))
        return configFile
    }

    private fun generateVpnAndAdBlockConfig(context: Context, byedpiPort: Int): File {
        val byedpiOutbound = JSONObject().apply {
            put("type", "socks")
            put("tag", "byedpi-out")
            put("server", "127.0.0.1")
            put("server_port", byedpiPort)
        }

        val root = JSONObject().apply {
            put("log", JSONObject().apply {
                put("level", "warn")
            })
            put("dns", buildLocalDnsConfig())
            put("inbounds", JSONArray().apply {
                put(JSONObject().apply {
                    put("type", "socks")
                    put("tag", "socks-in")
                    put("listen", "127.0.0.1")
                    put("listen_port", SOCKS_PORT)
                })
            })
            put("outbounds", JSONArray().apply {
                put(byedpiOutbound)
                put(JSONObject().apply {
                    put("type", "direct")
                    put("tag", "direct")
                })
            })
            put("route", JSONObject().apply {
                put("rules", buildRouteRules(enableAdBlock = true, "byedpi-out"))
                put("final", "byedpi-out")
            })
        }

        val configFile = File(context.filesDir, "singbox_vpn_adblock.json")
        configFile.writeText(root.toString(2))
        return configFile
    }

    private fun generateVpnOnlyConfig(context: Context, byedpiPort: Int): File {
        val byedpiOutbound = JSONObject().apply {
            put("type", "socks")
            put("tag", "byedpi-out")
            put("server", "127.0.0.1")
            put("server_port", byedpiPort)
        }

        val root = JSONObject().apply {
            put("log", JSONObject().apply {
                put("level", "warn")
            })
            put("dns", buildLocalDnsConfig())
            put("inbounds", JSONArray().apply {
                put(JSONObject().apply {
                    put("type", "socks")
                    put("tag", "socks-in")
                    put("listen", "127.0.0.1")
                    put("listen_port", SOCKS_PORT)
                })
            })
            put("outbounds", JSONArray().apply {
                put(byedpiOutbound)
                put(JSONObject().apply {
                    put("type", "direct")
                    put("tag", "direct")
                })
            })
            put("route", JSONObject().apply {
                put("rules", JSONArray().apply {
                    put(JSONObject().apply {
                        put("network", JSONArray().apply { put("udp") })
                        put("port", JSONArray().apply { put(443) })
                        put("action", "reject")
                    })
                    put(JSONObject().apply {
                        put("port", JSONArray().apply { put(53) })
                        put("action", "hijack-dns")
                    })
                    put(JSONObject().apply {
                        put("inbound", JSONArray().apply { put("socks-in") })
                        put("outbound", "byedpi-out")
                    })
                })
                put("final", "byedpi-out")
            })
        }

        val configFile = File(context.filesDir, "singbox_vpn_only.json")
        configFile.writeText(root.toString(2))
        return configFile
    }

    private fun generateVlessConfig(context: Context, vlessUrl: String, enableAdBlock: Boolean = true): File? {
        try {
            if (!vlessUrl.startsWith("vless://", ignoreCase = true)) return null
            val raw = vlessUrl.substring(8)
            val atIdx = raw.indexOf('@')
            val colonIdx = raw.indexOf(':', atIdx)
            val qIdx = raw.indexOf('?', colonIdx)

            val uuid = raw.substring(0, atIdx)
            val host = raw.substring(atIdx + 1, colonIdx)
            val portStr = if (qIdx != -1) raw.substring(colonIdx + 1, qIdx) else raw.substring(colonIdx + 1)
            val port = portStr.toIntOrNull() ?: 443

            var sni = host
            var pbk = ""
            var sid = ""
            var fp = "chrome"

            if (qIdx != -1) {
                val query = raw.substring(qIdx + 1)
                val params = query.split("&")
                for (p in params) {
                    val kv = p.split("=")
                    if (kv.size == 2) {
                        when (kv[0].lowercase()) {
                            "sni" -> sni = kv[1]
                            "pbk" -> pbk = kv[1]
                            "sid" -> sid = kv[1]
                            "fp" -> fp = kv[1]
                        }
                    }
                }
            }

            val vlessOutbound = JSONObject().apply {
                put("type", "vless")
                put("tag", "vless-out")
                put("server", host)
                put("server_port", port)
                put("uuid", uuid)
                put("flow", "xtls-rprx-vision")
                put("tls", JSONObject().apply {
                    put("enabled", true)
                    put("server_name", sni)
                    put("utls", JSONObject().apply {
                        put("enabled", true)
                        put("fingerprint", fp)
                    })
                    if (pbk.isNotBlank()) {
                        put("reality", JSONObject().apply {
                            put("enabled", true)
                            put("public_key", pbk)
                            put("short_id", sid)
                        })
                    }
                })
            }

            val root = JSONObject().apply {
                put("log", JSONObject().put("level", "warn"))
                put("dns", JSONObject().apply {
                    put("servers", JSONArray().apply {
                        put(JSONObject().apply {
                            put("tag", "dns-filter")
                            put("type", "udp")
                            put("server", "127.0.0.1")
                            put("server_port", 5353)
                            put("detour", "direct")
                        })
                    })
                    put("final", "dns-filter")
                })
                put("inbounds", JSONArray().apply {
                    put(JSONObject().apply {
                        put("type", "socks")
                        put("tag", "socks-in")
                        put("listen", "127.0.0.1")
                        put("listen_port", SOCKS_PORT)
                    })
                })
                put("outbounds", JSONArray().apply {
                    put(vlessOutbound)
                    put(JSONObject().apply {
                        put("type", "direct")
                        put("tag", "direct")
                    })
                })
                put("route", JSONObject().apply {
                    put("rules", buildRouteRules(enableAdBlock, "vless-out"))
                    put("final", "vless-out")
                })
            }

            val configFile = File(context.filesDir, "singbox_vless.json")
            configFile.writeText(root.toString(2))
            return configFile
        } catch (e: Exception) {
            Log.e(TAG, "Failed to parse vless URL: ${e.message}")
            return null
        }
    }

    private fun buildRouteRules(
        enableAdBlock: Boolean,
        outboundTag: String,
        rejectQuic: Boolean = true
    ): JSONArray {
        val rules = JSONArray()
        // 0. Reject QUIC (UDP 443) only in direct DPI-bypass modes that require clean TCP
        if (rejectQuic) {
            rules.put(JSONObject().apply {
                put("network", JSONArray().apply { put("udp") })
                put("port", JSONArray().apply { put(443) })
                put("action", "reject")
            })
        }
        if (enableAdBlock) {
            // 1. Hijack standard DNS on port 53 to our local dns-filter
            rules.put(JSONObject().apply {
                put("port", JSONArray().apply { put(53) })
                put("action", "hijack-dns")
            })
            // 2. Reject DoT (DNS over TLS) on port 853 so apps immediately fallback to port 53
            rules.put(JSONObject().apply {
                put("port", JSONArray().apply { put(853) })
                put("action", "reject")
            })
            // 3. Reject DoH (DNS over HTTPS) endpoints so Chrome & browsers fallback to system DNS
            rules.put(JSONObject().apply {
                put("domain_suffix", JSONArray().apply {
                    put("dns.google")
                    put("dns.google.com")
                    put("cloudflare-dns.com")
                    put("chrome.cloudflare-dns.com")
                    put("mozilla.cloudflare-dns.com")
                    put("dns.quad9.net")
                    put("doh.opendns.com")
                    put("dns.nextdns.io")
                    put("doh.cleanbrowsing.org")
                    put("dns.alidns.com")
                    put("doh.pub")
                    put("sm2.doh.pub")
                })
                put("action", "reject")
            })
            // 4. Reject direct connections to 0.0.0.0 / ::0 immediately
            rules.put(JSONObject().apply {
                put("ip_cidr", JSONArray().apply {
                    put("0.0.0.0/32")
                    put("::0/128")
                })
                put("action", "reject")
            })
        }
        // Main outbound traffic rule
        rules.put(JSONObject().apply {
            put("inbound", JSONArray().apply { put("socks-in") })
            put("outbound", outboundTag)
        })
        return rules
    }
}
