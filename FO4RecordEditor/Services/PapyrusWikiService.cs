using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace FO4RecordEditor.Services;











public static class PapyrusWikiService
{
    public static string LookupFunction(string wikiRoot, string script, string function)
    {
        if (string.IsNullOrWhiteSpace(wikiRoot) || !Directory.Exists(wikiRoot))
            return ToolError.Fail(NotConfiguredMessage);
        function = (function ?? "").Trim();
        script = (script ?? "").Trim();
        if (function.Length == 0) return ToolError.Fail("Provide a function name, e.g. 'GetBaseObject'.");

        string? path = script.Length > 0 ? FindFile(wikiRoot, $"{function}_-_{script}.html") : null;
        if (path == null)
        {
            var matches = SafeEnumerate(wikiRoot, $"{function}_-_*.html").ToList();
            if (matches.Count == 0)
                return $"No CK wiki page found for function '{function}'" +
                       (script.Length > 0 ? $" on script '{script}'" : "") +
                       ". Names are case-sensitive to the wiki's own spelling; call papyrus_script_info " +
                       "on the owning script first to see its exact function list.";
            if (matches.Count > 1)
            {
                var owners = matches.Select(m => Path.GetFileNameWithoutExtension(m).Split("_-_").Last());
                return $"'{function}' is defined on multiple scripts: {string.Join(", ", owners)}. " +
                       "Pass 'script' to disambiguate.";
            }
            path = matches[0];
        }

        var html = File.ReadAllText(path);
        var title = ExtractTag(html, "h1") ?? Path.GetFileNameWithoutExtension(path).Replace("_-_", " - ");
        var memberOf = Regex.Match(html, @"<b>Member of:</b>\s*<a[^>]*>([^<]+)</a>");
        var syntax = StripTags(ExtractSection(html, "Syntax"));
        var parameters = StripTags(ExtractSection(html, "Parameters"));
        var returnValue = StripTags(ExtractSection(html, "Return_Value"));
        var caveat = StripTags(ExtractSection(html, "Caveat"));

        var sb = new StringBuilder();
        sb.AppendLine(title);
        if (memberOf.Success) sb.AppendLine($"Member of: {memberOf.Groups[1].Value}");
        if (syntax.Length > 0) sb.AppendLine($"Syntax: {syntax}");
        if (parameters.Length > 0) sb.AppendLine($"Parameters: {parameters}");
        if (returnValue.Length > 0) sb.AppendLine($"Return Value: {returnValue}");
        if (caveat.Length > 0) sb.AppendLine($"Caveat: {caveat}");
        return sb.ToString().TrimEnd();
    }

    public static string LookupScriptInfo(string wikiRoot, string script)
    {
        if (string.IsNullOrWhiteSpace(wikiRoot) || !Directory.Exists(wikiRoot))
            return ToolError.Fail(NotConfiguredMessage);
        script = (script ?? "").Trim();


        script = Regex.Replace(script, @"[_ ]?Script$", "", RegexOptions.IgnoreCase);
        if (script.Length == 0) return ToolError.Fail("Provide a script name, e.g. 'ActiveMagicEffect' or 'ObjectReference'.");

        var path = FindFile(wikiRoot, $"{script}_Script.html");
        if (path == null)
            return $"No CK wiki page found for script '{script}'. Check the exact name (case-sensitive) -- " +
                   "the wiki mirror's own page list is under fallout4\\*_Script.html.";

        var html = File.ReadAllText(path);
        var extends = Regex.Match(html, @"<b>Extends:</b>\s*<a[^>]*>([^<]+)</a>");
        var definition = StripTags(ExtractSection(html, "Definition"));
        var globalFns = StripTags(ExtractSection(html, "Global_Functions"));
        var memberFns = StripTags(ExtractSection(html, "Member_Functions"));
        var events = StripTags(ExtractSection(html, "Events"));

        var sb = new StringBuilder();
        sb.AppendLine($"{script} Script");
        if (extends.Success) sb.AppendLine($"Extends: {extends.Groups[1].Value}");
        if (definition.Length > 0) sb.AppendLine($"Definition: {definition}");
        if (globalFns.Length > 0 && !globalFns.Equals("None", StringComparison.OrdinalIgnoreCase))
            sb.AppendLine($"Global Functions: {globalFns}");
        if (memberFns.Length > 0 && !memberFns.Equals("None", StringComparison.OrdinalIgnoreCase))
            sb.AppendLine($"Member Functions: {memberFns}");
        if (events.Length > 0 && !events.Equals("None", StringComparison.OrdinalIgnoreCase))
            sb.AppendLine($"Events: {events}");
        return sb.ToString().TrimEnd();
    }

    private const string NotConfiguredMessage =
        "No CK wiki mirror configured. Launch the MCP server with '--ck-wiki <folder>' pointing at an " +
        "offline Creation Kit Wiki HTML mirror (or set CkWikiPath in Settings for the in-app AI panel).";

    private static IEnumerable<string> SafeEnumerate(string root, string pattern)
    {
        try { return Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories); }
        catch { return Enumerable.Empty<string>(); }
    }

    private static string? FindFile(string root, string fileName) => SafeEnumerate(root, fileName).FirstOrDefault();

    private static string? ExtractTag(string html, string tag)
    {
        var m = Regex.Match(html, $@"<{tag}[^>]*>(.*?)</{tag}>", RegexOptions.Singleline);
        return m.Success ? StripTags(m.Groups[1].Value) : null;
    }




    private static string ExtractSection(string html, string sectionId)
    {
        var m = Regex.Match(html,
            $@"<span[^>]*\bid=""{Regex.Escape(sectionId)}""[^>]*>.*?</h2>(.*?)(?=<h2|<div id=""catlinks)",
            RegexOptions.Singleline);
        return m.Success ? m.Groups[1].Value : "";
    }

    private static string StripTags(string html)
    {
        var text = Regex.Replace(html, "<.*?>", " ", RegexOptions.Singleline);
        text = System.Net.WebUtility.HtmlDecode(text);
        return Regex.Replace(text, @"\s+", " ").Trim();
    }
}
