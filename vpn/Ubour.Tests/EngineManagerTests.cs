using Xunit;
using Ubour;

namespace Ubour.Tests;

public class EngineManagerTests
{
    [Fact]
    public void EngineManager_InitialState_IsNotRunning()
    {
        var manager = new EngineManager();
        Assert.False(manager.IsRunning);
        Assert.Equal(AppOperationMode.WARP_AND_ADBLOCK, manager.CurrentMode);
    }

    [Fact]
    public void EngineManager_StopWhenNotRunning_DoesNotThrow()
    {
        var manager = new EngineManager();
        var exception = Record.Exception(() => manager.Stop());
        Assert.Null(exception);
        Assert.False(manager.IsRunning);
    }

    [Fact]
    public void EngineManager_SupportsAllFiveModes()
    {
        var modes = Enum.GetValues<AppOperationMode>();
        Assert.Equal(5, modes.Length);
        Assert.Contains(AppOperationMode.WARP_AND_ADBLOCK, modes);
        Assert.Contains(AppOperationMode.DPI_AND_ADBLOCK, modes);
        Assert.Contains(AppOperationMode.ADBLOCK_ONLY, modes);
        Assert.Contains(AppOperationMode.DPI_ONLY, modes);
        Assert.Contains(AppOperationMode.CUSTOM_VLESS, modes);
    }
}
