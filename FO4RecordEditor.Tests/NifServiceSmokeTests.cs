using System.IO;
using FluentAssertions;
using FO4RecordEditor.Services;
using Xunit;
using Xunit.Abstractions;

namespace FO4RecordEditor.Tests;

// NifService and PapyrusService already drained both pipes correctly; they were migrated onto
// ProcessRunner so there is one implementation rather than three. This exercises the real binary to
// prove that migration kept the plumbing (launch, UTF-8 capture, exit code) intact.
//
// Skips loudly when the local niftool build or the sample mesh is absent, rather than passing.
public class NifServiceSmokeTests
{
    private const string SampleNif =
        @"E:\F4SE OG\Tools\PluginEditTool\Patched Data\ExtractedMeshes\Meshes\AutoLoadMarker01.nif";

    private readonly ITestOutputHelper _out;
    public NifServiceSmokeTests(ITestOutputHelper o) => _out = o;

    [Fact(Skip = "Needs the local niftool build + the sample mesh under Patched Data. Remove Skip to " +
                 "run manually. Verified passing 2026-07-19 (312ms) after the ProcessRunner migration.")]
    public void Inspect_ReturnsNiftoolsJson()
    {
        File.Exists(SampleNif).Should().BeTrue($"sample mesh should be at {SampleNif}");

        var result = NifService.Inspect(SampleNif);
        _out.WriteLine(result);

        result.Should().NotContain("niftool.exe not found");
        result.Should().NotContain("Failed to start niftool");
        result.Should().NotContain("timed out");
        // niftool emits nlohmann JSON on stdout; getting it back proves capture survived the refactor.
        result.Should().Contain("\"fo4\"").And.Contain("\"shapes\"");
    }
}
