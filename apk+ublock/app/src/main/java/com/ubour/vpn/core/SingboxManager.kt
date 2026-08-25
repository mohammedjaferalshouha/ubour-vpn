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
    const val SOCKS_PORT = 10809

    fun isRunning(): Boolean {
        return process?.isAlive == true
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

    fun startVless(context: Context, vlessUrl: String, enableAdBlock: Boolean = true): Boolean {
        stop()
        val configFile = generateVlessConfig(context, vlessUrl, enableAdBlock) ?: return false
        return startProcess(context, configFile)
    }

    fun stop() {
        try {
            process?.destroy()
            process = null
            Log.i(TAG, "Sing-box process stopped")
        } catch (e: Exception) {
            Log.e(TAG, "Error stopping Sing-box: ${e.message}")
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
                        Log.i("SingboxCore", line)
                    }
                } catch (_: Exception) {}
            }.start()

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
            put("server", warp.endpointHost)
            put("server_port", warp.endpointPort)
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

        val warpOutbound = JSONObject().apply {
            put("type", "wireguard")
            put("tag", "warp-out")
            put("server", warp.endpointHost)
            put("server_port", warp.endpointPort)
            put("local_address", addrs)
            put("private_key", warp.privateKey)
            put("peer_public_key", warp.peerPublicKey)
            if (warp.reserved.size == 3) {
                put("reserved", JSONArray().apply {
                    warp.reserved.forEach { put(it) }
                })
            }
            put("peers", JSONArray().apply {
                put(peerObj)
            })
            put("mtu", 1280)
        }

        val dnsFilterOutbound = JSONObject().apply {
            put("type", "direct")
            put("tag", "dns-filter-out")
            put("override_address", "127.0.0.1")
            put("override_port", 5353)
        }

        val root = JSONObject().apply {
            put("log", JSONObject().apply {
                put("level", "info")
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
                put(warpOutbound)
                if (enableAdBlock) {
                    put(dnsFilterOutbound)
                }
                put(JSONObject().apply {
                    put("type", "direct")
                    put("tag", "direct")
                })
            })
            put("route", JSONObject().apply {
                put("rules", JSONArray().apply {
                    if (enableAdBlock) {
                        put(JSONObject().apply {
                            put("port", JSONArray().apply { put(53) })
                            put("outbound", "dns-filter-out")
                        })
                    }
                    put(JSONObject().apply {
                        put("inbound", JSONArray().apply { put("socks-in") })
                        put("outbound", "warp-out")
                    })
                })
                put("final", "warp-out")
            })
        }

        val configFile = File(context.filesDir, "singbox_warp.json")
        configFile.writeText(root.toString(2))
        return configFile
    }

    private fun generateAdBlockOnlyConfig(context: Context): File {
        val dnsFilterOutbound = JSONObject().apply {
            put("type", "direct")
            put("tag", "dns-filter-out")
            put("override_address", "127.0.0.1")
            put("override_port", 5353)
        }

        val root = JSONObject().apply {
            put("log", JSONObject().apply {
                put("level", "info")
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
                put(JSONObject().apply {
                    put("type", "direct")
                    put("tag", "direct")
                })
                put(dnsFilterOutbound)
            })
            put("route", JSONObject().apply {
                put("rules", JSONArray().apply {
                    put(JSONObject().apply {
                        put("port", JSONArray().apply { put(53) })
                        put("outbound", "dns-filter-out")
                    })
                    put(JSONObject().apply {
                        put("inbound", JSONArray().apply { put("socks-in") })
                        put("outbound", "direct")
                    })
                })
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

        val dnsFilterOutbound = JSONObject().apply {
            put("type", "direct")
            put("tag", "dns-filter-out")
            put("override_address", "127.0.0.1")
            put("override_port", 5353)
        }

        val root = JSONObject().apply {
            put("log", JSONObject().apply {
                put("level", "info")
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
                put(byedpiOutbound)
                put(dnsFilterOutbound)
                put(JSONObject().apply {
                    put("type", "direct")
                    put("tag", "direct")
                })
            })
            put("route", JSONObject().apply {
                put("rules", JSONArray().apply {
                    put(JSONObject().apply {
                        put("port", JSONArray().apply { put(53) })
                        put("outbound", "dns-filter-out")
                    })
                    put(JSONObject().apply {
                        put("inbound", JSONArray().apply { put("socks-in") })
                        put("outbound", "byedpi-out")
                    })
                })
                put("final", "byedpi-out")
            })
        }

        val configFile = File(context.filesDir, "singbox_vpn_adblock.json")
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

            val dnsFilterOutbound = JSONObject().apply {
                put("type", "direct")
                put("tag", "dns-filter-out")
                put("override_address", "127.0.0.1")
                put("override_port", 5353)
            }

            val root = JSONObject().apply {
                put("log", JSONObject().put("level", "info"))
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
                    })
                    if (enableAdBlock) {
                        put(dnsFilterOutbound)
                    }
                })
                put("route", JSONObject().apply {
                    put("rules", JSONArray().apply {
                        if (enableAdBlock) {
                            put(JSONObject().apply {
                                put("port", JSONArray().apply { put(53) })
                                put("outbound", "dns-filter-out")
                            })
                        }
                        put(JSONObject().apply {
                            put("inbound", JSONArray().apply { put("socks-in") })
                            put("outbound", "vless-out")
                        })
                    })
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
}
