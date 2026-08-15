using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace FO4RecordEditor.Services.Graph.F4SE;














public static class F4SERegionMerge
{
    public const string BeginPrefix = "#pragma region FO4IDE_BODY ";
    public const string EndPrefix = "#pragma endregion FO4IDE_BODY ";


    public const string SignatureChangedBanner =
        "\t#pragma message(\"FO4IDE: preserved body signature changed; review it.\")";

    private static readonly Regex Marker = new(
        @"^[ \t]*#pragma[ \t]+(?<kind>region|endregion)[ \t]+FO4IDE_BODY[ \t]+(?<name>[^\r\n]*?)[ \t]*$",
        RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex LegacyMarker = new(
        @"^[ \t]*" + "/" + "/[ \t]*(?<kind>>>>|<<<)[ \t]*body:[ \t]*(?<name>[^\r\n]*?)[ \t]*$",
        RegexOptions.Multiline | RegexOptions.Compiled);


    public sealed record Region(string Name, string Body, string SignatureLine);


    public static string Begin(string name) => BeginPrefix + name;


    public static string End(string name) => EndPrefix + name;

    private static IEnumerable<Match> MarkerMatches(string text) =>
        Marker.Matches(text).Cast<Match>()
            .Concat(LegacyMarker.Matches(text).Cast<Match>())
            .OrderBy(match => match.Index);

    private static bool IsBegin(Match match) =>
        match.Groups["kind"].Value is "region" or ">>>";








    public static IReadOnlyDictionary<string, Region> Read(string? existing)
    {
        var regions = new Dictionary<string, Region>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(existing)) return regions;

        var normalized = existing.Replace("\r\n", "\n");
        string? openName = null;
        int openEnd = -1, openStart = -1;

        foreach (var match in MarkerMatches(normalized))
        {
            var name = match.Groups["name"].Value;
            if (IsBegin(match))
            {
                openName = name;
                openStart = match.Index;
                openEnd = match.Index + match.Length;
                continue;
            }



            if (openName == null || !string.Equals(openName, name, StringComparison.Ordinal)) continue;

            var body = normalized[openEnd..match.Index].Trim('\n');
            regions[name] = new Region(name, body, SignatureLineBefore(normalized, openStart));
            openName = null;
        }

        return regions;
    }









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






    public static string Merge(string generated, string? existing)
    {
        var preserved = Read(existing);
        if (preserved.Count == 0) return generated;

        var normalized = generated.Replace("\r\n", "\n");
        var result = new StringBuilder(normalized.Length);
        int cursor = 0;
        string? openName = null;
        int openEnd = -1, openStart = -1;

        foreach (var match in MarkerMatches(normalized))
        {
            if (IsBegin(match))
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
