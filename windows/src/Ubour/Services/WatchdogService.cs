using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Ubour.Services;

public static class WatchdogService
{
    private static Process? _watchdogProcess;

    public static void StartWatchdog(params Process?[] managedProcesses)
    {
        StopWatchdog();

        try
        {
            var identities = new List<string>();
            foreach (var process in managedProcesses)
            {
                try
                {
                    if (process == null || process.HasExited) continue;
                    string? path = process.MainModule?.FileName;
                    if (string.IsNullOrWhiteSpace(path)) continue;
                    string escapedPath = path.Replace("'", "''", StringComparison.Ordinal);
                    long startedAt = process.StartTime.ToUniversalTime().Ticks;
                    identities.Add($"@{{ Id={process.Id}; StartTicks={startedAt}; Path='{escapedPath}' }}");
                }
                catch { }
            }

            if (identities.Count == 0) return;
            int parentPid = Environment.ProcessId;
            string entries = string.Join(",", identities);
            string script = "$ErrorActionPreference='SilentlyContinue'; " +
                $"Wait-Process -Id {parentPid}; " +
                $"foreach ($identity in @({entries})) {{ " +
                "$owned=Get-Process -Id $identity.Id; " +
                "if ($owned -and $owned.Path -ieq $identity.Path -and [math]::Abs($owned.StartTime.ToUniversalTime().Ticks - $identity.StartTicks) -lt 10000000) { " +
                "Stop-Process -Id $identity.Id -Force -ErrorAction SilentlyContinue } }";

            _watchdogProcess = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -NonInteractive -WindowStyle Hidden -Command \"{script}\"",
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
