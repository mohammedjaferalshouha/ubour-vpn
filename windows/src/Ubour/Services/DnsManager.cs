using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.NetworkInformation;
using Microsoft.Win32;

namespace Ubour.Services;

public static class DnsManager
{
    private static readonly HashSet<string> ModifiedInterfaces = new();
    private static readonly object LockObj = new();
    private static bool _isRegisteredExitHandler = false;

    static DnsManager()
    {
        RegisterExitHandlers();
    }

    private static void RegisterExitHandlers()
    {
        if (_isRegisteredExitHandler) return;
        _isRegisteredExitHandler = true;

        AppDomain.CurrentDomain.ProcessExit += (s, e) => RestoreDns();
        AppDomain.CurrentDomain.UnhandledException += (s, e) => RestoreDns();
    }

    public static List<string> GetActiveNetworkInterfaces()
    {
        var list = new List<string>();
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus == OperationalStatus.Up &&
                    (ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ||
                     ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet) &&
                    !ni.Description.Contains("Virtual", StringComparison.OrdinalIgnoreCase) &&
                    !ni.Description.Contains("Hyper-V", StringComparison.OrdinalIgnoreCase) &&
                    !ni.Description.Contains("VMware", StringComparison.OrdinalIgnoreCase) &&
                    !ni.Description.Contains("TAP", StringComparison.OrdinalIgnoreCase) &&
                    !ni.Description.Contains("Wintun", StringComparison.OrdinalIgnoreCase) &&
                    !ni.Description.Contains("Ubour", StringComparison.OrdinalIgnoreCase))
                {
                    list.Add(ni.Name);
                }
            }
        }
        catch { }
        return list;
    }

    public static bool SetLocalDns()
    {
        lock (LockObj)
        {
            var interfaces = GetActiveNetworkInterfaces();
            if (interfaces.Count == 0) return false;

            bool success = true;
            foreach (var iface in interfaces)
            {
                try
                {
                    RunNetsh($"interface ipv4 set dnsservers name=\"{iface}\" source=static address=127.0.0.1 validate=no");
                    RunNetsh($"interface ipv6 set dnsservers name=\"{iface}\" source=static address=::1 validate=no");
                    RunPowerShell($"Set-DnsClientServerAddress -InterfaceAlias \"{iface}\" -ServerAddresses ('127.0.0.1', '::1') -ErrorAction SilentlyContinue");
                    ModifiedInterfaces.Add(iface);
                }
                catch
                {
                    success = false;
                }
            }

            FlushDns();
            return success;
        }
    }

    public static (string primaryV4, string secondaryV4, string primaryV6, string secondaryV6) ResolveDnsPair(string primaryDns)
    {
        return primaryDns switch
        {
            "1.1.1.1" => ("1.1.1.1", "1.0.0.1", "2606:4700:4700::1111", "2606:4700:4700::1001"),
            "8.8.8.8" => ("8.8.8.8", "8.8.4.4", "2001:4860:4860::8888", "2001:4860:4860::8844"),
            "94.140.14.14" => ("94.140.14.14", "94.140.15.15", "2a10:50c0::ad1:ff", "2a10:50c0::ad2:ff"),
            "9.9.9.9" => ("9.9.9.9", "149.112.112.112", "2620:fe::fe", "2620:fe::9"),
            "208.67.222.222" => ("208.67.222.222", "208.67.220.220", "2620:119:35::35", "2620:119:53::53"),
            _ => (primaryDns, "8.8.8.8", "2001:4860:4860::8888", "2001:4860:4860::8844")
        };
    }

    public static bool SetCustomDns(string primaryDns, string? secondaryDns = null)
    {
        lock (LockObj)
        {
            var interfaces = GetActiveNetworkInterfaces();
            if (interfaces.Count == 0) return false;

            var (v4_1, v4_2, v6_1, v6_2) = ResolveDnsPair(primaryDns);
            if (!string.IsNullOrWhiteSpace(secondaryDns)) v4_2 = secondaryDns;

            bool success = true;
            foreach (var iface in interfaces)
            {
                try
                {
                    RunNetsh($"interface ipv4 set dnsservers name=\"{iface}\" source=static address={v4_1} validate=no");
                    RunNetsh($"interface ipv4 add dnsservers name=\"{iface}\" address={v4_2} index=2 validate=no");

                    RunNetsh($"interface ipv6 set dnsservers name=\"{iface}\" source=static address={v6_1} validate=no");
                    RunNetsh($"interface ipv6 add dnsservers name=\"{iface}\" address={v6_2} index=2 validate=no");

                    RunPowerShell($"Set-DnsClientServerAddress -InterfaceAlias \"{iface}\" -ServerAddresses ('{v4_1}', '{v4_2}', '{v6_1}', '{v6_2}') -ErrorAction SilentlyContinue");
                    ModifiedInterfaces.Add(iface);
                }
                catch
                {
                    success = false;
                }
            }

            FlushDns();
            return success;
        }
    }

    public static bool NeedsDnsRestore()
    {
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus == OperationalStatus.Up &&
                    (ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ||
                     ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet))
                {
                    var ipProps = ni.GetIPProperties();
                    foreach (var dns in ipProps.DnsAddresses)
                    {
                        string s = dns.ToString();
                        if (s == "127.0.0.1" || s == "::1")
                        {
                            return true;
                        }
                    }
                }
            }
        }
        catch { }
        return false;
    }

    public static void RestoreDnsIfModified()
    {
        if (NeedsDnsRestore() || ModifiedInterfaces.Count > 0)
        {
            RestoreDns();
        }
    }

    public static void RestoreDns()
    {
        lock (LockObj)
        {
            var interfaces = new List<string>(ModifiedInterfaces);
            if (interfaces.Count == 0)
            {
                interfaces = GetActiveNetworkInterfaces();
            }

            foreach (var iface in interfaces)
            {
                try
                {
                    RunNetsh($"interface ipv4 set dnsservers name=\"{iface}\" source=dhcp");
                    RunNetsh($"interface ipv6 set dnsservers name=\"{iface}\" source=dhcp");
                    RunPowerShell($"Set-DnsClientServerAddress -InterfaceAlias \"{iface}\" -ResetServerAddresses -ErrorAction SilentlyContinue");
                }
                catch { }
            }
            ModifiedInterfaces.Clear();

            // Clean up any legacy browser policy registry entries if they exist
            CleanLegacyBrowserPolicies();

            FlushDns();
        }
    }

    private static void CleanLegacyBrowserPolicies()
    {
        string[] paths = new[]
        {
            @"SOFTWARE\Policies\Google\Chrome",
            @"SOFTWARE\Policies\Microsoft\Edge"
        };

        foreach (var p in paths)
        {
            try
            {
                using var hklm = Registry.LocalMachine.OpenSubKey(p, true);
                if (hklm != null)
                {
                    hklm.DeleteValue("DnsOverHttpsMode", false);
                    hklm.DeleteValue("BuiltInDnsClientEnabled", false);
                }
            }
            catch { }

            try
            {
                using var hkcu = Registry.CurrentUser.OpenSubKey(p, true);
                if (hkcu != null)
                {
                    hkcu.DeleteValue("DnsOverHttpsMode", false);
                    hkcu.DeleteValue("BuiltInDnsClientEnabled", false);
                }
            }
            catch { }
        }
    }

        private static void RunPowerShell(string command)
    {
        try
        {
            var psi = new ProcessStartInfo("powershell.exe", $"-NoProfile -NonInteractive -WindowStyle Hidden -Command \"{command}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false
            };
            Process.Start(psi)?.WaitForExit(1000);
        }
        catch { }
    }

    private static void RunNetsh(string args)
    {
        try
        {
            var psi = new ProcessStartInfo("netsh", args)
            {
                CreateNoWindow = true,
                UseShellExecute = false
            };
            var proc = Process.Start(psi);
            proc?.WaitForExit(1000);
        }
        catch { }
    }

    public static void FlushDns()
    {
        try
        {
            var psi = new ProcessStartInfo("ipconfig", "/flushdns")
            {
                CreateNoWindow = true,
                UseShellExecute = false
            };
            Process.Start(psi)?.WaitForExit(1000);
        }
        catch { }
    }
}
