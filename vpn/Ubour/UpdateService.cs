using System.Net.Http.Headers;
using System.Text.Json;

namespace Ubour;

public sealed class UpdateService
{
    private const string EngineReleaseApi = "https://api.github.com/repos/ValdikSS/GoodbyeDPI/releases";
    private const string BundledVersion = "0.2.3rc3";

    public async Task<UpdateResult> CheckEngineAsync()
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Ubour", "1.0.0"));
        using var response = await client.GetAsync(EngineReleaseApi);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        var release = document.RootElement.EnumerateArray()
            .Where(x => !x.GetProperty("draft").GetBoolean())
            .OrderByDescending(x => x.GetProperty("published_at").GetDateTimeOffset())
            .FirstOrDefault();
        if (release.ValueKind == JsonValueKind.Undefined) return UpdateResult.NoUpdate();
        var tag = release.GetProperty("tag_name").GetString() ?? "";
        var url = release.GetProperty("html_url").GetString();
        return tag.Contains(BundledVersion, StringComparison.OrdinalIgnoreCase)
            ? UpdateResult.Current()
            : UpdateResult.Available(tag, url);
    }
}

public sealed record UpdateResult(bool HasUpdate, string? Version, string? UpdateUrl)
{
    public static UpdateResult Current() => new(false, null, null);
    public static UpdateResult NoUpdate() => new(false, null, null);
    public static UpdateResult Available(string version, string? url) => new(true, version, url);
    public string Message(bool english) => HasUpdate
        ? (english ? $"Official engine update available: {Version}" : $"تحديث رسمي لمحرك التشغيل متاح: {Version}")
        : (english ? "The bundled engine is current." : "محرك التشغيل المضمّن محدّث.");
}
