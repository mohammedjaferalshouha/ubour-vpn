using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace Ubour.Services;

public class GoodbyeDpiManager
{
    private Process? _process;
    public bool IsRunning => _process != null && !_process.HasExited;

    private static readonly Dictionary<string, string> DnsV6Map = new(StringComparer.OrdinalIgnoreCase)
    {
        { "1.1.1.1", "2606:4700:4700::1111" },
        { "1.0.0.1", "2606:4700:4700::1001" },
        { "8.8.8.8", "2001:4860:4860::8888" },
        { "8.8.4.4", "2001:4860:4860::8844" },
        { "9.9.9.9", "2620:fe::fe" },
        { "94.140.14.14", "2a10:50c0::ad1:ff" },
        { "94.140.15.15", "2a10:50c0::ad2:ff" }
    };

    public bool Start(string baseDir, string mode = "-1")
    {
        Stop();
        try
        {
            CleanupWinDivertService();

            string arch = Environment.Is64BitOperatingSystem ? "x86_64" : "x86";
            string exePath = Path.Combine(baseDir, "engine", arch, "goodbyedpi.exe");

            if (!File.Exists(exePath))
            {
                exePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "engine", arch, "goodbyedpi.exe");
            }

            if (!File.Exists(exePath)) return false;

            string workDir = Path.GetDirectoryName(exePath)!;

            string dpiFlag = "-1";
            if (mode == "-9" || mode.Equals("Aggressive", StringComparison.OrdinalIgnoreCase))
            {
                dpiFlag = "-9";
            }
            else if (mode == "-5" || mode.Equals("Medium", StringComparison.OrdinalIgnoreCase) || mode.Equals("Compatible", StringComparison.OrdinalIgnoreCase))
            {
                dpiFlag = "-5";
            }
            else
            {
                dpiFlag = "-1";
            }

            string args = dpiFlag;

            var startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = args,
                WorkingDirectory = workDir,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = false,
                RedirectStandardError = false
            };

            _process = Process.Start(startInfo);
            AppLogger.Info($"[GoodbyeDPI] Started with arguments: {args}");
            return _process != null && !_process.HasExited;
        }
        catch (Exception ex)
        {
            AppLogger.Error($"[GoodbyeDPI] Failed to start: {ex.Message}");
            return false;
        }
    }

    public void Stop()
    {
        try
        {
            if (_process != null)
            {
                if (!_process.HasExited)
                {
                    _process.Kill(true);
                    _process.WaitForExit(1000);
                }
                _process.Dispose();
                _process = null;
                AppLogger.Info("[GoodbyeDPI] Stopped cleanly.");
            }

            foreach (var proc in Process.GetProcessesByName("goodbyedpi"))
            {
                try { proc.Kill(); } catch { }
            }

            CleanupWinDivertService();
        }
        catch { }
    }

    private static void CleanupWinDivertService()
    {
        try
        {
            var psi = new ProcessStartInfo("sc", "stop WinDivert")
            {
                CreateNoWindow = true,
                UseShellExecute = false
            };
            Process.Start(psi)?.WaitForExit(300);
        }
        catch { }
    }
}
