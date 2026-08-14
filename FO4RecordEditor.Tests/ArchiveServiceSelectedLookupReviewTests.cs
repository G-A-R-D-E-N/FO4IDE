using System.IO;
using System.Text;
using FluentAssertions;
using FO4RecordEditor.Services;
using Xunit;

namespace FO4RecordEditor.Tests;

public sealed class ArchiveServiceSelectedLookupReviewTests
{
    [Theory]
    [InlineData(@"\Meshes\A.txt")]
    [InlineData("/Meshes/A.txt")]
    public void ExtractSelected_AcceptsLeadingSeparatorLookup(string selectedPath)
    {
        var content = Encoding.ASCII.GetBytes("payload");
        var archive = BuildTestBa2((@"Meshes\A.txt", content));
        var outDir = Path.Combine(
            Path.GetTempPath(),
            $"ArchiveExtractSelectedLeading_{Guid.NewGuid():N}");

        try
        {
            ArchiveService.ExtractSelected(archive, new[] { selectedPath }, outDir)
                .Should().Contain("Extracted 1 of 1 selected");

            File.ReadAllBytes(Path.Combine(outDir, "Meshes", "A.txt"))
                .Should().Equal(content);
        }
        finally
        {
            try { File.Delete(archive); } catch { }
            try { Directory.Delete(outDir, recursive: true); } catch { }
        }
    }

    private static string BuildTestBa2(params (string path, byte[] content)[] entries)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true))
        {
            const int headerSize = 24;
            const int entrySize = 36;
            uint dataStart = headerSize + checked((uint)(entrySize * entries.Length));
            var offsets = new uint[entries.Length];
            var cursor = dataStart;
            for (var i = 0; i < entries.Length; i++)
            {
                offsets[i] = cursor;
                cursor += checked((uint)entries[i].content.Length);
            }

            writer.Write(Encoding.ASCII.GetBytes("BTDX"));
            writer.Write((uint)1);
            writer.Write(Encoding.ASCII.GetBytes("GNRL"));
            writer.Write((uint)entries.Length);
            writer.Write((ulong)cursor);

            for (var i = 0; i < entries.Length; i++)
            {
                writer.Write((uint)0);
                writer.Write(Encoding.ASCII.GetBytes("txt\0"));
                writer.Write((uint)0);
                writer.Write((uint)0);
                writer.Write((ulong)offsets[i]);
                writer.Write((uint)0);
                writer.Write((uint)entries[i].content.Length);
                writer.Write((uint)0);
            }

            foreach (var (_, content) in entries) writer.Write(content);
            foreach (var (path, _) in entries)
            {
                var pathBytes = Encoding.UTF8.GetBytes(path);
                writer.Write(checked((short)pathBytes.Length));
                writer.Write(pathBytes);
            }
        }

        var archivePath = Path.Combine(
            Path.GetTempPath(),
            $"ArchiveSelectedLookup_{Guid.NewGuid():N}.ba2");
        File.WriteAllBytes(archivePath, stream.ToArray());
        return archivePath;
    }
}
