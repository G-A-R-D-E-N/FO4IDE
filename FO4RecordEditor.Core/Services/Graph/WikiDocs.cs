using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using FO4RecordEditor.Services.Papyrus;

namespace FO4RecordEditor.Services.Graph;


public sealed record WikiParameterDoc(string? Description, string? DocumentedDefault);


public sealed record WikiFunctionDoc
{
    public string? Summary { get; init; }

    public string? SyntaxLine { get; init; }

    public string? ReturnValue { get; init; }

    public IReadOnlyDictionary<string, WikiParameterDoc> Parameters { get; init; } =
        new Dictionary<string, WikiParameterDoc>(StringComparer.OrdinalIgnoreCase);
}


public sealed record WikiScriptDoc(string? Summary);


public sealed record WikiDocStats(int PagesIndexed, int PagesParsed, int PagesFailed)
{
    public static readonly WikiDocStats Empty = new(0, 0, 0);

    public bool Available => PagesIndexed > 0;
}


public interface IWikiDocProvider
{
    WikiFunctionDoc? Function(string scriptName, string functionName);

    WikiScriptDoc? Script(string scriptName);

    WikiDocStats Stats { get; }
}






public sealed class NullWikiDocProvider : IWikiDocProvider
{
    public static readonly NullWikiDocProvider Instance = new();

    private NullWikiDocProvider() { }

    public WikiFunctionDoc? Function(string scriptName, string functionName) => null;

    public WikiScriptDoc? Script(string scriptName) => null;

    public WikiDocStats Stats => WikiDocStats.Empty;
}


















public sealed class CkWikiDocProvider : IWikiDocProvider
{

    private static readonly Regex SectionPattern = new(
        @"<span[^>]*id=""(?<id>Syntax|Parameters|Return_Value)""[^>]*>.*?</h2>(?<body>.*?)(?=<h2|<div id=""catlinks)",
        RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ListItemPattern = new(
        @"<li>(?<body>.*?)</li>", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex TagPattern = new("<[^>]+>", RegexOptions.Compiled);

    private static readonly Regex DefaultPattern = new(
        @"Default:\s*(?<value>[^<]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly string? _root;
    private readonly ConcurrentDictionary<string, WikiFunctionDoc?> _functions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lazy<Dictionary<string, string>> _byFunction;
    private readonly Lazy<Dictionary<string, string>> _byScript;
    private int _parsed;
    private int _failed;

    public CkWikiDocProvider(string? wikiRoot)
    {
        _root = string.IsNullOrWhiteSpace(wikiRoot) || !Directory.Exists(wikiRoot) ? null : wikiRoot;
        _byFunction = new Lazy<Dictionary<string, string>>(IndexFunctions);
        _byScript = new Lazy<Dictionary<string, string>>(IndexScripts);
    }

    public WikiDocStats Stats =>
        _root == null ? WikiDocStats.Empty : new WikiDocStats(_byFunction.Value.Count, _parsed, _failed);

    public WikiFunctionDoc? Function(string scriptName, string functionName)
    {
        if (_root == null) return null;
        return _functions.GetOrAdd($"{functionName}|{scriptName}", _ => ReadFunction(scriptName, functionName));
    }

    public WikiScriptDoc? Script(string scriptName)
    {
        if (_root == null) return null;
        if (!_byScript.Value.TryGetValue(scriptName, out var path)) return null;

        var text = ReadAllText(path);
        return text == null ? null : new WikiScriptDoc(FirstParagraph(text));
    }

    private Dictionary<string, string> IndexFunctions()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (_root == null) return map;

        foreach (var path in EnumerateHtml())
        {
            var name = WebUtility.UrlDecode(Path.GetFileNameWithoutExtension(path));
            int separator = name.IndexOf("_-_", StringComparison.Ordinal);
            if (separator <= 0) continue;

            var function = name[..separator];
            var script = name[(separator + 3)..];
            map[$"{function}|{script}"] = path;
        }
        return map;
    }

    private Dictionary<string, string> IndexScripts()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (_root == null) return map;

        foreach (var path in EnumerateHtml())
        {
            var name = WebUtility.UrlDecode(Path.GetFileNameWithoutExtension(path));
            if (!name.EndsWith("_Script", StringComparison.OrdinalIgnoreCase)) continue;
            map[name[..^"_Script".Length]] = path;
        }
        return map;
    }

    private IEnumerable<string> EnumerateHtml() =>


        _root == null ? Array.Empty<string>() : PapyrusFileWalk.EnumerateFiles(_root, "*.html");

    private WikiFunctionDoc? ReadFunction(string scriptName, string functionName)
    {
        if (!_byFunction.Value.TryGetValue($"{functionName}|{scriptName}", out var path)) return null;

        var text = ReadAllText(path);
        if (text == null)
        {
            System.Threading.Interlocked.Increment(ref _failed);
            return null;
        }

        string? syntax = null, returnValue = null;
        var parameters = new Dictionary<string, WikiParameterDoc>(StringComparer.OrdinalIgnoreCase);

        foreach (Match section in SectionPattern.Matches(text))
        {
            var body = section.Groups["body"].Value;
            switch (section.Groups["id"].Value.ToLowerInvariant())
            {
                case "syntax":
                    syntax = Clean(body).Split('\n').FirstOrDefault(l => l.Trim().Length > 0)?.Trim();
                    break;

                case "return_value":
                    returnValue = Clean(body).Trim();
                    break;

                case "parameters":
                    foreach (Match item in ListItemPattern.Matches(body))
                    {
                        var raw = item.Groups["body"].Value;
                        var cleaned = Clean(raw).Trim();
                        if (cleaned.Length == 0) continue;

                        int colon = cleaned.IndexOf(':');
                        var name = (colon > 0 ? cleaned[..colon] : cleaned).Trim();
                        if (name.Length == 0 || name.Contains(' ')) continue;

                        var described = colon > 0 ? cleaned[(colon + 1)..].Trim() : null;
                        var defaulted = DefaultPattern.Match(raw);
                        parameters[name] = new WikiParameterDoc(
                            described, defaulted.Success ? defaulted.Groups["value"].Value.Trim() : null);
                    }
                    break;
            }
        }

        System.Threading.Interlocked.Increment(ref _parsed);
        return new WikiFunctionDoc
        {
            Summary = FirstParagraph(text),
            SyntaxLine = syntax,
            ReturnValue = returnValue,
            Parameters = parameters,
        };
    }

    private static string? ReadAllText(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? FirstParagraph(string html)
    {
        var match = Regex.Match(html, @"<p>(?<body>.*?)</p>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        if (!match.Success) return null;

        var text = Clean(match.Groups["body"].Value).Trim();
        return text.Length == 0 ? null : text;
    }

    private static string Clean(string html) =>
        WebUtility.HtmlDecode(TagPattern.Replace(html, " "))
            .Replace(' ', ' ')
            .Replace("\r", "");
}
