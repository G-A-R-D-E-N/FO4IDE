using System.Text.Json;
using FluentAssertions;
using FO4RecordEditor.Services;
using Xunit;

namespace FO4RecordEditor.Tests;

public class OverrideProtectionAuditTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CopyAsOverrideMany_DuplicateFormKeysAreRejectedBeforeCreatingThePatch(bool overwrite)
    {
        var source = $"DuplicateBatchSource_{Guid.NewGuid():N}.esp";
        var patch = $"DuplicateBatchPatch_{Guid.NewGuid():N}.esp";
        WriteService.CreatePlugin(source).Should().Contain("Created");
        WriteService.CreateRecord(source, "BOOK", "DuplicateBatchBook", env: null).Should().Contain("Created");

        var formKey = WriteService.GetMutable(source)!.Books.Single().FormKey.ToString();
        var items = JsonSerializer.Serialize(new[]
        {
            new { formKey, source },
            new { formKey, source },
        });

        var json = WriteService.CopyAsOverrideMany(env: null, items, patch, overwrite);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("total").GetInt32().Should().Be(2);
        root.GetProperty("ok").GetInt32().Should().Be(0);
        root.GetProperty("failed").GetInt32().Should().Be(2);
        root.GetProperty("requiresOverwrite").GetBoolean().Should().BeFalse();
        root.GetProperty("failures").GetArrayLength().Should().Be(1);
        root.GetProperty("failures")[0].GetProperty("formKey").GetString().Should().Be(formKey);
        root.GetProperty("failures")[0].GetProperty("reason").GetString().Should().Contain("more than once");

        WriteService.GetMutable(patch).Should().BeNull(
            "duplicate destinations must be rejected before the first source record creates or mutates a patch");
    }
}
