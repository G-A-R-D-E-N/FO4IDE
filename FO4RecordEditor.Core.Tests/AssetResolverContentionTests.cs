using System.Text;
using FluentAssertions;
using FO4RecordEditor.Services;

namespace FO4RecordEditor.Core.Tests;

[CollectionDefinition("AssetResolver serial", DisableParallelization = true)]
public sealed class AssetResolverCollectionDefinition
{
}

[Collection("AssetResolver serial")]
public sealed class AssetResolverContentionTests
{
    [Fact]
    public void ResolveText_ReportsAllLooseProviderModsAndAmbiguityCrossPlatform()
    {
        var root = Path.Combine(Path.GetTempPath(), "fo4re-assets-" + Guid.NewGuid().ToString("N"));
        var high = Path.Combine(root, "HighPriorityMod");
        var low = Path.Combine(root, "LowPriorityMod");
        const string relative = @"Meshes\Actors\Test\Shared.nif";
        var highPath = NativePath(high, relative);
        var lowPath = NativePath(low, relative);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(highPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(lowPath)!);
            File.WriteAllBytes(highPath, [1, 2, 3]);
            File.WriteAllBytes(lowPath, [4, 5]);

            AssetResolver.SetSessionDataRoots([high, low]);

            var hits = AssetResolver.ResolveAll(relative);
            var text = AssetResolver.ResolveText(relative, extract: false);

            hits.Should().HaveCount(2);
            hits.Select(hit => hit.Provider).Should().Equal("HighPriorityMod", "LowPriorityMod");
            hits.Select(hit => hit.Kind).Should().Equal("loose", "loose");
            text.Should().Contain("Providers: 2; ambiguous: true.");
            text.Should().Contain("Winning mod: HighPriorityMod.");
            text.Should().Contain("Winner: " + highPath);
            text.Should().Contain("LowPriorityMod: loose LowPriorityMod");
        }
        finally
        {
            Reset(root);
        }
    }

    [Fact]
    public void ResolveAll_CountsEachProvidingModOnce_WhenAModHasLooseAndPackedCopies()
    {
        var root = Path.Combine(Path.GetTempPath(), "fo4re-assets-" + Guid.NewGuid().ToString("N"));
        var high = Path.Combine(root, "HighPriorityMod");
        var low = Path.Combine(root, "LowPriorityMod");
        const string relative = @"Textures\Shared\Conflict.dds";
        var highLoose = NativePath(high, relative);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(highLoose)!);
            File.WriteAllBytes(highLoose, [1, 2, 3]);
            WriteArchive(high, "HighPacked.ba2", (relative, new byte[] { 9, 9 }));
            var lowArchive = WriteArchive(low, "LowPacked.ba2", (relative, new byte[] { 4, 5 }));

            AssetResolver.SetSessionDataRoots([high, low]);

            var hits = AssetResolver.ResolveAll(relative);

            hits.Should().HaveCount(2, "provider count is the number of contending mods, not loose/archive source hits");
            hits[0].Provider.Should().Be("HighPriorityMod");
            hits[0].Kind.Should().Be("loose");
            hits[0].Path.Should().Be(highLoose);
            hits[1].Provider.Should().Be("LowPriorityMod");
            hits[1].Kind.Should().Be("archive");
            hits[1].Path.Should().Be(lowArchive);
        }
        finally
        {
            Reset(root);
        }
    }

    [Fact]
    public void ResolveAll_UsesDeterministicArchiveRepresentative_WhenOneModPacksTheAssetTwice()
    {
        var root = Path.Combine(Path.GetTempPath(), "fo4re-assets-" + Guid.NewGuid().ToString("N"));
        var mod = Path.Combine(root, "PackedMod");
        const string relative = @"Meshes\Shared\Packed.nif";

        try
        {
            WriteArchive(mod, "Z_Last.ba2", (relative, new byte[] { 9 }));
            var expected = WriteArchive(mod, "A_First.ba2", (relative, new byte[] { 1 }));

            AssetResolver.SetSessionDataRoots([mod]);

            var hits = AssetResolver.ResolveAll(relative);

            hits.Should().ContainSingle();
            hits[0].Provider.Should().Be("PackedMod");
            hits[0].Kind.Should().Be("archive");
            hits[0].Path.Should().Be(expected);
        }
        finally
        {
            Reset(root);
        }
    }

    private static string NativePath(string root, string relative) =>
        Path.Combine(root, relative.Replace('\\', Path.DirectorySeparatorChar));

    private static string WriteArchive(string root, string archiveName, params (string path, byte[] content)[] entries)
    {
        Directory.CreateDirectory(root);
        var archivePath = Path.Combine(root, archiveName);
        using var stream = File.Create(archivePath);
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: false);
        const int headerSize = 24;
        const int entrySize = 36;
        uint dataStart = headerSize + (uint)(entrySize * entries.Length);
        var offsets = new uint[entries.Length];
        var cursor = dataStart;
        for (var i = 0; i < entries.Length; i++)
        {
            offsets[i] = cursor;
            cursor += (uint)entries[i].content.Length;
        }

        writer.Write(Encoding.ASCII.GetBytes("BTDX"));
        writer.Write((uint)1);
        writer.Write(Encoding.ASCII.GetBytes("GNRL"));
        writer.Write((uint)entries.Length);
        writer.Write((ulong)cursor);

        for (var i = 0; i < entries.Length; i++)
        {
            writer.Write((uint)0);
            writer.Write(Encoding.ASCII.GetBytes("bin\0"));
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
            writer.Write((short)pathBytes.Length);
            writer.Write(pathBytes);
        }

        return archivePath;
    }

    private static void Reset(string root)
    {
        AssetResolver.SetSessionDataRoots(Array.Empty<string>());
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}
