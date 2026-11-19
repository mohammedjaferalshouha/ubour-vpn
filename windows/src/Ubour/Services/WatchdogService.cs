using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace Ubour.Services;

public static class WatchdogService
{
    private static Process? _watchdogProcess;

    public static void StartWatchdog(params Process?[] managedProcesses)
    {
        StopWatchdog();

        try
        {
            var identities = new List<(int ProcessId, long StartTicks, string ExecutablePath)>();
            foreach (var process in managedProcesses)
            {
                try
                {
                    if (process == null || process.HasExited) continue;
                    string? path = GoodbyeDpiManager.TryGetProcessImagePath(process);
                    if (string.IsNullOrWhiteSpace(path) && !string.IsNullOrWhiteSpace(process.StartInfo.FileName))
                    {
                        path = Path.GetFullPath(process.StartInfo.FileName);
                    }
                    if (string.IsNullOrWhiteSpace(path)) continue;

                    long startedAt = process.StartTime.ToUniversalTime().Ticks;
                    identities.Add((process.Id, startedAt, path));
                }
                catch { }
            }

            if (identities.Count == 0) return;

            bool stopWinDivert = identities.Any(id =>
                string.Equals(Path.GetFileName(id.ExecutablePath), "goodbyedpi.exe", StringComparison.OrdinalIgnoreCase));

            string script = BuildWatchdogScript(Environment.ProcessId, identities, stopWinDivert);
            string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));

            _watchdogProcess = Process.Start(new ProcessStartInfo
            {
                FileName = ResolvePowerShellExecutable(),
                Arguments = $"-NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand {encoded}",
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            });
            if (_watchdogProcess != null) AppLogger.Info($"[Watchdog] Recovery guard started for {identities.Count} app-owned process(es).");
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"[Watchdog] Failed to start recovery guard: {ex.Message}");
        }
    }

    private static string ResolvePowerShellExecutable()
    {
        if (Environment.Is64BitOperatingSystem && !Environment.Is64BitProcess)
        {
            string sysnativePs = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "Sysnative",
                "WindowsPowerShell",
                "v1.0",
                "powershell.exe");
            if (File.Exists(sysnativePs)) return sysnativePs;
        }
        return "powershell.exe";
    }

    internal static string BuildWatchdogScript(
        int parentPid,
        IReadOnlyList<(int ProcessId, long StartTicks, string ExecutablePath)> identities,
        bool stopWinDivert)
    {
        var entries = identities.Select(identity =>
        {
            string escapedPath = identity.ExecutablePath.Replace("'", "''", StringComparison.Ordinal);
            return $"@{{ Id={identity.ProcessId}; StartTicks={identity.StartTicks}; Path='{escapedPath}' }}";
        });

        var sb = new StringBuilder();
        sb.Append("$ErrorActionPreference='SilentlyContinue'; ");
        sb.Append($"Wait-Process -Id {parentPid}; ");
        sb.Append($"foreach ($identity in @({string.Join(",", entries)})) {{ ");
        sb.Append("$owned=Get-Process -Id $identity.Id; ");
        sb.Append("$ownedPath=$owned.Path; ");
        sb.Append("if (-not $ownedPath) { $ownedPath=(Get-CimInstance Win32_Process -Filter ('ProcessId=' + $identity.Id)).ExecutablePath }; ");
        sb.Append("if ($owned -and $ownedPath -ieq $identity.Path -and [math]::Abs($owned.StartTime.ToUniversalTime().Ticks - $identity.StartTicks) -lt 10000000) { ");
        sb.Append("Stop-Process -Id $identity.Id -Force -ErrorAction SilentlyContinue; ");
        sb.Append("Wait-Process -Id $identity.Id -Timeout 5 -ErrorAction SilentlyContinue } }");

        if (stopWinDivert)
        {
            sb.Append("; foreach ($svc in @('WinDivert','WinDivert1.4','WinDivert14')) { ");
            sb.Append("& sc.exe stop $svc | Out-Null; ");
            sb.Append("for ($i = 0; $i -lt 40; $i++) { ");
            sb.Append("$q = (& sc.exe query $svc 2>&1 | Out-String); ");
            sb.Append("if ($LASTEXITCODE -eq 1060 -or $q -match 'STOPPED') { break }; ");
            sb.Append("Start-Sleep -Milliseconds 100 }; ");
            sb.Append("& sc.exe delete $svc | Out-Null }");
        }

        return sb.ToString();
    }

    public static void StopWatchdog()
    {
        try
        {
            if (_watchdogProcess != null && !_watchdogProcess.HasExited)
                _watchdogProcess.Kill(true);
        }
        catch { }
        finally
        {
            _watchdogProcess?.Dispose();
            _watchdogProcess = null;
        }
    }
}
