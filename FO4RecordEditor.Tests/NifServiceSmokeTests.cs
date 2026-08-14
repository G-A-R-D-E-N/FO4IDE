using System.IO;
using FluentAssertions;
using FO4RecordEditor.Services;
using Xunit;
using Xunit.Abstractions;

namespace FO4RecordEditor.Tests;






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

        result.Should().Contain("\"fo4\"").And.Contain("\"shapes\"");
    }
}
