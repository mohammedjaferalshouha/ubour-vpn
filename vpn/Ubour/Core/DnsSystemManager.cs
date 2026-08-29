using System.Diagnostics;
using System.Net.NetworkInformation;

namespace Ubour.Core;

public sealed class DnsSystemManager
{
    private static readonly Lazy<DnsSystemManager> _instance = new(() => new DnsSystemManager());
    public static DnsSystemManager Instance => _instance.Value;

    private readonly Dictionary<string, bool> _originalIsDhcp = new();
    private readonly Dictionary<string, List<string>> _originalStaticDns = new();
    private bool _isDnsModified;

    private DnsSystemManager() { }

    public void ApplyLocalDns(int port = 53)
    {
        if (_isDnsModified) return;

        try
        {
            var activeInterfaces = NetworkInterface.GetAllNetworkInterfaces()
                .Where(ni => ni.OperationalStatus == OperationalStatus.Up &&
                             (ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet ||
                              ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211) &&
                             !ni.Description.Contains("Loopback") &&
                             !ni.Description.Contains("Virtual") &&
                             !ni.Description.Contains("TAP") &&
                             !ni.Description.Contains("Wintun"))
                .ToList();

            foreach (var iface in activeInterfaces)
            {
                var name = iface.Name;
                var ipProps = iface.GetIPProperties();
                var dnsServers = ipProps.DnsAddresses
                    .Where(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    .Select(a => a.ToString())
                    .ToList();

                _originalStaticDns[name] = dnsServers;
                _originalIsDhcp[name] = ipProps.GetIPv4Properties()?.IsDhcpEnabled ?? true;

                // Set Primary DNS to 127.0.0.1
                RunNetsh($"interface ipv4 set dns name=\"{name}\" static 127.0.0.1 primary validate=no");
                Debug.WriteLine($"Applied local DNS to network adapter: {name}");
            }

            _isDnsModified = true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error applying local DNS: {ex.Message}");
        }
    }

    public void RestoreOriginalDns()
    {
        if (!_isDnsModified) return;

        try
        {
            foreach (var (name, isDhcp) in _originalIsDhcp)
            {
                if (isDhcp)
                {
                    RunNetsh($"interface ipv4 set dns name=\"{name}\" dhcp");
                }
                else if (_originalStaticDns.TryGetValue(name, out var servers) && servers.Count > 0)
                {
                    RunNetsh($"interface ipv4 set dns name=\"{name}\" static {servers[0]} primary validate=no");
                    for (var i = 1; i < servers.Count; i++)
                    {
                        RunNetsh($"interface ipv4 add dns name=\"{name}\" {servers[i]} index={i + 1} validate=no");
                    }
                }
                else
                {
                    RunNetsh($"interface ipv4 set dns name=\"{name}\" dhcp");
                }
                Debug.WriteLine($"Restored DNS on network adapter: {name}");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error restoring DNS: {ex.Message}");
        }
        finally
        {
            ProxyManager.DisableProxy();
            _originalIsDhcp.Clear();
            _originalStaticDns.Clear();
            _isDnsModified = false;
        }
    }

    private static void RunNetsh(string arguments)
    {
        try
        {
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = "netsh.exe",
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });
            proc?.WaitForExit(3000);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"netsh command failed ({arguments}): {ex.Message}");
        }
    }
}
