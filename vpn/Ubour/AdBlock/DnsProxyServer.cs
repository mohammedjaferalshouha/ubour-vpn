using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;

namespace Ubour.AdBlock;

public sealed class DnsProxyServer
{
    private static readonly Lazy<DnsProxyServer> _instance = new(() => new DnsProxyServer());
    public static DnsProxyServer Instance => _instance.Value;

    private UdpClient? _udpListener;
    private CancellationTokenSource? _cts;
    private readonly HttpClient _dohClient;
    private string _upstreamDoh = "https://cloudflare-dns.com/dns-query";

    public bool IsRunning => _udpListener != null && _cts is { IsCancellationRequested: false };
    public int BoundPort { get; private set; } = 53;

    public void SetUpstreamDoh(string dohUrl)
    {
        if (!string.IsNullOrWhiteSpace(dohUrl))
            _upstreamDoh = dohUrl.Trim();
    }

    private DnsProxyServer()
    {
        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            EnableMultipleHttp2Connections = true
        };
        _dohClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(5)
        };
    }

    public bool Start(int preferredPort = 53)
    {
        if (IsRunning) return true;

        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        try
        {
            _udpListener = new UdpClient(new IPEndPoint(IPAddress.Loopback, preferredPort));
            BoundPort = preferredPort;
        }
        catch (SocketException)
        {
            if (preferredPort == 53)
            {
                try
                {
                    _udpListener = new UdpClient(new IPEndPoint(IPAddress.Loopback, 5353));
                    BoundPort = 5353;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to bind DNS proxy to fallback port 5353: {ex.Message}");
                    return false;
                }
            }
            else
            {
                return false;
            }
        }

        Task.Run(async () =>
        {
            while (!token.IsCancellationRequested && _udpListener != null)
            {
                try
                {
                    var result = await _udpListener.ReceiveAsync(token);
                    _ = ProcessDnsQueryAsync(result.Buffer, result.RemoteEndPoint, token);
                }
                catch (OperationCanceledException) { break; }
                catch (ObjectDisposedException) { break; }
                catch (Exception ex)
                {
                    Debug.WriteLine($"DNS Receive error: {ex.Message}");
                }
            }
        }, token);

        Debug.WriteLine($"DNS Proxy Server running on 127.0.0.1:{BoundPort}");
        return true;
    }

    public void Stop()
    {
        if (!IsRunning) return;
        try
        {
            _cts?.Cancel();
            _udpListener?.Close();
            _udpListener?.Dispose();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error stopping DNS Proxy: {ex.Message}");
        }
        finally
        {
            _udpListener = null;
            _cts = null;
        }
    }

    private async Task ProcessDnsQueryAsync(byte[] query, IPEndPoint clientEndpoint, CancellationToken ct)
    {
        if (query.Length < 12) return;

        var (domain, qType, questionLength) = ParseQuery(query);
        if (string.IsNullOrEmpty(domain)) return;

        if (AdBlockEngine.Instance.IsDomainBlocked(domain))
        {
            var blockedResponse = CreateBlockedResponse(query, qType, questionLength);
            if (_udpListener != null && !ct.IsCancellationRequested)
            {
                try
                {
                    await _udpListener.SendAsync(blockedResponse, blockedResponse.Length, clientEndpoint);
                }
                catch { }
            }
            return;
        }

        // Forward to Upstream DoH or DNS
        var response = await ResolveUpstreamAsync(query, ct);
        if (response != null && _udpListener != null && !ct.IsCancellationRequested)
        {
            try
            {
                await _udpListener.SendAsync(response, response.Length, clientEndpoint);
            }
            catch { }
        }
    }

    private static (string domain, int qType, int questionLength) ParseQuery(byte[] data)
    {
        if (data.Length < 12) return ("", 0, 0);

        var sb = new StringBuilder();
        var pos = 12;
        while (pos < data.Length)
        {
            var len = data[pos++];
            if (len == 0) break;
            if (pos + len > data.Length) return ("", 0, 0);

            if (sb.Length > 0) sb.Append('.');
            sb.Append(Encoding.ASCII.GetString(data, pos, len));
            pos += len;
        }

        if (pos + 4 > data.Length) return ("", 0, 0);
        var qType = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(pos, 2));
        var questionLength = pos + 4;

        return (sb.ToString(), qType, questionLength);
    }

    private static byte[] CreateBlockedResponse(byte[] query, int qType, int questionLength)
    {
        switch (qType)
        {
            case 1: // Type A -> 0.0.0.0
            {
                var resp = new byte[questionLength + 16];
                resp[0] = query[0];
                resp[1] = query[1];
                resp[2] = 0x81; // Standard response, Recursion Desired
                resp[3] = 0x80; // Recursion Available, NOERROR
                BinaryPrimitives.WriteUInt16BigEndian(resp.AsSpan(4, 2), 1); // QDCOUNT
                BinaryPrimitives.WriteUInt16BigEndian(resp.AsSpan(6, 2), 1); // ANCOUNT
                BinaryPrimitives.WriteUInt16BigEndian(resp.AsSpan(8, 2), 0); // NSCOUNT
                BinaryPrimitives.WriteUInt16BigEndian(resp.AsSpan(10, 2), 0); // ARCOUNT

                // Copy Question Section
                Array.Copy(query, 12, resp, 12, questionLength - 12);

                // Answer: Pointer to QNAME (0xC00C)
                var aPos = questionLength;
                resp[aPos++] = 0xC0;
                resp[aPos++] = 0x0C;
                BinaryPrimitives.WriteUInt16BigEndian(resp.AsSpan(aPos, 2), 1); aPos += 2; // Type A
                BinaryPrimitives.WriteUInt16BigEndian(resp.AsSpan(aPos, 2), 1); aPos += 2; // Class IN
                BinaryPrimitives.WriteUInt32BigEndian(resp.AsSpan(aPos, 4), 300); aPos += 4; // TTL 300
                BinaryPrimitives.WriteUInt16BigEndian(resp.AsSpan(aPos, 2), 4); aPos += 2; // RDLENGTH 4
                resp[aPos++] = 0;
                resp[aPos++] = 0;
                resp[aPos++] = 0;
                resp[aPos++] = 0;
                return resp;
            }

            case 28: // Type AAAA -> ::0
            {
                var resp = new byte[questionLength + 28];
                resp[0] = query[0];
                resp[1] = query[1];
                resp[2] = 0x81;
                resp[3] = 0x80;
                BinaryPrimitives.WriteUInt16BigEndian(resp.AsSpan(4, 2), 1);
                BinaryPrimitives.WriteUInt16BigEndian(resp.AsSpan(6, 2), 1);
                BinaryPrimitives.WriteUInt16BigEndian(resp.AsSpan(8, 2), 0);
                BinaryPrimitives.WriteUInt16BigEndian(resp.AsSpan(10, 2), 0);

                Array.Copy(query, 12, resp, 12, questionLength - 12);

                var aPos = questionLength;
                resp[aPos++] = 0xC0;
                resp[aPos++] = 0x0C;
                BinaryPrimitives.WriteUInt16BigEndian(resp.AsSpan(aPos, 2), 28); aPos += 2; // Type AAAA
                BinaryPrimitives.WriteUInt16BigEndian(resp.AsSpan(aPos, 2), 1); aPos += 2; // Class IN
                BinaryPrimitives.WriteUInt32BigEndian(resp.AsSpan(aPos, 4), 300); aPos += 4; // TTL 300
                BinaryPrimitives.WriteUInt16BigEndian(resp.AsSpan(aPos, 2), 16); aPos += 2; // RDLENGTH 16
                Array.Clear(resp, aPos, 16);
                return resp;
            }

            default: // HTTPS (65), CNAME, TXT -> NOERROR with 0 answers (NODATA)
            {
                var resp = new byte[questionLength];
                resp[0] = query[0];
                resp[1] = query[1];
                resp[2] = 0x81;
                resp[3] = 0x80;
                BinaryPrimitives.WriteUInt16BigEndian(resp.AsSpan(4, 2), 1); // QDCOUNT
                BinaryPrimitives.WriteUInt16BigEndian(resp.AsSpan(6, 2), 0); // ANCOUNT = 0
                BinaryPrimitives.WriteUInt16BigEndian(resp.AsSpan(8, 2), 0);
                BinaryPrimitives.WriteUInt16BigEndian(resp.AsSpan(10, 2), 0);

                Array.Copy(query, 12, resp, 12, questionLength - 12);
                return resp;
            }
        }
    }

    private async Task<byte[]?> ResolveUpstreamAsync(byte[] query, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, _upstreamDoh)
            {
                Content = new ByteArrayContent(query)
            };
            req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/dns-message");
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/dns-message"));

            using var resp = await _dohClient.SendAsync(req, ct);
            if (resp.IsSuccessStatusCode)
            {
                return await resp.Content.ReadAsByteArrayAsync(ct);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"DoH resolution failed: {ex.Message}");
        }

        // Fallback to traditional UDP DNS query to 1.1.1.1
        try
        {
            using var fallbackClient = new UdpClient();
            fallbackClient.Client.ReceiveTimeout = 2500;
            var target = new IPEndPoint(IPAddress.Parse("1.1.1.1"), 53);
            await fallbackClient.SendAsync(query, query.Length, target);
            var result = await fallbackClient.ReceiveAsync(ct);
            return result.Buffer;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"UDP DNS fallback failed: {ex.Message}");
            return null;
        }
    }
}
