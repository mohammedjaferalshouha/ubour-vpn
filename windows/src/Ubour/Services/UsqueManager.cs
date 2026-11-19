using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Ubour.Services;

/// <summary>
/// Manages the supporting Cloudflare WARP MASQUE engine (usque) for HTTP/3 QUIC and HTTP/2 port-443 tunneling.
/// </summary>
public sealed class UsqueManager : IDisposable
{
    private static readonly SemaphoreSlim RegistrationLock = new(1, 1);

    private readonly object _processLock = new();
    private Process? _process;
    private volatile bool _connectedToMasque;
    private volatile string? _startupError;

    public bool IsRunning
    {
        get
        {
            lock (_processLock)
            {
                return _process != null && !_process.HasExited;
            }
        }
    }

    public Process? OwnedProcess
    {
        get
        {
            lock (_processLock)
            {
                return _process;
            }
        }
    }

    public int ListenPort { get; private set; }
    public bool UsingHttp2 { get; private set; }
    public string EndpointHost { get; private set; } = "162.159.198.2";
    public string? LastError => _startupError;
    public bool IsConnectedToMasque => _connectedToMasque;

    /// <summary>
    /// Resolves the architecture-matched usque.exe path under the given package root.
    /// </summary>
    public static string? ResolveExecutablePath(string? baseDir = null)
    {
        string arch = Environment.Is64BitOperatingSystem ? "x86_64" : "x86";
        if (!string.IsNullOrWhiteSpace(baseDir))
        {
            string candidate = Path.Combine(baseDir, "engine", arch, "usque.exe");
            if (File.Exists(candidate)) return candidate;
        }

        string appCandidate = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "engine", arch, "usque.exe");
        return File.Exists(appCandidate) ? appCandidate : null;
    }

    /// <summary>
    /// Resolves the persistent MASQUE account config file path in AppData with Temp fallback.
    /// </summary>
    public static string ResolveDefaultConfigPath()
    {
        string appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Ubour",
            "warp_masque.json");

        try
        {
            string dir = Path.GetDirectoryName(appDataPath)!;
            Directory.CreateDirectory(dir);
            return appDataPath;
        }
        catch
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "Ubour");
            Directory.CreateDirectory(tempDir);
            return Path.Combine(tempDir, "warp_masque.json");
        }
    }

    /// <summary>
    /// Ensures a valid registered & enrolled MASQUE config exists on disk without overwriting valid credentials.
    /// </summary>
    public static async Task<bool> EnsureRegisteredAsync(
        string? baseDir = null,
        string? configPathOverride = null,
        CancellationToken cancellationToken = default)
    {
        string targetPath = !string.IsNullOrWhiteSpace(configPathOverride)
            ? configPathOverride
            : ResolveDefaultConfigPath();

        if (IsValidConfigFile(targetPath))
        {
            return true;
        }

        string? exePath = ResolveExecutablePath(baseDir);
        if (string.IsNullOrEmpty(exePath))
        {
            AppLogger.Error("[MASQUE] usque.exe binary not found in engine directory.");
            return false;
        }

        await RegistrationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsValidConfigFile(targetPath))
            {
                return true;
            }

            string tempDir = Path.Combine(Path.GetTempPath(), "Ubour");
            Directory.CreateDirectory(tempDir);
            string tempRegPath = Path.Combine(tempDir, $"usque_reg_{Guid.NewGuid():N}.json");

            try
            {
                AppLogger.Info("[MASQUE] Registering and enrolling Cloudflare WARP MASQUE device...");
                var startInfo = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = $"-c \"{tempRegPath}\" register -a -n \"Ubour\"",
                    WorkingDirectory = Path.GetDirectoryName(exePath)!,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var proc = Process.Start(startInfo);
                if (proc == null) return false;

                var stdoutTask = proc.StandardOutput.ReadToEndAsync(cancellationToken);
                var stderrTask = proc.StandardError.ReadToEndAsync(cancellationToken);

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(15));

                try
                {
                    await proc.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
                }
                catch
                {
                    try { if (!proc.HasExited) proc.Kill(true); } catch { }
                    AppLogger.Warn("[MASQUE] Device registration timed out.");
                    return false;
                }

                string stderr = await stderrTask.ConfigureAwait(false);
                _ = await stdoutTask.ConfigureAwait(false);

                if (proc.ExitCode != 0 || !IsValidConfigFile(tempRegPath))
                {
                    AppLogger.Warn($"[MASQUE] Registration failed (exit={proc.ExitCode}): {stderr.Trim()}");
                    return false;
                }

                string targetDir = Path.GetDirectoryName(targetPath)!;
                Directory.CreateDirectory(targetDir);
                File.Copy(tempRegPath, targetPath, overwrite: true);
                AppLogger.Info("[MASQUE] Device registered and enrolled successfully.");
                return true;
            }
            finally
            {
                try { if (File.Exists(tempRegPath)) File.Delete(tempRegPath); } catch { }
            }
        }
        finally
        {
            RegistrationLock.Release();
        }
    }

    internal static bool IsValidConfigFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return false;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            string? priv = root.TryGetProperty("private_key", out var p) ? p.GetString() : null;
            string? pub = root.TryGetProperty("endpoint_pub_key", out var k) ? k.GetString() : null;
            return !string.IsNullOrWhiteSpace(priv) && !string.IsNullOrWhiteSpace(pub);
        }
        catch
        {
            return false;
        }
    }

    internal static string ReadEndpointHost(string configPath, bool useHttp2)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(configPath));
            var root = doc.RootElement;
            string key = useHttp2 ? "endpoint_h2_v4" : "endpoint_v4";
            if (root.TryGetProperty(key, out var val))
            {
                string? host = val.GetString();
                if (!string.IsNullOrWhiteSpace(host)) return host.Trim();
            }
            if (root.TryGetProperty("endpoint_v4", out var fallback))
            {
                string? host = fallback.GetString();
                if (!string.IsNullOrWhiteSpace(host)) return host.Trim();
            }
        }
        catch { }

        return "162.159.198.2";
    }

    /// <summary>
    /// Starts usque in local SOCKS5 proxy mode (--always-reconnect) and verifies live encrypted WARP traffic.
    /// </summary>
    public async Task<WarpConnectivityResult> StartAndVerifyAsync(
        string? baseDir = null,
        bool useHttp2 = false,
        int timeoutMs = 6500,
        bool measureSpeed = false,
        string dnsServer = "1.1.1.1",
        string? configPathOverride = null)
    {
        Stop();

        string cfgPath = !string.IsNullOrWhiteSpace(configPathOverride)
            ? configPathOverride
            : ResolveDefaultConfigPath();

        if (!await EnsureRegisteredAsync(baseDir, cfgPath).ConfigureAwait(false))
        {
            return new WarpConnectivityResult
            {
                Success = false,
                ErrorMessage = "Failed to register Cloudflare WARP MASQUE device."
            };
        }

        string? exePath = ResolveExecutablePath(baseDir);
        if (string.IsNullOrEmpty(exePath))
        {
            return new WarpConnectivityResult
            {
                Success = false,
                ErrorMessage = "usque.exe binary not found."
            };
        }

        int port = GetFreeLocalPort();
        string endpointHost = ReadEndpointHost(cfgPath, useHttp2);
        string http2Arg = useHttp2 ? " --http2" : "";
        string args = $"-c \"{cfgPath}\" socks -b 127.0.0.1 -p {port} -d {dnsServer} --always-reconnect{http2Arg}";

        _connectedToMasque = false;
        _startupError = null;
        UsingHttp2 = useHttp2;
        EndpointHost = endpointHost;

        var startInfo = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = args,
            WorkingDirectory = Path.GetDirectoryName(exePath)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        var proc = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        proc.OutputDataReceived += (_, e) => { if (!string.IsNullOrEmpty(e.Data)) HandleLogLine(e.Data, false); };
        proc.ErrorDataReceived += (_, e) => { if (!string.IsNullOrEmpty(e.Data)) HandleLogLine(e.Data, true); };

        if (!proc.Start())
        {
            proc.Dispose();
            return new WarpConnectivityResult { Success = false, ErrorMessage = "Failed to launch usque.exe." };
        }

        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        lock (_processLock)
        {
            _process = proc;
            ListenPort = port;
        }

        bool listening = await WaitForReadyAsync(proc, port, Math.Min(4000, timeoutMs)).ConfigureAwait(false);
        if (!listening)
        {
            string err = _startupError ?? "usque failed to connect to MASQUE server.";
            Stop();
            return new WarpConnectivityResult { Success = false, ErrorMessage = err };
        }

        var probe = await ProbeSocksProxyAsync(port, timeoutMs, measureSpeed).ConfigureAwait(false);
        if (!probe.Success)
        {
            if (!string.IsNullOrWhiteSpace(_startupError) && string.IsNullOrWhiteSpace(probe.ErrorMessage))
            {
                probe.ErrorMessage = _startupError;
            }
            Stop();
            return probe;
        }

        return probe;
    }

    /// <summary>
    /// Probes Cloudflare trace (and optional download speed) directly through a running SOCKS5 proxy port.
    /// </summary>
    public static async Task<WarpConnectivityResult> ProbeSocksProxyAsync(
        int socksPort,
        int timeoutMs = 5500,
        bool measureSpeed = false)
    {
        try
        {
            using var handler = new SocketsHttpHandler
            {
                Proxy = new WebProxy($"socks5://127.0.0.1:{socksPort}"),
                UseProxy = true,
                ConnectTimeout = TimeSpan.FromMilliseconds(timeoutMs)
            };
            using var http = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromMilliseconds(timeoutMs)
            };

            string? traceBody = null;
            long latencyMs = 0;

            foreach (string traceUrl in new[] { "https://1.1.1.1/cdn-cgi/trace", "https://www.cloudflare.com/cdn-cgi/trace" })
            {
                try
                {
                    var sw = Stopwatch.StartNew();
                    using var resp = await http.GetAsync(traceUrl).ConfigureAwait(false);
                    if (resp.IsSuccessStatusCode)
                    {
                        string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                        sw.Stop();
                        if (!string.IsNullOrWhiteSpace(body) &&
                            body.Contains("colo=", StringComparison.OrdinalIgnoreCase) &&
                            !body.Contains("warp=off", StringComparison.OrdinalIgnoreCase))
                        {
                            traceBody = body;
                            latencyMs = Math.Max(1, sw.ElapsedMilliseconds);
                            break;
                        }
                    }
                }
                catch { }
            }

            if (string.IsNullOrWhiteSpace(traceBody))
            {
                return new WarpConnectivityResult
                {
                    Success = false,
                    ErrorMessage = "Cloudflare MASQUE handshake or encrypted tunnel verification failed."
                };
            }

            var location = WarpManager.ResolveLocationFromTrace(traceBody);
            double downloadMbps = 0;

            if (measureSpeed)
            {
                try
                {
                    var speedSw = Stopwatch.StartNew();
                    byte[] bytes = await http.GetByteArrayAsync("https://speed.cloudflare.com/__down?bytes=524288").ConfigureAwait(false);
                    speedSw.Stop();
                    if (bytes.Length > 0 && speedSw.Elapsed.TotalSeconds > 0.001)
                    {
                        downloadMbps = (bytes.Length * 8.0) / (speedSw.Elapsed.TotalSeconds * 1_000_000.0);
                    }
                }
                catch { }
            }

            return new WarpConnectivityResult
            {
                Success = true,
                LatencyMs = latencyMs,
                DownloadMbps = downloadMbps,
                Location = location
            };
        }
        catch (Exception ex)
        {
            return new WarpConnectivityResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    /// <summary>
    /// Runs an isolated MASQUE connectivity probe (HTTP/3 QUIC 443 first, with optional HTTP/2 TLS 443 fallback)
    /// and guarantees the temporary usque process stops cleanly before returning.
    /// </summary>
    public static async Task<(WarpConnectivityResult Result, bool UsedHttp2)> VerifyMasqueConnectivityAsync(
        string? baseDir = null,
        bool allowHttp2Fallback = true,
        int timeoutMs = 6000,
        bool measureSpeed = false,
        string? configPathOverride = null)
    {
        using var probe = new UsqueManager();
        var quicRes = await probe.StartAndVerifyAsync(
            baseDir: baseDir,
            useHttp2: false,
            timeoutMs: timeoutMs,
            measureSpeed: measureSpeed,
            configPathOverride: configPathOverride).ConfigureAwait(false);

        if (quicRes.Success || !allowHttp2Fallback)
        {
            probe.Stop();
            return (quicRes, false);
        }

        probe.Stop();
        var h2Res = await probe.StartAndVerifyAsync(
            baseDir: baseDir,
            useHttp2: true,
            timeoutMs: timeoutMs,
            measureSpeed: measureSpeed,
            configPathOverride: configPathOverride).ConfigureAwait(false);

        probe.Stop();
        return (h2Res, h2Res.Success);
    }

    private void HandleLogLine(string line, bool isError)
    {
        if (line.Contains("Connected to MASQUE server", StringComparison.OrdinalIgnoreCase))
        {
            _connectedToMasque = true;
        }
        else if (line.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
                 line.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                 line.Contains("no such host", StringComparison.OrdinalIgnoreCase) ||
                 line.Contains("timed out", StringComparison.OrdinalIgnoreCase))
        {
            // Ignore benign client-side close/UDP messages once connected; retain startup failures
            if (!_connectedToMasque && !line.Contains("Config file not found", StringComparison.OrdinalIgnoreCase))
            {
                _startupError ??= line;
            }
        }

        if (isError && !_connectedToMasque && !line.Contains("Hint:", StringComparison.OrdinalIgnoreCase) && !line.Contains("Config file not found", StringComparison.OrdinalIgnoreCase))
        {
            AppLogger.Warn($"[usque] {line}");
        }
        else
        {
            AppLogger.Info($"[usque] {line}");
        }
    }

    private async Task<bool> WaitForReadyAsync(Process process, int port, int timeoutMs)
    {
        var sw = Stopwatch.StartNew();
        bool portReady = false;

        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (process.HasExited) return false;

            if (!portReady)
            {
                try
                {
                    using var client = new TcpClient();
                    var connectTask = client.ConnectAsync(IPAddress.Loopback, port);
                    if (await Task.WhenAny(connectTask, Task.Delay(150)).ConfigureAwait(false) == connectTask && client.Connected)
                    {
                        portReady = true;
                    }
                }
                catch { }
            }

            if (portReady && _connectedToMasque)
            {
                return true;
            }

            await Task.Delay(80).ConfigureAwait(false);
        }

        return portReady && !process.HasExited;
    }

    /// <summary>
    /// Finds a loopback port where BOTH TCP and UDP are currently free to bind (avoids Hyper-V/WinNAT UDP exclusions).
    /// </summary>
    internal static int GetFreeLocalPort()
    {
        for (int attempt = 0; attempt < 25; attempt++)
        {
            using var udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            int port = ((IPEndPoint)udp.Client.LocalEndPoint!).Port;
            try
            {
                using var tcp = new TcpListener(IPAddress.Loopback, port);
                tcp.Start();
                return port;
            }
            catch
            {
                // TCP port occupied; try another ephemeral port.
            }
        }

        using var fallback = new TcpListener(IPAddress.Loopback, 0);
        fallback.Start();
        return ((IPEndPoint)fallback.LocalEndpoint).Port;
    }

    public void Stop()
    {
        Process? procToStop;
        lock (_processLock)
        {
            procToStop = _process;
            _process = null;
            ListenPort = 0;
        }

        _connectedToMasque = false;

        if (procToStop != null)
        {
            try
            {
                if (!procToStop.HasExited)
                {
                    procToStop.Kill(true);
                    procToStop.WaitForExit(1500);
                }
            }
            catch { }
            finally
            {
                try { procToStop.Dispose(); } catch { }
            }
        }
    }

    public void Dispose()
    {
        Stop();
    }
}
