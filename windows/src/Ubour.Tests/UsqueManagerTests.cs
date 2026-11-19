using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Ubour.Services;
using Xunit;

namespace Ubour.Tests;

public class UsqueManagerTests
{
    [Fact]
    public async Task UsqueManager_EnsureRegisteredAndLiveQuicAndHttp2_ConnectPassTrafficAndUnlock()
    {
        string baseDir = TestPackagePaths.GetWindowsX64PackageDirectory();
        string tempDir = Path.Combine(Path.GetTempPath(), "UbourUsqueTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        string cfgPath = Path.Combine(tempDir, "warp_masque.json");

        try
        {
            bool regOk = await UsqueManager.EnsureRegisteredAsync(baseDir, cfgPath);
            Assert.True(regOk);
            Assert.True(UsqueManager.IsValidConfigFile(cfgPath));

            // 1. Live MASQUE HTTP/3 QUIC (port 443)
            var (quicRes, quicUsedH2) = await UsqueManager.VerifyMasqueConnectivityAsync(
                baseDir, allowHttp2Fallback: false, timeoutMs: 8000, measureSpeed: true, configPathOverride: cfgPath);
            Assert.True(quicRes.Success, quicRes.ErrorMessage ?? "MASQUE QUIC 443 failed");
            Assert.False(quicUsedH2);
            Assert.NotNull(quicRes.Location);
            Assert.False(string.IsNullOrWhiteSpace(quicRes.Location!.ColoCode));
            Assert.True(quicRes.DownloadMbps > 0, "Expected positive download speed over MASQUE QUIC");

            // 2. Live MASQUE HTTP/2 TCP+TLS (port 443)
            using (var usqueH2 = new UsqueManager())
            {
                var h2Res = await usqueH2.StartAndVerifyAsync(
                    baseDir, useHttp2: true, timeoutMs: 8000, measureSpeed: true, configPathOverride: cfgPath);
                Assert.True(h2Res.Success, h2Res.ErrorMessage ?? "MASQUE HTTP/2 443 failed");
                Assert.NotNull(h2Res.Location);
                Assert.True(h2Res.DownloadMbps > 0, "Expected positive download speed over MASQUE HTTP/2");
                usqueH2.Stop();
                Assert.False(usqueH2.IsRunning);
            }

            // 3. Full sing-box + usque end-to-end chain + AdBlock TCP-over-SOCKS DNS test
            using (var usque = new UsqueManager())
            {
                var startRes = await usque.StartAndVerifyAsync(
                    baseDir, useHttp2: false, timeoutMs: 8000, measureSpeed: false, configPathOverride: cfgPath);
                Assert.True(startRes.Success, startRes.ErrorMessage);

                using var sb = new SingboxManager();
                var chainRes = await sb.VerifyWarpMasqueChainAsync(baseDir, usque.ListenPort, usque.EndpointHost, timeoutMs: 7000, measureSpeed: true);
                Assert.True(chainRes.Success, chainRes.ErrorMessage ?? "sing-box -> usque chain failed");
                Assert.NotNull(chainRes.Location);
                Assert.True(chainRes.DownloadMbps > 0);

                // AdBlockEngine with upstreamSocksPort routes allowed DNS queries over MASQUE and still blocks ads
                AdBlockEngine.Initialize(baseDir);
                int dnsPort = GetFreeTcpAndUdpPort();
                var ad = new AdBlockEngine();
                Assert.True(ad.Start(upstreamDns: "1.1.1.1", port: dnsPort, upstreamSocksPort: usque.ListenPort));
                try
                {
                    Assert.True(ad.VerifyHealth(dnsPort, 2000));

                    byte[] blocked = SendUdpDnsQuery(dnsPort, BuildDnsQuery("doubleclick.net"));
                    Assert.True(blocked.Length > 12 && (blocked[3] & 0x0F) == 0 && blocked[^4] == 0 && blocked[^1] == 0);

                    byte[] allowed = SendUdpDnsQuery(dnsPort, BuildDnsQuery("example.com"));
                    Assert.True(allowed.Length > 20 && (allowed[3] & 0x0F) == 0 && allowed[7] >= 1);
                }
                finally
                {
                    ad.Stop();
                    Assert.False(ad.IsRunning);
                }

                usque.Stop();
            }

            // 4. Verify usque.exe binary is unlocked cleanly on disk after Stop
            string? exePath = UsqueManager.ResolveExecutablePath(baseDir);
            Assert.NotNull(exePath);
            using (var fs = File.Open(exePath!, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                Assert.True(fs.CanRead);
            }
        }
        finally
        {
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void WatchdogAndGlobalCleanup_IncludeUsqueCleanly()
    {
        string script = WatchdogService.BuildWatchdogScript(
            1234,
            new (int, long, string)[]
            {
                (5678, 999999L, @"C:\Ubour\engine\x86_64\sing-box.exe"),
                (5679, 999999L, @"C:\Ubour\engine\x86_64\usque.exe")
            },
            stopWinDivert: false);

        Assert.Contains("sing-box.exe", script);
        Assert.Contains("usque.exe", script);

        using var usque = new UsqueManager();
        App.RegisterSessionManagers(null, null, null, usque);
        try
        {
            var ex = Record.Exception(() => App.GlobalCleanup());
            Assert.Null(ex);
            Assert.False(usque.IsRunning);
        }
        finally
        {
            App.RegisterSessionManagers(null, null, null, null);
        }
    }

    private static int GetFreeTcpAndUdpPort()
    {
        using var tcp = new TcpListener(IPAddress.Loopback, 0);
        tcp.Start();
        return ((IPEndPoint)tcp.LocalEndpoint).Port;
    }

    private static byte[] BuildDnsQuery(string domain)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        bw.Write((byte)0x42); bw.Write((byte)0x19);
        bw.Write((byte)0x01); bw.Write((byte)0x00);
        bw.Write((byte)0x00); bw.Write((byte)0x01);
        bw.Write((byte)0x00); bw.Write((byte)0x00);
        bw.Write((byte)0x00); bw.Write((byte)0x00);
        bw.Write((byte)0x00); bw.Write((byte)0x00);
        foreach (string label in domain.Split('.'))
        {
            byte[] lb = Encoding.ASCII.GetBytes(label);
            bw.Write((byte)lb.Length);
            bw.Write(lb);
        }
        bw.Write((byte)0x00);
        bw.Write((byte)0x00); bw.Write((byte)0x01);
        bw.Write((byte)0x00); bw.Write((byte)0x01);
        return ms.ToArray();
    }

    private static byte[] SendUdpDnsQuery(int port, byte[] query)
    {
        using var udp = new UdpClient();
        udp.Client.ReceiveTimeout = 5000;
        udp.Send(query, query.Length, "127.0.0.1", port);
        var ep = new IPEndPoint(IPAddress.Any, 0);
        return udp.Receive(ref ep);
    }
}
