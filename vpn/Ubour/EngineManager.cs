using System.Diagnostics;

namespace Ubour;

public sealed class EngineManager
{
    private Process? _process;
    public bool IsRunning => _process is { HasExited: false };

    public void Start()
    {
        if (IsRunning) return;
        var architecture = Environment.Is64BitOperatingSystem ? "x86_64" : "x86";
        var enginePath = Path.Combine(AppContext.BaseDirectory, "engine", architecture, "goodbyedpi.exe");
        if (!File.Exists(enginePath)) throw new FileNotFoundException("لم يتم العثور على محرك التشغيل المضمّن.", enginePath);
        _process = Process.Start(new ProcessStartInfo
        {
            FileName = enginePath,
            Arguments = "-9",
            WorkingDirectory = Path.GetDirectoryName(enginePath)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        }) ?? throw new InvalidOperationException("تعذر تشغيل محرك الاتصال.");
        _process.EnableRaisingEvents = true;
    }

    public void Stop()
    {
        if (!IsRunning) return;
        try { _process!.Kill(entireProcessTree: true); _process.WaitForExit(3000); }
        catch (InvalidOperationException) { }
        finally { _process?.Dispose(); _process = null; }
    }
}
