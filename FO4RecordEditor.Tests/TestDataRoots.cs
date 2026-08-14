using System;
using System.IO;
using System.Linq;

namespace FO4RecordEditor.Tests;

public static class TestDataRoots
{
    private static readonly string[] Candidates =
    {
        @"E:\Modlists\Fallen World Alpha 2\Stock Folder\Data",
        @"E:\SteamLibrary\steamapps\common\Fallout 4\Data",
        @"D:\InstallTest\Stock Folder\Data",
        "/media/ricky/D Drive/Fallout4Backup/Fallout 4/Data",
        "/media/ricky/D Drive/InstallTest/Stock Folder/Data",

        "/run/media/ricky/Games-Storage/Modlists/Fallen World Alpha 2/Stock Folder/Data",
        "/run/media/ricky/Games-Storage/SteamLibrary/steamapps/common/Fallout 4/Data",
    };

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

    public static string? Archive(string fileName)
    {
        var root = DataRoot;
        if (root == null) return null;
        var full = Path.Combine(root, fileName);
        return File.Exists(full) ? full : null;
    }

    public static bool FixturesRequired =>
        Environment.GetEnvironmentVariable("FO4RE_REQUIRE_FIXTURES") == "1";
}
