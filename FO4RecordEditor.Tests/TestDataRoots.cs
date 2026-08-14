using System;
using System.IO;
using System.Linq;

namespace FO4RecordEditor.Tests;

/// <summary>
/// Finds a real Fallout 4 <c>Data</c> folder for the tests that need real game archives.
/// </summary>
/// <remarks>
/// Every fixture-dependent test used to hardcode one absolute <c>E:\</c> path, so on any machine
/// whose install lives elsewhere -- a different drive letter, a different modlist name, a Linux
/// checkout -- the test skipped and xunit recorded a pass. A green run therefore did not mean the
/// code had been exercised. Resolving from an env var plus a candidate list means these actually
/// run wherever the archives happen to be.
/// <para>
/// Set <c>FO4RE_TEST_DATA</c> to point at a Data folder explicitly; it wins over the candidates.
/// Set <c>FO4RE_REQUIRE_FIXTURES=1</c> to turn "fixture missing" into a hard failure instead of a
/// skip, which is what you want in a run that is supposed to prove something.
/// </para>
/// </remarks>
public static class TestDataRoots
{
    private static readonly string[] Candidates =
    {
        @"E:\Modlists\Fallen World Alpha 2\Stock Folder\Data",
        @"E:\SteamLibrary\steamapps\common\Fallout 4\Data",
        @"D:\InstallTest\Stock Folder\Data",
        "/media/ricky/D Drive/Fallout4Backup/Fallout 4/Data",
        "/media/ricky/D Drive/InstallTest/Stock Folder/Data",
        // udisks2 mounts removable volumes under /run/media/<user>, not /media/<user>; without these
        // every fixture-dependent test silently skipped on this machine and still reported green,
        // which is the exact failure mode the remarks above describe.
        "/run/media/ricky/Games-Storage/Modlists/Fallen World Alpha 2/Stock Folder/Data",
        "/run/media/ricky/Games-Storage/SteamLibrary/steamapps/common/Fallout 4/Data",
    };

    /// <summary>A Data folder that exists, or null.</summary>
    public static string? DataRoot
    {
        get
        {
            var explicitRoot = Environment.GetEnvironmentVariable("FO4RE_TEST_DATA");
            if (!string.IsNullOrWhiteSpace(explicitRoot) && Directory.Exists(explicitRoot))
                return explicitRoot;
            return Candidates.FirstOrDefault(Directory.Exists);
        }
    }

    /// <summary>Full path to an archive inside the resolved Data folder, or null if either is missing.</summary>
    public static string? Archive(string fileName)
    {
        var root = DataRoot;
        if (root == null) return null;
        var full = Path.Combine(root, fileName);
        return File.Exists(full) ? full : null;
    }

    /// <summary>True when a missing fixture should fail the test rather than skip it.</summary>
    public static bool FixturesRequired =>
        Environment.GetEnvironmentVariable("FO4RE_REQUIRE_FIXTURES") == "1";
}
