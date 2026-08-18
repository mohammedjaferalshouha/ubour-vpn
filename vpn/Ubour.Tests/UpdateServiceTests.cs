using Xunit;
using Ubour;

namespace Ubour.Tests;

public class UpdateServiceTests
{
    [Fact]
    public void UpdateResult_Current_HasNoUpdate()
    {
        var result = UpdateResult.Current();
        Assert.False(result.HasUpdate);
        Assert.Null(result.Version);
        Assert.Null(result.UpdateUrl);
        Assert.Contains("محدّث", result.Message(false));
        Assert.Contains("current", result.Message(true));
    }

    [Fact]
    public void UpdateResult_Available_HasUpdate()
    {
        var result = UpdateResult.Available("0.2.4", "https://github.com/ValdikSS/GoodbyeDPI/releases/tag/0.2.4");
        Assert.True(result.HasUpdate);
        Assert.Equal("0.2.4", result.Version);
        Assert.Equal("https://github.com/ValdikSS/GoodbyeDPI/releases/tag/0.2.4", result.UpdateUrl);
        Assert.Contains("0.2.4", result.Message(false));
        Assert.Contains("0.2.4", result.Message(true));
    }

    [Fact]
    public void UpdateResult_NoUpdate_HasNoUpdate()
    {
        var result = UpdateResult.NoUpdate();
        Assert.False(result.HasUpdate);
        Assert.Null(result.Version);
    }
}
