using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;

namespace Ubour.Services;

public static class AppLogger
{
    private static readonly ConcurrentQueue<string> Logs = new();
    private static readonly StringBuilder LogBuilder = new();
    private static readonly object LockObj = new();
    public static event Action<string>? OnLogAdded;

    public static void Info(string message) => Log("INFO", message);
    public static void Warn(string message) => Log("WARN", message);
    public static void Error(string message) => Log("ERROR", message);
    public static void Block(string domain, string type) => Log("BLOCK", $"{type}: {domain}");
    public static void Pass(string domain, string ip) => Log("PASS", $"Allowed: {domain} -> {ip}");

    private static void Log(string level, string message)
    {
        string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        string line = $"[{timestamp}] [{level}] {message}";

        lock (LockObj)
        {
            Logs.Enqueue(line);
            LogBuilder.AppendLine(line);
            while (Logs.Count > 1000) Logs.TryDequeue(out _);
        }

        OnLogAdded?.Invoke(line);
    }

    public static string GetAllLogs()
    {
        lock (LockObj)
        {
            return string.Join(Environment.NewLine, Logs);
        }
    }

    public static void Clear()
    {
        lock (LockObj)
        {
            while (Logs.TryDequeue(out _)) { }
            LogBuilder.Clear();
        }
    }

    public static void SaveToFile(string filePath)
    {
        lock (LockObj)
        {
            File.WriteAllText(filePath, LogBuilder.ToString(), Encoding.UTF8);
        }
    }
}
