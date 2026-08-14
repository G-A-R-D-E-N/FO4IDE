using System.Buffers.Binary;
using System.IO;
using System.Text;
using FluentAssertions;
using FO4RecordEditor.Services;
using Xunit;

namespace FO4RecordEditor.Tests;

public class DuplicateFormIdScannerAuditTests
{
    [Fact]
    public void Scan_RejectsExcessiveGroupNestingBeforeRecursionCanExhaustTheStack()
    {
        const int nestedGroups = 130;
        const int headerLength = 24;
        var path = Path.Combine(Path.GetTempPath(), $"DeepGroups_{Guid.NewGuid():N}.esp");
        var bytes = new byte[(nestedGroups + 1) * headerLength];

        for (var i = 0; i < nestedGroups; i++)
        {
            var offset = i * headerLength;
            Encoding.ASCII.GetBytes("GRUP").CopyTo(bytes, offset);
            BinaryPrimitives.WriteUInt32LittleEndian(
                bytes.AsSpan(offset + 4, 4),
                checked((uint)(bytes.Length - offset)));
        }

        var recordOffset = nestedGroups * headerLength;
        Encoding.ASCII.GetBytes("KYWD").CopyTo(bytes, recordOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(recordOffset + 12, 4), 0x00000800u);
        File.WriteAllBytes(path, bytes);

        try
        {
            var result = DuplicateFormIdScanner.Scan(path);
            result.Error.Should().Contain("nesting");
            result.Duplicates.Should().BeEmpty();
        }
        finally
        {
            DuplicateFormIdScanner.Invalidate(path);
            try { File.Delete(path); } catch { }
        }
    }

    [Fact]
    public void Scan_ReportsDuplicateZeroFormIdsOutsideTheTes4Header()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ZeroFormIds_{Guid.NewGuid():N}.esp");
        var bytes = new byte[48];
        Encoding.ASCII.GetBytes("KYWD").CopyTo(bytes, 0);
        Encoding.ASCII.GetBytes("BOOK").CopyTo(bytes, 24);
        File.WriteAllBytes(path, bytes);

        try
        {
            var result = DuplicateFormIdScanner.Scan(path);
            result.Error.Should().BeNull();
            result.Duplicates.Should().ContainSingle(d =>
                d.RawFormId == 0 &&
                d.Count == 2 &&
                d.RecordTypes.SequenceEqual(new[] { "KYWD", "BOOK" }));
        }
        finally
        {
            DuplicateFormIdScanner.Invalidate(path);
            try { File.Delete(path); } catch { }
        }
    }

    [Fact]
    public void Cache_DoesNotAliasCaseDistinctPaths_OnLinux()
    {
        if (!OperatingSystem.IsLinux()) return;

        var root = Path.Combine(Path.GetTempPath(), $"CaseCache_{Guid.NewGuid():N}");
        var upperDirectory = Path.Combine(root, "Mods");
        var lowerDirectory = Path.Combine(root, "mods");
        Directory.CreateDirectory(upperDirectory);
        Directory.CreateDirectory(lowerDirectory);
        var upper = Path.Combine(upperDirectory, "Same.esp");
        var lower = Path.Combine(lowerDirectory, "Same.esp");

        var duplicate = new byte[48];
        Encoding.ASCII.GetBytes("KYWD").CopyTo(duplicate, 0);
        Encoding.ASCII.GetBytes("KYWD").CopyTo(duplicate, 24);
        BinaryPrimitives.WriteUInt32LittleEndian(duplicate.AsSpan(12, 4), 0x00000800u);
        BinaryPrimitives.WriteUInt32LittleEndian(duplicate.AsSpan(36, 4), 0x00000800u);
        var clean = duplicate.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(clean.AsSpan(36, 4), 0x00000801u);

        File.WriteAllBytes(upper, duplicate);
        File.WriteAllBytes(lower, clean);
        var timestamp = DateTime.UtcNow.AddMinutes(-1);
        File.SetLastWriteTimeUtc(upper, timestamp);
        File.SetLastWriteTimeUtc(lower, timestamp);

        try
        {
            DuplicateFormIdScanner.Scan(upper).Duplicates.Should().ContainSingle();
            DuplicateFormIdScanner.Scan(lower).Duplicates.Should().BeEmpty(
                "case-distinct files on a case-sensitive filesystem must not share a cache entry");
        }
        finally
        {
            DuplicateFormIdScanner.Invalidate(upper);
            DuplicateFormIdScanner.Invalidate(lower);
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public void SavePlugin_InvalidatesACachedScanEvenWhenLengthAndTimestampAreReused()
    {
        var plugin = $"DuplicateCache_{Guid.NewGuid():N}.esp";
        var path = Path.Combine(Path.GetTempPath(), plugin);

        WriteService.CreatePlugin(plugin).Should().Contain("Created");
        WriteService.CreateRecord(plugin, "KYWD", "CacheOne", env: null).Should().Contain("Created");
        WriteService.CreateRecord(plugin, "KYWD", "CacheTwo", env: null).Should().Contain("Created");
        WriteService.SavePlugin(plugin, path, env: null).Should().Contain("Saved");

        try
        {
            var malformed = File.ReadAllBytes(path);
            var records = FindSignatures(malformed, "KYWD");
            records.Should().HaveCount(2);
            var firstFormId = BinaryPrimitives.ReadUInt32LittleEndian(malformed.AsSpan(records[0] + 12, 4));
            BinaryPrimitives.WriteUInt32LittleEndian(malformed.AsSpan(records[1] + 12, 4), firstFormId);
            File.WriteAllBytes(path, malformed);

            var cachedLength = new FileInfo(path).Length;
            var cachedTimestamp = File.GetLastWriteTimeUtc(path);
            var cached = DuplicateFormIdScanner.Scan(path);
            cached.Error.Should().BeNull();
            cached.Duplicates.Should().ContainSingle(d => d.RawFormId == firstFormId);

            WriteService.SavePlugin(plugin, path, env: null).Should().Contain("Saved");
            new FileInfo(path).Length.Should().Be(cachedLength);
            File.SetLastWriteTimeUtc(path, cachedTimestamp);
            File.GetLastWriteTimeUtc(path).Should().Be(cachedTimestamp);

            var rescanned = DuplicateFormIdScanner.Scan(path);
            rescanned.Error.Should().BeNull();
            rescanned.Duplicates.Should().BeEmpty();
        }
        finally
        {
            DuplicateFormIdScanner.Invalidate(path);
            try { File.Delete(path); } catch { }
        }
    }

    private static List<int> FindSignatures(byte[] bytes, string signature)
    {
        var marker = Encoding.ASCII.GetBytes(signature);
        var offsets = new List<int>();
        for (var i = 0; i <= bytes.Length - 24; i++)
        {
            if (bytes.AsSpan(i, 4).SequenceEqual(marker))
            {


                var isGroupLabel = i >= 8 && bytes.AsSpan(i - 8, 4).SequenceEqual("GRUP"u8);
                if (!isGroupLabel) offsets.Add(i);
            }
        }
        return offsets;
    }
}
