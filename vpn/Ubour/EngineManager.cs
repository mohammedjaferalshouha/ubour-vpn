using System.Diagnostics;
using Ubour.AdBlock;
using Ubour.Core;

namespace Ubour;

public enum AppOperationMode
{
    WARP_AND_ADBLOCK,
    DPI_AND_ADBLOCK,
    ADBLOCK_ONLY,
    DPI_ONLY,
    CUSTOM_VLESS
}

public sealed class EngineManager
{
    private static readonly Lazy<EngineManager> _instance = new(() => new EngineManager());
    public static EngineManager Instance => _instance.Value;

    public bool IsRunning { get; private set; }
    public AppOperationMode CurrentMode { get; private set; } = AppOperationMode.WARP_AND_ADBLOCK;
    public DateTime? ConnectedAt { get; private set; }

    public EngineManager() { }

    public void Start(AppOperationMode mode, string? customVlessUrl = null)
    {
        if (IsRunning) Stop();

        CurrentMode = mode;
        AdBlockEngine.Instance.LoadEmbeddedFilters();

        switch (mode)
        {
            case AppOperationMode.ADBLOCK_ONLY:
            {
                var dnsStarted = DnsProxyServer.Instance.Start(53);
                if (dnsStarted)
                {
                    DnsSystemManager.Instance.ApplyLocalDns(DnsProxyServer.Instance.BoundPort);
                }
                break;
            }

            case AppOperationMode.DPI_ONLY:
            {
                GoodbyeDpiManager.Instance.Start("-9");
                break;
            }

            case AppOperationMode.DPI_AND_ADBLOCK:
            {
                GoodbyeDpiManager.Instance.Start("-9");
                var dnsStarted = DnsProxyServer.Instance.Start(53);
                if (dnsStarted)
                {
                    DnsSystemManager.Instance.ApplyLocalDns(DnsProxyServer.Instance.BoundPort);
                }
                break;
            }

            case AppOperationMode.WARP_AND_ADBLOCK:
            {
                DnsProxyServer.Instance.Start(5353);
                SingboxWindowsManager.Instance.StartWarp(enableAdBlock: true);
                break;
            }

            case AppOperationMode.CUSTOM_VLESS:
            {
                DnsProxyServer.Instance.Start(5353);
                SingboxWindowsManager.Instance.StartVless(customVlessUrl ?? "", enableAdBlock: true);
                break;
            }
        }

        IsRunning = true;
        ConnectedAt = DateTime.UtcNow;
    }

    public void Stop()
    {
        try
        {
            GoodbyeDpiManager.Instance.Stop();
            SingboxWindowsManager.Instance.Stop();
            DnsProxyServer.Instance.Stop();
            DnsSystemManager.Instance.RestoreOriginalDns();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error during EngineManager.Stop(): {ex.Message}");
        }
        finally
        {
            IsRunning = false;
            ConnectedAt = null;
        }
    }
}
