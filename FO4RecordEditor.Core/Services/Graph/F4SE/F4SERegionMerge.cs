using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace FO4RecordEditor.Services.Graph.F4SE;

/// <summary>
/// Carries hand-written function bodies across a regeneration.
/// </summary>
/// <remarks>
/// This is what makes the emitter usable more than once. A developer fills in a body, changes the
/// graph, regenerates, and keeps the body. Everything outside a marked region is machine owned and
/// is overwritten without asking.
/// <para>
/// A region whose signature changed keeps its body and gains a banner rather than being dropped.
/// Silently discarding hand-written code because a parameter was added would be the worst possible
/// behaviour here, so the rule is that this class never deletes.
/// </para>
/// </remarks>
public static class F4SERegionMerge
{
    public const string BeginPrefix = "// >>> body: ";
    public const string EndPrefix = "// <<< body: ";

    /// <summary>The banner added when a preserved body no longer matches its regenerated signature.</summary>
    public const string SignatureChangedBanner =
        "\t// SIGNATURE CHANGED: this body was written against a different signature. Review it.";

    private static readonly Regex Marker = new(
        @"^[ \t]*//[ \t]*(?<kind>>>>|<<<)[ \t]*body:[ \t]*(?<name>[^\r\n]*?)[ \t]*$",
        RegexOptions.Multiline | RegexOptions.Compiled);

    /// <summary>One preserved region: the text between its markers, and the line above them.</summary>
    public sealed record Region(string Name, string Body, string SignatureLine);

    /// <summary>Opens a region.</summary>
    public static string Begin(string name) => BeginPrefix + name;

    /// <summary>Closes a region.</summary>
    public static string End(string name) => EndPrefix + name;

    /// <summary>
    /// Reads every marked region out of a previously generated file.
    /// </summary>
    /// <remarks>
    /// The signature line is captured as well so the merge can tell whether the body still belongs
    /// to the function it was written for.
    /// </remarks>
    public static IReadOnlyDictionary<string, Region> Read(string? existing)
    {
        var regions = new Dictionary<string, Region>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(existing)) return regions;

        var normalized = existing.Replace("\r\n", "\n");
        string? openName = null;
        int openEnd = -1, openStart = -1;

        foreach (Match match in Marker.Matches(normalized))
        {
            var name = match.Groups["name"].Value;
            if (match.Groups["kind"].Value == ">>>")
            {
                openName = name;
                openStart = match.Index;
                openEnd = match.Index + match.Length;
                continue;
            }

            // An end marker with no matching begin, or one naming a different region, is a file a
            // human edited badly. Ignore it rather than pair it with the wrong body.
            if (openName == null || !string.Equals(openName, name, StringComparison.Ordinal)) continue;

            var body = normalized[openEnd..match.Index].Trim('\n');
            regions[name] = new Region(name, body, SignatureLineBefore(normalized, openStart));
            openName = null;
        }

        return regions;
    }

    /// <summary>
    /// The declaration a region belongs to: the nearest line above it that is not a lone brace.
    /// </summary>
    /// <remarks>
    /// A body marker sits inside the function, so the line immediately above it is the opening
    /// brace. Taking that would compare "{" against "{" on every regeneration and never notice a
    /// signature change, which is the whole reason this is captured.
    /// </remarks>
    private static string SignatureLineBefore(string text, int markerStart)
    {
        int end = text.LastIndexOf('\n', Math.Max(0, markerStart - 1));

        while (end > 0)
        {
            int start = text.LastIndexOf('\n', end - 1) + 1;
            var line = text[start..end].Trim();

            if (line.Length > 0 && line != "{" && line != "}") return line;
            if (start == 0) return "";
            end = start - 1;
        }

        return "";
    }

    /// <summary>
    /// Rewrites a freshly generated file, putting preserved bodies back into their regions.
    /// </summary>
    /// <param name="generated">The newly emitted text, with empty or stub regions.</param>
    /// <param name="existing">The previous contents of the same file, or null on a first emit.</param>
    public static string Merge(string generated, string? existing)
    {
        var preserved = Read(existing);
        if (preserved.Count == 0) return generated;

        var normalized = generated.Replace("\r\n", "\n");
        var result = new StringBuilder(normalized.Length);
        int cursor = 0;
        string? openName = null;
        int openEnd = -1, openStart = -1;

        foreach (Match match in Marker.Matches(normalized))
        {
            if (match.Groups["kind"].Value == ">>>")
            {
                openName = match.Groups["name"].Value;
                openStart = match.Index;
                openEnd = match.Index + match.Length;
                continue;
            }

            var name = match.Groups["name"].Value;
            if (openName == null || !string.Equals(openName, name, StringComparison.Ordinal)) continue;
            openName = null;

            if (!preserved.TryGetValue(name, out var region)) continue;

            result.Append(normalized, cursor, openEnd - cursor);
            result.Append('\n');

            var signature = SignatureLineBefore(normalized, openStart);
            if (!string.Equals(signature, region.SignatureLine, StringComparison.Ordinal)
                && region.SignatureLine.Length > 0)
            {
                result.Append(SignatureChangedBanner).Append('\n');
            }

            result.Append(region.Body).Append('\n');
            cursor = match.Index;
        }

        result.Append(normalized, cursor, normalized.Length - cursor);
        return result.ToString();
    }

    /// <summary>Region names in a file that the new emit no longer produces.</summary>
    /// <remarks>
    /// A body whose function was deleted from the graph has nowhere to go. The caller is told rather
    /// than left to notice the code vanished.
    /// </remarks>
    public static IReadOnlyList<string> Orphaned(string generated, string? existing)
    {
        var oldNames = Read(existing).Keys;
        var newNames = Read(generated).Keys;
        var orphans = new List<string>();
        foreach (var name in oldNames)
        {
            if (!newNames.Contains(name)) orphans.Add(name);
        }
        return orphans;
    }
}
