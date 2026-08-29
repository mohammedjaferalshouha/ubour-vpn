using System;
using System.IO;
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
