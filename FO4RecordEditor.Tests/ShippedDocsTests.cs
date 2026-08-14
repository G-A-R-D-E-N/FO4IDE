using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using FO4RecordEditor.Services;
using Xunit;
using Xunit.Abstractions;

namespace FO4RecordEditor.Tests;

// The public docs drifted to 53 tools against 57 in source, and four tools (create_cell,
// create_placed_object, set_furniture_markers, set_message_buttons) were undocumented entirely.
// These are the GPL-3.0 release docs, so a wrong count ships to other people.
public class ShippedDocsTests
{
    private readonly ITestOutputHelper _out;
    public ShippedDocsTests(ITestOutputHelper o) => _out = o;

    // Walk up from the test assembly to the repo root (the folder holding README.md next to the
    // FO4RecordEditor project). Anchors on the test dll rather than AppContext.BaseDirectory,
    // which points at the runner's own dir (e.g. xunit.console) rather than the test output.
    // Fails loudly rather than silently passing on a layout change.
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(typeof(ShippedDocsTests).Assembly.Location);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "README.md")) &&
                Directory.Exists(Path.Combine(dir.FullName, "FO4RecordEditor")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException(
            $"Could not locate the repo root above {AppContext.BaseDirectory}.");
    }

    private static string[] ToolNames() => PluginToolExecutor.ToolDefinitions()
        .Select(t => (string)t.GetType().GetProperty("name")!.GetValue(t)!)
        .ToArray();

    [Theory]
    [InlineData("README.md")]
    [InlineData("docs/MCP_SETUP.md")]
    public void StatedToolCountMatchesTheRegisteredTools(string relative)
    {
        var path = Path.Combine(RepoRoot(), relative.Replace('/', Path.DirectorySeparatorChar));
        File.Exists(path).Should().BeTrue($"{relative} should exist at {path}");

        var text = File.ReadAllText(path);
        var expected = ToolNames().Length;

        // "53 tools", "**53 tools**", "the same 53 operations"
        // {2,3}, not {2}: with a three-digit count the two-digit form matched the LAST two digits
        // ("105 tools" captured "05") and then compared 5 against 105, so this guard silently
        // stopped guarding anything the moment the tool count passed 99.
        var counts = Regex.Matches(text, @"\*{0,2}(\d{2,3})\*{0,2}\s+(?:tools|operations)\b")
            .Select(m => int.Parse(m.Groups[1].Value))
            .Distinct()
            .ToList();

        counts.Should().NotBeEmpty($"{relative} should state a tool count so this test can guard it");

        foreach (var c in counts) _out.WriteLine($"{relative} states {c}; source has {expected}");
        counts.Should().AllSatisfy(c => c.Should().Be(expected),
            $"{relative} states a tool count that no longer matches the registered tools");
    }

    // MCP_SETUP.md is the public tool reference -- a tool missing from it is undiscoverable.
    [Fact]
    public void McpSetupDocumentsEveryTool()
    {
        var path = Path.Combine(RepoRoot(), "docs", "MCP_SETUP.md");
        var text = File.ReadAllText(path);

        var missing = ToolNames().Where(n => !text.Contains(n, StringComparison.Ordinal)).ToList();
        if (missing.Count > 0)
            _out.WriteLine("Not documented in docs/MCP_SETUP.md:\n  " + string.Join("\n  ", missing));

        missing.Should().BeEmpty("every tool must appear in the public MCP tool reference");
    }

    // 'ping' is a JSON-RPC method the client handles, not a tool -- telling users to ask the AI to
    // call it sends them chasing a failure.
    [Fact]
    public void McpSetupDoesNotPresentPingAsATool()
    {
        var path = Path.Combine(RepoRoot(), "docs", "MCP_SETUP.md");
        var text = File.ReadAllText(path);

        text.Should().NotContain("call `ping`",
            "ping is a JSON-RPC method, not a tool; point users at list_plugins instead");
    }
}
