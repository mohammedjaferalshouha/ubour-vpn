using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Ubour.Core;

public static class ProxyManager
{
    [DllImport("wininet.dll", SetLastError = true)]
    private static extern bool InternetSetOption(IntPtr hInternet, int dwOption, IntPtr lpBuffer, int dwBufferLength);

    private const int INTERNET_OPTION_SETTINGS_CHANGED = 39;
    private const int INTERNET_OPTION_REFRESH = 37;

    public static void EnableProxy(string server = "127.0.0.1:2080")
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings", true);
            if (key != null)
            {
                key.SetValue("ProxyEnable", 1, RegistryValueKind.DWord);
                key.SetValue("ProxyServer", server, RegistryValueKind.String);
            }
            NotifyWinInet();
        }
        catch { }
    }

    public static void DisableProxy()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings", true);
            if (key != null)
            {
                key.SetValue("ProxyEnable", 0, RegistryValueKind.DWord);
                key.SetValue("ProxyServer", "", RegistryValueKind.String);
                try { key.DeleteValue("ProxyServer", false); } catch { }
                try { key.DeleteValue("AutoConfigURL", false); } catch { }
            }
            NotifyWinInet();
        }
        catch { }
    }

    public static void NotifyWinInet()
    {
        try
        {
            InternetSetOption(IntPtr.Zero, INTERNET_OPTION_SETTINGS_CHANGED, IntPtr.Zero, 0);
            InternetSetOption(IntPtr.Zero, INTERNET_OPTION_REFRESH, IntPtr.Zero, 0);
        }
        catch { }
    }
}
