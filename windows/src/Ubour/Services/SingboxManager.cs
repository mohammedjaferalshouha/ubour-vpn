using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using Ubour.Models;

namespace Ubour.Services;

public class SingboxManager
{
    private Process? _process;
    private string? _currentConfigFile;
    public bool IsRunning => _process != null && !_process.HasExited;

    public bool StartWarp(string baseDir, WarpConfig warp, bool enableAdBlock = true)
    {
        Stop();
        string configPath = GenerateWarpConfig(warp, enableAdBlock);
        bool started = StartProcess(baseDir, configPath);
        if (started)
        {
            ProxyManager.EnableProxy("127.0.0.1:2080");
        }
        return started;
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

    private string GenerateWarpConfig(WarpConfig warp, bool enableAdBlock)
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), "Ubour");
        Directory.CreateDirectory(tmpDir);
        string configFile = Path.Combine(tmpDir, "singbox_warp.json");

        var dnsObj = BuildDnsConfig(enableAdBlock);
        var routeRules = BuildRouteRules(enableAdBlock, "warp-ep");

        var configObj = new
        {
            log = new { level = "info" },
            dns = dnsObj,
            endpoints = new object[]
            {
                new
                {
                    type = "wireguard",
                    tag = "warp-ep",
                    system = false,
                    name = "warp",
                    address = new string[] { warp.LocalIpv4, warp.LocalIpv6 },
                    private_key = warp.PrivateKey,
                    mtu = 1280,
                    peers = new object[]
                    {
                        new
                        {
                            address = warp.EndpointHost,
                            port = warp.EndpointPort,
                            public_key = warp.PeerPublicKey,
                            allowed_ips = new string[] { "0.0.0.0/0", "::/0" },
                            reserved = new int[] { warp.Reserved[0], warp.Reserved[1], warp.Reserved[2] }
                        }
                    }
                }
            },
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
            string portStr = qIdx != -1 ? raw.Substring(colonIdx + 1, qIdx - colonIdx - 1) : raw.Substring(colonIdx + 1);
            int port = int.TryParse(portStr, out int p) ? p : 443;

            string sni = host;
            string pbk = "";
            string sid = "";

            if (qIdx != -1)
            {
                string query = raw.Substring(qIdx + 1);
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

    private static object BuildDnsConfig(bool enableAdBlock = true)
    {
        if (enableAdBlock)
        {
            return new
            {
                servers = new object[]
                {
                    new
                    {
                        tag = "dns-filter",
                        type = "udp",
                        server = "127.0.0.1",
                        server_port = 53
                    }
                },
                final = "dns-filter"
            };
        }
        else
        {
            return new
            {
                servers = new object[]
                {
                    new
                    {
                        tag = "dns-direct",
                        type = "udp",
                        server = "1.1.1.1",
                        server_port = 53
                    }
                },
                final = "dns-direct"
            };
        }
    }

    private static object[] BuildRouteRules(bool enableAdBlock, string mainOutbound)
    {
        var rules = new List<object>();

        if (enableAdBlock)
        {
            rules.Add(new
            {
                port = new int[] { 53 },
                action = "hijack-dns"
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

        if (mainOutbound != "direct")
        {
            rules.Add(new
            {
                inbound = new string[] { "mixed-in" },
                action = "route",
                outbound = mainOutbound
            });
        }

        return rules.ToArray();
    }

    private bool StartProcess(string baseDir, string configFile)
    {
        try
        {
            string arch = Environment.Is64BitOperatingSystem ? "x86_64" : "x86";
            string exePath = Path.Combine(baseDir, "engine", arch, "sing-box.exe");

            if (!File.Exists(exePath))
            {
                exePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "engine", arch, "sing-box.exe");
            }

            if (!File.Exists(exePath)) return false;

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

            _process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            _process.OutputDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) AppLogger.Info($"[sing-box] {e.Data}"); };
            _process.ErrorDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) AppLogger.Warn($"[sing-box] {e.Data}"); };
            _process.Start();
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
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

            foreach (var proc in Process.GetProcessesByName("sing-box"))
            {
                try { proc.Kill(); } catch { }
            }

            if (!string.IsNullOrEmpty(_currentConfigFile) && File.Exists(_currentConfigFile))
            {
                try { File.Delete(_currentConfigFile); } catch { }
                _currentConfigFile = null;
            }
        }
        catch { }
    }
}
