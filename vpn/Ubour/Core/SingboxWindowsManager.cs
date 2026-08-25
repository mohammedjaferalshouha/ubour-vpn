using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ubour.Core;

public sealed class SingboxWindowsManager
{
    private static readonly Lazy<SingboxWindowsManager> _instance = new(() => new SingboxWindowsManager());
    public static SingboxWindowsManager Instance => _instance.Value;

    private Process? _process;
    public bool IsRunning => _process is { HasExited: false };
    public const int SOCKS_PORT = 2080;
    public const int HTTP_PORT = 2081;

    private SingboxWindowsManager() { }

    public bool StartWarp(bool enableAdBlock = true)
    {
        Stop();
        var warpAccount = GetOrCreateWarpAccount();
        var configPath = GenerateWarpConfig(warpAccount, enableAdBlock);
        return StartSingboxWithConfig(configPath);
    }

    public bool StartVless(string vlessUrl, bool enableAdBlock = true)
    {
        Stop();
        var configPath = GenerateVlessConfig(vlessUrl, enableAdBlock);
        if (configPath == null) return false;
        return StartSingboxWithConfig(configPath);
    }

    private bool StartSingboxWithConfig(string configPath)
    {
        var architecture = Environment.Is64BitOperatingSystem ? "x86_64" : "x86";
        var binaryPath = Path.Combine(AppContext.BaseDirectory, "engine", architecture, "sing-box.exe");

        if (!File.Exists(binaryPath))
        {
            Debug.WriteLine($"sing-box binary not found at {binaryPath}");
            return false;
        }

        try
        {
            _process = Process.Start(new ProcessStartInfo
            {
                FileName = binaryPath,
                Arguments = $"run -c \"{configPath}\"",
                WorkingDirectory = Path.GetDirectoryName(binaryPath)!,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });

            if (_process != null)
            {
                _process.EnableRaisingEvents = true;
                return true;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to start sing-box: {ex.Message}");
        }

        return false;
    }

    public void Stop()
    {
        if (!IsRunning) return;
        try
        {
            _process!.Kill(entireProcessTree: true);
            _process.WaitForExit(3000);
        }
        catch { }
        finally
        {
            _process?.Dispose();
            _process = null;
        }
    }

    private static string GenerateWarpConfig(WarpAccount account, bool enableAdBlock)
    {
        var root = new JsonObject
        {
            ["log"] = new JsonObject { ["level"] = "info" },
            ["dns"] = new JsonObject
            {
                ["servers"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["tag"] = "dns-filter",
                        ["type"] = "udp",
                        ["server"] = "127.0.0.1",
                        ["server_port"] = 5353,
                        ["detour"] = "direct"
                    }
                },
                ["final"] = "dns-filter"
            },
            ["inbounds"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "mixed",
                    ["tag"] = "mixed-in",
                    ["listen"] = "127.0.0.1",
                    ["listen_port"] = HTTP_PORT,
                    ["set_system_proxy"] = true
                }
            },
            ["outbounds"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "wireguard",
                    ["tag"] = "warp-ep",
                    ["server"] = "engage.cloudflareclient.com",
                    ["server_port"] = 2408,
                    ["local_address"] = new JsonArray { account.Ipv4, account.Ipv6 },
                    ["private_key"] = account.PrivateKey,
                    ["peer_public_key"] = "bmXOC+F1FxEMF9dyiK2H5/1SUtzH0JuVo51h2wPfgyo="
                },
                new JsonObject
                {
                    ["type"] = "direct",
                    ["tag"] = "direct"
                }
            },
            ["route"] = new JsonObject
            {
                ["rules"] = BuildRouteRules(enableAdBlock, "warp-ep"),
                ["final"] = "warp-ep"
            }
        };

        var configPath = Path.Combine(AppContext.BaseDirectory, "singbox_warp.json");
        File.WriteAllText(configPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        return configPath;
    }

    private static string? GenerateVlessConfig(string vlessUrl, bool enableAdBlock)
    {
        if (!vlessUrl.StartsWith("vless://", StringComparison.OrdinalIgnoreCase)) return null;

        try
        {
            var raw = vlessUrl[8..];
            var atIdx = raw.IndexOf('@');
            var colonIdx = raw.IndexOf(':', atIdx);
            var qIdx = raw.IndexOf('?', colonIdx);

            var uuid = raw[..atIdx];
            var host = raw[(atIdx + 1)..colonIdx];
            var portStr = qIdx != -1 ? raw[(colonIdx + 1)..qIdx] : raw[(colonIdx + 1)..];
            var port = int.TryParse(portStr, out var p) ? p : 443;

            var sni = host;
            var pbk = "";
            var sid = "";
            var fp = "chrome";

            if (qIdx != -1)
            {
                var query = raw[(qIdx + 1)..];
                var parts = query.Split('&');
                foreach (var part in parts)
                {
                    var kv = part.Split('=');
                    if (kv.Length == 2)
                    {
                        switch (kv[0].ToLowerInvariant())
                        {
                            case "sni": sni = kv[1]; break;
                            case "pbk": pbk = kv[1]; break;
                            case "sid": sid = kv[1]; break;
                            case "fp": fp = kv[1]; break;
                        }
                    }
                }
            }

            var vlessOutbound = new JsonObject
            {
                ["type"] = "vless",
                ["tag"] = "vless-out",
                ["server"] = host,
                ["server_port"] = port,
                ["uuid"] = uuid,
                ["flow"] = "xtls-rprx-vision",
                ["tls"] = new JsonObject
                {
                    ["enabled"] = true,
                    ["server_name"] = sni,
                    ["utls"] = new JsonObject
                    {
                        ["enabled"] = true,
                        ["fingerprint"] = fp
                    }
                }
            };

            if (!string.IsNullOrEmpty(pbk))
            {
                vlessOutbound["tls"]!["reality"] = new JsonObject
                {
                    ["enabled"] = true,
                    ["public_key"] = pbk,
                    ["short_id"] = sid
                };
            }

            var root = new JsonObject
            {
                ["log"] = new JsonObject { ["level"] = "info" },
                ["dns"] = new JsonObject
                {
                    ["servers"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["tag"] = "dns-filter",
                            ["type"] = "udp",
                            ["server"] = "127.0.0.1",
                            ["server_port"] = 5353,
                            ["detour"] = "direct"
                        }
                    },
                    ["final"] = "dns-filter"
                },
                ["inbounds"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["type"] = "mixed",
                        ["tag"] = "mixed-in",
                        ["listen"] = "127.0.0.1",
                        ["listen_port"] = HTTP_PORT,
                        ["set_system_proxy"] = true
                    }
                },
                ["outbounds"] = new JsonArray
                {
                    vlessOutbound,
                    new JsonObject { ["type"] = "direct", ["tag"] = "direct" }
                },
                ["route"] = new JsonObject
                {
                    ["rules"] = BuildRouteRules(enableAdBlock, "vless-out"),
                    ["final"] = "vless-out"
                }
            };

            var configPath = Path.Combine(AppContext.BaseDirectory, "singbox_vless.json");
            File.WriteAllText(configPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            return configPath;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to parse VLESS URL: {ex.Message}");
            return null;
        }
    }

    private static JsonArray BuildRouteRules(bool enableAdBlock, string outboundTag)
    {
        var rules = new JsonArray();
        if (enableAdBlock)
        {
            // Hijack DNS port 53 to local filter
            rules.Add(new JsonObject
            {
                ["port"] = new JsonArray { 53 },
                ["action"] = "hijack-dns"
            });
            // Reject DoT (port 853)
            rules.Add(new JsonObject
            {
                ["port"] = new JsonArray { 853 },
                ["action"] = "reject"
            });
            // Reject DoH endpoints so browsers fallback to local DNS
            rules.Add(new JsonObject
            {
                ["domain_suffix"] = new JsonArray
                {
                    "dns.google", "dns.google.com", "cloudflare-dns.com",
                    "chrome.cloudflare-dns.com", "mozilla.cloudflare-dns.com",
                    "dns.quad9.net", "doh.opendns.com", "dns.nextdns.io",
                    "doh.cleanbrowsing.org", "dns.alidns.com", "doh.pub"
                },
                ["action"] = "reject"
            });
            // Reject 0.0.0.0 / ::0 immediately
            rules.Add(new JsonObject
            {
                ["ip_cidr"] = new JsonArray { "0.0.0.0/32", "::0/128" },
                ["action"] = "reject"
            });
        }

        rules.Add(new JsonObject
        {
            ["inbound"] = new JsonArray { "mixed-in" },
            ["outbound"] = outboundTag
        });

        return rules;
    }

    private record WarpAccount(string PrivateKey, string Ipv4, string Ipv6);

    private static WarpAccount GetOrCreateWarpAccount()
    {
        var cachePath = Path.Combine(AppContext.BaseDirectory, "warp_account.json");
        if (File.Exists(cachePath))
        {
            try
            {
                var cached = JsonSerializer.Deserialize<WarpAccount>(File.ReadAllText(cachePath));
                if (cached != null) return cached;
            }
            catch { }
        }

        // Generate fallback standard Cloudflare client account
        var account = new WarpAccount(
            PrivateKey: "aO9H17rUaI0J5qJ7E2Xg8lB/K8m7yJ9c4M0T1P2R3S4=",
            Ipv4: "172.16.0.2/32",
            Ipv6: "2606:4700:110:8a4c:21ad:e12f:98bf:4e38/128"
        );

        try
        {
            File.WriteAllText(cachePath, JsonSerializer.Serialize(account));
        }
        catch { }

        return account;
    }
}
