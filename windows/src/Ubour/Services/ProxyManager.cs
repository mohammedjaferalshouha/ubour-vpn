using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;

namespace Ubour.Services;

public static class ProxyManager
{
    [DllImport("wininet.dll", SetLastError = true)]
    private static extern bool InternetSetOption(IntPtr hInternet, int dwOption, IntPtr lpBuffer, int dwBufferLength);

    private const int InternetOptionSettingsChanged = 39;
    private const int InternetOptionRefresh = 37;
    private const string SettingsKey = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
    private static readonly object LockObj = new();
    private static ProxySnapshot? _snapshot;

    public static void EnableProxy(string server = "127.0.0.1:2080")
    {
        lock (LockObj)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(SettingsKey, writable: true);
                if (key == null) return;

                _snapshot ??= LoadSnapshot() ?? CaptureSnapshot(key, server);
                SaveSnapshot(_snapshot);
                key.SetValue("ProxyEnable", 1, RegistryValueKind.DWord);
                key.SetValue("ProxyServer", server, RegistryValueKind.String);
                NotifyWinInet();
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"[Proxy] Could not enable local proxy: {ex.Message}");
            }
        }
    }

    public static void DisableProxy()
    {
        RestoreIfModified();
    }

    public static void RestoreIfModified()
    {
        lock (LockObj)
        {
            try
            {
                var snapshot = _snapshot ?? LoadSnapshot();
                if (snapshot == null) return;

                using var key = Registry.CurrentUser.OpenSubKey(SettingsKey, writable: true);
                if (key == null) return;

                bool stillOwned = Convert.ToInt32(key.GetValue("ProxyEnable", 0), System.Globalization.CultureInfo.InvariantCulture) == 1 &&
                    string.Equals(key.GetValue("ProxyServer") as string, snapshot.AppliedServer, StringComparison.OrdinalIgnoreCase);
                if (stillOwned)
                {
                    RestoreValue(key, "ProxyEnable", snapshot.ProxyEnable);
                    RestoreValue(key, "ProxyServer", snapshot.ProxyServer);
                }
                _snapshot = null;
                DeleteSnapshot();
                NotifyWinInet();
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"[Proxy] Could not restore previous proxy settings: {ex.Message}");
            }
        }
    }

    public static void NotifyWinInet()
    {
        try
        {
            InternetSetOption(IntPtr.Zero, InternetOptionSettingsChanged, IntPtr.Zero, 0);
            InternetSetOption(IntPtr.Zero, InternetOptionRefresh, IntPtr.Zero, 0);
        }
        catch { }
    }

    private static ProxySnapshot CaptureSnapshot(RegistryKey key, string appliedServer) => new(
        CaptureValue(key, "ProxyEnable"), CaptureValue(key, "ProxyServer"), appliedServer);

    private static RegistryValueSnapshot CaptureValue(RegistryKey key, string name)
    {
        object? value = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        if (value == null) return new RegistryValueSnapshot(false, null, null);
        var kind = key.GetValueKind(name);
        return new RegistryValueSnapshot(true, Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture), kind);
    }

    private static void RestoreValue(RegistryKey key, string name, RegistryValueSnapshot value)
    {
        if (!value.Exists)
        {
            key.DeleteValue(name, throwOnMissingValue: false);
            return;
        }

        object restored = value.Kind switch
        {
            RegistryValueKind.DWord when int.TryParse(value.Value, out int dword) => dword,
            RegistryValueKind.QWord when long.TryParse(value.Value, out long qword) => qword,
            RegistryValueKind.Binary when value.Value != null => Convert.FromBase64String(value.Value),
            RegistryValueKind.MultiString when value.Value != null => value.Value.Split('\n'),
            _ => value.Value ?? string.Empty
        };
        key.SetValue(name, restored, value.Kind ?? RegistryValueKind.String);
    }

    private static string SnapshotPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Ubour", "system-state", "proxy.json");

    private static ProxySnapshot? LoadSnapshot()
    {
        try
        {
            if (!File.Exists(SnapshotPath)) return null;
            return JsonSerializer.Deserialize<ProxySnapshot>(File.ReadAllText(SnapshotPath));
        }
        catch { return null; }
    }

    private static void SaveSnapshot(ProxySnapshot snapshot)
    {
        string temporaryPath = SnapshotPath + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SnapshotPath)!);
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(snapshot));
            File.Move(temporaryPath, SnapshotPath, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); } catch { }
        }
    }

    private static void DeleteSnapshot()
    {
        try { if (File.Exists(SnapshotPath)) File.Delete(SnapshotPath); } catch { }
    }

    private sealed record ProxySnapshot(RegistryValueSnapshot ProxyEnable, RegistryValueSnapshot ProxyServer, string AppliedServer);
    private sealed record RegistryValueSnapshot(bool Exists, string? Value, RegistryValueKind? Kind);
}
