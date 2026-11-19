using System;
using System.IO;
using Xunit;
using Ubour.Models;
using Ubour.Services;

namespace Ubour.Tests;

public class LocalizationAndSettingsTests
{
    [Theory]
    [InlineData("AppName")]
    [InlineData("BtnConnect")]
    [InlineData("BtnDisconnect")]
    [InlineData("StatusConnected")]
    [InlineData("StatusDisconnecting")]
    [InlineData("ModeWarp")]
    [InlineData("ModeAdBlockOnly")]
    [InlineData("ModeVpnOnly")]
    [InlineData("ModeVpnAdBlock")]
    [InlineData("DpiStrength")]
    [InlineData("WarpEndpointLabel")]
    [InlineData("BtnScanPorts")]
    [InlineData("ScanningPorts")]
    [InlineData("ScanningPortsReal")]
    [InlineData("PortScanFound")]
    [InlineData("PortScanFoundSpeed")]
    [InlineData("PortScanNoneFound")]
    [InlineData("WarpConnectFailed")]
    [InlineData("WarpProtocolLabel")]
    [InlineData("WarpProtocolAuto")]
    [InlineData("WarpProtocolMasque")]
    [InlineData("WarpProtocolWireGuard")]
    [InlineData("MasqueReadyStatus")]
    [InlineData("MasqueUnavailableStatus")]
    public void LocalizationManager_ContainsKeys_InBothLanguages(string key)
    {
        string arVal = LocalizationManager.Get(key, "ar");
        string enVal = LocalizationManager.Get(key, "en");

        Assert.False(string.IsNullOrEmpty(arVal));
        Assert.False(string.IsNullOrEmpty(enVal));
        Assert.NotEqual(key, arVal);
        Assert.NotEqual(key, enVal);
    }

    [Fact]
    public void AppSettings_SaveAndLoad_RoundtripsCorrectly()
    {
        string testDirectory = Path.Combine(Path.GetTempPath(), "UbourTests", Guid.NewGuid().ToString("N"));
        string settingsPath = Path.Combine(testDirectory, "settings.json");
        var settings = new AppSettings
        {
            Language = "en",
            Theme = "light",
            SelectedDns = "9.9.9.9",
            SelectedMode = AppOperationMode.CUSTOM_VLESS,
            DpiMode = "-5",
            CustomVlessUrl = "vless://test@1.2.3.4:443"
        };

        settings.WarpEndpointHost = "188.114.97.1";
        settings.WarpEndpointPort = 500;
        settings.WarpProtocol = "MASQUE";

        settings.Save(settingsPath);

        var loaded = AppSettings.Load(settingsPath);
        Assert.Equal("en", loaded.Language);
        Assert.Equal("light", loaded.Theme);
        Assert.Equal("9.9.9.9", loaded.SelectedDns);
        Assert.Equal(AppOperationMode.CUSTOM_VLESS, loaded.SelectedMode);
        Assert.Equal("-5", loaded.DpiMode);
        Assert.Equal("vless://test@1.2.3.4:443", loaded.CustomVlessUrl);
        Assert.Equal("188.114.97.1", loaded.WarpEndpointHost);
        Assert.Equal(500, loaded.WarpEndpointPort);
        Assert.Equal("MASQUE", loaded.WarpProtocol);

        try { Directory.Delete(testDirectory, recursive: true); } catch { }
    }

    [Fact]
    public void WarpManager_GetDefaultEndpoints_ContainsRecommendedPort4500()
    {
        var endpoints = WarpManager.GetDefaultEndpoints();
        Assert.NotEmpty(endpoints);
        Assert.Contains(endpoints, ep => ep.Port == 4500);
        Assert.Contains(endpoints, ep => ep.Port == 500);
        Assert.Contains(endpoints, ep => ep.Port == 1701);
        Assert.Equal(4500, endpoints[0].Port);
    }

    [Fact]
    public async Task WarpManager_ScanEndpointsAsync_ReturnsNonEmptyResults()
    {
        var results = await WarpManager.ScanEndpointsAsync();
        Assert.NotEmpty(results);
        Assert.Contains(results, ep => ep.Port == 4500);
        Assert.All(results, ep =>
        {
            Assert.False(string.IsNullOrWhiteSpace(ep.Host));
            Assert.True(ep.Port > 0);
            Assert.False(string.IsNullOrWhiteSpace(ep.DisplayText));
        });
    }

    [Fact]
    public void WarpManager_ResolveLocationFromTrace_MapsKnownColosAndFallback()
    {
        var amman = WarpManager.ResolveLocationFromTrace("fl=1\ncolo=AMM\nloc=JO\nwarp=on\nip=104.28.1.1\n");
        Assert.NotNull(amman);
        Assert.Equal("AMM", amman.ColoCode);
        Assert.Equal("JO", amman.CountryCode);
        Assert.Contains("عمّان", amman.FormatDisplay("ar"));
        Assert.Contains("Amman", amman.FormatDisplay("en"));

        var frankfurt = WarpManager.ResolveLocationFromTrace("colo=FRA\nloc=DE\nwarp=on\n");
        Assert.NotNull(frankfurt);
        Assert.Contains("فرانكفورت", frankfurt.FormatDisplay("ar"));
        Assert.Contains("Frankfurt", frankfurt.FormatDisplay("en"));

        var paris = WarpManager.ResolveLocationFromTrace("colo=CDG\nloc=FR\nwarp=on\n");
        Assert.NotNull(paris);
        Assert.Contains("باريس", paris.FormatDisplay("ar"));
        Assert.Contains("Paris", paris.FormatDisplay("en"));

        Assert.Null(WarpManager.ResolveLocationFromTrace(""));
    }

    [Fact]
    public async Task WireGuardProbe_HandshakeInitiation_AcceptedByRealLocalSingboxWireGuardPeer()
    {
        string baseDir = TestPackagePaths.GetWindowsX64PackageDirectory();
        string singboxExe = Path.Combine(baseDir, "engine", "x86_64", "sing-box.exe");

        // Generate two real X25519 keypairs via WireGuardProbe
        byte[] serverPriv = new byte[32];
        byte[] clientPriv = new byte[32];
        System.Security.Cryptography.RandomNumberGenerator.Fill(serverPriv);
        System.Security.Cryptography.RandomNumberGenerator.Fill(clientPriv);
        serverPriv[0] &= 248; serverPriv[31] = (byte)((serverPriv[31] & 127) | 64);
        clientPriv[0] &= 248; clientPriv[31] = (byte)((clientPriv[31] & 127) | 64);

        string serverPrivB64 = Convert.ToBase64String(serverPriv);
        string serverPubB64 = Convert.ToBase64String(WireGuardProbe.X25519Basepoint(serverPriv));
        string clientPrivB64 = Convert.ToBase64String(clientPriv);
        string clientPubB64 = Convert.ToBase64String(WireGuardProbe.X25519Basepoint(clientPriv));

        int wgPort;
        using (var udpTmp = new System.Net.Sockets.UdpClient(0))
        {
            wgPort = ((System.Net.IPEndPoint)udpTmp.Client.LocalEndPoint!).Port;
        }

        string cfgPath = Path.Combine(Path.GetTempPath(), $"wg_srv_{Guid.NewGuid():N}.json");
        var serverConfig = new
        {
            log = new { level = "debug" },
            endpoints = new object[]
            {
                new
                {
                    type = "wireguard",
                    tag = "wg-srv",
                    system = false,
                    listen_port = wgPort,
                    address = new[] { "10.99.0.1/24" },
                    private_key = serverPrivB64,
                    peers = new object[]
                    {
                        new
                        {
                            public_key = clientPubB64,
                            allowed_ips = new[] { "10.99.0.2/32" }
                        }
                    }
                }
            }
        };
        File.WriteAllText(cfgPath, System.Text.Json.JsonSerializer.Serialize(serverConfig));

        var startedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var proc = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = singboxExe,
                Arguments = $"run -c \"{cfgPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
        proc.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null && e.Data.Contains("sing-box started", StringComparison.OrdinalIgnoreCase))
            {
                startedTcs.TrySetResult(true);
            }
        };
        Assert.True(proc.Start());
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        try
        {
            await Task.WhenAny(startedTcs.Task, Task.Delay(5000));
            await Task.Delay(150);
            var probeConfig = new WarpConfig
            {
                PrivateKey = clientPrivB64,
                PublicKey = clientPubB64,
                PeerPublicKey = serverPubB64,
                Reserved = new[] { 0, 0, 0 }
            };

            var (ok, latencyMs) = await WarpManager.ProbeWireGuardEndpointAsync("127.0.0.1", wgPort, probeConfig, timeoutMs: 3000);
            Assert.True(ok, "Real sing-box WireGuard endpoint must accept our Noise_IKpsk2 Handshake Initiation and return Type 2 response.");
            Assert.True(latencyMs >= 0);

            // Also verify reserved bytes do not alter mac1 (Cloudflare strips reserved before mac1 check).
            await Task.Delay(100); // Ensure strictly increasing TAI64N timestamp for the same sing-box peer
            byte[] serverPubBytes = WireGuardProbe.X25519Basepoint(serverPriv);
            byte[] pktWithReserved = WireGuardProbe.BuildHandshakeInitiation(clientPriv, serverPubBytes, new[] { 229, 12, 13 }, out uint reservedSender);
            Assert.Equal(229, pktWithReserved[1]);
            Assert.Equal(12, pktWithReserved[2]);
            Assert.Equal(13, pktWithReserved[3]);
            // Zeroing reserved back out must still match standard WireGuard mac1 and be accepted by sing-box.
            pktWithReserved[1] = 0;
            pktWithReserved[2] = 0;
            pktWithReserved[3] = 0;
            byte[] mac1Label = System.Text.Encoding.ASCII.GetBytes("mac1----");
            byte[] mac1Input = new byte[mac1Label.Length + serverPubBytes.Length];
            Buffer.BlockCopy(mac1Label, 0, mac1Input, 0, mac1Label.Length);
            Buffer.BlockCopy(serverPubBytes, 0, mac1Input, mac1Label.Length, serverPubBytes.Length);
            byte[] expectedMac1 = WireGuardProbe.Blake2sKeyed(WireGuardProbe.Blake2sHash(mac1Input), pktWithReserved.AsSpan(0, 116), 16);
            Assert.Equal(expectedMac1, pktWithReserved.AsSpan(116, 16).ToArray());

            using var udpReservedCheck = new System.Net.Sockets.UdpClient(System.Net.Sockets.AddressFamily.InterNetwork);
            using var ctsReservedCheck = new System.Threading.CancellationTokenSource(3000);
            await udpReservedCheck.SendAsync(pktWithReserved, pktWithReserved.Length, new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, wgPort));
            var rxReserved = await udpReservedCheck.ReceiveAsync(ctsReservedCheck.Token);
            Assert.True(WireGuardProbe.IsMatchingHandshakeResponse(rxReserved.Buffer, reservedSender));
        }
        finally
        {
            try { if (!proc.HasExited) proc.Kill(true); proc.WaitForExit(1000); } catch { }
            try { File.Delete(cfgPath); } catch { }
        }
    }

}
