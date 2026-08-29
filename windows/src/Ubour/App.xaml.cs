using System;
using System.Windows;
using Ubour.Services;

namespace Ubour;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Instant startup: run recovery asynchronously in the background so window opens immediately
        Task.Run(() =>
        {
            try
            {
                ProxyManager.DisableProxy();
                DnsManager.RestoreDnsIfModified();
            }
            catch { }
        });

        AppDomain.CurrentDomain.ProcessExit += (s, ev) => GlobalCleanup();
        AppDomain.CurrentDomain.UnhandledException += (s, ev) => GlobalCleanup();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        GlobalCleanup();
        base.OnExit(e);
    }

    public static void GlobalCleanup()
    {
        try
        {
            WatchdogService.StopWatchdog();
            ProxyManager.DisableProxy();
            DnsManager.RestoreDns();
        }
        catch { }
    }
}
