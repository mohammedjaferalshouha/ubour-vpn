using System;
using System.IO;
using System.Reflection;
using Xunit;
using Ubour.Models;
using Ubour.Services;

namespace Ubour.Tests;

public class SingboxManagerTests
{
    [Fact]
    public void SingboxManager_GenerateWarpConfig_ProducesValidJsonStructure()
    {
        string testDirectory = Path.Combine(Path.GetTempPath(), "UbourTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testDirectory);
        var manager = new SingboxManager();
        var warpConfig = new WarpConfig
        {
            LocalIpv4 = "172.16.0.2/32",
            LocalIpv6 = "2606:4700:110:8f68:da91:a9d3:26d6:7b7d/128",
            PrivateKey = "aGVsbG93b3JsZGhlbGxvd29ybGRoZWxsb3dvcmxkMQ==",
            PeerPublicKey = "bm90YXJlYWxrZXlub3RhcmVhbGtleW5vdGFyZWFsMQ==",
            EndpointHost = "engage.cloudflareclient.com",
            EndpointPort = 2408,
            Reserved = new int[] { 1, 2, 3 }
        };

        var method = typeof(SingboxManager).GetMethod("GenerateWarpConfig", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);

        string? configPath = method.Invoke(manager, new object?[] { warpConfig, true, testDirectory }) as string;
        Assert.NotNull(configPath);
        Assert.True(File.Exists(configPath));

        string json = File.ReadAllText(configPath);
        Assert.Contains("wireguard", json);
        Assert.DoesNotContain("\"detour\": \"direct\"", json);
        Assert.Contains("direct-warp", json);
        Assert.Contains("auto_detect_interface", json);
        Assert.Contains("tun-in", json);
        Assert.Contains("engage.cloudflareclient.com", json);
        Assert.Contains("dns-filter", json);
        Assert.Contains("mixed-in", json);

        using var config = System.Text.Json.JsonDocument.Parse(json);
        var dnsServer = config.RootElement.GetProperty("dns").GetProperty("servers")[0];
        Assert.Equal("127.0.0.1", dnsServer.GetProperty("server").GetString());
        Assert.Contains(
            config.RootElement.GetProperty("route").GetProperty("rules").EnumerateArray(),
            rule => rule.TryGetProperty("action", out var action) && action.GetString() == "hijack-dns");

        try { Directory.Delete(testDirectory, recursive: true); } catch { }
    }

    [Fact]
    public void SingboxManager_GeneratedWarpConfig_PassesBundledSingboxCheckWithoutStartingTunnel()
    {
        string testDirectory = Path.Combine(Path.GetTempPath(), "UbourTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testDirectory);
        var manager = new SingboxManager();
        var warpConfig = new WarpConfig
        {
            LocalIpv4 = "172.16.0.2/32",
            LocalIpv6 = "2606:4700:110:8f68:da91:a9d3:26d6:7b7d/128",
            PrivateKey = "aGVsbG93b3JsZGhlbGxvd29ybGRoZWxsb3dvcmxkMQ==",
            PeerPublicKey = "bm90YXJlYWxrZXlub3RhcmVhbGtleW5vdGFyZWFsMQ==",
            EndpointHost = "198.51.100.10",
            EndpointPort = 2408,
            Reserved = new[] { 1, 2, 3 }
        };

        try
        {
            var generate = typeof(SingboxManager).GetMethod("GenerateWarpConfig", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(generate);
            string? configPath = generate.Invoke(manager, new object?[] { warpConfig, true, testDirectory }) as string;
            Assert.NotNull(configPath);

            string baseDir = TestPackagePaths.GetWindowsX64PackageDirectory();
            string exePath = Path.Combine(baseDir, "engine", "x86_64", "sing-box.exe");
            Assert.True(File.Exists(exePath), $"Bundled sing-box not found at {exePath}");

            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = exePath,
                Arguments = $"check -c \"{configPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            Assert.NotNull(process);
            string stdout = process.StandardOutput.ReadToEnd();
            string stderr = process.StandardError.ReadToEnd();
            Assert.True(process.WaitForExit(10000), "sing-box check exceeded 10 seconds.");
            Assert.True(process.ExitCode == 0, $"sing-box check failed. stdout: {stdout}; stderr: {stderr}");
        }
        finally
        {
            try { Directory.Delete(testDirectory, recursive: true); } catch { }
        }
    }

    [Fact]
    public void SingboxManager_IsIpv6Supported_ExecutesSafely()
    {
        var exception = Record.Exception(() => SingboxManager.IsIpv6Supported());
        Assert.Null(exception);
    }

    [Fact]
    public void SingboxManager_WarpStartupLog_TracksTunStartupAndWireGuardBindFailure()
    {
        var manager = new SingboxManager();
        var handleLog = typeof(SingboxManager).GetMethod("HandleProcessLog", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(handleLog);

        handleLog.Invoke(manager, new object[] { "INFO inbound/tun[tun-in]: started at Ubour-tun", false });
        Assert.True(manager.HasTunStarted);

        const string bindFailure = "ERROR endpoint/wireguard[warp-ep]: unable to update bind: listen udp6 [::]:62088: An invalid argument was supplied.";
        handleLog.Invoke(manager, new object[] { bindFailure, true });
        Assert.Equal(bindFailure, manager.WarpStartupError);
        Assert.False(manager.VerifyTunRunning(timeoutMs: 100));
    }

    [Fact(Skip = "This legacy test starts a live WARP process and changes the Windows proxy. Use the offline generated-config validation instead.")]
    public async Task SingboxManager_StartWarp_WithIpv6Disabled_StartsAndConnectsSuccessfully()
    {
        var manager = new SingboxManager();
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

        string baseDir = TestPackagePaths.GetWindowsX64PackageDirectory();
        bool started = manager.StartWarp(baseDir, warpConfig, true);
        try
        {
            Assert.True(started);
            await Task.Delay(2000);
            Assert.True(manager.VerifyRunning(2080));
        }
        finally
        {
            manager.Stop();
        }
    }

    [Fact]
    public void SingboxManager_GenerateVlessConfig_ParsesRealityUrlCorrectly()
    {
        var manager = new SingboxManager();
        string sampleVless = "vless://b831381d-6324-4d53-ad4f-8cda48b30811@104.21.5.12:443?encryption=none&security=reality&sni=speedtest.net&fp=chrome&pbk=1y2z3a4b5c6d7e8f9g0h&sid=12345678#SampleServer";

        var method = typeof(SingboxManager).GetMethod("GenerateVlessConfig", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);

        string? configPath = method.Invoke(manager, new object[] { sampleVless, true }) as string;
        Assert.NotNull(configPath);
        Assert.True(File.Exists(configPath));

        string json = File.ReadAllText(configPath);
        Assert.Contains("vless", json);
        Assert.Contains("104.21.5.12", json);
        Assert.Contains("b831381d-6324-4d53-ad4f-8cda48b30811", json);
        Assert.Contains("speedtest.net", json);
        Assert.Contains("1y2z3a4b5c6d7e8f9g0h", json);

        try { File.Delete(configPath); } catch { }
    }

    [Fact]
    public void SingboxManager_GenerateAdBlockOnlyConfig_ProducesValidDirectRules()
    {
        var manager = new SingboxManager();
        var method = typeof(SingboxManager).GetMethod("GenerateAdBlockOnlyConfig", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);

        string? configPath = method.Invoke(manager, Array.Empty<object>()) as string;
        Assert.NotNull(configPath);
        Assert.True(File.Exists(configPath));

        string json = File.ReadAllText(configPath);
        Assert.Contains("dns-filter", json);
        Assert.Contains("hijack-dns", json);
        Assert.Contains("direct", json);

        try { File.Delete(configPath); } catch { }
    }

    [Fact]
    public void SingboxManager_DnsFilterAndAntiLeakRules_AreEnforced()
    {
        var manager = new SingboxManager();
        var method = typeof(SingboxManager).GetMethod("GenerateAdBlockOnlyConfig", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);

        string? configPath = method.Invoke(manager, Array.Empty<object>()) as string;
        Assert.NotNull(configPath);
        Assert.True(File.Exists(configPath));

        string json = File.ReadAllText(configPath);

        // 1. DNS hijacking on port 53
        Assert.Contains("\"action\": \"hijack-dns\"", json);
        Assert.Contains("53", json);

        // 2. Reject port 853 (DoT)
        Assert.Contains("853", json);

        // 3. Reject UDP port 443 (DoH / QUIC)
        Assert.Contains("\"network\": \"udp\"", json);
        Assert.Contains("443", json);

        // 4. Cloudflare IPv6 DoH resolvers whitelist
        Assert.Contains("ipv6a.cloudflare-dns.com", json);
        Assert.Contains("ipv6b.cloudflare-dns.com", json);

        // 5. Popular DoH domains rejection
        Assert.Contains("dns.google", json);
        Assert.Contains("cloudflare-dns.com", json);
        Assert.Contains("dns.quad9.net", json);

        // 6. Invalidation CIDR rejection
        Assert.Contains("0.0.0.0/32", json);
        Assert.Contains("::0/128", json);

        try { File.Delete(configPath); } catch { }
    }

    [Fact]
    public void SingboxManager_GenerateAdBlockOnlyConfig_SingboxCheck_PassesCleanly()
    {
        var manager = new SingboxManager();
        var method = typeof(SingboxManager).GetMethod("GenerateAdBlockOnlyConfig", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);

        string? configPath = method.Invoke(manager, Array.Empty<object>()) as string;
        Assert.NotNull(configPath);
        Assert.True(File.Exists(configPath));

        string baseDir = TestPackagePaths.GetWindowsX64PackageDirectory();
        string exePath = Path.Combine(baseDir, "engine", "x86_64", "sing-box.exe");

        if (File.Exists(exePath))
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = exePath,
                Arguments = $"check -c \"{configPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = System.Diagnostics.Process.Start(psi);
            Assert.NotNull(proc);
            proc.WaitForExit();
            Assert.Equal(0, proc.ExitCode);
        }

        try { File.Delete(configPath); } catch { }
    }

    [Fact]
    public void SingboxManager_GenerateVlessConfig_WithValidRealityKey_SingboxCheck_PassesCleanly()
    {
        var manager = new SingboxManager();
        // Valid 32-byte base64 reality pubkey with #SampleServer fragment
        string sampleVless = "vless://b831381d-6324-4d53-ad4f-8cda48b30811@104.21.5.12:443?encryption=none&security=reality&sni=speedtest.net&fp=chrome&pbk=bmXOC-F1FxEMF9dyiK2H5_1SUtzH0JuVo51h2wPfgyo&sid=12345678#SampleServer";

        var method = typeof(SingboxManager).GetMethod("GenerateVlessConfig", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);

        string? configPath = method.Invoke(manager, new object[] { sampleVless, true }) as string;
        Assert.NotNull(configPath);
        Assert.True(File.Exists(configPath));

        string json = File.ReadAllText(configPath);
        Assert.Contains("\"short_id\": \"12345678\"", json);
        Assert.DoesNotContain("#SampleServer", json);

        string baseDir = TestPackagePaths.GetWindowsX64PackageDirectory();
        string exePath = Path.Combine(baseDir, "engine", "x86_64", "sing-box.exe");

        if (File.Exists(exePath))
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = exePath,
                Arguments = $"check -c \"{configPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = System.Diagnostics.Process.Start(psi);
            Assert.NotNull(proc);
            proc.WaitForExit();
            Assert.Equal(0, proc.ExitCode);
        }

        try { File.Delete(configPath); } catch { }
    }
}
