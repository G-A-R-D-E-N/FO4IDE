using System.Text;
using FluentAssertions;
using FO4RecordEditor.Services.Archives;
using Mutagen.Bethesda.Archives;
using Noggog;

namespace FO4RecordEditor.Core.Tests;

public sealed class ArchiveExtractionReviewTests : IDisposable
{
    private readonly string _temp = Path.Combine(
        Path.GetTempPath(),
        "fo4re-archive-review-" + Guid.NewGuid().ToString("N"));

    public ArchiveExtractionReviewTests() => Directory.CreateDirectory(_temp);

    public void Dispose()
    {
        try { Directory.Delete(_temp, recursive: true); }
        catch { }
    }

    [Theory]
    [InlineData(@"safe\CONIN$.txt")]
    [InlineData(@"safe\CONOUT$")]
    [InlineData(@"safe\conin$.pex")]
    [InlineData(@"safe\COM¹.txt")]
    [InlineData(@"safe\com²")]
    [InlineData(@"safe\LPT³.nif")]
    public void Plan_RejectsWindowsDeviceNames(string archivePath)
    {
        var output = Path.Combine(_temp, "reserved");
        var entries = new IArchiveFile[] { new StubArchiveFile(archivePath, "payload"u8.ToArray()) };

        ArchiveExtraction.TryCreatePlan(entries, output, out _, out var plan, out var error)
            .Should().BeFalse();

        plan.Should().BeEmpty();
        error.Should().Contain("reserved Windows device name");
        Directory.Exists(output).Should().BeFalse("planning an unsafe batch must not touch disk");
    }

    [Fact]
    public void Plan_RejectsAnOverlongComponentBeforeWritingAnyEarlierEntries()
    {
        var output = Path.Combine(_temp, "overlong");
        var overlong = new string('a', 256) + ".nif";
        var entries = new IArchiveFile[]
        {
            new StubArchiveFile(@"Meshes\Valid.nif", "valid"u8.ToArray()),
            new StubArchiveFile(@"Meshes\" + overlong, "invalid"u8.ToArray()),
        };

        ArchiveExtraction.TryCreatePlan(entries, output, out _, out var plan, out var error)
            .Should().BeFalse();

        plan.Should().BeEmpty();
        error.Should().Contain("255-unit filename limit");
        Directory.Exists(output).Should().BeFalse("the complete batch must fail during planning");
    }

    [Fact]
    public void Plan_RejectsAComponentWhoseUtf8EncodingExceedsThePortableLimit()
    {
        var output = Path.Combine(_temp, "overlong-utf8");
        var overlong = new string('é', 128);
        var entries = new IArchiveFile[]
        {
            new StubArchiveFile(overlong, "invalid"u8.ToArray()),
        };

        ArchiveExtraction.TryCreatePlan(entries, output, out _, out var plan, out var error)
            .Should().BeFalse();

        plan.Should().BeEmpty();
        error.Should().Contain("255-unit filename limit");
        Directory.Exists(output).Should().BeFalse();
    }

    [Theory]
    [InlineData(@"\Meshes\A.nif", @"Meshes\A.nif")]
    [InlineData("/Meshes/A.nif", @"Meshes\A.nif")]
    [InlineData(@"Meshes\A.nif", @"Meshes\A.nif")]
    public void NormalizeLookupPath_AcceptsCallerLeadingSeparators(string input, string expected)
    {
        ArchiveExtraction.NormalizeLookupPath(input).Should().Be(expected);
    }

    [Fact]
    public void Plan_RejectsAPathThatResolvesOutsideTheDirectorySnapshot()
    {
        var output = Path.Combine(_temp, "alias");
        Directory.CreateDirectory(output);
        var unrelated = Path.Combine(output, "LongFileName.nif");
        File.WriteAllText(unrelated, "must survive");
        var entries = new IArchiveFile[]
        {
            new StubArchiveFile(@"LONGFI~1.NIF", Encoding.ASCII.GetBytes("replacement")),
        };

        ArchiveExtraction.TryCreatePlan(
                entries,
                output,
                out _,
                out var plan,
                out var error,
                directoryEnumerated: null,
                directCandidateExists: candidate =>
                    string.Equals(
                        Path.GetFileName(candidate),
                        "LONGFI~1.NIF",
                        StringComparison.OrdinalIgnoreCase))
            .Should().BeFalse();

        plan.Should().BeEmpty();
        error.Should().Contain("8.3 short-name alias");
        File.ReadAllText(unrelated).Should().Be("must survive");
    }

    [Fact]
    public void PlannedWrite_PreservesExistingUnixPermissions()
    {
        if (!OperatingSystem.IsLinux()) return;

        var output = Path.Combine(_temp, "planned-mode");
        var destination = Path.Combine(output, "Meshes", "Thing.nif");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.WriteAllText(destination, "old");
        var expectedMode = UnixFileMode.UserRead |
                           UnixFileMode.UserWrite |
                           UnixFileMode.UserExecute |
                           UnixFileMode.GroupRead;
        File.SetUnixFileMode(destination, expectedMode);
        var replacement = Encoding.ASCII.GetBytes("new payload");
        var entries = new IArchiveFile[]
        {
            new StubArchiveFile(@"Meshes\Thing.nif", replacement),
        };

        ArchiveExtraction.TryCreatePlan(entries, output, out var root, out var plan, out var planError)
            .Should().BeTrue(planError);
        ArchiveExtraction.TryWritePlannedEntry(plan.Single(), root, out var writeError)
            .Should().BeTrue(writeError);

        File.ReadAllBytes(destination).Should().Equal(replacement);
        File.GetUnixFileMode(destination).Should().Be(expectedMode);
    }

    [Fact]
    public void ExplicitWrite_PreservesExistingUnixPermissions()
    {
        if (!OperatingSystem.IsLinux()) return;

        var destination = Path.Combine(_temp, "explicit-mode", "Thing.nif");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.WriteAllText(destination, "old");
        var expectedMode = UnixFileMode.UserRead |
                           UnixFileMode.UserWrite |
                           UnixFileMode.GroupRead;
        File.SetUnixFileMode(destination, expectedMode);
        var replacement = Encoding.ASCII.GetBytes("replacement");

        ArchiveExtraction.TryWriteExplicitFile(destination, replacement, out var error)
            .Should().BeTrue(error);

        File.ReadAllBytes(destination).Should().Equal(replacement);
        File.GetUnixFileMode(destination).Should().Be(expectedMode);
    }

    private sealed class StubArchiveFile(string path, byte[] bytes) : IArchiveFile
    {
        public string Path { get; } = path;
        public uint Size => checked((uint)bytes.Length);
        public byte[] GetBytes() => bytes.ToArray();
        public ReadOnlySpan<byte> GetSpan() => bytes;
        public ReadOnlyMemorySlice<byte> GetMemorySlice() =>
            throw new NotSupportedException("The extraction writer uses GetBytes for this test fixture.");
        public Stream AsStream() => new MemoryStream(bytes, writable: false);
    }
}
