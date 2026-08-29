using System;
using System.Diagnostics;

namespace Ubour.Services;

public static class WatchdogService
{
    private static Process? _watchdogProcess;

    public static void StartWatchdog()
    {
        StopWatchdog();

        try
        {
            int currentPid = Environment.ProcessId;

            // PowerShell one-liner that waits for main PID to exit, then resets proxy and DNS
            string psScript = $"$ErrorActionPreference = 'SilentlyContinue'; Wait-Process -Id {currentPid}; Set-ItemProperty -Path 'HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\Internet Settings' -Name ProxyEnable -Value 0; Set-ItemProperty -Path 'HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\Internet Settings' -Name ProxyServer -Value ''; Remove-ItemProperty -Path 'HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\Internet Settings' -Name ProxyServer -ErrorAction SilentlyContinue; $sig = '[DllImport(\\\"wininet.dll\\\")] public static extern bool InternetSetOption(IntPtr hInternet, int dwOption, IntPtr lpBuffer, int dwBufferLength);'; $type = Add-Type -MemberDefinition $sig -Name WinINetWatchdog -Namespace WinINet -PassThru; $type::InternetSetOption([IntPtr]::Zero, 39, [IntPtr]::Zero, 0); $type::InternetSetOption([IntPtr]::Zero, 37, [IntPtr]::Zero, 0); Get-NetAdapter | Where-Object Status -eq 'Up' | Set-DnsClientServerAddress -ResetServerAddresses";

            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -NonInteractive -WindowStyle Hidden -Command \"{psScript}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            _watchdogProcess = Process.Start(startInfo);
            AppLogger.Info($"[Watchdog] Safety guard started for PID {currentPid}");
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"[Watchdog] Failed to launch safety guard: {ex.Message}");
        }
    }

    public static void StopWatchdog()
    {
        try
        {
            if (_watchdogProcess != null && !_watchdogProcess.HasExited)
            {
                _watchdogProcess.Kill(true);
                _watchdogProcess.Dispose();
                _watchdogProcess = null;
                AppLogger.Info("[Watchdog] Safety guard stopped cleanly.");
            }
        }
        catch { }
    }
}
