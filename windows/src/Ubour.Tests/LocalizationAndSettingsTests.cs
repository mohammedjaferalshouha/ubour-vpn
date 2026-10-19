using System;
using System.IO;
using Xunit;
using Ubour.Models;
using Ubour.Services;

namespace Ubour.Tests;

public class LocalizationAndSettingsTests
{
    [Theory]
    [InlineData("AppName")]
    [InlineData("BtnConnect")]
    [InlineData("BtnDisconnect")]
    [InlineData("StatusConnected")]
    [InlineData("ModeWarp")]
    [InlineData("ModeAdBlockOnly")]
    [InlineData("ModeVpnOnly")]
    [InlineData("ModeVpnAdBlock")]
    [InlineData("DpiStrength")]
    [InlineData("WarpEndpointLabel")]
    [InlineData("BtnScanPorts")]
    [InlineData("ScanningPorts")]
    [InlineData("PortScanFound")]
    public void LocalizationManager_ContainsKeys_InBothLanguages(string key)
    {
        string arVal = LocalizationManager.Get(key, "ar");
        string enVal = LocalizationManager.Get(key, "en");

        Assert.False(string.IsNullOrEmpty(arVal));
        Assert.False(string.IsNullOrEmpty(enVal));
        Assert.NotEqual(key, arVal);
        Assert.NotEqual(key, enVal);
    }

    [Fact]
    public void AppSettings_SaveAndLoad_RoundtripsCorrectly()
    {
        string testDirectory = Path.Combine(Path.GetTempPath(), "UbourTests", Guid.NewGuid().ToString("N"));
        string settingsPath = Path.Combine(testDirectory, "settings.json");
        var settings = new AppSettings
        {
            Language = "en",
            Theme = "light",
            SelectedDns = "9.9.9.9",
            SelectedMode = AppOperationMode.CUSTOM_VLESS,
            DpiMode = "-5",
            CustomVlessUrl = "vless://test@1.2.3.4:443"
        };

        settings.WarpEndpointHost = "188.114.97.1";
        settings.WarpEndpointPort = 500;

        settings.Save(settingsPath);

        var loaded = AppSettings.Load(settingsPath);
        Assert.Equal("en", loaded.Language);
        Assert.Equal("light", loaded.Theme);
        Assert.Equal("9.9.9.9", loaded.SelectedDns);
        Assert.Equal(AppOperationMode.CUSTOM_VLESS, loaded.SelectedMode);
        Assert.Equal("-5", loaded.DpiMode);
        Assert.Equal("vless://test@1.2.3.4:443", loaded.CustomVlessUrl);
        Assert.Equal("188.114.97.1", loaded.WarpEndpointHost);
        Assert.Equal(500, loaded.WarpEndpointPort);

        try { Directory.Delete(testDirectory, recursive: true); } catch { }
    }

    [Fact]
    public void WarpManager_GetDefaultEndpoints_ContainsRecommendedPort4500()
    {
        var endpoints = WarpManager.GetDefaultEndpoints();
        Assert.NotEmpty(endpoints);
        Assert.Contains(endpoints, ep => ep.Port == 4500);
        Assert.Contains(endpoints, ep => ep.Port == 500);
        Assert.Contains(endpoints, ep => ep.Port == 1701);
        Assert.Equal(4500, endpoints[0].Port);
    }

    [Fact]
    public async Task WarpManager_ScanEndpointsAsync_ReturnsNonEmptyResults()
    {
        var results = await WarpManager.ScanEndpointsAsync();
        Assert.NotEmpty(results);
        Assert.Contains(results, ep => ep.Port == 4500);
        Assert.All(results, ep =>
        {
            Assert.False(string.IsNullOrWhiteSpace(ep.Host));
            Assert.True(ep.Port > 0);
            Assert.False(string.IsNullOrWhiteSpace(ep.DisplayText));
        });
    }

}
