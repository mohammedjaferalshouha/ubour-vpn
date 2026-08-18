using System;
using System.IO;
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
    public void EngineManager_StartWithMissingEngine_ThrowsFileNotFoundException()
    {
        var manager = new EngineManager();
        var tempEngineDir = Path.Combine(AppContext.BaseDirectory, "engine");
        
        if (!Directory.Exists(tempEngineDir))
        {
            Assert.Throws<FileNotFoundException>(() => manager.Start());
        }
    }
}
