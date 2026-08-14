using System.Diagnostics.CodeAnalysis;

namespace FO4RecordEditor.Services;

public readonly record struct ToolResult(string Text, bool IsError)
{
    public static ToolResult Ok(string text) => new(text, false);
    public static ToolResult Fail(string text) => new(text, true);
}

public static class ToolError
{

    private const char Sentinel = '\u0091';

    public static string Fail(string message) =>
        message.Length > 0 && message[0] == Sentinel ? message : Sentinel + message;

    public static bool IsMarked([NotNullWhen(true)] string? text) =>
        text is { Length: > 0 } && text[0] == Sentinel;

    public static ToolResult Unwrap(string text) =>
        IsMarked(text) ? ToolResult.Fail(text[1..]) : new ToolResult(text, LooksLikeLegacyFailure(text));

    private static readonly string[] FailureOpeners =
    {
        "Could not ", "Failed to ", "Cannot ", "Can't ", "Unable to ",
        "Invalid ", "Unknown tool:", "Tool error:", "Error:", "Unsupported ",
        "No environment loaded",
    };

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
