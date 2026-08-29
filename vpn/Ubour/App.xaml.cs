using System.Windows;
using Ubour.Core;

namespace Ubour;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try { ProxyManager.DisableProxy(); } catch { }
        AppDomain.CurrentDomain.ProcessExit += (s, ev) => GlobalCleanup();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        GlobalCleanup();
        base.OnExit(e);
    }

    private static void GlobalCleanup()
    {
        try
        {
            EngineManager.Instance.Stop();
            ProxyManager.DisableProxy();
        }
        catch { }
    }
}
