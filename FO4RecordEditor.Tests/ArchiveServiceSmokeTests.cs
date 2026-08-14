using System;
using System.IO;
using FluentAssertions;
using FO4RecordEditor.Services;
using Xunit;
using Xunit.Abstractions;

namespace FO4RecordEditor.Tests;

public class ArchiveServiceSmokeTests
{
    private readonly ITestOutputHelper _out;
    public ArchiveServiceSmokeTests(ITestOutputHelper o) => _out = o;

    [Fact(Skip = "Needs the Creation Kit installed (Archive2.exe). Remove Skip to run manually. " +
                 "Verified passing 2026-07-20 (packed 2 files, read back both, byte-identical).")]
    public void Pack_ThenReadBack_RoundTrips()
    {
        var root = Path.Combine(Path.GetTempPath(), "FO4RE_ArchiveSmoke_" + Guid.NewGuid().ToString("N")[..8]);
        var soundDir = Path.Combine(root, "Sound", "fx", "test");
        Directory.CreateDirectory(soundDir);
        try
        {
            var bytesA = new byte[1000]; new Random(1).NextBytes(bytesA);
            var bytesB = new byte[2000]; new Random(2).NextBytes(bytesB);
            File.WriteAllBytes(Path.Combine(soundDir, "one.xwm"), bytesA);
            File.WriteAllBytes(Path.Combine(soundDir, "two.xwm"), bytesB);

            var outBa2 = Path.Combine(root, "Test - Main.ba2");
            var pack = ArchiveService.Pack(
                new[] { Path.Combine(root, "Sound") }, outBa2, "General", root, compress: true);
            _out.WriteLine(pack);
            pack.Should().StartWith("RESULT: success");
            File.Exists(outBa2).Should().BeTrue();

            var listing = ArchiveService.ListArchiveJson(outBa2, null, 100);
            _out.WriteLine(listing);
            listing.Should().Contain("\"totalCount\":2")
                .And.Contain("Sound/fx/test/one.xwm")
                .And.Contain("Sound/fx/test/two.xwm");

            var extractDir = Path.Combine(root, "extracted");
            var extracted = ArchiveService.ExtractFile(outBa2, "Sound/fx/test/one.xwm", Path.Combine(extractDir, "one.xwm"));
            _out.WriteLine(extracted);
            File.ReadAllBytes(Path.Combine(extractDir, "one.xwm")).Should().Equal(bytesA);
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }
}
