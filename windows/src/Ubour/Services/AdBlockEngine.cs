using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Ubour.Services;

public class AdBlockEngine
{
    private const long FNV_OFFSET_BASIS = unchecked((long)0xcbf29ce484222325UL);
    private const long FNV_PRIME = 1099511628211L;

    private static long[] _blockedHashes = Array.Empty<long>();
    private static long[] _whitelistHashes = Array.Empty<long>();
    private static readonly HashSet<string> ProtectedSystemWhitelist = new(StringComparer.OrdinalIgnoreCase)
    {
        "youtube.com", "www.youtube.com", "m.youtube.com", "googlevideo.com", "ytimg.com", "i.ytimg.com",
        "google.com", "www.google.com", "google.jo", "google.com.sa", "google.com.eg", "google.com.kw",
        "chatgpt.com", "openai.com", "chat.openai.com", "ws.chatgpt.com",
        "whatsapp.com", "whatsapp.net", "web.whatsapp.com",
        "cloudflare.com", "microsoft.com", "apple.com", "github.com", "githubusercontent.com",
        "dns.msftncsi.com", "msftncsi.com", "www.msftconnecttest.com", "msftconnecttest.com", "ipv6.msftconnecttest.com",
        "netlify.app", "global-weather-observatory.netlify.app",
        "speedtest.net", "www.speedtest.net", "fast.com", "netflix.com",
        "turtlecute.org", "adblock.turtlecute.org", "adblock-tester.com", "d3ward.github.io"
    };

    private static readonly HashSet<string> AllowedAdSubdomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "speedtest.net", "www.speedtest.net", "turtlecute.org", "adblock.turtlecute.org", "adblock-tester.com", "d3ward.github.io"
    };

    private static readonly object _initLock = new();

    private static readonly string[] DohEndpoints = new[]
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
        "doh.pub",
        "sm2.doh.pub"
    };

    public static int TotalRules => _blockedHashes.Length;
    public static long BlockedAdsCount { get; private set; } = 0;
    public static long BlockedTrackersCount { get; private set; } = 0;
    public static long TotalQueries { get; private set; } = 0;

    private UdpClient? _udpServer;
    private UdpClient? _udpServerV6;
    private TcpListener? _tcpServer;
    private TcpListener? _tcpServerV6;
    private CancellationTokenSource? _cts;
    private string _upstreamDns = "1.1.1.1";
    private string _secondaryDns = "1.0.0.1";
    public bool IsRunning { get; private set; } = false;

    public static void ResetStats()
    {
        BlockedAdsCount = 0;
        BlockedTrackersCount = 0;
        TotalQueries = 0;
    }

    private static long HashDomain(string domain)
    {
        long hash = FNV_OFFSET_BASIS;
        for (int i = 0; i < domain.Length; i++)
        {
            hash ^= (long)domain[i];
            hash *= FNV_PRIME;
        }
        return hash;
    }

    public static void Initialize(string baseDir)
    {
        lock (_initLock)
        {
            if (_blockedHashes.Length > 0) return;

            var blockedList = new List<long>(4_200_000);
            var whiteList = new List<long>(20_000);

            foreach (var doh in DohEndpoints)
            {
                blockedList.Add(HashDomain(doh.ToLowerInvariant()));
            }

            string rulesPath = Path.Combine(baseDir, "rules", "adblock_rules.txt");
            if (!File.Exists(rulesPath))
            {
                rulesPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "rules", "adblock_rules.txt");
            }

            if (File.Exists(rulesPath))
            {
                try
                {
                    using var reader = new StreamReader(rulesPath, Encoding.UTF8);
                    string? line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        string trimmed = line.Trim();
                        if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#") || trimmed.StartsWith("!") || trimmed.StartsWith("[")) continue;
                        ParseRule(trimmed, blockedList, whiteList);
                    }
                }
                catch { }
            }

            _blockedHashes = PrepareSortedArray(blockedList);
            _whitelistHashes = PrepareSortedArray(whiteList);
        }
    }

    private static void ParseRule(string rule, List<long> blockedList, List<long> whiteList)
    {
        // Ignore cosmetic / element hiding rules
        if (rule.Contains("##") || rule.Contains("#@#") || rule.Contains("#?#") || rule.Contains("#$#") || rule.Contains("$$"))
        {
            return;
        }

        bool isWhitelist = false;
        if (rule.StartsWith("@@"))
        {
            isWhitelist = true;
            rule = rule.Substring(2);
        }

        if (rule.StartsWith("||"))
        {
            rule = rule.Substring(2);
        }
        else if (rule.StartsWith("0.0.0.0 ") || rule.StartsWith("127.0.0.1 "))
        {
            var parts = rule.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                string domain = parts[1].Trim().ToLowerInvariant();
                if (domain != "localhost" && domain != "broadcasthost" && domain.Contains('.') && !domain.Contains('#'))
                {
                    if (isWhitelist) whiteList.Add(HashDomain(domain));
                    else blockedList.Add(HashDomain(domain));
                }
            }
            return;
        }
        else if (rule.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            rule = rule.Substring(7);
        }
        else if (rule.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            rule = rule.Substring(8);
        }

        // Remove options starting with $ (e.g. $third-party,image)
        int dollarIdx = rule.IndexOf('$');
        if (dollarIdx != -1)
        {
            rule = rule.Substring(0, dollarIdx);
        }

        // Remove trailing carat ^
        int caratIdx = rule.IndexOf('^');
        if (caratIdx != -1)
        {
            rule = rule.Substring(0, caratIdx);
        }

        // If rule contains path slash, comma list, wildcard or port, ignore for DNS level blocking
        if (rule.Contains('/') || rule.Contains(',') || rule.Contains('*') || rule.Contains(':'))
        {
            return;
        }

        string cleanDomain = rule.Trim().Trim('.').ToLowerInvariant();

        if (cleanDomain.Contains('.') && !cleanDomain.Contains(' ') && cleanDomain.Length >= 3)
        {
            if (isWhitelist)
            {
                whiteList.Add(HashDomain(cleanDomain));
            }
            else
            {
                blockedList.Add(HashDomain(cleanDomain));
            }
        }
    }

    private static long[] PrepareSortedArray(List<long> list)
    {
        if (list.Count == 0) return Array.Empty<long>();
        var arr = list.ToArray();
        Array.Sort(arr);

        int uniqueCount = 1;
        for (int i = 1; i < arr.Length; i++)
        {
            if (arr[i] != arr[i - 1])
            {
                arr[uniqueCount++] = arr[i];
            }
        }
        Array.Resize(ref arr, uniqueCount);
        return arr;
    }

    public static bool IsDomainBlocked(string rawDomain)
    {
        TotalQueries++;
        string domain = rawDomain.Trim().TrimEnd('.').ToLowerInvariant();
        if (string.IsNullOrEmpty(domain)) return false;

        if (ProtectedSystemWhitelist.Contains(domain) && !AllowedAdSubdomains.Contains(domain))
        {
            return false;
        }


        var localBlocked = _blockedHashes;
        var localWhite = _whitelistHashes;

        if (localWhite.Length > 0)
        {
            long dHash = HashDomain(domain);
            if (Array.BinarySearch(localWhite, dHash) >= 0) return false;
        }

        if (localBlocked.Length == 0) return false;

        // 1. Exact match
        long hash = HashDomain(domain);
        if (Array.BinarySearch(localBlocked, hash) >= 0)
        {
            RecordBlock(domain);
            return true;
        }

        // 2. Subdomain hierarchical matching
        string current = domain;
        while (current.Contains("."))
        {
            int dot = current.IndexOf('.');
            if (dot == -1 || dot == current.Length - 1) break;
            current = current.Substring(dot + 1);
            long subHash = HashDomain(current);
            if (Array.BinarySearch(localBlocked, subHash) >= 0)
            {
                RecordBlock(domain);
                AppLogger.Block(domain, "DNS-BLOCK");
                return true;
            }
        }

        return false;
    }

    private static void RecordBlock(string domain)
    {
        if (domain.Contains("track") || domain.Contains("analytics") || domain.Contains("metric") || domain.Contains("telemetry") || domain.Contains("adjust") || domain.Contains("appsflyer"))
        {
            BlockedTrackersCount++;
        }
        else
        {
            BlockedAdsCount++;
        }
    }

    public bool Start(string upstreamDns = "1.1.1.1", string? secondaryDns = null, int port = 53)
    {
        Stop();
        _upstreamDns = !string.IsNullOrWhiteSpace(upstreamDns) ? upstreamDns : "1.1.1.1";
        if (!string.IsNullOrWhiteSpace(secondaryDns))
        {
            _secondaryDns = secondaryDns;
        }
        else
        {
            _secondaryDns = DnsManager.ResolveDnsPair(_upstreamDns).secondaryV4;
        }
        _cts = new CancellationTokenSource();

        try
        {
            _udpServer = new UdpClient(new IPEndPoint(IPAddress.Loopback, port));
            Task.Run(() => ListenUdpLoopAsync(_udpServer, _cts.Token));

            _tcpServer = new TcpListener(IPAddress.Loopback, port);
            _tcpServer.Start();
            Task.Run(() => ListenTcpLoopAsync(_tcpServer, _cts.Token));

            try
            {
                _udpServerV6 = new UdpClient(new IPEndPoint(IPAddress.IPv6Loopback, port));
                Task.Run(() => ListenUdpLoopAsync(_udpServerV6, _cts.Token));

                _tcpServerV6 = new TcpListener(IPAddress.IPv6Loopback, port);
                _tcpServerV6.Start();
                Task.Run(() => ListenTcpLoopAsync(_tcpServerV6, _cts.Token));
            }
            catch { }

            IsRunning = true;
            AppLogger.Info($"[DNS Filter] Started on 127.0.0.1 & [::1]:{port} (Dual-Stack) with upstream {_upstreamDns} and backup {_secondaryDns}");
            return true;
        }
        catch
        {
            Stop();
            return false;
        }
    }

    public void Stop()
    {
        IsRunning = false;
        try
        {
            _cts?.Cancel();

            _udpServer?.Close();
            _udpServer?.Dispose();
            _udpServer = null;

            _udpServerV6?.Close();
            _udpServerV6?.Dispose();
            _udpServerV6 = null;

            _tcpServer?.Stop();
            _tcpServer = null;

            _tcpServerV6?.Stop();
            _tcpServerV6 = null;
        }
        catch { }
    }

    public bool VerifyHealth(int port = 53, int timeoutMs = 1500)
    {
        try
        {
            using var client = new TcpClient();
            var connectTask = client.ConnectAsync(IPAddress.Loopback, port);
            if (!connectTask.Wait(timeoutMs)) return false;
            return client.Connected;
        }
        catch
        {
            return false;
        }
    }

    private async Task ListenUdpLoopAsync(UdpClient server, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var result = await server.ReceiveAsync(ct);
                _ = Task.Run(() => ProcessUdpQuery(server, result.Buffer, result.RemoteEndPoint, ct), ct);
            }
            catch (OperationCanceledException) { break; }
            catch { }
        }
    }

    private async Task ProcessUdpQuery(UdpClient server, byte[] query, IPEndPoint clientEp, CancellationToken ct)
    {
        try
        {
            string? qname = ParseQName(query);

            // Instant authoritative response for Windows NCSI probe
            if (qname != null && qname.Equals("dns.msftncsi.com", StringComparison.OrdinalIgnoreCase))
            {
                byte[] ncsiResp = CreateStaticAResponse(query, IPAddress.Parse("131.107.255.255"));
                await server.SendAsync(ncsiResp, ncsiResp.Length, clientEp);
                return;
            }

            if (qname != null && IsDomainBlocked(qname))
            {
                byte[] blockedResp = CreateBlockedResponse(query);
                await server.SendAsync(blockedResp, blockedResp.Length, clientEp);
                return;
            }

            // Primary upstream with 2500ms timeout
            byte[]? upstreamResp = await QueryUpstreamUdpAsync(query, _upstreamDns, 2500, ct);

            // Failover to secondary upstream if primary failed or timed out
            if (upstreamResp == null && !string.IsNullOrWhiteSpace(_secondaryDns) && _secondaryDns != _upstreamDns)
            {
                upstreamResp = await QueryUpstreamUdpAsync(query, _secondaryDns, 2500, ct);
            }

            if (upstreamResp != null)
            {
                await server.SendAsync(upstreamResp, upstreamResp.Length, clientEp);
            }
            else
            {
                byte[] servFail = CreateServFailResponse(query);
                await server.SendAsync(servFail, servFail.Length, clientEp);
            }
        }
        catch { }
    }

    private async Task ListenTcpLoopAsync(TcpListener server, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var client = await server.AcceptTcpClientAsync(ct);
                _ = Task.Run(() => ProcessTcpClientAsync(client, ct), ct);
            }
            catch (OperationCanceledException) { break; }
            catch { }
        }
    }

    private async Task ProcessTcpClientAsync(TcpClient client, CancellationToken ct)
    {
        using (client)
        {
            try
            {
                using var stream = client.GetStream();
                stream.ReadTimeout = 3000;
                stream.WriteTimeout = 3000;

                byte[] lenBuf = new byte[2];
                int read = await stream.ReadAsync(lenBuf, 0, 2, ct);
                if (read < 2) return;

                int queryLen = (lenBuf[0] << 8) | lenBuf[1];
                if (queryLen <= 0 || queryLen > 4096) return;

                byte[] queryBuf = new byte[queryLen];
                int totalRead = 0;
                while (totalRead < queryLen)
                {
                    int r = await stream.ReadAsync(queryBuf, totalRead, queryLen - totalRead, ct);
                    if (r <= 0) break;
                    totalRead += r;
                }
                if (totalRead < queryLen) return;

                string? qname = ParseQName(queryBuf);

                // Instant authoritative response for Windows NCSI probe
                if (qname != null && qname.Equals("dns.msftncsi.com", StringComparison.OrdinalIgnoreCase))
                {
                    byte[] ncsiResp = CreateStaticAResponse(queryBuf, IPAddress.Parse("131.107.255.255"));
                    byte[] outLenBuf = new byte[] { (byte)((ncsiResp.Length >> 8) & 0xFF), (byte)(ncsiResp.Length & 0xFF) };
                    await stream.WriteAsync(outLenBuf, 0, 2, ct);
                    await stream.WriteAsync(ncsiResp, 0, ncsiResp.Length, ct);
                    return;
                }

                if (qname != null && IsDomainBlocked(qname))
                {
                    byte[] blockedResp = CreateBlockedResponse(queryBuf);
                    byte[] outLenBuf = new byte[] { (byte)((blockedResp.Length >> 8) & 0xFF), (byte)(blockedResp.Length & 0xFF) };
                    await stream.WriteAsync(outLenBuf, 0, 2, ct);
                    await stream.WriteAsync(blockedResp, 0, blockedResp.Length, ct);
                    return;
                }

                byte[]? tcpResp = await QueryUpstreamTcpAsync(queryBuf, _upstreamDns, 2500, ct);
                if (tcpResp == null && !string.IsNullOrWhiteSpace(_secondaryDns) && _secondaryDns != _upstreamDns)
                {
                    tcpResp = await QueryUpstreamTcpAsync(queryBuf, _secondaryDns, 2500, ct);
                }

                if (tcpResp != null)
                {
                    byte[] outLenBuf = new byte[] { (byte)((tcpResp.Length >> 8) & 0xFF), (byte)(tcpResp.Length & 0xFF) };
                    await stream.WriteAsync(outLenBuf, 0, 2, ct);
                    await stream.WriteAsync(tcpResp, 0, tcpResp.Length, ct);
                }
                else
                {
                    byte[] servFail = CreateServFailResponse(queryBuf);
                    byte[] outLenBuf = new byte[] { (byte)((servFail.Length >> 8) & 0xFF), (byte)(servFail.Length & 0xFF) };
                    await stream.WriteAsync(outLenBuf, 0, 2, ct);
                    await stream.WriteAsync(servFail, 0, servFail.Length, ct);
                }
            }
            catch { }
        }
    }

    private static string? ParseQName(byte[] data)
    {
        if (data.Length < 12) return null;
        int offset = 12;
        var sb = new StringBuilder();

        while (offset < data.Length)
        {
            int len = data[offset] & 0xFF;
            if (len == 0) break;
            if (len > 63) return null;
            offset++;

            if (offset + len > data.Length) return null;
            if (sb.Length > 0) sb.Append('.');
            sb.Append(Encoding.ASCII.GetString(data, offset, len));
            offset += len;
        }

        return sb.Length > 0 ? sb.ToString() : null;
    }

    private static byte[] CreateBlockedResponse(byte[] query)
    {
        if (query.Length < 12) return query;

        int offset = 12;
        while (offset < query.Length)
        {
            int len = query[offset] & 0xFF;
            if (len == 0)
            {
                offset += 1;
                break;
            }
            offset += len + 1;
        }

        if (offset + 4 > query.Length) return query;

        int qtype = ((query[offset] & 0xFF) << 8) | (query[offset + 1] & 0xFF);
        int questionLength = offset + 4; // QNAME + 0x00 + QTYPE (2) + QCLASS (2)

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        bw.Write(query[0]);
        bw.Write(query[1]);
        bw.Write((byte)0x81);
        bw.Write((byte)0x80); // Flags: standard response, NOERROR
        bw.Write((byte)0x00);
        bw.Write((byte)0x01); // QDCOUNT = 1

        if (qtype == 28) // Type AAAA (IPv6) -> ::0
        {
            bw.Write((byte)0x00);
            bw.Write((byte)0x01); // ANCOUNT = 1
            bw.Write((byte)0x00);
            bw.Write((byte)0x00); // NSCOUNT = 0
            bw.Write((byte)0x00);
            bw.Write((byte)0x00); // ARCOUNT = 0

            bw.Write(query, 12, questionLength - 12);

            bw.Write((byte)0xC0);
            bw.Write((byte)0x0C); // Pointer to QNAME
            bw.Write((byte)0x00);
            bw.Write((byte)0x1C); // Type AAAA (28)
            bw.Write((byte)0x00);
            bw.Write((byte)0x01); // Class IN (1)
            bw.Write((byte)0x00);
            bw.Write((byte)0x00);
            bw.Write((byte)0x01);
            bw.Write((byte)0x2C); // TTL = 300
            bw.Write((byte)0x00);
            bw.Write((byte)0x10); // RDLENGTH = 16
            for (int i = 0; i < 16; i++) bw.Write((byte)0x00);
        }
        else if (qtype == 1) // Type A (IPv4) -> 0.0.0.0
        {
            bw.Write((byte)0x00);
            bw.Write((byte)0x01); // ANCOUNT = 1
            bw.Write((byte)0x00);
            bw.Write((byte)0x00); // NSCOUNT = 0
            bw.Write((byte)0x00);
            bw.Write((byte)0x00); // ARCOUNT = 0

            bw.Write(query, 12, questionLength - 12);

            bw.Write((byte)0xC0);
            bw.Write((byte)0x0C); // Pointer to QNAME
            bw.Write((byte)0x00);
            bw.Write((byte)0x01); // Type A (1)
            bw.Write((byte)0x00);
            bw.Write((byte)0x01); // Class IN (1)
            bw.Write((byte)0x00);
            bw.Write((byte)0x00);
            bw.Write((byte)0x01);
            bw.Write((byte)0x2C); // TTL = 300
            bw.Write((byte)0x00);
            bw.Write((byte)0x04); // RDLENGTH = 4
            bw.Write((byte)0x00);
            bw.Write((byte)0x00);
            bw.Write((byte)0x00);
            bw.Write((byte)0x00);
        }
        else
        {
            bw.Write((byte)0x00);
            bw.Write((byte)0x00); // ANCOUNT = 0
            bw.Write((byte)0x00);
            bw.Write((byte)0x00);
            bw.Write((byte)0x00);
            bw.Write((byte)0x00);

            bw.Write(query, 12, questionLength - 12);
        }

        return ms.ToArray();
    }

    private static async Task<byte[]?> QueryUpstreamUdpAsync(byte[] query, string upstreamIp, int timeoutMs, CancellationToken ct)
    {
        try
        {
            var upstreamEp = new IPEndPoint(IPAddress.Parse(upstreamIp), 53);
            using var forwarder = new UdpClient();
            forwarder.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(timeoutMs);

            await forwarder.SendAsync(query, query.Length, upstreamEp);
            var result = await forwarder.ReceiveAsync(timeoutCts.Token);
            return result.Buffer;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<byte[]?> QueryUpstreamTcpAsync(byte[] query, string upstreamIp, int timeoutMs, CancellationToken ct)
    {
        try
        {
            using var upstreamTcp = new TcpClient();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(timeoutMs);

            await upstreamTcp.ConnectAsync(IPAddress.Parse(upstreamIp), 53, timeoutCts.Token);
            using var upStream = upstreamTcp.GetStream();
            upStream.ReadTimeout = timeoutMs;
            upStream.WriteTimeout = timeoutMs;

            byte[] lenBuf = new byte[] { (byte)((query.Length >> 8) & 0xFF), (byte)(query.Length & 0xFF) };
            await upStream.WriteAsync(lenBuf, 0, 2, timeoutCts.Token);
            await upStream.WriteAsync(query, 0, query.Length, timeoutCts.Token);

            byte[] upLenBuf = new byte[2];
            int upRead = await upStream.ReadAsync(upLenBuf, 0, 2, timeoutCts.Token);
            if (upRead < 2) return null;

            int respLen = (upLenBuf[0] << 8) | upLenBuf[1];
            if (respLen <= 0 || respLen > 4096) return null;

            byte[] respBuf = new byte[respLen];
            int totalRespRead = 0;
            while (totalRespRead < respLen)
            {
                int r = await upStream.ReadAsync(respBuf, totalRespRead, respLen - totalRespRead, timeoutCts.Token);
                if (r <= 0) break;
                totalRespRead += r;
            }
            return totalRespRead == respLen ? respBuf : null;
        }
        catch
        {
            return null;
        }
    }

    private static byte[] CreateServFailResponse(byte[] query)
    {
        if (query.Length < 12) return query;
        byte[] resp = (byte[])query.Clone();
        resp[2] = 0x81;
        resp[3] = 0x82; // SERVFAIL
        return resp;
    }

    private static byte[] CreateStaticAResponse(byte[] query, IPAddress ip)
    {
        if (query.Length < 12) return query;

        int offset = 12;
        while (offset < query.Length)
        {
            int len = query[offset] & 0xFF;
            if (len == 0)
            {
                offset += 1;
                break;
            }
            offset += len + 1;
        }

        if (offset + 4 > query.Length) return query;

        int questionLength = offset + 4;

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        bw.Write(query[0]);
        bw.Write(query[1]);
        bw.Write((byte)0x81);
        bw.Write((byte)0x80); // Flags: standard response, NOERROR
        bw.Write((byte)0x00);
        bw.Write((byte)0x01); // QDCOUNT = 1
        bw.Write((byte)0x00);
        bw.Write((byte)0x01); // ANCOUNT = 1
        bw.Write((byte)0x00);
        bw.Write((byte)0x00); // NSCOUNT = 0
        bw.Write((byte)0x00);
        bw.Write((byte)0x00); // ARCOUNT = 0

        bw.Write(query, 12, questionLength - 12);

        bw.Write((byte)0xC0);
        bw.Write((byte)0x0C); // Pointer to QNAME
        bw.Write((byte)0x00);
        bw.Write((byte)0x01); // Type A (1)
        bw.Write((byte)0x00);
        bw.Write((byte)0x01); // Class IN (1)
        bw.Write((byte)0x00);
        bw.Write((byte)0x00);
        bw.Write((byte)0x00);
        bw.Write((byte)0x3C); // TTL = 60
        bw.Write((byte)0x00);
        bw.Write((byte)0x04); // RDLENGTH = 4

        byte[] ipBytes = ip.GetAddressBytes();
        bw.Write(ipBytes, 0, 4);

        return ms.ToArray();
    }
}
