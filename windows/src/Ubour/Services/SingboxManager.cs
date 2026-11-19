using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Ubour.Models;

namespace Ubour.Services;

public sealed class WarpConnectivityResult
{
    public bool Success { get; set; }
    public long LatencyMs { get; set; }
    public double DownloadMbps { get; set; }
    public WarpLocationInfo? Location { get; set; }
    public string? ErrorMessage { get; set; }
}

public class SingboxManager : IDisposable
{
    private Process? _process;
    private string? _currentConfigFile;
    private volatile bool _tunStarted;
    private volatile string? _warpStartupError;
    public bool IsRunning => _process != null && !_process.HasExited;
    public Process? OwnedProcess => _process;
    public bool HasTunStarted => _tunStarted;
    public string? WarpStartupError => _warpStartupError;

    public bool StartWarp(string baseDir, WarpConfig warp, bool enableAdBlock = true)
    {
        Stop();
        _tunStarted = false;
        _warpStartupError = null;
        string configPath = GenerateWarpConfig(warp, enableAdBlock, baseDir);
        return StartProcess(baseDir, configPath);
    }

    public bool StartWarpViaSocks(string baseDir, int upstreamSocksPort, string endpointHost = "162.159.198.2", bool enableAdBlock = true)
    {
        Stop();
        _tunStarted = false;
        _warpStartupError = null;
        string configPath = GenerateWarpMasqueConfig(upstreamSocksPort, endpointHost, enableAdBlock, baseDir);
        return StartProcess(baseDir, configPath);
    }

    public async Task<WarpConnectivityResult> VerifyWarpMasqueChainAsync(
        string baseDir,
        int upstreamSocksPort,
        string endpointHost = "162.159.198.2",
        int timeoutMs = 5500,
        bool measureSpeed = false)
    {
        int probePort = GetFreeLocalTcpPort();
        string? probeConfig = null;
        Process? probeProcess = null;
        string? startupError = null;
        var recentLogs = new List<string>();
        var logLock = new object();

        try
        {
            probeConfig = GenerateWarpMasqueProbeConfig(upstreamSocksPort, endpointHost, probePort);
            probeProcess = LaunchSingboxProcess(baseDir, probeConfig, (line, _) =>
            {
                lock (logLock)
                {
                    if (recentLogs.Count < 12)
                    {
                        recentLogs.Add(line);
                    }
                    if (startupError == null &&
                        (line.Contains("fatal", StringComparison.OrdinalIgnoreCase) ||
                         line.Contains("error", StringComparison.OrdinalIgnoreCase)))
                    {
                        startupError = line;
                    }
                }
            });

            if (probeProcess == null)
            {
                return new WarpConnectivityResult { Success = false, ErrorMessage = "Failed to start sing-box MASQUE chain probe." };
            }

            if (!await WaitForPortListeningAsync(probeProcess, probePort, Math.Clamp(timeoutMs, 6000, 10000)).ConfigureAwait(false))
            {
                bool exited = probeProcess.HasExited;
                if (exited)
                {
                    try { probeProcess.WaitForExit(500); } catch { }
                }

                string? capturedError;
                string logsSummary;
                lock (logLock)
                {
                    capturedError = startupError;
                    logsSummary = recentLogs.Count > 0 ? string.Join(" | ", recentLogs) : "(none)";
                }

                string exitInfo = exited ? $", code={probeProcess.ExitCode}" : "";
                return new WarpConnectivityResult
                {
                    Success = false,
                    ErrorMessage = !string.IsNullOrWhiteSpace(capturedError)
                        ? capturedError
                        : $"Probe proxy port {probePort} did not open (exited={exited}{exitInfo}). Logs: {logsSummary}"
                };
            }

            return await ProbeWarpThroughRunningProxyAsync(probePort, timeoutMs, measureSpeed).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return new WarpConnectivityResult { Success = false, ErrorMessage = startupError ?? ex.Message };
        }
        finally
        {
            if (probeProcess != null)
            {
                try
                {
                    if (!probeProcess.HasExited)
                    {
                        probeProcess.Kill(true);
                        probeProcess.WaitForExit(1500);
                    }
                    probeProcess.Dispose();
                }
                catch { }
            }

            if (!string.IsNullOrEmpty(probeConfig) && File.Exists(probeConfig))
            {
                try { File.Delete(probeConfig); } catch { }
            }
        }
    }

    public bool StartAdBlockOnly(string baseDir)
    {
        Stop();
        string configPath = GenerateAdBlockOnlyConfig();
        bool started = StartProcess(baseDir, configPath);
        if (started)
        {
            ProxyManager.EnableProxy("127.0.0.1:2080");
        }
        return started;
    }

    public bool StartVless(string baseDir, string vlessUrl, bool enableAdBlock = true)
    {
        Stop();
        string? configPath = GenerateVlessConfig(vlessUrl, enableAdBlock);
        if (string.IsNullOrEmpty(configPath)) return false;
        bool started = StartProcess(baseDir, configPath);
        if (started)
        {
            ProxyManager.EnableProxy("127.0.0.1:2080");
        }
        return started;
    }

    public bool VerifyRunning(int port = 2080, int timeoutMs = 2500)
    {
        if (_process == null || _process.HasExited) return false;
        try
        {
            using var client = new TcpClient();
            var task = client.ConnectAsync(IPAddress.Loopback, port);
            if (!task.Wait(timeoutMs)) return false;
            return client.Connected;
        }
        catch
        {
            return false;
        }
    }

    public bool VerifyTunRunning(int timeoutMs = 5000)
    {
        var timer = Stopwatch.StartNew();
        while (timer.ElapsedMilliseconds < timeoutMs)
        {
            if (_process == null || _process.HasExited || !string.IsNullOrEmpty(_warpStartupError)) return false;
            if (_tunStarted)
            {
                // Allow asynchronous sing-box startup diagnostics (including WireGuard bind errors) to arrive.
                Thread.Sleep(500);
                return _process != null && !_process.HasExited && string.IsNullOrEmpty(_warpStartupError);
            }
            Thread.Sleep(100);
        }
        return false;
    }

    public async Task<WarpConnectivityResult> VerifyWarpEndpointConnectivityAsync(
        string baseDir,
        WarpConfig warp,
        int timeoutMs = 5500,
        bool measureSpeed = false)
    {
        int probePort = GetFreeLocalTcpPort();
        string? probeConfig = null;
        Process? probeProcess = null;
        string? startupError = null;

        try
        {
            probeConfig = GenerateWarpProbeConfig(warp, probePort);
            probeProcess = LaunchSingboxProcess(baseDir, probeConfig, (line, isError) =>
            {
                if (line.Contains("endpoint/wireguard[warp-ep]", StringComparison.OrdinalIgnoreCase) &&
                    (line.Contains("unable to update bind", StringComparison.OrdinalIgnoreCase) ||
                     line.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
                     line.Contains("fatal", StringComparison.OrdinalIgnoreCase)))
                {
                    startupError ??= line;
                }
            });

            if (probeProcess == null)
            {
                return new WarpConnectivityResult { Success = false, ErrorMessage = "Failed to start sing-box probe." };
            }

            if (!await WaitForPortListeningAsync(probeProcess, probePort, Math.Clamp(timeoutMs, 5000, 10000)).ConfigureAwait(false))
            {
                if (probeProcess.HasExited)
                {
                    try { probeProcess.WaitForExit(500); } catch { }
                }
                return new WarpConnectivityResult { Success = false, ErrorMessage = startupError ?? "Probe proxy port did not open." };
            }

            var result = await ProbeWarpThroughRunningProxyAsync(probePort, timeoutMs, measureSpeed).ConfigureAwait(false);
            if (!result.Success && !string.IsNullOrWhiteSpace(startupError))
            {
                result.ErrorMessage = startupError;
            }
            return result;
        }
        catch (Exception ex)
        {
            return new WarpConnectivityResult { Success = false, ErrorMessage = startupError ?? ex.Message };
        }
        finally
        {
            if (probeProcess != null)
            {
                try
                {
                    if (!probeProcess.HasExited)
                    {
                        probeProcess.Kill(true);
                        probeProcess.WaitForExit(1500);
                    }
                    probeProcess.Dispose();
                }
                catch { }
            }

            if (!string.IsNullOrEmpty(probeConfig) && File.Exists(probeConfig))
            {
                try { File.Delete(probeConfig); } catch { }
            }
        }
    }

    public async Task<WarpConnectivityResult> ProbeWarpThroughRunningProxyAsync(
        int proxyPort = 2080,
        int timeoutMs = 4500,
        bool measureSpeed = false)
    {
        try
        {
            using var handler = new SocketsHttpHandler
            {
                Proxy = new WebProxy($"http://127.0.0.1:{proxyPort}"),
                UseProxy = true,
                ConnectTimeout = TimeSpan.FromMilliseconds(timeoutMs)
            };
            using var http = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromMilliseconds(timeoutMs)
            };

            string? traceBody = null;
            long latencyMs = 0;

            foreach (string traceUrl in new[] { "https://1.1.1.1/cdn-cgi/trace", "https://www.cloudflare.com/cdn-cgi/trace" })
            {
                try
                {
                    var sw = Stopwatch.StartNew();
                    using var resp = await http.GetAsync(traceUrl).ConfigureAwait(false);
                    if (resp.IsSuccessStatusCode)
                    {
                        string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                        sw.Stop();
                        if (!string.IsNullOrWhiteSpace(body) &&
                            body.Contains("colo=", StringComparison.OrdinalIgnoreCase) &&
                            !body.Contains("warp=off", StringComparison.OrdinalIgnoreCase))
                        {
                            traceBody = body;
                            latencyMs = Math.Max(1, sw.ElapsedMilliseconds);
                            break;
                        }
                    }
                }
                catch { }
            }

            if (string.IsNullOrWhiteSpace(traceBody))
            {
                return new WarpConnectivityResult
                {
                    Success = false,
                    ErrorMessage = "WARP WireGuard handshake did not complete or no traffic passed through the tunnel."
                };
            }

            var location = WarpManager.ResolveLocationFromTrace(traceBody);
            double downloadMbps = 0;

            if (measureSpeed)
            {
                try
                {
                    var speedSw = Stopwatch.StartNew();
                    byte[] bytes = await http.GetByteArrayAsync("https://speed.cloudflare.com/__down?bytes=262144").ConfigureAwait(false);
                    speedSw.Stop();
                    if (bytes.Length > 0 && speedSw.Elapsed.TotalSeconds > 0.001)
                    {
                        downloadMbps = (bytes.Length * 8.0) / (speedSw.Elapsed.TotalSeconds * 1_000_000.0);
                    }
                }
                catch { }
            }

            return new WarpConnectivityResult
            {
                Success = true,
                LatencyMs = latencyMs,
                DownloadMbps = downloadMbps,
                Location = location
            };
        }
        catch (Exception ex)
        {
            return new WarpConnectivityResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    private static int GetFreeLocalTcpPort() => UsqueManager.GetFreeLocalPort();

    private static async Task<bool> WaitForPortListeningAsync(Process process, int port, int timeoutMs)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (process.HasExited) return false;
            try
            {
                using var client = new TcpClient();
                using var cts = new CancellationTokenSource(250);
                await client.ConnectAsync(IPAddress.Loopback, port, cts.Token).ConfigureAwait(false);
                if (client.Connected)
                {
                    return true;
                }
            }
            catch { }
            await Task.Delay(50).ConfigureAwait(false);
        }
        return false;
    }

    public static bool IsIpv6Supported()
    {
        try
        {
            if (!Socket.OSSupportsIPv6)
            {
                return false;
            }

            var nics = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces();
            foreach (var nic in nics)
            {
                if (nic.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up &&
                    nic.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Loopback &&
                    nic.Supports(System.Net.NetworkInformation.NetworkInterfaceComponent.IPv6))
                {
                    var props = nic.GetIPProperties();
                    if (props != null && props.UnicastAddresses != null)
                    {
                        var hasV6Addr = props.UnicastAddresses.Any(a => a.Address.AddressFamily == AddressFamily.InterNetworkV6 && !a.Address.IsIPv6LinkLocal && !a.Address.IsIPv6SiteLocal);
                        if (hasV6Addr) return true;
                    }
                }
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    private string GenerateWarpConfig(WarpConfig warp, bool enableAdBlock, string? baseDir = null)
    {
        string configFile = Path.Combine(ResolveWritableConfigDir(baseDir), "singbox_warp.json");
        var configObj = BuildWarpConfigObject(warp, enableAdBlock, includeTun: true, mixedPort: 2080);

        File.WriteAllText(configFile, JsonSerializer.Serialize(configObj, new JsonSerializerOptions { WriteIndented = true }));
        _currentConfigFile = configFile;
        return configFile;
    }

    private string GenerateWarpMasqueConfig(int upstreamSocksPort, string endpointHost, bool enableAdBlock, string? baseDir = null)
    {
        string configFile = Path.Combine(ResolveWritableConfigDir(baseDir), "singbox_warp_masque.json");
        var configObj = BuildWarpMasqueConfigObject(upstreamSocksPort, endpointHost, enableAdBlock, includeTun: true, mixedPort: 2080);

        File.WriteAllText(configFile, JsonSerializer.Serialize(configObj, new JsonSerializerOptions { WriteIndented = true }));
        _currentConfigFile = configFile;
        return configFile;
    }

    private static string ResolveWritableConfigDir(string? baseDir)
    {
        string configDir = !string.IsNullOrWhiteSpace(baseDir) && Directory.Exists(baseDir)
            ? baseDir
            : Path.Combine(Path.GetTempPath(), "Ubour");

        try
        {
            Directory.CreateDirectory(configDir);
            string testFile = Path.Combine(configDir, ".write_test");
            File.WriteAllText(testFile, "test");
            File.Delete(testFile);
            return configDir;
        }
        catch
        {
            string fallback = Path.Combine(Path.GetTempPath(), "Ubour");
            Directory.CreateDirectory(fallback);
            return fallback;
        }
    }

    private static string GenerateWarpProbeConfig(WarpConfig warp, int mixedPort)
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), "Ubour");
        Directory.CreateDirectory(tmpDir);
        string configFile = Path.Combine(tmpDir, $"singbox_warp_probe_{Guid.NewGuid():N}.json");
        var configObj = BuildWarpConfigObject(warp, enableAdBlock: false, includeTun: false, mixedPort: mixedPort);
        File.WriteAllText(configFile, JsonSerializer.Serialize(configObj, new JsonSerializerOptions { WriteIndented = true }));
        return configFile;
    }

    private static string GenerateWarpMasqueProbeConfig(int upstreamSocksPort, string endpointHost, int mixedPort)
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), "Ubour");
        Directory.CreateDirectory(tmpDir);
        string configFile = Path.Combine(tmpDir, $"singbox_warp_masque_probe_{Guid.NewGuid():N}.json");
        var configObj = BuildWarpMasqueConfigObject(upstreamSocksPort, endpointHost, enableAdBlock: false, includeTun: false, mixedPort: mixedPort);
        File.WriteAllText(configFile, JsonSerializer.Serialize(configObj, new JsonSerializerOptions { WriteIndented = true }));
        return configFile;
    }

    private static object BuildWarpMasqueConfigObject(int upstreamSocksPort, string endpointHost, bool enableAdBlock, bool includeTun, int mixedPort)
    {
        var dnsObj = BuildDnsConfig(enableAdBlock, "warp-ep", "1.1.1.1", endpointHost, useTcpRemoteDns: true);
        var routeRules = BuildRouteRules(enableAdBlock, "warp-ep", endpointHost, endpointPort: 0, isMasqueSocks: true);

        var inboundsList = new List<object>();
        if (includeTun)
        {
            inboundsList.Add(new
            {
                type = "tun",
                tag = "tun-in",
                interface_name = "Ubour-tun",
                address = new string[] { "172.19.0.1/30" },
                auto_route = true,
                strict_route = false,
                stack = "system"
            });
        }

        inboundsList.Add(new
        {
            type = "mixed",
            tag = "mixed-in",
            listen = "127.0.0.1",
            listen_port = mixedPort
        });

        return new
        {
            log = new { level = "info" },
            dns = dnsObj,
            inbounds = inboundsList.ToArray(),
            outbounds = new object[]
            {
                new
                {
                    type = "socks",
                    tag = "warp-ep",
                    server = "127.0.0.1",
                    server_port = upstreamSocksPort
                },
                new
                {
                    type = "direct",
                    tag = "direct",
                    domain_resolver = "dns-direct"
                }
            },
            route = new
            {
                rules = routeRules,
                auto_detect_interface = true,
                default_domain_resolver = "dns-direct"
            }
        };
    }

    private static object BuildWarpConfigObject(WarpConfig warp, bool enableAdBlock, bool includeTun, int mixedPort)
    {
        var dnsObj = BuildDnsConfig(enableAdBlock, "warp-ep", "1.1.1.1", warp.EndpointHost);
        var routeRules = BuildRouteRules(enableAdBlock, "warp-ep", warp.EndpointHost, warp.EndpointPort);

        bool hasIpv6 = IsIpv6Supported();
        string[] addresses = hasIpv6 && !string.IsNullOrWhiteSpace(warp.LocalIpv6)
            ? new string[] { warp.LocalIpv4, warp.LocalIpv6 }
            : new string[] { warp.LocalIpv4 };

        string[] allowedIps = hasIpv6
            ? new string[] { "0.0.0.0/0", "::/0" }
            : new string[] { "0.0.0.0/0" };

        var inboundsList = new List<object>();
        if (includeTun)
        {
            inboundsList.Add(new
            {
                type = "tun",
                tag = "tun-in",
                interface_name = "Ubour-tun",
                address = new string[] { "172.19.0.1/30" },
                auto_route = true,
                strict_route = false,
                stack = "system"
            });
        }

        inboundsList.Add(new
        {
            type = "mixed",
            tag = "mixed-in",
            listen = "127.0.0.1",
            listen_port = mixedPort
        });

        return new
        {
            log = new { level = "info" },
            dns = dnsObj,
            endpoints = new object[]
            {
                new
                {
                    type = "wireguard",
                    tag = "warp-ep",
                    detour = "direct-warp",
                    system = false,
                    name = "warp",
                    address = addresses,
                    private_key = warp.PrivateKey,
                    mtu = 1280,
                    peers = new object[]
                    {
                        new
                        {
                            address = warp.EndpointHost,
                            port = warp.EndpointPort,
                            public_key = warp.PeerPublicKey,
                            allowed_ips = allowedIps,
                            reserved = new int[] { warp.Reserved[0], warp.Reserved[1], warp.Reserved[2] }
                        }
                    }
                }
            },
            inbounds = inboundsList.ToArray(),
            outbounds = new object[]
            {
                new
                {
                    type = "direct",
                    tag = "direct-warp",
                    udp_fragment = true,
                    domain_resolver = "dns-direct"
                },
                new
                {
                    type = "direct",
                    tag = "direct",
                    domain_resolver = "dns-direct"
                }
            },
            route = new
            {
                rules = routeRules,
                auto_detect_interface = true,
                default_domain_resolver = "dns-direct"
            }
        };
    }

    private string GenerateAdBlockOnlyConfig()
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), "Ubour");
        Directory.CreateDirectory(tmpDir);
        string configFile = Path.Combine(tmpDir, "singbox_adblock.json");

        var dnsObj = BuildDnsConfig();
        var routeRules = BuildRouteRules(true, "direct");

        var configObj = new
        {
            log = new { level = "info" },
            dns = dnsObj,
            inbounds = new object[]
            {
                new
                {
                    type = "mixed",
                    tag = "mixed-in",
                    listen = "127.0.0.1",
                    listen_port = 2080
                }
            },
            outbounds = new object[]
            {
                new
                {
                    type = "direct",
                    tag = "direct"
                }
            },
            route = new
            {
                rules = routeRules,
                auto_detect_interface = true
            }
        };

        File.WriteAllText(configFile, JsonSerializer.Serialize(configObj, new JsonSerializerOptions { WriteIndented = true }));
        _currentConfigFile = configFile;
        return configFile;
    }

    private string? GenerateVlessConfig(string vlessUrl, bool enableAdBlock)
    {
        try
        {
            if (!vlessUrl.StartsWith("vless://", StringComparison.OrdinalIgnoreCase)) return null;
            string raw = vlessUrl.Substring(8);
            int atIdx = raw.IndexOf('@');
            int colonIdx = raw.IndexOf(':', atIdx);
            int qIdx = raw.IndexOf('?', colonIdx);

            string uuid = raw.Substring(0, atIdx);
            string host = raw.Substring(atIdx + 1, colonIdx - atIdx - 1);
            int hashIdx = raw.IndexOf('#');
            string portStr;
            if (qIdx != -1)
            {
                portStr = raw.Substring(colonIdx + 1, qIdx - colonIdx - 1);
            }
            else if (hashIdx != -1 && hashIdx > colonIdx)
            {
                portStr = raw.Substring(colonIdx + 1, hashIdx - colonIdx - 1);
            }
            else
            {
                portStr = raw.Substring(colonIdx + 1);
            }
            int port = int.TryParse(portStr, out int p) ? p : 443;

            string sni = host;
            string pbk = "";
            string sid = "";

            if (qIdx != -1)
            {
                string query = raw.Substring(qIdx + 1);
                int qHashIdx = query.IndexOf('#');
                if (qHashIdx != -1)
                {
                    query = query.Substring(0, qHashIdx);
                }
                string[] parts = query.Split('&');
                foreach (var part in parts)
                {
                    string[] kv = part.Split('=');
                    if (kv.Length == 2)
                    {
                        if (kv[0].Equals("sni", StringComparison.OrdinalIgnoreCase)) sni = kv[1];
                        if (kv[0].Equals("pbk", StringComparison.OrdinalIgnoreCase)) pbk = kv[1];
                        if (kv[0].Equals("sid", StringComparison.OrdinalIgnoreCase)) sid = kv[1];
                    }
                }
            }

            string tmpDir = Path.Combine(Path.GetTempPath(), "Ubour");
            Directory.CreateDirectory(tmpDir);
            string configFile = Path.Combine(tmpDir, "singbox_vless.json");

            var dnsObj = BuildDnsConfig(enableAdBlock);
            var routeRules = BuildRouteRules(enableAdBlock, "vless-out");

            var configObj = new
            {
                log = new { level = "info" },
                dns = dnsObj,
                inbounds = new object[]
                {
                    new
                    {
                        type = "mixed",
                        tag = "mixed-in",
                        listen = "127.0.0.1",
                        listen_port = 2080
                    }
                },
                outbounds = new object[]
                {
                    new
                    {
                        type = "vless",
                        tag = "vless-out",
                        server = host,
                        server_port = port,
                        uuid = uuid,
                        flow = "xtls-rprx-vision",
                        tls = new
                        {
                            enabled = true,
                            server_name = sni,
                            utls = new
                            {
                                enabled = true,
                                fingerprint = "chrome"
                            },
                            reality = new
                            {
                                enabled = !string.IsNullOrEmpty(pbk),
                                public_key = pbk,
                                short_id = sid
                            }
                        }
                    },
                    new
                    {
                        type = "direct",
                        tag = "direct"
                    }
                },
                route = new
                {
                    rules = routeRules,
                    auto_detect_interface = true
                }
            };

            File.WriteAllText(configFile, JsonSerializer.Serialize(configObj, new JsonSerializerOptions { WriteIndented = true }));
            _currentConfigFile = configFile;
            return configFile;
        }
        catch
        {
            return null;
        }
    }

    private static object BuildDnsConfig(bool enableAdBlock = true, string mainOutbound = "direct", string upstreamDns = "1.1.1.1", string? bypassDomain = null, bool useTcpRemoteDns = false)
    {
        bool needsDirectResolver = mainOutbound != "direct";
        bool hasHostnameBypass = needsDirectResolver &&
                                 !string.IsNullOrWhiteSpace(bypassDomain) &&
                                 !IPAddress.TryParse(bypassDomain, out _);

        if (enableAdBlock)
        {
            var servers = new List<object>
            {
                new
                {
                    tag = "dns-filter",
                    type = "udp",
                    server = "127.0.0.1",
                    server_port = 53
                }
            };

            if (needsDirectResolver)
            {
                servers.Add(new
                {
                    tag = "dns-direct",
                    type = "udp",
                    server = upstreamDns,
                    server_port = 53
                });
            }

            if (hasHostnameBypass)
            {
                return new
                {
                    servers = servers.ToArray(),
                    rules = new object[]
                    {
                        new
                        {
                            domain = new string[] { bypassDomain! },
                            action = "route",
                            server = "dns-direct"
                        }
                    },
                    final = "dns-filter"
                };
            }

            return new
            {
                servers = servers.ToArray(),
                final = "dns-filter"
            };
        }
        else
        {
            var serverObj = new Dictionary<string, object>
            {
                ["tag"] = "dns-remote",
                ["type"] = useTcpRemoteDns ? "tcp" : "udp",
                ["server"] = upstreamDns,
                ["server_port"] = 53
            };
            if (mainOutbound != "direct")
            {
                serverObj["detour"] = mainOutbound;
            }

            var servers = new List<object> { serverObj };
            if (needsDirectResolver)
            {
                servers.Add(new
                {
                    tag = "dns-direct",
                    type = "udp",
                    server = upstreamDns,
                    server_port = 53
                });
            }

            if (hasHostnameBypass)
            {
                return new
                {
                    servers = servers.ToArray(),
                    rules = new object[]
                    {
                        new
                        {
                            domain = new string[] { bypassDomain! },
                            action = "route",
                            server = "dns-direct"
                        }
                    },
                    final = "dns-remote"
                };
            }

            return new
            {
                servers = servers.ToArray(),
                final = "dns-remote"
            };
        }
    }

    private static object[] BuildRouteRules(bool enableAdBlock, string mainOutbound, string? endpointHost = null, int endpointPort = 0, bool isMasqueSocks = false)
    {
        var rules = new List<object>();

        if (isMasqueSocks)
        {
            // Keep usque.exe direct BEFORE any catch-all / QUIC block rules to avoid TUN routing loops.
            rules.Add(new
            {
                process_name = new string[] { "usque.exe" },
                action = "route",
                outbound = "direct"
            });
            rules.Add(new
            {
                ip_cidr = new string[] { "162.159.198.0/24", "162.159.199.0/24" },
                action = "route",
                outbound = "direct"
            });
            rules.Add(new
            {
                domain = new string[] { "consumer-masque.cloudflareclient.com", "api.cloudflareclient.com" },
                action = "route",
                outbound = "direct"
            });
        }

        if (mainOutbound != "direct")
        {
            rules.Add(new
            {
                process_name = new string[] { "Ubour.exe" },
                action = "route",
                outbound = mainOutbound
            });
        }

        rules.Add(new
        {
            port = new int[] { 53 },
            action = "hijack-dns"
        });

        if (enableAdBlock)
        {
            rules.Add(new
            {
                domain = new string[] { "ipv6a.cloudflare-dns.com", "ipv6b.cloudflare-dns.com" },
                action = "route",
                outbound = "direct"
            });
            rules.Add(new
            {
                port = new int[] { 853 },
                action = "reject"
            });
            rules.Add(new
            {
                network = "udp",
                port = new int[] { 443 },
                action = "reject"
            });
            rules.Add(new
            {
                domain_suffix = new string[]
                {
                    "dns.google",
                    "dns.google.com",
                    "cloudflare-dns.com",
                    "chrome.cloudflare-dns.com",
                    "mozilla.cloudflare-dns.com",
                    "dns.quad9.net",
                    "doh.opendns.com",
                    "dns.nextdns.io",
                    "doh.cleanbrowsing.org",
                    "dns.alidns.com",
                    "doh.pub"
                },
                action = "reject"
            });
            rules.Add(new
            {
                ip_cidr = new string[] { "0.0.0.0/32", "::0/128" },
                action = "reject"
            });
        }
        else if (isMasqueSocks)
        {
            // Force browser HTTP/3 traffic onto TLS/TCP inside the MASQUE SOCKS tunnel instead of stalling on SOCKS5 UDP.
            rules.Add(new
            {
                network = "udp",
                port = new int[] { 443 },
                action = "reject"
            });
        }

        if (!string.IsNullOrWhiteSpace(endpointHost))
        {
            if (IPAddress.TryParse(endpointHost, out _))
            {
                rules.Add(new
                {
                    ip_cidr = new string[] { $"{endpointHost}/32" },
                    action = "route",
                    outbound = "direct"
                });
            }
            else
            {
                rules.Add(new
                {
                    domain = new string[] { endpointHost },
                    action = "route",
                    outbound = "direct"
                });
            }
        }

        if (endpointPort > 0 && !isMasqueSocks)
        {
            rules.Add(new
            {
                network = "udp",
                port = new int[] { endpointPort },
                action = "route",
                outbound = "direct"
            });
        }

        if (mainOutbound != "direct")
        {
            rules.Add(new
            {
                inbound = new string[] { "tun-in", "mixed-in" },
                action = "route",
                outbound = mainOutbound
            });
        }

        return rules.ToArray();
    }

    private static Process? LaunchSingboxProcess(string baseDir, string configFile, Action<string, bool> onLogLine)
    {
        string arch = Environment.Is64BitOperatingSystem ? "x86_64" : "x86";
        string exePath = Path.Combine(baseDir, "engine", arch, "sing-box.exe");

        if (!File.Exists(exePath))
        {
            exePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "engine", arch, "sing-box.exe");
        }

        if (!File.Exists(exePath)) return null;

        string workDir = Path.GetDirectoryName(exePath)!;

        var startInfo = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = $"run -c \"{configFile}\"",
            WorkingDirectory = workDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.EnvironmentVariables["ENABLE_DEPRECATED_MISSING_DOMAIN_RESOLVER"] = "true";

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.OutputDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) onLogLine(e.Data, false); };
        process.ErrorDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) onLogLine(e.Data, true); };
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }

    private bool StartProcess(string baseDir, string configFile)
    {
        try
        {
            _process = LaunchSingboxProcess(baseDir, configFile, HandleProcessLog);
            AppLogger.Info($"[sing-box] Process started with config: {configFile}");
            if (_process == null || _process.HasExited) return false;

            Thread.Sleep(300);
            return !_process.HasExited;
        }
        catch
        {
            return false;
        }
    }

    private void HandleProcessLog(string line, bool isError)
    {
        if (line.Contains("inbound/tun[tun-in]: started at Ubour-tun", StringComparison.OrdinalIgnoreCase))
            _tunStarted = true;

        if (line.Contains("endpoint/wireguard[warp-ep]", StringComparison.OrdinalIgnoreCase) &&
            (line.Contains("unable to update bind", StringComparison.OrdinalIgnoreCase) ||
             line.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
             line.Contains("fatal", StringComparison.OrdinalIgnoreCase)))
            _warpStartupError ??= line;

        if (line.Contains("inbound/tun[tun-in]", StringComparison.OrdinalIgnoreCase) &&
            (line.Contains("error", StringComparison.OrdinalIgnoreCase) || line.Contains("failed", StringComparison.OrdinalIgnoreCase)))
            _warpStartupError ??= line;

        if (line.Contains("outbound/socks[warp-ep]", StringComparison.OrdinalIgnoreCase) &&
            line.Contains("fatal", StringComparison.OrdinalIgnoreCase))
            _warpStartupError ??= line;

        if (isError) AppLogger.Warn($"[sing-box] {line}");
        else AppLogger.Info($"[sing-box] {line}");
    }

    public void Stop()
    {
        try
        {
            ProxyManager.DisableProxy();

            if (_process != null)
            {
                if (!_process.HasExited)
                {
                    _process.Kill(true);
                    _process.WaitForExit(1000);
                }
                _process.Dispose();
                _process = null;
            }

            if (!string.IsNullOrEmpty(_currentConfigFile) && File.Exists(_currentConfigFile))
            {
                if (_currentConfigFile.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase))
                {
                    try { File.Delete(_currentConfigFile); } catch { }
                }
                _currentConfigFile = null;
            }
        }
        catch { }
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }
}
