using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;
using Ubour.Models;
using Ubour.Services;

namespace Ubour.Tests;

public class Issue36HistoricalScenariosTests
{
    private static string GetSingboxExePath()
    {
        string baseDir = TestPackagePaths.GetWindowsX64PackageDirectory();
        string exe = Path.Combine(baseDir, "engine", "x86_64", "sing-box.exe");
        if (File.Exists(exe)) return exe;

        string fallback = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\Ubour-windows-x64\engine\x86_64\sing-box.exe"));
        if (File.Exists(fallback)) return fallback;

        throw new FileNotFoundException($"sing-box.exe not found at {exe} or {fallback}");
    }

    [Fact]
    public void Scenario1_v1_6_4_WireGuardEndpointWithoutDetour_StructureAnalysis()
    {
        // In v1.6.4 (Issue #36 Comment 0):
        // WireGuard endpoint was configured without any "detour".
        // When detour is missing, sing-box initializes a dual-stack wildcard listener (listen udp6 [::]:port).
        // On Windows machines where IPv6 is disabled on the network adapter, Winsock returns WSAEINVAL (10022).
        var v164Config = new
        {
            log = new { level = "info" },
            endpoints = new object[]
            {
                new
                {
                    type = "wireguard",
                    tag = "warp-ep",
                    system = false,
                    name = "warp",
                    address = new string[] { "172.16.0.2/32", "2606:4700:110:8b48:c63a:9238:b4ec:2dff/128" },
                    private_key = "kPb52PbQsB2vh+sjoNzSvhMhmGcEmFF7ORfcexUSKkI=",
                    peers = new object[]
                    {
                        new
                        {
                            address = "162.159.195.1",
                            port = 500,
                            public_key = "bmXOC+F1FxEMF9dyiK2H5/1SUtzH0JuVo51h2wPfgyo=",
                            allowed_ips = new string[] { "0.0.0.0/0", "::/0" }
                        }
                    }
                }
            },
            inbounds = new object[]
            {
                new { type = "mixed", tag = "mixed-in", listen = "127.0.0.1", listen_port = 2080 }
            },
            outbounds = new object[]
            {
                new { type = "direct", tag = "direct" }
            }
        };

        string json = JsonSerializer.Serialize(v164Config);
        Assert.DoesNotContain("detour", json);
        Assert.Contains("2606:4700", json);
    }

    [Fact(Skip = "This historical reproduction sends traffic to the public Cloudflare WARP endpoint. It is not suitable for an offline/repeatable test.")]
    public async Task Scenario2_v1_6_5_DetourToEmptyDirectOutbound_FailsWithDeterministicError()
    {
        // In v1.6.5 (commit bc1bda4):
        // "detour": "direct" was added to warp-ep, but "direct" outbound was empty (no options).
        // sing-box fails when routing through empty direct detour:
        // "connect to server: detour to an empty direct outbound makes no sense"
        string exe = GetSingboxExePath();
        string tempConfig = Path.Combine(Path.GetTempPath(), $"s2_test_{Guid.NewGuid():N}.json");

        int testPort = 2191;
        var s2Config = new
        {
            log = new { level = "debug" },
            endpoints = new object[]
            {
                new
                {
                    type = "wireguard",
                    tag = "warp-ep",
                    detour = "direct",
                    address = new string[] { "172.16.0.2/32" },
                    private_key = "8ELLb5KUvwsJxc0mQjkBaWYHsxFZG6pkHkVjRMHw52g=",
                    peers = new object[]
                    {
                        new
                        {
                            address = "188.114.97.1",
                            port = 4500,
                            public_key = "bmXOC+F1FxEMF9dyiK2H5/1SUtzH0JuVo51h2wPfgyo=",
                            allowed_ips = new string[] { "0.0.0.0/0" },
                            reserved = new int[] { 217, 49, 109 }
                        }
                    }
                }
            },
            inbounds = new object[]
            {
                new { type = "mixed", tag = "mixed-in", listen = "127.0.0.1", listen_port = testPort }
            },
            outbounds = new object[]
            {
                new { type = "direct", tag = "direct" }
            },
            route = new
            {
                rules = new object[]
                {
                    new { inbound = new string[] { "mixed-in" }, action = "route", outbound = "warp-ep" }
                }
            }
        };

        File.WriteAllText(tempConfig, JsonSerializer.Serialize(s2Config), new UTF8Encoding(false));

        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = $"run -c \"{tempConfig}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };

        var proc = Process.Start(psi);
        Assert.NotNull(proc);

        var logBuilder = new StringBuilder();
        proc.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (logBuilder) logBuilder.AppendLine(e.Data); };
        proc.BeginErrorReadLine();

        try
        {
            await Task.Delay(800);
            // Trigger connection through the proxy
            try
            {
                using var tcp = new TcpClient();
                await tcp.ConnectAsync(IPAddress.Loopback, testPort);
                var stream = tcp.GetStream();
                stream.Write(new byte[] { 5, 1, 0 }, 0, 3);
                var buf = new byte[2];
                await stream.ReadExactlyAsync(buf, 0, 2);
                var req = new byte[] { 5, 1, 0, 1, 1, 1, 1, 1, 0, 80 };
                stream.Write(req, 0, req.Length);
                await Task.Delay(1000);
            }
            catch { }

            string logs;
            lock (logBuilder) logs = logBuilder.ToString();

            Assert.Contains("detour to an empty direct outbound makes no sense", logs);
        }
        finally
        {
            try { proc.Kill(); } catch { }
            try { File.Delete(tempConfig); } catch { }
        }
    }

    [Fact(Skip = "This legacy test starts TUN/WireGuard and sends traffic to a public endpoint; a local process/port check cannot prove a WARP handshake.")]
    public async Task CurrentCodebase_LiveWarpHandshake_RequiresExternalEndpointAndSystemTunnel()
    {
        // Test current implementation:
        // 1. Non-empty detour "direct-warp" with "udp_fragment": true.
        // 2. warp-ep uses "detour": "direct-warp".
        // 3. IsIpv6Supported checks active interfaces and ignores link-local/site-local.
        // 4. Outer WireGuard packets use direct outbound dialer without raw udp6 bind.
        // 5. Successful handshake initiated and received.
        string exe = GetSingboxExePath();
        string tempConfig = Path.Combine(Path.GetTempPath(), $"current_resolved_{Guid.NewGuid():N}.json");

        int testPort = 2192;
        var warpConfig = new WarpConfig
        {
            LocalIpv4 = "172.16.0.2/32",
            LocalIpv6 = "2606:4700:110:8f68:da91:a9d3:26d6:7b7d/128",
            PrivateKey = "8ELLb5KUvwsJxc0mQjkBaWYHsxFZG6pkHkVjRMHw52g=",
            PeerPublicKey = "bmXOC+F1FxEMF9dyiK2H5/1SUtzH0JuVo51h2wPfgyo=",
            EndpointHost = "188.114.97.1",
            EndpointPort = 4500,
            Reserved = new int[] { 217, 49, 109 }
        };

        var manager = new SingboxManager();
        var generateMethod = typeof(SingboxManager).GetMethod("GenerateWarpConfig", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(generateMethod);

        string? configPath = generateMethod.Invoke(manager, new object?[] { warpConfig, true, null }) as string;
        Assert.NotNull(configPath);
        Assert.True(File.Exists(configPath));

        string configJson = File.ReadAllText(configPath);

        // Verification of architecture safeguards
        Assert.Contains("\"detour\": \"direct-warp\"", configJson);
        Assert.Contains("\"tag\": \"direct-warp\"", configJson);
        Assert.Contains("\"udp_fragment\": true", configJson);
        Assert.Contains("\"auto_detect_interface\": true", configJson);
        Assert.Contains("\"tag\": \"tun-in\"", configJson);
        Assert.Contains("\"tag\": \"mixed-in\"", configJson);

        // Test sing-box running with non-empty detour
        var resolvedTestConfig = new
        {
            log = new { level = "debug" },
            endpoints = new object[]
            {
                new
                {
                    type = "wireguard",
                    tag = "warp-ep",
                    detour = "direct-warp",
                    address = new string[] { "172.16.0.2/32" },
                    private_key = "8ELLb5KUvwsJxc0mQjkBaWYHsxFZG6pkHkVjRMHw52g=",
                    peers = new object[]
                    {
                        new
                        {
                            address = "188.114.97.1",
                            port = 4500,
                            public_key = "bmXOC+F1FxEMF9dyiK2H5/1SUtzH0JuVo51h2wPfgyo=",
                            allowed_ips = new string[] { "0.0.0.0/0" },
                            reserved = new int[] { 217, 49, 109 }
                        }
                    }
                }
            },
            inbounds = new object[]
            {
                new { type = "mixed", tag = "mixed-in", listen = "127.0.0.1", listen_port = testPort }
            },
            outbounds = new object[]
            {
                new { type = "direct", tag = "direct-warp", udp_fragment = true },
                new { type = "direct", tag = "direct" }
            },
            route = new
            {
                rules = new object[]
                {
                    new { inbound = new string[] { "mixed-in" }, action = "route", outbound = "warp-ep" }
                }
            }
        };

        File.WriteAllText(tempConfig, JsonSerializer.Serialize(resolvedTestConfig), new UTF8Encoding(false));

        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = $"run -c \"{tempConfig}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };

        var proc = Process.Start(psi);
        Assert.NotNull(proc);

        var logBuilder = new StringBuilder();
        proc.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (logBuilder) logBuilder.AppendLine(e.Data); };
        proc.BeginErrorReadLine();

        try
        {
            await Task.Delay(800);
            try
            {
                using var tcp = new TcpClient();
                await tcp.ConnectAsync(IPAddress.Loopback, testPort);
                var stream = tcp.GetStream();
                stream.Write(new byte[] { 5, 1, 0 }, 0, 3);
                var buf = new byte[2];
                await stream.ReadExactlyAsync(buf, 0, 2);
                var req = new byte[] { 5, 1, 0, 1, 1, 1, 1, 1, 0, 80 };
                stream.Write(req, 0, req.Length);
                await Task.Delay(2000);
            }
            catch { }

            string logs;
            lock (logBuilder) logs = logBuilder.ToString();

            // Assert: NO empty direct outbound error
            Assert.DoesNotContain("detour to an empty direct outbound makes no sense", logs);

            // Assert: NO dual-stack wildcard bind error
            Assert.DoesNotContain("unable to update bind", logs);

            // Assert: WireGuard handshake successfully sent and received
            Assert.Contains("sending handshake initiation", logs);
            Assert.Contains("received handshake response", logs);
        }
        finally
        {
            try { proc.Kill(); } catch { }
            try { File.Delete(tempConfig); } catch { }
            try { File.Delete(configPath); } catch { }
        }
    }
}
