using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Ubour.Services;

public class WarpConfig
{
    public string PrivateKey { get; set; } = "";
    public string PublicKey { get; set; } = "";
    public string PeerPublicKey { get; set; } = "bmXOC+F1FxEMF9dyiK2H5/1SUtzH0JuVo51h2wPfgyo=";
    public string LocalIpv4 { get; set; } = "172.16.0.2/32";
    public string LocalIpv6 { get; set; } = "2606:4700:110:8a43:e459:c9b0:2598:5068/128";
    public string EndpointHost { get; set; } = "162.159.192.1";
    public int EndpointPort { get; set; } = 2408;
    public int[] Reserved { get; set; } = new int[] { 0, 0, 0 };
}

public static class WarpManager
{
    private static readonly string CacheFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Ubour",
        "warp_account.json"
    );

    private static readonly (string host, int port)[] KnownFastEndpoints = new[]
    {
        ("162.159.192.1", 2408),
        ("162.159.193.1", 2408),
        ("162.159.195.1", 2408),
        ("188.114.96.1", 2408),
        ("188.114.97.1", 2408),
        ("162.159.192.1", 500),
        ("162.159.193.1", 500),
        ("162.159.195.1", 500),
        ("188.114.96.1", 500),
        ("188.114.97.1", 500),
        ("162.159.192.1", 4500),
        ("162.159.193.1", 4500),
        ("162.159.195.1", 4500),
        ("188.114.96.1", 4500),
        ("188.114.97.1", 4500)
    };

    public static async Task<(string host, int port)> FindFastestEndpointAsync()
    {
        var tasks = new List<Task<(string host, int port, long latency)>>();
        foreach (var ep in KnownFastEndpoints)
        {
            tasks.Add(Task.Run(async () =>
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    using var udp = new UdpClient();
                    udp.Client.ReceiveTimeout = 400;
                    udp.Client.SendTimeout = 400;
                    byte[] pingData = new byte[] { 0x01, 0x00, 0x00, 0x00 };
                    await udp.SendAsync(pingData, pingData.Length, ep.host, ep.port);
                    sw.Stop();
                    return (ep.host, ep.port, sw.ElapsedMilliseconds);
                }
                catch
                {
                    return (ep.host, ep.port, 9999L);
                }
            }));
        }

        try
        {
            var results = await Task.WhenAll(tasks);
            var best = results.OrderBy(r => r.latency).FirstOrDefault();
            if (best.latency < 9999)
            {
                return (best.host, best.port);
            }
        }
        catch { }

        return ("162.159.192.1", 2408);
    }

    public static async Task<WarpConfig> GetOrRegisterConfigAsync()
    {
        WarpConfig? config = null;
        try
        {
            if (File.Exists(CacheFile))
            {
                string json = await File.ReadAllTextAsync(CacheFile);
                var cached = JsonSerializer.Deserialize<WarpConfig>(json);
                if (cached != null && !string.IsNullOrWhiteSpace(cached.PrivateKey) && cached.Reserved != null && cached.Reserved.Length == 3)
                {
                    config = cached;
                }
            }
        }
        catch { }

        if (config == null)
        {
            config = await RegisterNewAccountAsync();
            try
            {
                string dir = Path.GetDirectoryName(CacheFile)!;
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                string json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(CacheFile, json);
            }
            catch { }
        }

        try
        {
            var (bestHost, bestPort) = await FindFastestEndpointAsync();
            config.EndpointHost = bestHost;
            config.EndpointPort = bestPort;
        }
        catch { }

        return config;
    }

    private static async Task<WarpConfig> RegisterNewAccountAsync()
    {
        var (privKeyB64, pubKeyB64) = GenerateCurve25519KeyPair();

        var config = new WarpConfig
        {
            PrivateKey = privKeyB64,
            PublicKey = pubKeyB64,
            PeerPublicKey = "bmXOC+F1FxEMF9dyiK2H5/1SUtzH0JuVo51h2wPfgyo=",
            LocalIpv4 = "172.16.0.2/32",
            LocalIpv6 = "2606:4700:110:8a43:e459:c9b0:2598:5068/128",
            EndpointHost = "162.159.192.1",
            EndpointPort = 2408,
            Reserved = new int[] { 122, 15, 67 }
        };

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("okhttp/3.12.1");

            var payload = new
            {
                install_id = "",
                tos = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                key = pubKeyB64,
                fcm_token = "",
                type = "Android",
                locale = "en_US"
            };

            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            var resp = await client.PostAsync("https://api.cloudflareclient.com/v0a2158/reg", content);
            if (resp.IsSuccessStatusCode)
            {
                string resBody = await resp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(resBody);
                var root = doc.RootElement;

                string regId = root.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? "" : "";
                string token = root.TryGetProperty("token", out var tokProp) ? tokProp.GetString() ?? "" : "";

                if (root.TryGetProperty("config", out var cfg))
                {
                    if (cfg.TryGetProperty("client_id", out var cidProp))
                    {
                        string cidStr = cidProp.GetString() ?? "";
                        if (!string.IsNullOrEmpty(cidStr))
                        {
                            byte[] cidBytes = Convert.FromBase64String(cidStr);
                            if (cidBytes.Length >= 3)
                            {
                                config.Reserved = new int[] { cidBytes[0], cidBytes[1], cidBytes[2] };
                            }
                        }
                    }

                    if (cfg.TryGetProperty("peers", out var peers) && peers.GetArrayLength() > 0)
                    {
                        var peer = peers[0];
                        if (peer.TryGetProperty("public_key", out var pk)) config.PeerPublicKey = pk.GetString() ?? config.PeerPublicKey;
                        if (peer.TryGetProperty("endpoint", out var ep))
                        {
                            if (ep.TryGetProperty("host", out var h))
                            {
                                string hostStr = h.GetString() ?? "";
                                if (hostStr.Contains(":")) hostStr = hostStr.Split(':')[0];
                                if (!string.IsNullOrEmpty(hostStr)) config.EndpointHost = hostStr;
                            }
                        }
                    }

                    if (cfg.TryGetProperty("interface", out var iface))
                    {
                        if (iface.TryGetProperty("addresses", out var addrs))
                        {
                            if (addrs.TryGetProperty("v4", out var v4)) config.LocalIpv4 = v4.GetString() + "/32";
                            if (addrs.TryGetProperty("v6", out var v6)) config.LocalIpv6 = v6.GetString() + "/128";
                        }
                    }
                }

                // Activate WARP
                if (!string.IsNullOrEmpty(regId) && !string.IsNullOrEmpty(token))
                {
                    try
                    {
                        using var patchReq = new HttpRequestMessage(HttpMethod.Patch, $"https://api.cloudflareclient.com/v0a2158/reg/{regId}");
                        patchReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                        patchReq.Content = new StringContent("{\"warp_enabled\":true}", Encoding.UTF8, "application/json");
                        await client.SendAsync(patchReq);
                    }
                    catch { }
                }
            }
        }
        catch { }

        return config;
    }

    private static (string privKey, string pubKey) GenerateCurve25519KeyPair()
    {
        byte[] priv = new byte[32];
        RandomNumberGenerator.Fill(priv);
        priv[0] &= 248;
        priv[31] = (byte)((priv[31] & 127) | 64);

        byte[] pub = GeneratePublicKey(priv);
        return (Convert.ToBase64String(priv), Convert.ToBase64String(pub));
    }

    private static readonly BigInteger P = BigInteger.Pow(2, 255) - 19;
    private static readonly BigInteger A24 = 121665;

    private static byte[] GeneratePublicKey(byte[] priv)
    {
        BigInteger x1 = 9;
        BigInteger x2 = 1, z2 = 0;
        BigInteger x3 = x1, z3 = 1;
        int swap = 0;

        for (int t = 254; t >= 0; t--)
        {
            int byteIndex = t / 8;
            int bitIndex = t % 8;
            int bit = (priv[byteIndex] >> bitIndex) & 1;

            if (bit != swap)
            {
                var tmpX = x2; x2 = x3; x3 = tmpX;
                var tmpZ = z2; z2 = z3; z3 = tmpZ;
                swap = bit;
            }

            var a = (x2 + z2) % P;
            var aa = (a * a) % P;
            var b = (x2 - z2) % P;
            if (b < 0) b += P;
            var bb = (b * b) % P;
            var e = (aa - bb) % P;
            if (e < 0) e += P;
            var c = (x3 + z3) % P;
            var d = (x3 - z3) % P;
            if (d < 0) d += P;
            var da = (d * a) % P;
            var cb = (c * b) % P;
            var sum = (da + cb) % P;
            x3 = (sum * sum) % P;
            var diff = (da - cb) % P;
            if (diff < 0) diff += P;
            z3 = (x1 * ((diff * diff) % P)) % P;
            x2 = (aa * bb) % P;
            z2 = (e * ((aa + A24 * e) % P)) % P;
        }

        if (swap == 1)
        {
            var tmpX = x2; x2 = x3; x3 = tmpX;
            var tmpZ = z2; z2 = z3; z3 = tmpZ;
        }

        var inv = BigInteger.ModPow(z2, P - 2, P);
        var res = (x2 * inv) % P;
        byte[] raw = res.ToByteArray();
        byte[] pub = new byte[32];
        for (int i = 0; i < Math.Min(raw.Length, 32); i++)
        {
            pub[i] = raw[i];
        }
        return pub;
    }
}
