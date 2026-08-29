using System;
using System.IO;
using System.Reflection;
using Xunit;
using Ubour.Services;

namespace Ubour.Tests;

public class AdBlockEngineTests
{
    [Fact]
    public void AdBlockEngine_Initialize_LoadsEmbeddedOrDirectRules()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "UbourTestRules_" + Guid.NewGuid().ToString("N"));
        string rulesDir = Path.Combine(tempDir, "rules");
        Directory.CreateDirectory(rulesDir);

        try
        {
            string rulesFile = Path.Combine(rulesDir, "adblock_rules.txt");
            File.WriteAllLines(rulesFile, new[]
            {
                "||doubleclick.net^",
                "||googleadservices.com^",
                "0.0.0.0 telemetry.app.com",
                "127.0.0.1 tracker.metrics.io",
                "@@||allowed.doubleclick.net^",
                "# Comment line",
                "! Another comment",
                "[Adblock Plus 2.0]",
                "||subdomain.badsite.org^$third-party"
            });

            AdBlockEngine.Initialize(tempDir);

            Assert.True(AdBlockEngine.TotalRules > 0);
            Assert.True(AdBlockEngine.IsDomainBlocked("doubleclick.net"));
            Assert.True(AdBlockEngine.IsDomainBlocked("ad.doubleclick.net"));
            Assert.True(AdBlockEngine.IsDomainBlocked("sub.ad.doubleclick.net"));
            Assert.True(AdBlockEngine.IsDomainBlocked("googleadservices.com"));
            Assert.True(AdBlockEngine.IsDomainBlocked("telemetry.app.com"));
            Assert.True(AdBlockEngine.IsDomainBlocked("tracker.metrics.io"));
            Assert.True(AdBlockEngine.IsDomainBlocked("subdomain.badsite.org"));

            // Whitelisted domain
            Assert.False(AdBlockEngine.IsDomainBlocked("allowed.doubleclick.net"));

            // Clean domain
            Assert.False(AdBlockEngine.IsDomainBlocked("wikipedia.org"));
            Assert.False(AdBlockEngine.IsDomainBlocked("github.com"));
            Assert.False(AdBlockEngine.IsDomainBlocked("microsoft.com"));
            Assert.False(AdBlockEngine.IsDomainBlocked("speedtest.net"));

            Assert.True(AdBlockEngine.TotalQueries > 0);
            Assert.True(AdBlockEngine.BlockedAdsCount > 0 || AdBlockEngine.BlockedTrackersCount > 0);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    [Fact]
    public void AdBlockEngine_ResetStats_ClearsCounters()
    {
        AdBlockEngine.IsDomainBlocked("doubleclick.net");
        AdBlockEngine.ResetStats();

        Assert.Equal(0, AdBlockEngine.BlockedAdsCount);
        Assert.Equal(0, AdBlockEngine.BlockedTrackersCount);
        Assert.Equal(0, AdBlockEngine.TotalQueries);
    }
}
