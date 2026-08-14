using FluentAssertions;
using FO4RecordEditor.Services;

namespace FO4RecordEditor.Tests;

public class SettingsMigrationTests
{
    [Theory]
    [InlineData("claude-3-7-sonnet-20250219", "claude-sonnet-4-6")]
    [InlineData("claude-3-opus-20240229", "claude-opus-4-8")]
    [InlineData("claude-3-5-haiku-20241022", "claude-haiku-4-5")]
    public void RetiredModelsAreMigrated(string saved, string expected)
        => SettingsService.MigrateModel(saved).Should().Be(expected);

    [Theory]
    [InlineData("claude-opus-4-8")]
    [InlineData("claude-sonnet-4-6")]
    [InlineData("opus")]
    [InlineData("")]
    public void CurrentOrUnknownModelsAreLeftAlone(string model)
        => SettingsService.MigrateModel(model).Should().Be(model);
}
