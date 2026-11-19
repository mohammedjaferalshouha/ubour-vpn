using System;
using System.Threading.Tasks;
using System.Windows;
using Ubour.Services;

namespace Ubour;

public partial class App : System.Windows.Application
{
    private static readonly object CleanupLock = new();
    private static GoodbyeDpiManager? _activeGoodbyeDpi;
    private static SingboxManager? _activeSingbox;
    private static AdBlockEngine? _activeAdBlock;
    private static UsqueManager? _activeUsque;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Instant startup: run recovery asynchronously in the background so window opens immediately
        Task.Run(() =>
        {
            try
            {
                ProxyManager.RestoreIfModified();
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

    public static void RegisterSessionManagers(GoodbyeDpiManager? goodbyeDpi, SingboxManager? singbox, AdBlockEngine? adBlock, UsqueManager? usque = null)
    {
        lock (CleanupLock)
        {
            _activeGoodbyeDpi = goodbyeDpi;
            _activeSingbox = singbox;
            _activeAdBlock = adBlock;
            _activeUsque = usque;
        }
    }

    public static void CleanupSession(GoodbyeDpiManager? goodbyeDpi, SingboxManager? singbox, AdBlockEngine? adBlock, UsqueManager? usque = null)
    {
        lock (CleanupLock)
        {
            try { WatchdogService.StopWatchdog(); } catch { }
            try { goodbyeDpi?.Stop(); } catch { }
            try { singbox?.Stop(); } catch { }
            try { usque?.Stop(); } catch { }
            try { adBlock?.Stop(); } catch { }
            try { GoodbyeDpiManager.CleanupWinDivertDriver(); } catch { }
            try { ProxyManager.RestoreIfModified(); } catch { }
            try { DnsManager.RestoreDns(); } catch { }
        }
    }

    public static void GlobalCleanup()
    {
        try
        {
            GoodbyeDpiManager? goodbyeDpi;
            SingboxManager? singbox;
            AdBlockEngine? adBlock;
            UsqueManager? usque;

            lock (CleanupLock)
            {
                goodbyeDpi = _activeGoodbyeDpi;
                singbox = _activeSingbox;
                adBlock = _activeAdBlock;
                usque = _activeUsque;
            }

            CleanupSession(goodbyeDpi, singbox, adBlock, usque);
        }
        catch { }
    }
}
