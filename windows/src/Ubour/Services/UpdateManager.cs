using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Ubour.Services;

public class UpdateInfo
{
    public bool HasUpdate { get; set; }
    public string CurrentVersion { get; set; } = string.Empty;
    public string LatestVersion { get; set; } = string.Empty;
    public string ReleaseUrl { get; set; } = string.Empty;
    public string DownloadUrlX64 { get; set; } = string.Empty;
    public string DownloadUrlX86 { get; set; } = string.Empty;
    public string ReleaseNotes { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
}

public static class UpdateManager
{
    public const string CurrentVersion = "1.6.2";
    public const string GitHubApiUrl = "https://api.github.com/repos/mohammedjaferalshouha/ubour-vpn/releases/latest";
    public const string ReleasesPageUrl = "https://github.com/mohammedjaferalshouha/ubour-vpn/releases/latest";

    private static readonly HttpClient DefaultClient = new()
    {
        Timeout = TimeSpan.FromSeconds(8)
    };

    static UpdateManager()
    {
        if (!DefaultClient.DefaultRequestHeaders.Contains("User-Agent"))
        {
            DefaultClient.DefaultRequestHeaders.Add("User-Agent", "Ubour-Windows-Client");
        }
    }

    public static async Task<UpdateInfo> CheckForUpdatesAsync(HttpClient? client = null)
    {
        var httpClient = client ?? DefaultClient;
        var result = new UpdateInfo
        {
            CurrentVersion = CurrentVersion,
            LatestVersion = CurrentVersion,
            ReleaseUrl = ReleasesPageUrl
        };

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, GitHubApiUrl);
            if (!req.Headers.Contains("User-Agent"))
            {
                req.Headers.Add("User-Agent", "Ubour-Windows-Client");
            }

            var response = await httpClient.SendAsync(req).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                result.ErrorMessage = $"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}";
                return result;
            }

            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string tagName = root.TryGetProperty("tag_name", out var tagElem) ? (tagElem.GetString() ?? "") : "";
            string htmlUrl = root.TryGetProperty("html_url", out var urlElem) ? (urlElem.GetString() ?? ReleasesPageUrl) : ReleasesPageUrl;
            string body = root.TryGetProperty("body", out var bodyElem) ? (bodyElem.GetString() ?? "") : "";

            string cleanRemoteVersion = CleanVersion(tagName);
            string cleanCurrentVersion = CleanVersion(CurrentVersion);

            result.LatestVersion = cleanRemoteVersion;
            result.ReleaseUrl = htmlUrl;
            result.ReleaseNotes = body;

            if (root.TryGetProperty("assets", out var assetsElem) && assetsElem.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assetsElem.EnumerateArray())
                {
                    if (asset.TryGetProperty("name", out var nameElem) &&
                        asset.TryGetProperty("browser_download_url", out var dlElem))
                    {
                        string name = nameElem.GetString() ?? "";
                        string downloadUrl = dlElem.GetString() ?? "";

                        if (name.Contains("x64", StringComparison.OrdinalIgnoreCase))
                        {
                            result.DownloadUrlX64 = downloadUrl;
                        }
                        else if (name.Contains("x86", StringComparison.OrdinalIgnoreCase) || name.Contains("386", StringComparison.OrdinalIgnoreCase))
                        {
                            result.DownloadUrlX86 = downloadUrl;
                        }
                    }
                }
            }

            result.HasUpdate = IsNewerVersion(cleanCurrentVersion, cleanRemoteVersion);
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.Message;
            result.HasUpdate = false;
        }

        return result;
    }

    public static string CleanVersion(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "0.0.0";
        var trimmed = raw.Trim();
        if (trimmed.StartsWith("v", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[1..];
        }
        return trimmed;
    }

    public static bool IsNewerVersion(string currentVer, string remoteVer)
    {
        string c = CleanVersion(currentVer);
        string r = CleanVersion(remoteVer);

        if (string.Equals(c, r, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var cParts = c.Split('.');
        var rParts = r.Split('.');

        int maxLen = Math.Max(cParts.Length, rParts.Length);
        for (int i = 0; i < maxLen; i++)
        {
            int cVal = i < cParts.Length && int.TryParse(cParts[i], out int cv) ? cv : 0;
            int rVal = i < rParts.Length && int.TryParse(rParts[i], out int rv) ? rv : 0;

            if (rVal > cVal) return true;
            if (rVal < cVal) return false;
        }

        return false;
    }
}
