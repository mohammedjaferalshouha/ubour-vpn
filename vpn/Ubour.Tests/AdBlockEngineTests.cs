using System.IO;
using Xunit;
using Ubour.AdBlock;

namespace Ubour.Tests;

public class AdBlockEngineTests
{
    [Fact]
    public void HashDomain_ComputesDeterministicFnv1a()
    {
        var hash1 = AdBlockEngine.HashDomain("ads.google.com");
        var hash2 = AdBlockEngine.HashDomain("ADS.GOOGLE.COM.");
        var hash3 = AdBlockEngine.HashDomain("doubleclick.net");

        Assert.Equal(hash1, hash2);
        Assert.NotEqual(hash1, hash3);
        Assert.True(hash1 != 0);
    }

    [Fact]
    public void AdBlockEngine_LoadsAndBlocksDomains()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllLines(tempFile, new[]
            {
                "||doubleclick.net^",
                "||googleadservices.com^",
                "0.0.0.0 telemetry.app.com",
                "@@||allowed.doubleclick.net^",
                "# Comment line",
                "! Another comment"
            });

            var engine = AdBlockEngine.Instance;
            engine.LoadEmbeddedFilters(tempFile);

            Assert.True(engine.IsLoaded);
            Assert.True(engine.IsDomainBlocked("doubleclick.net"));
            Assert.True(engine.IsDomainBlocked("ad.doubleclick.net"));
            Assert.True(engine.IsDomainBlocked("sub.ad.doubleclick.net"));
            Assert.True(engine.IsDomainBlocked("googleadservices.com"));
            Assert.True(engine.IsDomainBlocked("telemetry.app.com"));

            // Whitelisted
            Assert.False(engine.IsDomainBlocked("allowed.doubleclick.net"));

            // Clean domain
            Assert.False(engine.IsDomainBlocked("wikipedia.org"));
            Assert.False(engine.IsDomainBlocked("github.com"));

            Assert.True(engine.BlockedAds > 0);
            Assert.True(engine.TotalQueries > 0);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}
