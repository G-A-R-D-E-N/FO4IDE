using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using FluentAssertions;
using FO4RecordEditor.Services;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Archives;
using Newtonsoft.Json.Linq;
using Xunit;

namespace FO4RecordEditor.Tests;

// Hand-builds a minimal, real, valid BTDX v1 GNRL archive (the exact byte layout
// Mutagen.Bethesda.Archives.Ba2.Ba2Reader parses) rather than mocking IArchiveReader, so these
// tests exercise the real binary format ArchiveService will see in the wild.
public class ArchiveServiceTests
{
    private static string BuildTestBa2(params (string path, byte[] content)[] entries)
    {
        var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, Encoding.ASCII, leaveOpen: true))
        {
            const int headerSize = 24;   // magic(4)+version(4)+fourcc(4)+numFiles(4)+nameTableOffset(8)
            const int entrySize = 36;    // nameHash(4)+ext(4)+dirHash(4)+flags(4)+offset(8)+size(4)+realSize(4)+align(4)

            uint dataStart = headerSize + (uint)(entrySize * entries.Length);
            var offsets = new uint[entries.Length];
            uint cursor = dataStart;
            for (int i = 0; i < entries.Length; i++) { offsets[i] = cursor; cursor += (uint)entries[i].content.Length; }
            uint nameTableOffset = cursor;

            bw.Write(Encoding.ASCII.GetBytes("BTDX"));
            bw.Write((uint)1);                              // version
            bw.Write(Encoding.ASCII.GetBytes("GNRL"));       // entry type
            bw.Write((uint)entries.Length);
            bw.Write((ulong)nameTableOffset);

            // BA2FileEntry, version==1 layout, one per entry
            for (int i = 0; i < entries.Length; i++)
            {
                bw.Write((uint)0);                                 // nameHash (unused; Path comes from name table)
                bw.Write(Encoding.ASCII.GetBytes("txt\0")[..4]);    // extension
                bw.Write((uint)0);                                 // dirHash
                bw.Write((uint)0);                                 // flags
                bw.Write((ulong)offsets[i]);                        // offset
                bw.Write((uint)0);                                  // size==0 -> uncompressed (Compressed = size != 0 for v<7)
                bw.Write((uint)entries[i].content.Length);          // realSize
                bw.Write((uint)0);                                  // align (version<=1 field)
            }

            foreach (var (_, content) in entries) bw.Write(content);

            foreach (var (path, _) in entries)
            {
                var pathBytes = Encoding.UTF8.GetBytes(path);
                bw.Write((short)pathBytes.Length);
                bw.Write(pathBytes);
            }
        }

        var path2 = Path.Combine(Path.GetTempPath(), $"ArchiveTest_{Guid.NewGuid():N}.ba2");
        File.WriteAllBytes(path2, ms.ToArray());
        return path2;
    }

    [Fact]
    public void ListArchive_ReportsPathAndSize()
    {
        var content = Encoding.ASCII.GetBytes("hello archive");
        var archive = BuildTestBa2((@"Meshes\Test\thing.txt", content));
        try
        {
            var result = ArchiveService.ListArchive(archive, filter: null, limit: 100);
            result.Should().Contain(@"Meshes\Test\thing.txt").And.Contain($"{content.Length}");
        }
        finally { try { File.Delete(archive); } catch { } }
    }

    [Fact]
    public void ListArchive_FilterNarrowsResults()
    {
        var archive = BuildTestBa2((@"Meshes\Test\thing.txt", Encoding.ASCII.GetBytes("x")));
        try
        {
            ArchiveService.ListArchive(archive, filter: "DoesNotExist", limit: 100)
                .Should().Contain("No entries").And.Contain("DoesNotExist");
        }
        finally { try { File.Delete(archive); } catch { } }
    }

    [Fact]
    public void ExtractFile_WritesExactBytes()
    {
        var content = Encoding.ASCII.GetBytes("the exact payload");
        var archive = BuildTestBa2((@"Scripts\Source\Test.psc", content));
        var outPath = Path.Combine(Path.GetTempPath(), $"ArchiveExtractOut_{Guid.NewGuid():N}.psc");
        try
        {
            var result = ArchiveService.ExtractFile(archive, @"Scripts\Source\Test.psc", outPath);
            result.Should().Contain("Extracted");
            File.Exists(outPath).Should().BeTrue();
            File.ReadAllBytes(outPath).Should().Equal(content);
        }
        finally
        {
            try { File.Delete(archive); } catch { }
            try { File.Delete(outPath); } catch { }
        }
    }

    [Fact]
    public void ExtractFile_UnknownInnerPath_ReturnsError()
    {
        var archive = BuildTestBa2((@"Meshes\Test\thing.txt", Encoding.ASCII.GetBytes("x")));
        try
        {
            ArchiveService.ExtractFile(archive, @"Meshes\NotHere.txt", Path.GetTempFileName())
                .Should().Contain("is not in");
        }
        finally { try { File.Delete(archive); } catch { } }
    }

    [Fact]
    public void ExtractAll_PreservesFolderStructure()
    {
        var content = Encoding.ASCII.GetBytes("payload");
        var archive = BuildTestBa2((@"Meshes\Sub\Dir\thing.txt", content));
        var outDir = Path.Combine(Path.GetTempPath(), $"ArchiveExtractAll_{Guid.NewGuid():N}");
        try
        {
            var result = ArchiveService.ExtractAll(archive, outDir, filter: null, limit: 100);
            result.Should().Contain("Extracted 1 of 1");
            var expected = Path.Combine(outDir, "Meshes", "Sub", "Dir", "thing.txt");
            File.Exists(expected).Should().BeTrue();
            File.ReadAllBytes(expected).Should().Equal(content);
        }
        finally
        {
            try { File.Delete(archive); } catch { }
            try { Directory.Delete(outDir, true); } catch { }
        }
    }

    [Fact]
    public void ExtractAll_OverLimit_RefusesWithoutWritingAnything()
    {
        var archive = BuildTestBa2(
            (@"Meshes\A.txt", Encoding.ASCII.GetBytes("a")),
            (@"Meshes\B.txt", Encoding.ASCII.GetBytes("b")));
        var outDir = Path.Combine(Path.GetTempPath(), $"ArchiveExtractLimit_{Guid.NewGuid():N}");
        try
        {
            var result = ArchiveService.ExtractAll(archive, outDir, filter: null, limit: 1);
            result.Should().Contain("2 matching entries").And.Contain("over the 1 limit");
            Directory.Exists(outDir).Should().BeFalse("a refusal must not create the output directory");
        }
        finally { try { Directory.Delete(outDir, true); } catch { } }
    }

    [Fact]
    public void ListArchiveJson_ReturnsStructuredEntriesAndCounts()
    {
        var archive = BuildTestBa2(
            (@"Meshes\A.txt", Encoding.ASCII.GetBytes("aa")),
            (@"Textures\B.txt", Encoding.ASCII.GetBytes("bbb")));
        try
        {
            var json = JObject.Parse(ArchiveService.ListArchiveJson(archive, filter: null, limit: 100));
            json["totalCount"]!.Value<int>().Should().Be(2);
            json["shownCount"]!.Value<int>().Should().Be(2);
            json["truncated"]!.Value<bool>().Should().BeFalse();
            var entries = (JArray)json["entries"]!;
            entries.First(e => e["path"]!.Value<string>() == @"Meshes\A.txt")["size"]!.Value<int>().Should().Be(2);
        }
        finally { try { File.Delete(archive); } catch { } }
    }

    [Fact]
    public void ListArchiveJson_Truncates_AndReportsIt()
    {
        var archive = BuildTestBa2(
            (@"A.txt", Encoding.ASCII.GetBytes("a")),
            (@"B.txt", Encoding.ASCII.GetBytes("b")),
            (@"C.txt", Encoding.ASCII.GetBytes("c")));
        try
        {
            var json = JObject.Parse(ArchiveService.ListArchiveJson(archive, filter: null, limit: 2));
            json["totalCount"]!.Value<int>().Should().Be(3);
            json["shownCount"]!.Value<int>().Should().Be(2);
            json["truncated"]!.Value<bool>().Should().BeTrue();
        }
        finally { try { File.Delete(archive); } catch { } }
    }

    [Fact]
    public void ListArchiveJson_MissingArchive_ReturnsJsonError()
    {
        var json = JObject.Parse(ArchiveService.ListArchiveJson(@"C:\does\not\exist.ba2", null, 100));
        json["error"].Should().NotBeNull();
    }

    [Fact]
    public void ExtractSelected_ExtractsOnlyChosenEntries()
    {
        var content = Encoding.ASCII.GetBytes("payload");
        var archive = BuildTestBa2(
            (@"Meshes\A.txt", content),
            (@"Meshes\B.txt", Encoding.ASCII.GetBytes("skip me")),
            (@"Meshes\C.txt", Encoding.ASCII.GetBytes("skip me too")));
        var outDir = Path.Combine(Path.GetTempPath(), $"ArchiveExtractSelected_{Guid.NewGuid():N}");
        try
        {
            var result = ArchiveService.ExtractSelected(archive, new List<string> { @"Meshes\A.txt" }, outDir);
            result.Should().Contain("Extracted 1 of 1 selected");

            var extracted = Path.Combine(outDir, "Meshes", "A.txt");
            File.Exists(extracted).Should().BeTrue();
            File.ReadAllBytes(extracted).Should().Equal(content);
            File.Exists(Path.Combine(outDir, "Meshes", "B.txt")).Should().BeFalse();
            File.Exists(Path.Combine(outDir, "Meshes", "C.txt")).Should().BeFalse();
        }
        finally
        {
            try { File.Delete(archive); } catch { }
            try { Directory.Delete(outDir, true); } catch { }
        }
    }

    [Fact]
    public void ExtractSelected_NoneMatch_ReturnsError()
    {
        var archive = BuildTestBa2((@"Meshes\A.txt", Encoding.ASCII.GetBytes("x")));
        try
        {
            ArchiveService.ExtractSelected(archive, new List<string> { @"Meshes\NotHere.txt" }, Path.GetTempPath())
                .Should().Contain("None of the selected files");
        }
        finally { try { File.Delete(archive); } catch { } }
    }

    // ---- filterMode: wildcard/regex (ported from AlexxEG/BSA_Browser's filter design) ----

    [Fact]
    public void ListArchiveJson_WildcardFilter_MatchesExtension()
    {
        var archive = BuildTestBa2(
            (@"Meshes\gun.nif", Encoding.ASCII.GetBytes("a")),
            (@"Textures\gun_d.dds", Encoding.ASCII.GetBytes("b")));
        try
        {
            var json = JObject.Parse(ArchiveService.ListArchiveJson(archive, "*.nif", 100, "wildcard"));
            var entries = (JArray)json["entries"]!;
            entries.Should().HaveCount(1);
            entries[0]["path"]!.Value<string>().Should().Be(@"Meshes\gun.nif");
        }
        finally { try { File.Delete(archive); } catch { } }
    }

    [Fact]
    public void ListArchiveJson_RegexFilter_Matches()
    {
        var archive = BuildTestBa2(
            (@"Meshes\gun01.nif", Encoding.ASCII.GetBytes("a")),
            (@"Meshes\gunXX.nif", Encoding.ASCII.GetBytes("b")));
        try
        {
            var json = JObject.Parse(ArchiveService.ListArchiveJson(archive, @"gun\d+\.nif", 100, "regex"));
            var entries = (JArray)json["entries"]!;
            entries.Should().HaveCount(1);
            entries[0]["path"]!.Value<string>().Should().Be(@"Meshes\gun01.nif");
        }
        finally { try { File.Delete(archive); } catch { } }
    }

    [Fact]
    public void ListArchiveJson_InvalidRegex_ReturnsJsonErrorNotException()
    {
        var archive = BuildTestBa2((@"Meshes\A.txt", Encoding.ASCII.GetBytes("x")));
        try
        {
            var json = JObject.Parse(ArchiveService.ListArchiveJson(archive, "[unterminated", 100, "regex"));
            json["error"].Should().NotBeNull();
        }
        finally { try { File.Delete(archive); } catch { } }
    }

    [Fact]
    public void ListArchiveJson_SimpleFilterIsUnchanged_PlainSubstring()
    {
        // 'simple' mode (the default, and what the AI-facing MCP tools use) must still be a plain
        // substring match, NOT wildcard/regex -- '*' has no special meaning in this mode.
        var archive = BuildTestBa2((@"Meshes\gun*special.nif", Encoding.ASCII.GetBytes("a")));
        try
        {
            var json = JObject.Parse(ArchiveService.ListArchiveJson(archive, "gun*special", 100, "simple"));
            ((JArray)json["entries"]!).Should().HaveCount(1);
        }
        finally { try { File.Delete(archive); } catch { } }
    }

    [Fact]
    public void ListArchiveJson_GlobAlias_MatchesExtensionAcrossNestedArchivePath()
    {
        var archive = BuildTestBa2(
            (@"Scripts\Nested\Quest.pex", Encoding.ASCII.GetBytes("a")),
            (@"Scripts\Nested\Quest.psc", Encoding.ASCII.GetBytes("b")));
        try
        {
            var json = JObject.Parse(ArchiveService.ListArchiveJson(archive, "*.pex", 100, "glob"));
            var entries = (JArray)json["entries"]!;
            entries.Should().ContainSingle();
            entries[0]["path"]!.Value<string>().Should().Be(@"Scripts\Nested\Quest.pex");
        }
        finally { try { File.Delete(archive); } catch { } }
    }

    [Fact]
    public void ListArchiveJson_UnknownFilterMode_ReturnsAnErrorInsteadOfSilentlyUsingContains()
    {
        var archive = BuildTestBa2((@"Scripts\Quest.pex", Encoding.ASCII.GetBytes("a")));
        try
        {
            var json = JObject.Parse(ArchiveService.ListArchiveJson(archive, "*.pex", 100, "typo"));
            json["error"]!.Value<string>().Should().Contain("filter mode must be");
        }
        finally { try { File.Delete(archive); } catch { } }
    }

    [Fact]
    public void RegexFilter_TimeoutIsCaughtAndReported()
    {
        var hostilePath = new string('a', 30_000) + "!";
        var archive = BuildTestBa2((hostilePath, Encoding.ASCII.GetBytes("x")));
        try
        {
            var reader = Archive.CreateReader(GameRelease.Fallout4, archive);
            var matcher = ArchiveService.BuildMatcher("^(a|aa)+$", "regex", TimeSpan.FromMilliseconds(1));

            ArchiveService.TryFilterFiles(reader.Files, matcher, "regex", out var files, out var error)
                .Should().BeFalse();

            files.Should().BeEmpty();
            error.Should().Contain("too long").And.Contain("stopped");
        }
        finally { try { File.Delete(archive); } catch { } }
    }

    [Fact]
    public void ExtractAll_UnsafeEntryRefusesTheWholeBatchBeforeWriting()
    {
        var archive = BuildTestBa2(
            (@"Meshes\Safe.nif", Encoding.ASCII.GetBytes("safe")),
            (@"..\escape.txt", Encoding.ASCII.GetBytes("escape")));
        var parent = Path.Combine(Path.GetTempPath(), $"ArchiveTraversal_{Guid.NewGuid():N}");
        var outDir = Path.Combine(parent, "out");
        try
        {
            var result = ToolError.Unwrap(ArchiveService.ExtractAll(archive, outDir, null, 100));

            result.IsError.Should().BeTrue();
            result.Text.Should().Contain("refused before writing anything").And.Contain("Unsafe archive entry");
            Directory.Exists(outDir).Should().BeFalse();
            File.Exists(Path.Combine(parent, "escape.txt")).Should().BeFalse();
        }
        finally
        {
            try { File.Delete(archive); } catch { }
            try { Directory.Delete(parent, recursive: true); } catch { }
        }
    }

    [Fact]
    public void ExtractSelected_UnsafeEntryRefusesBeforeWriting()
    {
        var archive = BuildTestBa2((@"..\selected-escape.txt", Encoding.ASCII.GetBytes("escape")));
        var parent = Path.Combine(Path.GetTempPath(), $"ArchiveSelectedTraversal_{Guid.NewGuid():N}");
        var outDir = Path.Combine(parent, "out");
        try
        {
            var result = ToolError.Unwrap(ArchiveService.ExtractSelected(
                archive,
                new[] { @"..\selected-escape.txt" },
                outDir));

            result.IsError.Should().BeTrue();
            result.Text.Should().Contain("Unsafe archive entry");
            Directory.Exists(outDir).Should().BeFalse();
            File.Exists(Path.Combine(parent, "selected-escape.txt")).Should().BeFalse();
        }
        finally
        {
            try { File.Delete(archive); } catch { }
            try { Directory.Delete(parent, recursive: true); } catch { }
        }
    }

    [Fact]
    public void ExtractAll_CaseInsensitiveFileCollisionRefusesBeforeWriting()
    {
        var archive = BuildTestBa2(
            (@"Meshes\Weapon.nif", Encoding.ASCII.GetBytes("first")),
            (@"meshes\WEAPON.NIF", Encoding.ASCII.GetBytes("second")));
        var outDir = Path.Combine(Path.GetTempPath(), $"ArchiveCollision_{Guid.NewGuid():N}");
        try
        {
            var result = ToolError.Unwrap(ArchiveService.ExtractAll(archive, outDir, null, 100));

            result.IsError.Should().BeTrue();
            result.Text.Should().Contain("same case-insensitive destination");
            Directory.Exists(outDir).Should().BeFalse();
        }
        finally
        {
            try { File.Delete(archive); } catch { }
            try { Directory.Delete(outDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void ExtractAll_MergesCaseOnlyDirectoryVariantsIntoOneDeterministicTree()
    {
        var archive = BuildTestBa2(
            (@"Scripts\Hardcore\A.pex", Encoding.ASCII.GetBytes("a")),
            (@"scripts\Source\B.psc", Encoding.ASCII.GetBytes("b")));
        var outDir = Path.Combine(Path.GetTempPath(), $"ArchiveCaseMerge_{Guid.NewGuid():N}");
        try
        {
            ArchiveService.ExtractAll(archive, outDir, null, 100)
                .Should().Contain("Extracted 2 of 2");

            var roots = Directory.GetDirectories(outDir);
            roots.Should().ContainSingle();
            Path.GetFileName(roots[0]).Should().Be("scripts");
            File.Exists(Path.Combine(outDir, "scripts", "Hardcore", "A.pex")).Should().BeTrue();
            File.Exists(Path.Combine(outDir, "scripts", "Source", "B.psc")).Should().BeTrue();
        }
        finally
        {
            try { File.Delete(archive); } catch { }
            try { Directory.Delete(outDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void ExtractFile_AtomicallyReplacesAnExistingDestination()
    {
        var content = Encoding.ASCII.GetBytes("replacement payload");
        var archive = BuildTestBa2((@"Meshes\Thing.nif", content));
        var outDir = Path.Combine(Path.GetTempPath(), $"ArchiveSingleAtomic_{Guid.NewGuid():N}");
        var outPath = Path.Combine(outDir, "Thing.nif");
        try
        {
            Directory.CreateDirectory(outDir);
            File.WriteAllText(outPath, "old payload that must not be partially reused");

            ArchiveService.ExtractFile(archive, @"Meshes\Thing.nif", outPath)
                .Should().Contain("Extracted");

            File.ReadAllBytes(outPath).Should().Equal(content);
            Directory.GetFiles(outDir, "*.tmp").Should().BeEmpty();
        }
        finally
        {
            try { File.Delete(archive); } catch { }
            try { Directory.Delete(outDir, recursive: true); } catch { }
        }
    }

    // ---- CompareArchivesJson (ported from AlexxEG/BSA_Browser's CompareForm.CompareAsync) ----

    [Fact]
    public void CompareArchivesJson_ClassifiesAddedRemovedChangedIdentical()
    {
        var archiveA = BuildTestBa2(
            (@"Meshes\Same.nif", Encoding.ASCII.GetBytes("identical content")),
            (@"Meshes\Old.nif", Encoding.ASCII.GetBytes("only in A")),
            (@"Meshes\Different.nif", Encoding.ASCII.GetBytes("version A content")));
        var archiveB = BuildTestBa2(
            (@"Meshes\Same.nif", Encoding.ASCII.GetBytes("identical content")),
            (@"Meshes\New.nif", Encoding.ASCII.GetBytes("only in B")),
            (@"Meshes\Different.nif", Encoding.ASCII.GetBytes("version B content!")));
        try
        {
            var json = JObject.Parse(ArchiveService.CompareArchivesJson(archiveA, archiveB));
            var added = ((JArray)json["added"]!).Select(t => t.Value<string>()).ToList();
            var removed = ((JArray)json["removed"]!).Select(t => t.Value<string>()).ToList();
            var changed = ((JArray)json["changed"]!).Select(t => t.Value<string>()).ToList();

            added.Should().ContainSingle().Which.Should().Be(@"Meshes\New.nif");
            removed.Should().ContainSingle().Which.Should().Be(@"Meshes\Old.nif");
            changed.Should().ContainSingle().Which.Should().Be(@"Meshes\Different.nif");
            json["identicalCount"]!.Value<int>().Should().Be(1);
        }
        finally
        {
            try { File.Delete(archiveA); } catch { }
            try { File.Delete(archiveB); } catch { }
        }
    }

    [Fact]
    public void CompareArchivesJson_SameSizeDifferentContent_IsChangedNotIdentical()
    {
        // Same length is not enough to call two entries identical -- must be a real byte comparison.
        var archiveA = BuildTestBa2((@"A.txt", Encoding.ASCII.GetBytes("AAAA")));
        var archiveB = BuildTestBa2((@"A.txt", Encoding.ASCII.GetBytes("BBBB")));
        try
        {
            var json = JObject.Parse(ArchiveService.CompareArchivesJson(archiveA, archiveB));
            ((JArray)json["changed"]!).Should().ContainSingle();
            json["identicalCount"]!.Value<int>().Should().Be(0);
        }
        finally
        {
            try { File.Delete(archiveA); } catch { }
            try { File.Delete(archiveB); } catch { }
        }
    }

    [Fact]
    public void CompareArchivesJson_MissingArchive_ReturnsJsonError()
    {
        var archive = BuildTestBa2((@"A.txt", Encoding.ASCII.GetBytes("x")));
        try
        {
            var json = JObject.Parse(ArchiveService.CompareArchivesJson(archive, @"C:\does\not\exist.ba2"));
            json["error"].Should().NotBeNull();
        }
        finally { try { File.Delete(archive); } catch { } }
    }

    [Fact]
    public void ArchiveNotFound_ReturnsFriendlyError()
    {
        ArchiveService.ListArchive(@"C:\does\not\exist.ba2", null, 100)
            .Should().Contain("not found");
    }
}
