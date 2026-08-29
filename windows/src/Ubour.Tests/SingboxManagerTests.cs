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

        string? configPath = method.Invoke(manager, new object[] { warpConfig, true }) as string;
        Assert.NotNull(configPath);
        Assert.True(File.Exists(configPath));

        string json = File.ReadAllText(configPath);
        Assert.Contains("wireguard", json);
        Assert.Contains("warp-ep", json);
        Assert.Contains("engage.cloudflareclient.com", json);
        Assert.Contains("dns-filter", json);
        Assert.Contains("mixed-in", json);

        try { File.Delete(configPath); } catch { }
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
}
