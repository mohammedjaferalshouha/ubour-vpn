using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Ubour.Services;

public class WarpLocationInfo
{
    public string ColoCode { get; set; } = "";
    public string CountryCode { get; set; } = "";
    public string CityAr { get; set; } = "";
    public string CityEn { get; set; } = "";
    public string CountryAr { get; set; } = "";
    public string CountryEn { get; set; } = "";
    public string ExitIp { get; set; } = "";
    public string WarpStatus { get; set; } = "";

    public string FormatDisplay(string lang = "ar")
    {
        bool isAr = string.Equals(lang, "ar", StringComparison.OrdinalIgnoreCase);
        string city = isAr ? CityAr : CityEn;
        string country = isAr ? CountryAr : CountryEn;

        string place;
        if (!string.IsNullOrWhiteSpace(city) && !string.IsNullOrWhiteSpace(country))
        {
            place = $"{city}، {country}";
            if (!isAr) place = $"{city}, {country}";
        }
        else if (!string.IsNullOrWhiteSpace(country))
        {
            place = country;
        }
        else if (!string.IsNullOrWhiteSpace(city))
        {
            place = city;
        }
        else
        {
            place = !string.IsNullOrWhiteSpace(CountryCode) ? CountryCode : (isAr ? "خادم كلاود فلير" : "Cloudflare Edge");
        }

        if (!string.IsNullOrWhiteSpace(ColoCode))
        {
            return $"{place} ({ColoCode})";
        }
        return place;
    }
}

public class WarpEndpointItem
{
    public string Host { get; set; } = "162.159.195.1";
    public int Port { get; set; } = 4500;
    public long LatencyMs { get; set; } = 0;
    public double DownloadMbps { get; set; } = 0;
    public bool IsReachable { get; set; } = false;
    public bool WasScanned { get; set; } = false;
    public string LocationName { get; set; } = "";
    public string ColoCode { get; set; } = "";
    public string Note { get; set; } = "";

    public string DisplayText
    {
        get
        {
            if (WasScanned)
            {
                if (IsReachable)
                {
                    string latencyPart = LatencyMs > 0 ? $"{LatencyMs} ms" : "";
                    string speedPart = DownloadMbps > 0 ? $"{DownloadMbps:F1} Mbps" : "";
                    string metrics = string.Join(" | ", new[] { latencyPart, speedPart }.Where(s => !string.IsNullOrEmpty(s)));
                    string metricsStr = !string.IsNullOrEmpty(metrics) ? $" ({metrics})" : "";
                    string locStr = !string.IsNullOrEmpty(LocationName) ? $" [{LocationName}]" : "";
                    return $"{Host}:{Port}{metricsStr}{locStr} ✓";
                }

                string unreachNote = !string.IsNullOrEmpty(Note) ? $" - {Note}" : "";
                return $"{Host}:{Port}{unreachNote}";
            }

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

    private static readonly Dictionary<string, (string CityAr, string CityEn, string CountryAr, string CountryEn)> ColoLocations =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["AMM"] = ("عمّان", "Amman", "الأردن", "Jordan"),
            ["DXB"] = ("دبي", "Dubai", "الإمارات", "UAE"),
            ["AUH"] = ("أبوظبي", "Abu Dhabi", "الإمارات", "UAE"),
            ["RUH"] = ("الرياض", "Riyadh", "السعودية", "Saudi Arabia"),
            ["JED"] = ("جدة", "Jeddah", "السعودية", "Saudi Arabia"),
            ["DMM"] = ("الدمام", "Dammam", "السعودية", "Saudi Arabia"),
            ["DOH"] = ("الدوحة", "Doha", "قطر", "Qatar"),
            ["KWI"] = ("الكويت", "Kuwait City", "الكويت", "Kuwait"),
            ["BAH"] = ("المنامة", "Manama", "البحرين", "Bahrain"),
            ["MCT"] = ("مسقط", "Muscat", "عُمان", "Oman"),
            ["CAI"] = ("القاهرة", "Cairo", "مصر", "Egypt"),
            ["BGW"] = ("بغداد", "Baghdad", "العراق", "Iraq"),
            ["BEY"] = ("بيروت", "Beirut", "لبنان", "Lebanon"),
            ["TLV"] = ("تل أبيب", "Tel Aviv", "فلسطين/إسرائيل", "Israel"),
            ["IST"] = ("إسطنبول", "Istanbul", "تركيا", "Turkey"),
            ["FRA"] = ("فرانكفورت", "Frankfurt", "ألمانيا", "Germany"),
            ["MUC"] = ("ميونخ", "Munich", "ألمانيا", "Germany"),
            ["BER"] = ("برلين", "Berlin", "ألمانيا", "Germany"),
            ["DUS"] = ("دوسلدورف", "Düsseldorf", "ألمانيا", "Germany"),
            ["HAM"] = ("هامبورغ", "Hamburg", "ألمانيا", "Germany"),
            ["CDG"] = ("باريس", "Paris", "فرنسا", "France"),
            ["MRS"] = ("مارسيليا", "Marseille", "فرنسا", "France"),
            ["LYS"] = ("ليون", "Lyon", "فرنسا", "France"),
            ["LHR"] = ("لندن", "London", "المملكة المتحدة", "United Kingdom"),
            ["MAN"] = ("مانشستر", "Manchester", "المملكة المتحدة", "United Kingdom"),
            ["AMS"] = ("أمستردام", "Amsterdam", "هولندا", "Netherlands"),
            ["BRU"] = ("بروكسل", "Brussels", "بلجيكا", "Belgium"),
            ["ZRH"] = ("زيورخ", "Zurich", "سويسرا", "Switzerland"),
            ["GVA"] = ("جنيف", "Geneva", "سويسرا", "Switzerland"),
            ["VIE"] = ("فيينا", "Vienna", "النمسا", "Austria"),
            ["MXP"] = ("ميلانو", "Milan", "إيطاليا", "Italy"),
            ["FCO"] = ("روما", "Rome", "إيطاليا", "Italy"),
            ["MAD"] = ("مدريد", "Madrid", "إسبانيا", "Spain"),
            ["BCN"] = ("برشلونة", "Barcelona", "إسبانيا", "Spain"),
            ["LIS"] = ("لشبونة", "Lisbon", "البرتغال", "Portugal"),
            ["ATH"] = ("أثينا", "Athens", "اليونان", "Greece"),
            ["WAW"] = ("وارسو", "Warsaw", "بولندا", "Poland"),
            ["PRG"] = ("براغ", "Prague", "التشيك", "Czechia"),
            ["BUD"] = ("بودابست", "Budapest", "المجر", "Hungary"),
            ["OTP"] = ("بوخارست", "Bucharest", "رومانيا", "Romania"),
            ["SOF"] = ("صوفيا", "Sofia", "بلغاريا", "Bulgaria"),
            ["ARN"] = ("ستوكهولم", "Stockholm", "السويد", "Sweden"),
            ["CPH"] = ("كوبنهاغن", "Copenhagen", "الدنمارك", "Denmark"),
            ["OSL"] = ("أوسلو", "Oslo", "النرويج", "Norway"),
            ["HEL"] = ("هلسنكي", "Helsinki", "فنلندا", "Finland"),
            ["DUB"] = ("دبلن", "Dublin", "أيرلندا", "Ireland"),
            ["IAD"] = ("أشبورن", "Ashburn", "الولايات المتحدة", "United States"),
            ["EWR"] = ("نيوآرك", "Newark", "الولايات المتحدة", "United States"),
            ["JFK"] = ("نيويورك", "New York", "الولايات المتحدة", "United States"),
            ["ORD"] = ("شيكاغو", "Chicago", "الولايات المتحدة", "United States"),
            ["DFW"] = ("دالاس", "Dallas", "الولايات المتحدة", "United States"),
            ["LAX"] = ("لوس أنجلوس", "Los Angeles", "الولايات المتحدة", "United States"),
            ["SJC"] = ("سان خوسيه", "San Jose", "الولايات المتحدة", "United States"),
            ["SEA"] = ("سياتل", "Seattle", "الولايات المتحدة", "United States"),
            ["MIA"] = ("ميامي", "Miami", "الولايات المتحدة", "United States"),
            ["ATL"] = ("أتلانتا", "Atlanta", "الولايات المتحدة", "United States"),
            ["YYZ"] = ("تورونتو", "Toronto", "كندا", "Canada"),
            ["SIN"] = ("سنغافورة", "Singapore", "سنغافورة", "Singapore"),
            ["HKG"] = ("هونغ كونغ", "Hong Kong", "هونغ كونغ", "Hong Kong"),
            ["NRT"] = ("طوكيو", "Tokyo", "اليابان", "Japan"),
            ["ICN"] = ("سيول", "Seoul", "كوريا الجنوبية", "South Korea"),
            ["SYD"] = ("سيدني", "Sydney", "أستراليا", "Australia")
        };

    public static WarpLocationInfo? ResolveLocationFromTrace(string? traceBody)
    {
        if (string.IsNullOrWhiteSpace(traceBody)) return null;

        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string line in traceBody.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            int eqIdx = line.IndexOf('=');
            if (eqIdx > 0 && eqIdx < line.Length - 1)
            {
                fields[line.Substring(0, eqIdx).Trim()] = line.Substring(eqIdx + 1).Trim();
            }
        }

        string colo = fields.TryGetValue("colo", out var c) ? c.ToUpperInvariant() : "";
        string loc = fields.TryGetValue("loc", out var l) ? l.ToUpperInvariant() : "";
        string ip = fields.TryGetValue("ip", out var ipVal) ? ipVal : "";
        string warp = fields.TryGetValue("warp", out var wVal) ? wVal : "";

        if (string.IsNullOrEmpty(colo) && string.IsNullOrEmpty(loc)) return null;

        var info = new WarpLocationInfo
        {
            ColoCode = colo,
            CountryCode = loc,
            ExitIp = ip,
            WarpStatus = warp
        };

        if (!string.IsNullOrEmpty(colo) && ColoLocations.TryGetValue(colo, out var known))
        {
            info.CityAr = known.CityAr;
            info.CityEn = known.CityEn;
            info.CountryAr = known.CountryAr;
            info.CountryEn = known.CountryEn;
            return info;
        }

        if (!string.IsNullOrEmpty(loc) && loc.Length == 2)
        {
            try
            {
                var region = new RegionInfo(loc);
                info.CountryEn = region.EnglishName;
                info.CountryAr = new CultureInfo("ar").TextInfo.ToTitleCase(region.DisplayName);
                try
                {
                    // On Windows with ICU/NLS, ar-JO provides localized Arabic country names cleanly.
                    info.CountryAr = region.NativeName;
                    var arRegion = new RegionInfo($"ar-{loc}");
                    info.CountryAr = arRegion.DisplayName;
                }
                catch
                {
                    info.CountryAr = region.DisplayName;
                }
            }
            catch { }
        }

        return info;
    }

    public static Task<List<WarpEndpointItem>> ScanEndpointsAsync()
    {
        return ScanEndpointsAsync(baseDir: null, lang: "ar", speedAndLocationProbe: null);
    }

    public static async Task<List<WarpEndpointItem>> ScanEndpointsAsync(
        string? baseDir,
        string lang = "ar",
        Func<WarpConfig, WarpEndpointItem, Task<(double DownloadMbps, WarpLocationInfo? Location)>>? speedAndLocationProbe = null)
    {
        WarpConfig? config = null;
        try
        {
            config = await GetOrRegisterConfigAsync(baseDir: baseDir);
        }
        catch { }

        if (config == null || string.IsNullOrWhiteSpace(config.PrivateKey) || string.IsNullOrWhiteSpace(config.PeerPublicKey))
        {
            var (priv, pub) = GenerateCurve25519KeyPair(baseDir);
            config = new WarpConfig
            {
                PrivateKey = priv,
                PublicKey = pub,
                PeerPublicKey = "bmXOC+F1FxEMF9dyiK2H5/1SUtzH0JuVo51h2wPfgyo=",
                Reserved = new[] { 0, 0, 0 }
            };
        }

        // Interleave port-outer / IP-inner so consecutive handshakes rotate across edge IPs
        // instead of bursting a single anycast endpoint. Priority ports run first; remaining
        // candidate ports only run if fewer than 14 endpoints respond.
        var candidatePortSet = new HashSet<int>(CandidatePorts);
        int[] priorityPortOrder =
        {
            4500, 2408, 500, 1701,
            854, 859, 864, 878, 880, 890, 891, 894, 903, 908, 928, 1014, 1180
        };
        var priorityPortSet = new HashSet<int>();
        var priorityPorts = new List<int>();
        foreach (int p in priorityPortOrder)
        {
            if (candidatePortSet.Contains(p) && priorityPortSet.Add(p))
            {
                priorityPorts.Add(p);
            }
        }

        var secondaryPorts = CandidatePorts.Where(p => !priorityPortSet.Contains(p)).Distinct().ToList();

        var priorityPairs = BuildPortInterleavedPairs(CandidateIps, priorityPorts);
        var reachable = new ConcurrentBag<WarpEndpointItem>();
        var probedSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        await ProbeCandidateBatchAsync(priorityPairs, config, reachable, probedSet).ConfigureAwait(false);

        if (reachable.Count < 14 && secondaryPorts.Count > 0)
        {
            var secondaryPairs = BuildPortInterleavedPairs(CandidateIps, secondaryPorts);
            await ProbeCandidateBatchAsync(secondaryPairs, config, reachable, probedSet).ConfigureAwait(false);
        }

        var sortedReachable = reachable
            .OrderBy(e => e.LatencyMs)
            .ThenBy(e => e.Port == 4500 ? 0 : 1)
            .ToList();

        // Measure real download throughput & edge location on top responding endpoints
        if (sortedReachable.Count > 0)
        {
            var probeFunc = speedAndLocationProbe ?? (async (cfg, item) =>
            {
                using var sb = new SingboxManager();
                var res = await sb.VerifyWarpEndpointConnectivityAsync(
                    baseDir ?? AppDomain.CurrentDomain.BaseDirectory,
                    new WarpConfig
                    {
                        PrivateKey = cfg.PrivateKey,
                        PublicKey = cfg.PublicKey,
                        PeerPublicKey = cfg.PeerPublicKey,
                        LocalIpv4 = cfg.LocalIpv4,
                        LocalIpv6 = cfg.LocalIpv6,
                        EndpointHost = item.Host,
                        EndpointPort = item.Port,
                        Reserved = cfg.Reserved
                    },
                    timeoutMs: 5000,
                    measureSpeed: true).ConfigureAwait(false);

                return res.Success ? (res.DownloadMbps, res.Location) : (0d, null);
            });

            foreach (var item in sortedReachable.Take(3))
            {
                try
                {
                    var (mbps, location) = await probeFunc(config, item).ConfigureAwait(false);
                    if (mbps > 0) item.DownloadMbps = mbps;
                    if (location != null)
                    {
                        item.ColoCode = location.ColoCode;
                        item.LocationName = location.FormatDisplay(lang);
                    }
                }
                catch { }
            }

            sortedReachable = sortedReachable
                .OrderByDescending(e => e.DownloadMbps > 0 ? 1 : 0)
                .ThenBy(e => e.LatencyMs)
                .ThenByDescending(e => e.DownloadMbps)
                .ToList();
        }

        var results = new List<WarpEndpointItem>(sortedReachable);
        var reachableSet = new HashSet<string>(sortedReachable.Select(e => $"{e.Host}:{e.Port}"), StringComparer.OrdinalIgnoreCase);

        bool isAr = string.Equals(lang, "ar", StringComparison.OrdinalIgnoreCase);
        string noReplyLabel = isAr ? "غير مستجيب" : "No WireGuard response";

        foreach (var def in GetDefaultEndpoints())
        {
            string key = $"{def.Host}:{def.Port}";
            if (!reachableSet.Contains(key) && probedSet.Contains(key))
            {
                results.Add(new WarpEndpointItem
                {
                    Host = def.Host,
                    Port = def.Port,
                    LatencyMs = 0,
                    IsReachable = false,
                    WasScanned = true,
                    Note = $"{def.Note} ({noReplyLabel})"
                });
            }
        }

        return results;
    }

    /// <summary>
    /// Builds unique (Host, Port) probe pairs ordered port-first so consecutive UDP packets alternate across IPs.
    /// </summary>
    private static List<(string Host, int Port)> BuildPortInterleavedPairs(string[] ips, IEnumerable<int> ports)
    {
        var pairs = new List<(string Host, int Port)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (int port in ports)
        {
            foreach (string ip in ips)
            {
                if (seen.Add($"{ip}:{port}"))
                {
                    pairs.Add((ip, port));
                }
            }
        }
        return pairs;
    }

    /// <summary>
    /// Probes a batch of WireGuard endpoints with bounded concurrency and inter-packet pacing
    /// to avoid triggering UDP flood or anycast rate-limiting on repeated scans.
    /// </summary>
    private static async Task ProbeCandidateBatchAsync(
        IEnumerable<(string Host, int Port)> pairs,
        WarpConfig config,
        ConcurrentBag<WarpEndpointItem> reachable,
        HashSet<string> probedSet)
    {
        using var semaphore = new SemaphoreSlim(8);
        var tasks = new List<Task>();

        foreach (var pair in pairs)
        {
            probedSet.Add($"{pair.Host}:{pair.Port}");
            await semaphore.WaitAsync().ConfigureAwait(false);
            await Task.Delay(25).ConfigureAwait(false);

            var current = pair;
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    var (ok, rttMs) = await ProbeWireGuardEndpointAsync(current.Host, current.Port, config, timeoutMs: 550).ConfigureAwait(false);
                    if (ok)
                    {
                        reachable.Add(new WarpEndpointItem
                        {
                            Host = current.Host,
                            Port = current.Port,
                            LatencyMs = Math.Max(1, rttMs),
                            IsReachable = true,
                            WasScanned = true,
                            Note = GetPortNote(current.Port)
                        });
                    }
                }
                finally
                {
                    semaphore.Release();
                }
            }));
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    public static async Task<(bool Success, long LatencyMs)> ProbeWireGuardEndpointAsync(
        string host,
        int port,
        WarpConfig config,
        int timeoutMs = 800)
    {
        try
        {
            byte[] privBytes = Convert.FromBase64String(config.PrivateKey);
            byte[] peerPubBytes = Convert.FromBase64String(config.PeerPublicKey);
            if (privBytes.Length != 32 || peerPubBytes.Length != 32) return (false, 0);

            IPAddress? targetIp = null;
            if (!IPAddress.TryParse(host, out targetIp))
            {
                var addrs = await Dns.GetHostAddressesAsync(host).ConfigureAwait(false);
                targetIp = addrs.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork) ?? addrs.FirstOrDefault();
            }
            if (targetIp == null) return (false, 0);

            byte[] packet = WireGuardProbe.BuildHandshakeInitiation(privBytes, peerPubBytes, config.Reserved, out uint senderIndex);

            using var udp = new UdpClient(targetIp.AddressFamily);
            using var cts = new CancellationTokenSource(timeoutMs);
            var sw = Stopwatch.StartNew();

            await udp.SendAsync(packet, packet.Length, new IPEndPoint(targetIp, port)).ConfigureAwait(false);

            while (!cts.IsCancellationRequested)
            {
                var receiveTask = udp.ReceiveAsync(cts.Token).AsTask();
                var completed = await receiveTask.ConfigureAwait(false);
                if (WireGuardProbe.IsMatchingHandshakeResponse(completed.Buffer, senderIndex))
                {
                    sw.Stop();
                    return (true, sw.ElapsedMilliseconds);
                }
            }
        }
        catch { }

        return (false, 0);
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
        byte[] fallbackPub = WireGuardProbe.X25519Basepoint(fallbackPriv);
        return (Convert.ToBase64String(fallbackPriv), Convert.ToBase64String(fallbackPub));
    }
}
