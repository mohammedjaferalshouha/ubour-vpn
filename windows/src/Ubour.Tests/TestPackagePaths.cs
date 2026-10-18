using System;
using System.IO;

namespace Ubour.Tests;

internal static class TestPackagePaths
{
    public static string GetWindowsX64PackageDirectory()
    {
        DirectoryInfo? directory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        while (directory != null)
        {
            string[] candidates =
            {
                Path.Combine(directory.FullName, "Ubour-windows-x64"),
                Path.Combine(directory.FullName, "windows", "Ubour-windows-x64"),
                Path.Combine(directory.FullName, "publish", "Ubour-windows-x64")
            };

            foreach (string candidate in candidates)
            {
                if (File.Exists(Path.Combine(candidate, "engine", "x86_64", "sing-box.exe")))
                    return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the Windows x64 package containing engine/x86_64/sing-box.exe.");
    }
}
