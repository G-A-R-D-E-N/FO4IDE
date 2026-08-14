using System.Diagnostics.CodeAnalysis;

namespace FO4RecordEditor.Services;

/// <summary>
/// Outcome of one MCP tool call: the text the agent sees plus whether it represents a failure.
/// The transports (<see cref="StdioMcpServer"/>, <see cref="PluginMcpServer"/>) use
/// <see cref="IsError"/> to populate the JSON-RPC <c>isError</c> field.
/// </summary>
public readonly record struct ToolResult(string Text, bool IsError)
{
    public static ToolResult Ok(string text) => new(text, false);
    public static ToolResult Fail(string text) => new(text, true);
}

/// <summary>
/// The error-marking contract between tool bodies and the MCP transports.
///
/// A tool body signals failure by returning a string built with <see cref="Fail"/>, which stamps
/// an invisible sentinel on the front. <see cref="Unwrap"/> strips it and reports the flag.
///
/// A sentinel is used rather than pattern-matching the message because failure and empty-success
/// are not distinguishable by wording here: "No conflicts found", "No problems found for X" and
/// "Nothing references X" are all successful reports about a clean plugin, while "No environment
/// loaded" is a hard failure. Any heuristic over that vocabulary marks clean plugins as broken.
/// </summary>
public static class ToolError
{
    // U+0091 PRIVATE USE ONE. Not producible from record data, so it cannot collide with a
    // legitimate result; stripped before the text reaches the agent.
    private const char Sentinel = '\u0091';

    /// <summary>Marks <paramref name="message"/> as a failure. Return this from a tool body.</summary>
    public static string Fail(string message) =>
        message.Length > 0 && message[0] == Sentinel ? message : Sentinel + message;

    /// <summary>True if <paramref name="text"/> was marked by <see cref="Fail"/>.</summary>
    public static bool IsMarked([NotNullWhen(true)] string? text) =>
        text is { Length: > 0 } && text[0] == Sentinel;

    /// <summary>Splits a possibly-marked string into its clean text and error flag.</summary>
    public static ToolResult Unwrap(string text) =>
        IsMarked(text) ? ToolResult.Fail(text[1..]) : new ToolResult(text, LooksLikeLegacyFailure(text));

    // Openers that unambiguously begin a failure report. Backstop only, for tool bodies not yet
    // migrated to Fail(); the sentinel is authoritative and always wins.
    //
    // Deliberately disjoint from the empty-success vocabulary -- "No conflicts found", "No problems
    // found for X", "Nothing references X" and "No records of type X" are successful reports about a
    // clean or empty result and must NOT appear here. Never add a bare "No " prefix.
    private static readonly string[] FailureOpeners =
    {
        "Could not ", "Failed to ", "Cannot ", "Can't ", "Unable to ",
        "Invalid ", "Unknown tool:", "Tool error:", "Error:", "Unsupported ",
        "No environment loaded",
    };

    /// <summary>
    /// Heuristic fallback for un-migrated tool bodies. Anchored to the first line so a phrase
    /// occurring inside a record dump cannot flip a successful read to an error.
    /// </summary>
    private static bool LooksLikeLegacyFailure(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        int nl = text.IndexOf('\n');
        var first = (nl < 0 ? text : text[..nl]).TrimStart();
        foreach (var opener in FailureOpeners)
            if (first.StartsWith(opener, StringComparison.Ordinal)) return true;
        return false;
    }
}
