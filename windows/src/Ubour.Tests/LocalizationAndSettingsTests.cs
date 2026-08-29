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
        var settings = new AppSettings
        {
            Language = "en",
            Theme = "light",
            SelectedDns = "9.9.9.9",
            SelectedMode = AppOperationMode.CUSTOM_VLESS,
            DpiMode = "-5",
            CustomVlessUrl = "vless://test@1.2.3.4:443"
        };

        settings.Save();

        var loaded = AppSettings.Load();
        Assert.Equal("en", loaded.Language);
        Assert.Equal("light", loaded.Theme);
        Assert.Equal("9.9.9.9", loaded.SelectedDns);
        Assert.Equal(AppOperationMode.CUSTOM_VLESS, loaded.SelectedMode);
        Assert.Equal("-5", loaded.DpiMode);
        Assert.Equal("vless://test@1.2.3.4:443", loaded.CustomVlessUrl);

        // Reset to default
        loaded.Language = "ar";
        loaded.Theme = "dark";
        loaded.SelectedDns = "8.8.8.8";
        loaded.SelectedMode = AppOperationMode.VPN_ONLY;
        loaded.DpiMode = "Stable";
        loaded.Save();
    }
}
