using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace Ubour.Services;

public class GoodbyeDpiManager
{
    private static readonly object WinDivertLock = new();
    private static readonly string[] WinDivertServiceNames = { "WinDivert", "WinDivert1.4", "WinDivert14" };

    private const uint ScManagerConnect = 0x0001;
    private const uint ServiceQueryStatus = 0x0004;
    private const uint ServiceStop = 0x0020;
    private const uint ServiceDelete = 0x00010000;
    private const int ServiceControlStop = 0x00000001;

    private const uint ServiceStopped = 1;
    private const uint ServiceStopPending = 3;

    private const int ErrorAccessDenied = 5;
    private const int ErrorBusy = 170;
    private const int ErrorDependentServicesRunning = 1051;
    private const int ErrorServiceDoesNotExist = 1060;
    private const int ErrorServiceNotActive = 1062;
    private const int ErrorServiceMarkedForDelete = 1072;

    private readonly object _processLock = new();
    private Process? _process;
    private string? _ownedExePath;
    private string? _ownedEngineDir;

    public bool IsRunning => _process != null && !_process.HasExited;
    public Process? OwnedProcess => _process;

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
            string arch = Environment.Is64BitOperatingSystem ? "x86_64" : "x86";
            string exePath = Path.Combine(baseDir, "engine", arch, "goodbyedpi.exe");

            if (!File.Exists(exePath))
            {
                exePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "engine", arch, "goodbyedpi.exe");
            }

            if (!File.Exists(exePath)) return false;

            string fullExePath = Path.GetFullPath(exePath);
            string workDir = Path.GetDirectoryName(fullExePath)!;
            string engineDir = Path.GetDirectoryName(workDir)!;

            string dpiFlag = ResolveDpiFlag(mode);
            string args = dpiFlag;

            var startInfo = new ProcessStartInfo
            {
                FileName = fullExePath,
                Arguments = args,
                WorkingDirectory = workDir,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = false,
                RedirectStandardError = false
            };

            var process = Process.Start(startInfo);
            if (process == null || process.HasExited)
            {
                try { process?.Dispose(); } catch { }
                CleanupWinDivertDriver();
                return false;
            }

            lock (_processLock)
            {
                _process = process;
                _ownedExePath = fullExePath;
                _ownedEngineDir = engineDir;
            }

            process.EnableRaisingEvents = true;
            process.Exited += (s, e) =>
            {
                bool wasActive;
                lock (_processLock)
                {
                    wasActive = ReferenceEquals(_process, process);
                    if (wasActive)
                    {
                        _process = null;
                    }
                }

                if (wasActive)
                {
                    AppLogger.Warn("[GoodbyeDPI] Process exited or crashed unexpectedly. Cleaning up WinDivert driver...");
                    try { process.Dispose(); } catch { }
                    CleanupWinDivertDriver();
                }
            };

            AppLogger.Info($"[GoodbyeDPI] Started with arguments: {args}");
            return !process.HasExited;
        }
        catch (Exception ex)
        {
            AppLogger.Error($"[GoodbyeDPI] Failed to start: {ex.Message}");
            CleanupWinDivertDriver();
            return false;
        }
    }

    private static string ResolveDpiFlag(string mode)
    {
        if (mode == "-9" || mode.Equals("Aggressive", StringComparison.OrdinalIgnoreCase)) return "-9";
        if (mode == "-5" || mode.Equals("Medium", StringComparison.OrdinalIgnoreCase) || mode.Equals("Compatible", StringComparison.OrdinalIgnoreCase)) return "-5";
        return "-1";
    }

    public void Stop()
    {
        try
        {
            Process? process;
            string? ownedExePath;
            string? ownedEngineDir;

            lock (_processLock)
            {
                process = _process;
                _process = null;
                ownedExePath = _ownedExePath;
                ownedEngineDir = _ownedEngineDir;
            }

            if (process != null)
            {
                try { process.EnableRaisingEvents = false; } catch { }
                TerminateProcessAndWait(process, 5000);
                try { process.Dispose(); } catch { }
                AppLogger.Info("[GoodbyeDPI] Stopped cleanly.");
            }

            StopOwnedEngineProcesses(ownedExePath, ownedEngineDir);
            CleanupWinDivertDriver();
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"[GoodbyeDPI] Stop encountered an error: {ex.Message}");
        }
    }

    /// <summary>
    /// Stops and unloads any WinDivert kernel driver services so driver files inside the engine folder are unlocked.
    /// </summary>
    public static bool CleanupWinDivertDriver(int timeoutMs = 5000)
    {
        lock (WinDivertLock)
        {
            bool allCleared = true;
            foreach (string serviceName in WinDivertServiceNames)
            {
                if (!StopAndDeleteKernelService(serviceName, timeoutMs))
                {
                    allCleared = false;
                }
            }
            return allCleared;
        }
    }

    internal static string? TryGetProcessImagePath(Process process)
    {
        try
        {
            var buffer = new char[1024];
            uint size = (uint)buffer.Length;
            if (QueryFullProcessImageNameW(process.Handle, 0, buffer, ref size) && size > 0)
            {
                return new string(buffer, 0, (int)size);
            }
        }
        catch { }

        try
        {
            return process.MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }

    private static void TerminateProcessAndWait(Process process, int timeoutMs)
    {
        try
        {
            if (process.HasExited) return;

            if (process.MainWindowHandle != IntPtr.Zero && process.CloseMainWindow())
            {
                if (process.WaitForExit(1000)) return;
            }

            if (!process.HasExited)
            {
                process.Kill(true);
                process.WaitForExit(timeoutMs);
            }
        }
        catch { }
    }

    private static void StopOwnedEngineProcesses(string? ownedExePath, string? ownedEngineDir)
    {
        var engineRoots = new List<string>();
        AddNormalizedRoot(engineRoots, ownedEngineDir);
        AddNormalizedRoot(engineRoots, Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "engine"));

        if (string.IsNullOrWhiteSpace(ownedExePath) && engineRoots.Count == 0) return;

        try
        {
            foreach (var proc in Process.GetProcessesByName("goodbyedpi"))
            {
                using (proc)
                {
                    string? imagePath = TryGetProcessImagePath(proc);
                    if (string.IsNullOrWhiteSpace(imagePath)) continue;

                    string fullImagePath = Path.GetFullPath(imagePath);
                    bool matchesOwnedExe = !string.IsNullOrWhiteSpace(ownedExePath) &&
                        string.Equals(fullImagePath, ownedExePath, StringComparison.OrdinalIgnoreCase);

                    bool matchesEngineDir = false;
                    foreach (string root in engineRoots)
                    {
                        if (fullImagePath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                        {
                            matchesEngineDir = true;
                            break;
                        }
                    }

                    if (matchesOwnedExe || matchesEngineDir)
                    {
                        TerminateProcessAndWait(proc, 5000);
                    }
                }
            }
        }
        catch { }
    }

    private static void AddNormalizedRoot(List<string> roots, string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) return;
        try
        {
            string normalized = Path.GetFullPath(directory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!roots.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            {
                roots.Add(normalized);
            }
        }
        catch { }
    }

    private static bool StopAndDeleteKernelService(string serviceName, int timeoutMs)
    {
        IntPtr scm = OpenSCManagerW(null, null, ScManagerConnect);
        if (scm == IntPtr.Zero)
        {
            return RunScStopAndDelete(serviceName, timeoutMs);
        }

        try
        {
            IntPtr service = OpenServiceW(scm, serviceName, ServiceQueryStatus | ServiceStop | ServiceDelete);
            if (service == IntPtr.Zero)
            {
                int err = Marshal.GetLastWin32Error();
                if (err == ErrorServiceDoesNotExist) return true;
                if (err == ErrorAccessDenied)
                {
                    return IsKernelServiceStoppedOrAbsent(scm, serviceName);
                }
                return RunScStopAndDelete(serviceName, timeoutMs);
            }

            bool wasActive = false;
            try
            {
                if (QueryServiceStatus(service, out var status) && status.dwCurrentState != ServiceStopped)
                {
                    wasActive = true;
                    var sw = Stopwatch.StartNew();
                    while (sw.ElapsedMilliseconds < timeoutMs)
                    {
                        if (status.dwCurrentState != ServiceStopPending)
                        {
                            if (!ControlService(service, ServiceControlStop, out status))
                            {
                                int ctrlErr = Marshal.GetLastWin32Error();
                                if (ctrlErr == ErrorServiceNotActive || ctrlErr == ErrorServiceDoesNotExist)
                                {
                                    break;
                                }
                                if (ctrlErr == ErrorDependentServicesRunning || ctrlErr == ErrorBusy)
                                {
                                    Thread.Sleep(100);
                                    continue;
                                }
                            }
                        }

                        if (!QueryServiceStatus(service, out status) || status.dwCurrentState == ServiceStopped)
                        {
                            break;
                        }

                        Thread.Sleep(50);
                    }
                }

                DeleteService(service);
            }
            finally
            {
                CloseServiceHandle(service);
            }

            // Poll briefly after closing our service handle so SCM finishes removing a pending-delete driver.
            var settleSw = Stopwatch.StartNew();
            while (settleSw.ElapsedMilliseconds < Math.Min(timeoutMs, 1500))
            {
                if (IsKernelServiceStoppedOrAbsent(scm, serviceName))
                {
                    if (wasActive)
                    {
                        AppLogger.Info($"[GoodbyeDPI] Kernel driver service '{serviceName}' stopped and unloaded.");
                    }
                    return true;
                }
                Thread.Sleep(25);
            }

            if (RunScStopAndDelete(serviceName, Math.Min(timeoutMs, 2000)) && IsKernelServiceStoppedOrAbsent(scm, serviceName))
            {
                if (wasActive)
                {
                    AppLogger.Info($"[GoodbyeDPI] Kernel driver service '{serviceName}' stopped and unloaded.");
                }
                return true;
            }

            if (wasActive)
            {
                AppLogger.Warn($"[GoodbyeDPI] Kernel driver service '{serviceName}' did not reach STOPPED state within timeout.");
            }
            return IsKernelServiceStoppedOrAbsent(scm, serviceName);
        }
        finally
        {
            CloseServiceHandle(scm);
        }
    }

    private static bool IsKernelServiceStoppedOrAbsent(IntPtr scm, string serviceName)
    {
        IntPtr service = OpenServiceW(scm, serviceName, ServiceQueryStatus);
        if (service == IntPtr.Zero)
        {
            return Marshal.GetLastWin32Error() == ErrorServiceDoesNotExist;
        }

        try
        {
            return !QueryServiceStatus(service, out var status) || status.dwCurrentState == ServiceStopped;
        }
        finally
        {
            CloseServiceHandle(service);
        }
    }

    private static bool RunScStopAndDelete(string serviceName, int timeoutMs)
    {
        try
        {
            RunScCommand($"stop \"{serviceName}\"", timeoutMs);
            RunScCommand($"delete \"{serviceName}\"", timeoutMs);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void RunScCommand(string arguments, int timeoutMs)
    {
        using var process = Process.Start(new ProcessStartInfo("sc.exe", arguments)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WindowStyle = ProcessWindowStyle.Hidden
        });

        if (process == null) return;
        _ = process.StandardOutput.ReadToEndAsync();
        _ = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(timeoutMs))
        {
            try { process.Kill(true); } catch { }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatus
    {
        public uint dwServiceType;
        public uint dwCurrentState;
        public uint dwControlsAccepted;
        public uint dwWin32ExitCode;
        public uint dwServiceSpecificExitCode;
        public uint dwCheckPoint;
        public uint dwWaitHint;
    }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr OpenSCManagerW(string? lpMachineName, string? lpDatabaseName, uint dwDesiredAccess);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr OpenServiceW(IntPtr hSCManager, string lpServiceName, uint dwDesiredAccess);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryServiceStatus(IntPtr hService, out ServiceStatus lpServiceStatus);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ControlService(IntPtr hService, int dwControl, out ServiceStatus lpServiceStatus);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteService(IntPtr hService);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseServiceHandle(IntPtr hSCObject);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageNameW(IntPtr hProcess, uint dwFlags, char[] lpExeName, ref uint lpdwSize);
}
