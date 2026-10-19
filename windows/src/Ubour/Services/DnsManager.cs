using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.Json;

namespace Ubour.Services;

public static class DnsManager
{
    private static readonly Dictionary<string, DnsInterfaceSnapshot> ModifiedInterfaces = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object LockObj = new();

    static DnsManager()
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) => RestoreDns();
        AppDomain.CurrentDomain.UnhandledException += (_, _) => RestoreDns();
    }

    public static List<string> GetActiveNetworkInterfaces()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(ni => ni.OperationalStatus == OperationalStatus.Up &&
                    (ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 || ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet) &&
                    !ni.Description.Contains("Virtual", StringComparison.OrdinalIgnoreCase) &&
                    !ni.Description.Contains("Hyper-V", StringComparison.OrdinalIgnoreCase) &&
                    !ni.Description.Contains("VMware", StringComparison.OrdinalIgnoreCase) &&
                    !ni.Description.Contains("TAP", StringComparison.OrdinalIgnoreCase) &&
                    !ni.Description.Contains("Wintun", StringComparison.OrdinalIgnoreCase) &&
                    !ni.Description.Contains("Ubour", StringComparison.OrdinalIgnoreCase))
                .Select(ni => ni.Name)
                .ToList();
        }
        catch { return new List<string>(); }
    }

    public static bool SetLocalDns() => ApplyDnsAddresses("127.0.0.1", "::1");

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
        if (string.IsNullOrWhiteSpace(primaryDns)) return false;
        var (v4_1, v4_2, v6_1, v6_2) = ResolveDnsPair(primaryDns);
        if (!string.IsNullOrWhiteSpace(secondaryDns)) v4_2 = secondaryDns;
        return ApplyDnsAddresses(v4_1, v4_2, v6_1, v6_2);
    }

    public static bool NeedsDnsRestore()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(ni => ni.OperationalStatus == OperationalStatus.Up &&
                    (ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 || ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet))
                .SelectMany(ni => ni.GetIPProperties().DnsAddresses)
                .Any(dns => dns.ToString() is "127.0.0.1" or "::1");
        }
        catch { return false; }
    }

    public static void RestoreDnsIfModified()
    {
        lock (LockObj)
        {
            LoadSnapshots();
            if (ModifiedInterfaces.Count > 0) RestoreDnsLocked();
        }
    }

    public static void RestoreDns()
    {
        lock (LockObj)
        {
            LoadSnapshots();
            if (ModifiedInterfaces.Count > 0) RestoreDnsLocked();
        }
    }

    public static void FlushDns()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("ipconfig", "/flushdns")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            });
            process?.WaitForExit(3000);
        }
        catch { }
    }

    private static bool ApplyDnsAddresses(params string[] addresses)
    {
        lock (LockObj)
        {
            var interfaces = GetActiveNetworkInterfaces();
            if (interfaces.Count == 0) return false;

            LoadSnapshots();
            bool success = true;
            foreach (string iface in interfaces)
            {
                if (!ModifiedInterfaces.ContainsKey(iface))
                {
                    var snapshot = CaptureSnapshot(iface);
                    if (snapshot == null)
                    {
                        success = false;
                        continue;
                    }
                    ModifiedInterfaces.Add(iface, snapshot);
                    if (!SaveSnapshots())
                    {
                        ModifiedInterfaces.Remove(iface);
                        success = false;
                        continue;
                    }
                }

                string escaped = EscapePowerShell(iface);
                string v4 = string.Join(",", addresses.Where(a => !a.Contains(':')));
                string v6 = string.Join(",", addresses.Where(a => a.Contains(':')));
                string command = $"$ErrorActionPreference='Stop'; $a='{escaped}'; $servers=@({ToPowerShellArray(v4 + (string.IsNullOrEmpty(v6) ? string.Empty : "," + v6))}); Set-DnsClientServerAddress -InterfaceAlias $a -ServerAddresses $servers -ErrorAction Stop";
                if (!RunPowerShell(command, out _, out _)) success = false;
            }

            FlushDns();
            return success;
        }
    }

    private static DnsInterfaceSnapshot? CaptureSnapshot(string alias)
    {
        string escaped = EscapePowerShell(alias);
        string command = $"$ErrorActionPreference='Stop'; $a='{escaped}'; $v4=Get-DnsClientServerAddress -InterfaceAlias $a -AddressFamily IPv4 -ErrorAction Stop; $v6=Get-DnsClientServerAddress -InterfaceAlias $a -AddressFamily IPv6 -ErrorAction SilentlyContinue; $d4=(Get-NetIPInterface -InterfaceAlias $a -AddressFamily IPv4 -ErrorAction Stop | Select-Object -First 1).Dhcp -eq 'Enabled'; $d6=(Get-NetIPInterface -InterfaceAlias $a -AddressFamily IPv6 -ErrorAction SilentlyContinue | Select-Object -First 1).Dhcp -eq 'Enabled'; [pscustomobject]@{{Alias=$a; V4Dhcp=$d4; V4=@($v4.ServerAddresses); V6Dhcp=$d6; V6=@($v6.ServerAddresses)}} | ConvertTo-Json -Compress";
        if (!RunPowerShell(command, out string output, out _) || string.IsNullOrWhiteSpace(output)) return null;
        try
        {
            using var json = JsonDocument.Parse(output.Trim());
            var root = json.RootElement;
            return new DnsInterfaceSnapshot(
                root.GetProperty("Alias").GetString() ?? alias,
                root.GetProperty("V4Dhcp").GetBoolean(), ReadStringArray(root.GetProperty("V4")),
                root.GetProperty("V6Dhcp").GetBoolean(), ReadStringArray(root.GetProperty("V6")));
        }
        catch { return null; }
    }

    private static void RestoreDnsLocked()
    {
        var failed = new Dictionary<string, DnsInterfaceSnapshot>(StringComparer.OrdinalIgnoreCase);
        foreach (var snapshot in ModifiedInterfaces.Values.ToArray())
        {
            string escaped = EscapePowerShell(snapshot.Alias);
            string command = $"$ErrorActionPreference='Stop'; $a='{escaped}'; function Restore-Family($family,$servers,$dhcp) {{ if ($dhcp) {{ & netsh.exe interface $family set dnsservers name=\"$a\" source=dhcp | Out-Null; if ($LASTEXITCODE -ne 0) {{ throw \"Failed to restore $family DNS through DHCP\" }}; return }}; if ($servers.Count -eq 0) {{ & netsh.exe interface $family set dnsservers name=\"$a\" source=static address=none validate=no | Out-Null; if ($LASTEXITCODE -ne 0) {{ throw \"Failed to restore empty static $family DNS list\" }}; return }}; & netsh.exe interface $family set dnsservers name=\"$a\" source=static address=$($servers[0]) validate=no | Out-Null; if ($LASTEXITCODE -ne 0) {{ throw \"Failed to restore primary $family DNS server\" }}; for ($i=1; $i -lt $servers.Count; $i++) {{ & netsh.exe interface $family add dnsservers name=\"$a\" address=$($servers[$i]) index=$($i+1) validate=no | Out-Null; if ($LASTEXITCODE -ne 0) {{ throw \"Failed to restore secondary $family DNS server\" }} }} }}; Restore-Family ipv4 @({ToPowerShellArray(string.Join(",", snapshot.V4))}) {(snapshot.V4Dhcp ? "$true" : "$false")}; Restore-Family ipv6 @({ToPowerShellArray(string.Join(",", snapshot.V6))}) {(snapshot.V6Dhcp ? "$true" : "$false")}";
            if (!RunPowerShell(command, out _, out string error))
            {
                AppLogger.Warn($"[DNS] Could not restore DNS for '{snapshot.Alias}': {error}");
                failed[snapshot.Alias] = snapshot;
            }
        }

        ModifiedInterfaces.Clear();
        foreach (var item in failed) ModifiedInterfaces[item.Key] = item.Value;
        if (ModifiedInterfaces.Count == 0) DeleteSnapshots();
        else SaveSnapshots();
        FlushDns();
    }

    private static bool RunPowerShell(string command, out string stdout, out string stderr)
    {
        stdout = string.Empty;
        stderr = string.Empty;
        try
        {
            string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(command));
            var startInfo = new ProcessStartInfo("powershell.exe", $"-NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand {encoded}")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            using var process = Process.Start(startInfo);
            if (process == null) return false;
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(10000))
            {
                try { process.Kill(true); } catch { }
                return false;
            }
            stdout = outputTask.GetAwaiter().GetResult();
            stderr = errorTask.GetAwaiter().GetResult();
            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            stderr = ex.Message;
            return false;
        }
    }

    private static string SnapshotPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Ubour", "system-state", "dns.json");

    private static void LoadSnapshots()
    {
        if (ModifiedInterfaces.Count != 0) return;
        try
        {
            if (!File.Exists(SnapshotPath)) return;
            var snapshots = JsonSerializer.Deserialize<DnsInterfaceSnapshot[]>(File.ReadAllText(SnapshotPath));
            if (snapshots == null) return;
            foreach (var snapshot in snapshots) ModifiedInterfaces[snapshot.Alias] = snapshot;
        }
        catch { }
    }

    private static bool SaveSnapshots()
    {
        string? temporaryPath = null;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SnapshotPath)!);
            temporaryPath = SnapshotPath + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(ModifiedInterfaces.Values));
            File.Move(temporaryPath, SnapshotPath, overwrite: true);
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"[DNS] Could not save DNS recovery state: {ex.Message}");
            try { if (temporaryPath != null && File.Exists(temporaryPath)) File.Delete(temporaryPath); } catch { }
            return false;
        }
    }

    private static void DeleteSnapshots()
    {
        try { if (File.Exists(SnapshotPath)) File.Delete(SnapshotPath); } catch { }
    }

    private static string[] ReadStringArray(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Array => value.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToArray(),
        JsonValueKind.String => new[] { value.GetString()! },
        _ => Array.Empty<string>()
    };

    private static string ToPowerShellArray(string commaSeparated) => string.Join(",", commaSeparated
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(value => $"'{EscapePowerShell(value)}'"));

    private static string EscapePowerShell(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    private sealed record DnsInterfaceSnapshot(string Alias, bool V4Dhcp, string[] V4, bool V6Dhcp, string[] V6);
}
