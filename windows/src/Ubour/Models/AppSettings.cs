using System;
using System.IO;
using System.Text.Json;

namespace Ubour.Models;

public class AppSettings
{
    public string Language { get; set; } = "ar"; // "ar" or "en"
    public string Theme { get; set; } = "dark"; // "dark" or "light"
    public AppOperationMode SelectedMode { get; set; } = AppOperationMode.VPN_ONLY;
    public string SelectedDns { get; set; } = "8.8.8.8";
    public string CustomVlessUrl { get; set; } = "";
    public bool EnableAdBlock { get; set; } = true;
    public string DpiMode { get; set; } = "Stable"; // "Stable" (-1), "Medium" (-5), "Aggressive" (-9)
    public bool WarpEnableAdBlock { get; set; } = true;

    private static readonly string SettingsFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Ubour",
        "settings.json"
    );

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                string json = File.ReadAllText(SettingsFilePath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json);
                if (settings != null) return settings;
            }
        }
        catch { }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            string dir = Path.GetDirectoryName(SettingsFilePath)!;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsFilePath, json);
        }
        catch { }
    }
}
