using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

namespace Ubour.AdBlock;

public sealed class AdBlockEngine
{
    private static readonly Lazy<AdBlockEngine> _instance = new(() => new AdBlockEngine());
    public static AdBlockEngine Instance => _instance.Value;

    private long[] _blockedHashes = Array.Empty<long>();
    private long[] _whitelistHashes = Array.Empty<long>();

    private long _totalQueries;
    private long _blockedAds;
    private long _blockedTrackers;

    public long TotalQueries => Interlocked.Read(ref _totalQueries);
    public long BlockedAds => Interlocked.Read(ref _blockedAds);
    public long BlockedTrackers => Interlocked.Read(ref _blockedTrackers);
    public int RulesCount => _blockedHashes.Length;
    public bool IsLoaded => _blockedHashes.Length > 0;

    private static readonly string[] DefaultFilterUrls =
    [
        "https://raw.githubusercontent.com/AdguardTeam/FiltersRegistry/master/filters/filter_15_DnsFilter/filter.txt",
        "https://big.oisd.nl",
        "https://raw.githubusercontent.com/hagezi/dns-blocklists/main/adblock/pro.txt",
        "https://raw.githubusercontent.com/StevenBlack/hosts/master/hosts"
    ];

    private AdBlockEngine() { }

    public static long HashDomain(string domain)
    {
        var clean = domain.Trim().ToLowerInvariant().TrimEnd('.');
        if (string.IsNullOrEmpty(clean)) return 0;
        ulong hash = 0xcbf29ce484222325UL;
        var bytes = Encoding.UTF8.GetBytes(clean);
        foreach (var b in bytes)
        {
            hash ^= b;
            hash *= 0x100000001b3UL;
        }
        return (long)hash;
    }

    public void LoadEmbeddedFilters(string? customPath = null)
    {
        var basePath = AppContext.BaseDirectory;
        var path = customPath ?? Path.Combine(basePath, "filters", "adblock_rules.txt");

        var cachedPath = Path.Combine(basePath, "filters", "filters_cached.txt");
        var targetPath = File.Exists(cachedPath) ? cachedPath : path;

        if (!File.Exists(targetPath))
        {
            Debug.WriteLine($"AdBlock filter file not found at: {targetPath}");
            return;
        }

        var blockedList = new List<long>(900000);
        var whiteList = new List<long>(5000);

        foreach (var line in File.ReadLines(targetPath))
        {
            ParseRuleLine(line, blockedList, whiteList);
        }

        _blockedHashes = PrepareSortedArray(blockedList);
        _whitelistHashes = PrepareSortedArray(whiteList);
        Debug.WriteLine($"AdBlockEngine loaded {_blockedHashes.Length:N0} rules.");
    }

    private static void ParseRuleLine(string line, List<long> blockedList, List<long> whiteList)
    {
        var clean = line.Trim();
        if (string.IsNullOrEmpty(clean) || clean.StartsWith('#') || clean.StartsWith('!'))
            return;

        // Exception / Whitelist
        if (clean.StartsWith("@@||"))
        {
            var domain = clean[4..].TrimEnd('^').Trim();
            if (!string.IsNullOrEmpty(domain))
                whiteList.Add(HashDomain(domain));
            return;
        }

        // uBlock / AdGuard rule
        if (clean.StartsWith("||"))
        {
            var domain = clean[2..].TrimEnd('^').Trim();
            if (!string.IsNullOrEmpty(domain))
                blockedList.Add(HashDomain(domain));
            return;
        }

        // Hosts format: 0.0.0.0 domain or 127.0.0.1 domain
        if (clean.StartsWith("0.0.0.0 ") || clean.StartsWith("127.0.0.1 "))
        {
            var parts = clean.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                var domain = parts[1].Trim();
                if (!string.IsNullOrEmpty(domain) && domain != "localhost" && domain != "broadcasthost")
                    blockedList.Add(HashDomain(domain));
            }
            return;
        }

        // Plain domain
        var plain = clean.TrimEnd('^').Trim();
        if (!string.IsNullOrEmpty(plain) && !plain.Contains(' ') && !plain.Contains('/'))
        {
            blockedList.Add(HashDomain(plain));
        }
    }

    private static long[] PrepareSortedArray(List<long> list)
    {
        if (list.Count == 0) return Array.Empty<long>();
        var arr = list.ToArray();
        Array.Sort(arr);

        // Deduplicate in-place
        var uniqueCount = 1;
        for (var i = 1; i < arr.Length; i++)
        {
            if (arr[i] != arr[i - 1])
            {
                arr[uniqueCount++] = arr[i];
            }
        }
        return arr.AsSpan(0, uniqueCount).ToArray();
    }

    public bool IsDomainBlocked(string rawDomain)
    {
        Interlocked.Increment(ref _totalQueries);
        var domain = rawDomain.Trim().ToLowerInvariant().TrimEnd('.');
        if (string.IsNullOrEmpty(domain)) return false;

        var localBlocked = _blockedHashes;
        var localWhite = _whitelistHashes;

        // Check Whitelist first
        if (localWhite.Length > 0)
        {
            var hash = HashDomain(domain);
            if (Array.BinarySearch(localWhite, hash) >= 0) return false;
        }

        if (localBlocked.Length == 0) return false;

        // Exact Match
        var domainHash = HashDomain(domain);
        if (Array.BinarySearch(localBlocked, domainHash) >= 0)
        {
            RecordBlock(domain);
            return true;
        }

        // Hierarchical Subdomain Matching
        var current = domain;
        while (current.Contains('.'))
        {
            var dotIdx = current.IndexOf('.');
            if (dotIdx == -1 || dotIdx == current.Length - 1) break;
            current = current[(dotIdx + 1)..];

            var subHash = HashDomain(current);
            if (Array.BinarySearch(localBlocked, subHash) >= 0)
            {
                RecordBlock(domain);
                return true;
            }
        }

        return false;
    }

    private void RecordBlock(string domain)
    {
        if (domain.Contains("track") || domain.Contains("analytics") || domain.Contains("metric") ||
            domain.Contains("telemetry") || domain.Contains("adjust") || domain.Contains("appsflyer"))
        {
            Interlocked.Increment(ref _blockedTrackers);
        }
        else
        {
            Interlocked.Increment(ref _blockedAds);
        }
    }

    public void ResetCounters()
    {
        Interlocked.Exchange(ref _totalQueries, 0);
        Interlocked.Exchange(ref _blockedAds, 0);
        Interlocked.Exchange(ref _blockedTrackers, 0);
    }

    public async Task<int> UpdateFiltersOnlineAsync(IProgress<int>? progress = null, CancellationToken ct = default)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var blockedList = new List<long>(2000000);
        var whiteList = new List<long>(20000);

        var total = DefaultFilterUrls.Length;
        for (var i = 0; i < total; i++)
        {
            ct.ThrowIfCancellationRequested();
            var url = DefaultFilterUrls[i];
            try
            {
                using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                if (response.IsSuccessStatusCode)
                {
                    using var stream = await response.Content.ReadAsStreamAsync(ct);
                    using var reader = new StreamReader(stream);
                    string? line;
                    while ((line = await reader.ReadLineAsync(ct)) != null)
                    {
                        ParseRuleLine(line, blockedList, whiteList);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to download filter from {url}: {ex.Message}");
            }
            progress?.Report((int)(((i + 1) / (double)total) * 100));
        }

        if (blockedList.Count > 10000)
        {
            _blockedHashes = PrepareSortedArray(blockedList);
            _whitelistHashes = PrepareSortedArray(whiteList);
        }

        return _blockedHashes.Length;
    }
}
