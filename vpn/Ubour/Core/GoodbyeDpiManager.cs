using System.Diagnostics;

namespace Ubour.Core;

public sealed class GoodbyeDpiManager
{
    private static readonly Lazy<GoodbyeDpiManager> _instance = new(() => new GoodbyeDpiManager());
    public static GoodbyeDpiManager Instance => _instance.Value;

    private Process? _process;
    public bool IsRunning => _process is { HasExited: false };

    private GoodbyeDpiManager() { }

    public bool Start(string arguments = "-9")
    {
        if (IsRunning) return true;

        var architecture = Environment.Is64BitOperatingSystem ? "x86_64" : "x86";
        var enginePath = Path.Combine(AppContext.BaseDirectory, "engine", architecture, "goodbyedpi.exe");

        if (!File.Exists(enginePath))
        {
            Debug.WriteLine($"GoodbyeDPI binary not found at: {enginePath}");
            return false;
        }

        try
        {
            _process = Process.Start(new ProcessStartInfo
            {
                FileName = enginePath,
                Arguments = arguments,
                WorkingDirectory = Path.GetDirectoryName(enginePath)!,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });

            if (_process != null)
            {
                _process.EnableRaisingEvents = true;
                return true;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to start GoodbyeDPI: {ex.Message}");
        }

        return false;
    }

    public void Stop()
    {
        if (!IsRunning) return;
        try
        {
            _process!.Kill(entireProcessTree: true);
            _process.WaitForExit(3000);
        }
        catch { }
        finally
        {
            _process?.Dispose();
            _process = null;
        }
    }
}
