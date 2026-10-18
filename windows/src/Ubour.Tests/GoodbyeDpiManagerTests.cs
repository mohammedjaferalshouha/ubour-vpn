using System;
using System.IO;
using System.Reflection;
using Xunit;
using Ubour.Services;

namespace Ubour.Tests;

public class GoodbyeDpiManagerTests
{
    [Fact]
    public void GoodbyeDpiManager_Stop_DoesNotThrow()
    {
        var manager = new GoodbyeDpiManager();
        var ex = Record.Exception(() => manager.Stop());
        Assert.Null(ex);
        Assert.False(manager.IsRunning);
    }

    [Theory]
    [InlineData("-1", "-1")]
    [InlineData("-5", "-5")]
    [InlineData("-9", "-9")]
    [InlineData("Aggressive", "-9")]
    [InlineData("aggressive", "-9")]
    [InlineData("Medium", "-5")]
    [InlineData("medium", "-5")]
    [InlineData("Compatible", "-5")]
    [InlineData("compatible", "-5")]
    [InlineData("unknown", "-1")]
    public void GoodbyeDpiManager_ProfileMapping_SelectsCorrectArguments(string mode, string expectedFlag)
    {
        var resolve = typeof(GoodbyeDpiManager).GetMethod("ResolveDpiFlag", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(resolve);
        string dpiFlag = (string)resolve.Invoke(null, new object[] { mode })!;
        Assert.Equal(expectedFlag, dpiFlag);
    }

    [Fact]
    public void GoodbyeDpi_Binaries_ExistForBothArchitectures()
    {
        string baseDir = TestPackagePaths.GetWindowsX64PackageDirectory();
        string x86Exe = Path.Combine(baseDir, "engine", "x86", "goodbyedpi.exe");
        string x64Exe = Path.Combine(baseDir, "engine", "x86_64", "goodbyedpi.exe");

        if (Directory.Exists(Path.Combine(baseDir, "engine")))
        {
            Assert.True(File.Exists(x86Exe), $"x86 goodbyedpi.exe must exist at {x86Exe}");
            Assert.True(File.Exists(x64Exe), $"x86_64 goodbyedpi.exe must exist at {x64Exe}");
            Assert.True(File.Exists(Path.Combine(baseDir, "engine", "x86", "WinDivert.dll")));
            Assert.True(File.Exists(Path.Combine(baseDir, "engine", "x86_64", "WinDivert.dll")));
        }
    }
}

public class PackagingVerificationTests
{
    [Fact]
    public void WindowsPackages_ContainAllRequiredCoreFiles()
    {
        string baseDir = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", ".."));
        string x64Dir = Path.Combine(baseDir, "Ubour-windows-x64");
        string x86Dir = Path.Combine(baseDir, "Ubour-windows-x86");

        if (Directory.Exists(x64Dir))
        {
            Assert.True(File.Exists(Path.Combine(x64Dir, "rules", "adblock_rules.txt")), "adblock_rules.txt must exist in x64");
            Assert.True(File.Exists(Path.Combine(x64Dir, "README.md")), "README.md must exist in x64");
            Assert.True(File.Exists(Path.Combine(x64Dir, "README.ar.md")), "README.ar.md must exist in x64");
        }

        if (Directory.Exists(x86Dir))
        {
            Assert.True(File.Exists(Path.Combine(x86Dir, "rules", "adblock_rules.txt")), "adblock_rules.txt must exist in x86");
            Assert.True(File.Exists(Path.Combine(x86Dir, "README.md")), "README.md must exist in x86");
            Assert.True(File.Exists(Path.Combine(x86Dir, "README.ar.md")), "README.ar.md must exist in x86");
        }
    }
}
