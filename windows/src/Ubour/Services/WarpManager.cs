using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Ubour.Services;

public class WarpEndpointItem
{
    public string Host { get; set; } = "162.159.195.1";
    public int Port { get; set; } = 4500;
    public long LatencyMs { get; set; } = 0;
    public string Note { get; set; } = "";

    public string DisplayText
    {
        get
        {
            string latencyStr = LatencyMs > 0 ? $" ({LatencyMs} ms)" : "";
            string noteStr = !string.IsNullOrEmpty(Note) ? $" - {Note}" : "";
            return $"{Host}:{Port}{latencyStr}{noteStr}";
        }
    }

    public override string ToString() => DisplayText;
}

public class WarpConfig
{
    public string PrivateKey { get; set; } = "";
    public string PublicKey { get; set; } = "";
    public string PeerPublicKey { get; set; } = "bmXOC+F1FxEMF9dyiK2H5/1SUtzH0JuVo51h2wPfgyo=";
    public string LocalIpv4 { get; set; } = "172.16.0.2/32";
    public string LocalIpv6 { get; set; } = "2606:4700:110:8a43:e459:c9b0:2598:5068/128";
    public string EndpointHost { get; set; } = "162.159.195.1";
    public int EndpointPort { get; set; } = 4500;
    public int[] Reserved { get; set; } = new int[] { 0, 0, 0 };
}

public static class WarpManager
{
    private static readonly string CacheFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Ubour",
        "warp_account.json"
    );

    public static readonly string[] CandidateIps = new[]
    {
        "162.159.195.1",
        "188.114.97.1",
        "188.114.96.1",
        "162.159.192.1",
        "162.159.193.1",
        "188.114.98.1",
        "188.114.99.1"
    };

    public static readonly int[] CandidatePorts = new[]
    {
        4500, 500, 1701, 854, 859, 864, 878, 880, 890, 891, 894, 903, 908, 928,
        934, 939, 942, 943, 945, 946, 955, 968, 987, 988, 1002, 1010, 1014, 1018,
        1070, 1074, 1180, 1387, 2147, 2187, 2222, 3128, 3389, 3478, 3556, 8080,
        8443, 51820, 2408
    };

    public static string GetPortNote(int port) => port switch
    {
        4500 => "موصى به (IPsec NAT-T)",
        500 => "IKE VPN",
        1701 => "L2TP VPN",
        2408 => "الافتراضي القديم",
        51820 => "WireGuard قياسي",
        8080 or 8443 => "منفذ بديل للويب",
        _ => "منفذ سحابي مخصص"
    };

    public static List<WarpEndpointItem> GetDefaultEndpoints(string? selectedHost = null, int? selectedPort = null)
    {
        var list = new List<WarpEndpointItem>();

        // Always put universal/recommended ports first
        string host = !string.IsNullOrWhiteSpace(selectedHost) ? selectedHost : "162.159.195.1";
        int port = (selectedPort.HasValue && selectedPort.Value > 0) ? selectedPort.Value : 4500;

        // Custom/Selected first if not default
        if (host != "162.159.195.1" || port != 4500)
        {
            list.Add(new WarpEndpointItem
            {
                Host = host,
                Port = port,
                Note = "المحدد حالياً"
            });
        }

        // Add recommended ports across IPs
        var topPorts = new[] { 4500, 500, 1701 };
        foreach (var ip in CandidateIps)
        {
            foreach (var p in topPorts)
            {
                if (!list.Any(x => x.Host == ip && x.Port == p))
                {
                    list.Add(new WarpEndpointItem
                    {
                        Host = ip,
                        Port = p,
                        Note = GetPortNote(p)
                    });
                }
            }
        }

        // Add remaining candidate ports
        foreach (var ip in CandidateIps.Take(3))
        {
            foreach (var p in CandidatePorts)
            {
                if (!list.Any(x => x.Host == ip && x.Port == p))
                {
                    list.Add(new WarpEndpointItem
                    {
                        Host = ip,
                        Port = p,
                        Note = GetPortNote(p)
                    });
                }
            }
        }

        return list;
    }

    public static async Task<List<WarpEndpointItem>> ScanEndpointsAsync()
    {
        var ipLatencies = new ConcurrentDictionary<string, long>();

        // 1. Fast parallel ICMP ping test to all candidate IPs
        var pingTasks = CandidateIps.Select(async ip =>
        {
            try
            {
                using var ping = new Ping();
                var reply = await ping.SendPingAsync(ip, 800);
                if (reply.Status == IPStatus.Success && reply.RoundtripTime >= 0)
                {
                    ipLatencies[ip] = reply.RoundtripTime;
                }
            }
            catch { }
        });

        await Task.WhenAll(pingTasks);

        var sortedIps = ipLatencies.OrderBy(k => k.Value).Select(k => k.Key).ToList();
        if (sortedIps.Count == 0)
        {
            sortedIps.AddRange(CandidateIps);
        }

        var results = new List<WarpEndpointItem>();

        // 2. Prioritize resilient ports (4500, 500, 1701) for the fastest IPs
        var priorityPorts = new[] { 4500, 500, 1701 };
        foreach (var ip in sortedIps)
        {
            long lat = ipLatencies.TryGetValue(ip, out var l) ? l : 0;
            foreach (var port in priorityPorts)
            {
                results.Add(new WarpEndpointItem
                {
                    Host = ip,
                    Port = port,
                    LatencyMs = lat,
                    Note = GetPortNote(port)
                });
            }
        }

        // 3. Add other ports for top IPs
        foreach (var ip in sortedIps.Take(2))
        {
            long lat = ipLatencies.TryGetValue(ip, out var l) ? l : 0;
            foreach (var port in CandidatePorts)
            {
                if (!results.Any(r => r.Host == ip && r.Port == port))
                {
                    results.Add(new WarpEndpointItem
                    {
                        Host = ip,
                        Port = port,
                        LatencyMs = lat,
                        Note = GetPortNote(port)
                    });
                }
            }
        }

        return results;
    }

    public static async Task<WarpConfig> GetOrRegisterConfigAsync(string? preferredHost = null, int? preferredPort = null, string? baseDir = null)
    {
        WarpConfig? config = null;
        try
        {
            if (File.Exists(CacheFile))
            {
                string json = await File.ReadAllTextAsync(CacheFile);
                var cached = JsonSerializer.Deserialize<WarpConfig>(json);
                if (cached != null && !string.IsNullOrWhiteSpace(cached.PrivateKey) && cached.Reserved != null && cached.Reserved.Length == 3 && (cached.Reserved[0] != 0 || cached.Reserved[1] != 0 || cached.Reserved[2] != 0))
                {
                    config = cached;
                }
            }
        }
        catch { }

        if (config == null)
        {
            config = await RegisterNewAccountAsync(baseDir);
            try
            {
                string dir = Path.GetDirectoryName(CacheFile)!;
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                string json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(CacheFile, json);
            }
            catch { }
        }

        // Apply preferred endpoint or safe defaults (4500 over 2408)
        if (!string.IsNullOrWhiteSpace(preferredHost))
        {
            config.EndpointHost = preferredHost;
        }
        else if (string.IsNullOrWhiteSpace(config.EndpointHost) || config.EndpointPort == 2408)
        {
            config.EndpointHost = "162.159.195.1";
        }

        if (preferredPort.HasValue && preferredPort.Value > 0)
        {
            config.EndpointPort = preferredPort.Value;
        }
        else if (config.EndpointPort == 0 || config.EndpointPort == 2408)
        {
            config.EndpointPort = 4500;
        }

        return config;
    }


    private static async Task<WarpConfig> RegisterNewAccountAsync(string? baseDir = null)
    {
        var (privKeyB64, pubKeyB64) = GenerateCurve25519KeyPair(baseDir);

        var config = new WarpConfig
        {
            PrivateKey = privKeyB64,
            PublicKey = pubKeyB64,
            PeerPublicKey = "bmXOC+F1FxEMF9dyiK2H5/1SUtzH0JuVo51h2wPfgyo=",
            LocalIpv4 = "172.16.0.2/32",
            LocalIpv6 = "2606:4700:110:8e66:d840:b996:8ad2:e7f6/128",
            EndpointHost = "162.159.195.1",
            EndpointPort = 4500,
            Reserved = new int[] { 0, 0, 0 }
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

    private static (string privKey, string pubKey) GenerateCurve25519KeyPair(string? baseDir = null)
    {
        try
        {
            string arch = Environment.Is64BitOperatingSystem ? "x86_64" : "x86";
            string[] candidates = new[]
            {
                !string.IsNullOrEmpty(baseDir) ? Path.Combine(baseDir, "engine", arch, "sing-box.exe") : "",
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "engine", arch, "sing-box.exe"),
                Path.Combine(AppContext.BaseDirectory, "engine", arch, "sing-box.exe")
            };

            string? exe = candidates.FirstOrDefault(c => !string.IsNullOrEmpty(c) && File.Exists(c));
            if (exe != null)
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = "generate wg-keypair",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    string output = proc.StandardOutput.ReadToEnd();
                    proc.WaitForExit(3000);
                    string? priv = null;
                    string? pub = null;
                    foreach (var line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (line.StartsWith("PrivateKey:", StringComparison.OrdinalIgnoreCase))
                            priv = line.Substring("PrivateKey:".Length).Trim();
                        else if (line.StartsWith("PublicKey:", StringComparison.OrdinalIgnoreCase))
                            pub = line.Substring("PublicKey:".Length).Trim();
                    }
                    if (!string.IsNullOrEmpty(priv) && !string.IsNullOrEmpty(pub))
                    {
                        return (priv, pub);
                    }
                }
            }
        }
        catch { }

        byte[] fallbackPriv = new byte[32];
        RandomNumberGenerator.Fill(fallbackPriv);
        fallbackPriv[0] &= 248;
        fallbackPriv[31] = (byte)((fallbackPriv[31] & 127) | 64);
        byte[] fallbackPub = GeneratePublicKey(fallbackPriv);
        return (Convert.ToBase64String(fallbackPriv), Convert.ToBase64String(fallbackPub));
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
