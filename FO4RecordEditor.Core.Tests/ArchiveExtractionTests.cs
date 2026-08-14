using System.Text;
using FluentAssertions;
using FO4RecordEditor.Services.Archives;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Archives;
using Noggog;

namespace FO4RecordEditor.Core.Tests;




public sealed class ArchiveExtractionTests : IDisposable
{
    private readonly string _temp = Path.Combine(
        Path.GetTempPath(),
        "fo4re-archive-extraction-" + Guid.NewGuid().ToString("N"));

    public ArchiveExtractionTests() => Directory.CreateDirectory(_temp);

    public void Dispose()
    {
        try { Directory.Delete(_temp, recursive: true); }
        catch { }
    }

    [Fact]
    public void Plan_UsesNativeDirectories_AndMergesCaseOnlyDirectoryVariants()
    {
        var entries = Entries(
            (@"Scripts\Hardcore\Quest.pex", Encoding.ASCII.GetBytes("pex")),
            (@"scripts\Source\Quest.psc", Encoding.ASCII.GetBytes("psc")));
        var output = Path.Combine(_temp, "out");

        ArchiveExtraction.TryCreatePlan(entries, output, out var root, out var plan, out var error)
            .Should().BeTrue(error);

        root.Should().Be(Path.GetFullPath(output));
        plan.Select(item => Path.GetRelativePath(root, item.DestinationPath)).Should().Equal(
            Path.Combine("scripts", "Hardcore", "Quest.pex"),
            Path.Combine("scripts", "Source", "Quest.psc"));
        Directory.Exists(output).Should().BeFalse("planning must not touch disk");
    }

    [Theory]
    [InlineData(@"..\escape.txt")]
    [InlineData(@"safe\..\escape.txt")]
    [InlineData(@"/absolute.txt")]
    [InlineData(@"\\server\share.txt")]
    [InlineData(@"C:\escape.txt")]
    [InlineData(@"safe\bad:name.txt")]
    [InlineData(@"safe\NUL.txt")]
    [InlineData("safe\\alias. ")]
    [InlineData(@"safe\\double.txt")]
    public void Plan_RejectsUnsafeOrAmbiguousArchivePathsBeforeWriting(string entryPath)
    {
        var entries = Entries((entryPath, Encoding.ASCII.GetBytes("payload")));
        var output = Path.Combine(_temp, "out");

        ArchiveExtraction.TryCreatePlan(entries, output, out _, out var plan, out var error)
            .Should().BeFalse();

        plan.Should().BeEmpty();
        error.Should().Contain("Unsafe archive entry");
        Directory.Exists(output).Should().BeFalse("an unsafe batch must fail before creating output");
        File.Exists(Path.Combine(_temp, "escape.txt")).Should().BeFalse();
    }

    [Fact]
    public void Plan_RejectsCaseInsensitiveFileCollisionsBeforeWriting()
    {
        var entries = Entries(
            (@"Meshes\Weapon.nif", Encoding.ASCII.GetBytes("first")),
            (@"meshes\WEAPON.NIF", Encoding.ASCII.GetBytes("second")));
        var output = Path.Combine(_temp, "out");

        ArchiveExtraction.TryCreatePlan(entries, output, out _, out var plan, out var error)
            .Should().BeFalse();

        plan.Should().BeEmpty();
        error.Should().Contain("same case-insensitive destination");
        Directory.Exists(output).Should().BeFalse();
    }

    [Fact]
    public void Plan_RejectsAPathThatIsBothAFileAndDirectory()
    {
        var entries = Entries(
            (@"Meshes\Conflict", Encoding.ASCII.GetBytes("file")),
            (@"Meshes\Conflict\Child.nif", Encoding.ASCII.GetBytes("child")));
        var output = Path.Combine(_temp, "out");

        ArchiveExtraction.TryCreatePlan(entries, output, out _, out _, out var error)
            .Should().BeFalse();

        error.Should().Contain("both a file and a parent directory");
        Directory.Exists(output).Should().BeFalse();
    }

    [Fact]
    public void Plan_ReusesExistingDirectoryCasing_InsteadOfCreatingALinuxSibling()
    {
        var output = Path.Combine(_temp, "out");
        Directory.CreateDirectory(Path.Combine(output, "Scripts"));
        var entries = Entries((@"scripts\Quest.pex", Encoding.ASCII.GetBytes("pex")));

        ArchiveExtraction.TryCreatePlan(entries, output, out var root, out var plan, out var error)
            .Should().BeTrue(error);

        Path.GetRelativePath(root, plan.Single().DestinationPath)
            .Should().Be(Path.Combine("Scripts", "Quest.pex"));
    }

    [Fact]
    public void PlannedWrite_CreatesTheRealTree_AndLeavesNoTemporaryFiles()
    {
        var payload = Encoding.ASCII.GetBytes("exact payload");
        var entries = Entries((@"Meshes\Sub\Thing.nif", payload));
        var output = Path.Combine(_temp, "out");
        ArchiveExtraction.TryCreatePlan(entries, output, out var root, out var plan, out var planError)
            .Should().BeTrue(planError);

        ArchiveExtraction.TryWritePlannedEntry(plan.Single(), root, out var writeError)
            .Should().BeTrue(writeError);

        var extracted = Path.Combine(output, "Meshes", "Sub", "Thing.nif");
        File.ReadAllBytes(extracted).Should().Equal(payload);
        Directory.GetFiles(output, "*.tmp", SearchOption.AllDirectories).Should().BeEmpty();
    }

    [Fact]
    public void PlannedWrite_ReadFailureDoesNotTruncateAnExistingDestination()
    {
        var output = Path.Combine(_temp, "out");
        var destination = Path.Combine(output, "Meshes", "Thing.nif");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.WriteAllText(destination, "original bytes must survive");
        var item = new ArchiveExtractionPlanItem(
            new ThrowingArchiveFile(@"Meshes\Thing.nif"),
            destination);

        ArchiveExtraction.TryWritePlannedEntry(item, output, out var error)
            .Should().BeFalse();

        error.Should().Contain("simulated corrupt archive entry");
        File.ReadAllText(destination).Should().Be("original bytes must survive");
        Directory.GetFiles(output, "*.tmp", SearchOption.AllDirectories).Should().BeEmpty();
    }

    [Fact]
    public void ExplicitWrite_ReplacesAnExistingFileThroughATemporaryFile()
    {
        var output = Path.Combine(_temp, "single", "Thing.nif");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, "old data");
        var replacement = Encoding.ASCII.GetBytes("new exact data");

        ArchiveExtraction.TryWriteExplicitFile(output, replacement, out var error)
            .Should().BeTrue(error);

        File.ReadAllBytes(output).Should().Equal(replacement);
        Directory.GetFiles(Path.GetDirectoryName(output)!, "*.tmp").Should().BeEmpty();
    }

    [Fact]
    public void Plan_CachesEachExistingParentDirectoryListing()
    {
        var output = Path.Combine(_temp, "cached-out");
        Directory.CreateDirectory(Path.Combine(output, "Scripts"));
        File.WriteAllText(Path.Combine(output, "Scripts", "Existing.pex"), "existing");
        var entries = Entries(Enumerable.Range(0, 100)
            .Select(index => ($@"scripts\Generated_{index:D3}.pex", Encoding.ASCII.GetBytes($"payload-{index}")))
            .ToArray());
        var enumerated = new List<string>();

        ArchiveExtraction.TryCreatePlan(
                entries,
                output,
                out _,
                out var plan,
                out var error,
                parent => enumerated.Add(parent))
            .Should().BeTrue(error);

        plan.Should().HaveCount(100);
        enumerated.Should().HaveCount(2,
            "the existing output root and Scripts directory should each be snapshotted once");
        enumerated.Distinct(FileSystemPathComparerForTest()).Should().HaveCount(2);
    }

    [Fact]
    public void PlannedWrite_LongValidDestinationName_UsesAShortTemporaryComponent_OnLinux()
    {
        if (!OperatingSystem.IsLinux()) return;

        var fileName = new string('a', 230) + ".nif";
        var payload = Encoding.ASCII.GetBytes("long-name payload");
        var entries = Entries(($@"Meshes\{fileName}", payload));
        var output = Path.Combine(_temp, "long-planned");
        ArchiveExtraction.TryCreatePlan(entries, output, out var root, out var plan, out var planError)
            .Should().BeTrue(planError);

        ArchiveExtraction.TryWritePlannedEntry(plan.Single(), root, out var writeError)
            .Should().BeTrue(writeError);

        File.ReadAllBytes(Path.Combine(output, "Meshes", fileName)).Should().Equal(payload);
        Directory.GetFiles(Path.Combine(output, "Meshes"), "*.tmp").Should().BeEmpty();
    }

    [Fact]
    public void ExplicitWrite_LongValidDestinationName_UsesAShortTemporaryComponent_OnLinux()
    {
        if (!OperatingSystem.IsLinux()) return;

        var directory = Path.Combine(_temp, "long-explicit");
        var fileName = new string('b', 230) + ".nif";
        var destination = Path.Combine(directory, fileName);
        var payload = Encoding.ASCII.GetBytes("explicit long-name payload");

        ArchiveExtraction.TryWriteExplicitFile(destination, payload, out var error)
            .Should().BeTrue(error);

        File.ReadAllBytes(destination).Should().Equal(payload);
        Directory.GetFiles(directory, "*.tmp").Should().BeEmpty();
    }

    private static StringComparer FileSystemPathComparerForTest() =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    [Fact]
    public void Plan_RejectsAnExistingSymlinkInsideTheExtractionRoot_OnLinux()
    {
        if (!OperatingSystem.IsLinux()) return;

        var output = Path.Combine(_temp, "out");
        var outside = Path.Combine(_temp, "outside");
        Directory.CreateDirectory(output);
        Directory.CreateDirectory(outside);
        Directory.CreateSymbolicLink(Path.Combine(output, "Meshes"), outside);
        var entries = Entries((@"Meshes\Escape.nif", Encoding.ASCII.GetBytes("payload")));

        ArchiveExtraction.TryCreatePlan(entries, output, out _, out _, out var error)
            .Should().BeFalse();

        error.Should().Contain("symbolic link or reparse point");
        File.Exists(Path.Combine(outside, "Escape.nif")).Should().BeFalse();
    }

    [Fact]
    public void Plan_RejectsANewRootBelowAnExistingSymlinkedAncestor_OnLinux()
    {
        if (!OperatingSystem.IsLinux()) return;

        var outside = Path.Combine(_temp, "outside");
        var linkedParent = Path.Combine(_temp, "linked-parent");
        Directory.CreateDirectory(outside);
        Directory.CreateSymbolicLink(linkedParent, outside);
        var output = Path.Combine(linkedParent, "new-root");
        var entries = Entries((@"Meshes\Escape.nif", Encoding.ASCII.GetBytes("payload")));

        ArchiveExtraction.TryCreatePlan(entries, output, out _, out var plan, out var error)
            .Should().BeFalse();

        plan.Should().BeEmpty();
        error.Should().Contain("symbolic link or reparse point");
        Directory.Exists(Path.Combine(outside, "new-root")).Should().BeFalse(
            "planning must not follow a symlinked ancestor or create the redirected root");
    }

    [Fact]
    public void ExplicitWrite_RejectsASymlinkedAncestor_OnLinux()
    {
        if (!OperatingSystem.IsLinux()) return;

        var outside = Path.Combine(_temp, "explicit-outside");
        var linkedParent = Path.Combine(_temp, "explicit-link");
        Directory.CreateDirectory(outside);
        Directory.CreateSymbolicLink(linkedParent, outside);
        var destination = Path.Combine(linkedParent, "nested", "Thing.nif");

        ArchiveExtraction.TryWriteExplicitFile(
                destination,
                Encoding.ASCII.GetBytes("payload"),
                out var error)
            .Should().BeFalse();

        error.Should().Contain("symbolic link or reparse point");
        File.Exists(Path.Combine(outside, "nested", "Thing.nif")).Should().BeFalse();
    }

    private sealed class ThrowingArchiveFile(string path) : IArchiveFile
    {
        public string Path { get; } = path;
        public uint Size => 123;
        public byte[] GetBytes() => throw new InvalidDataException("simulated corrupt archive entry");
        public ReadOnlySpan<byte> GetSpan() => throw new InvalidDataException("simulated corrupt archive entry");
        public ReadOnlyMemorySlice<byte> GetMemorySlice() => throw new InvalidDataException("simulated corrupt archive entry");
        public Stream AsStream() => throw new InvalidDataException("simulated corrupt archive entry");
    }

    private IReadOnlyList<IArchiveFile> Entries(params (string path, byte[] content)[] entries)
    {
        var archivePath = Path.Combine(_temp, $"Archive_{Guid.NewGuid():N}.ba2");
        using (var stream = File.Create(archivePath))
        using (var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: false))
        {
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
                pathBytes.Length.Should().BeLessThan(short.MaxValue);
                writer.Write((short)pathBytes.Length);
                writer.Write(pathBytes);
            }
        }

        return Archive.CreateReader(GameRelease.Fallout4, archivePath).Files.ToList();
    }
}
