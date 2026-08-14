using System.Collections.ObjectModel;
using System.Text;
using FO4RecordEditor.Models;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;

namespace FO4RecordEditor.Services;

/// <summary>
/// Context object injected into every C# script run.
/// Scripts reference it as "Ctx" (or just call helper methods directly).
/// </summary>
public class ScriptContext
{
    private readonly StringBuilder _log = new();
    public ObservableCollection<RecordNode> RootNodes { get; }

    public ScriptContext(ObservableCollection<RecordNode> roots) => RootNodes = roots;

    // All file-level nodes
    public IEnumerable<RecordNode> AllFiles =>
        RootNodes.SelectMany(r => r.SelfAndDescendants()).Where(n => n.FilePath != null && n.IsLeaf == false);

    // Every node in the entire tree
    public IEnumerable<RecordNode> All =>
        RootNodes.SelectMany(r => r.SelfAndDescendants());

    // Records where Key matches the given signature (e.g. "COBJ", "PERK", "AVIF")
    public IEnumerable<RecordNode> RecordsOfType(string sig) =>
        All.Where(n => string.Equals(n.Key, sig, StringComparison.OrdinalIgnoreCase));

    // All nodes whose key matches
    public IEnumerable<RecordNode> WithKey(string key) =>
        All.Where(n => string.Equals(n.Key, key, StringComparison.OrdinalIgnoreCase));

    // All leaf nodes whose value matches
    public IEnumerable<RecordNode> WithValue(string value, StringComparison cmp = StringComparison.OrdinalIgnoreCase) =>
        All.Where(n => n.IsLeaf && string.Equals(n.Value, value, cmp));

    // All leaf nodes whose value contains the text
    public IEnumerable<RecordNode> Containing(string text, StringComparison cmp = StringComparison.OrdinalIgnoreCase) =>
        All.Where(n => n.IsLeaf && n.Value.Contains(text, cmp));

    // Recursive search from a root for a descendant matching a path
    public RecordNode? Navigate(RecordNode root, string path) => root.Navigate(path);

    public void Log(string msg)
    {
        _log.AppendLine(msg);
    }

    public string GetLog() => _log.ToString();
    public void ClearLog() => _log.Clear();
}

public class ScriptEngine
{
    private static readonly ScriptOptions _options = ScriptOptions.Default
        .AddImports(
            "System",
            "System.Linq",
            "System.Collections.Generic",
            "System.Text",
            "FO4RecordEditor.Models",
            "FO4RecordEditor.Services")
        .AddReferences(
            typeof(RecordNode).Assembly,
            typeof(ScriptContext).Assembly,
            typeof(Enumerable).Assembly);

    public static async Task<ScriptResult> RunAsync(string code, ScriptContext ctx)
    {
        ctx.ClearLog();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var globals = new ScriptGlobals { Ctx = ctx };
            // Inject convenience aliases at top of every script
            var wrapper = $"""
                var All     = Ctx.All;
                var Log     = (Action<string>)Ctx.Log;
                {code}
                """;
            await CSharpScript.RunAsync(wrapper, _options, globals);
            stopwatch.Stop();
            return new ScriptResult(true, ctx.GetLog(), null, stopwatch.ElapsedMilliseconds);
        }
        catch (CompilationErrorException ex)
        {
            return new ScriptResult(false, ctx.GetLog(), "Compile error:\n" + string.Join("\n", ex.Diagnostics), stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            return new ScriptResult(false, ctx.GetLog(), ex.ToString(), stopwatch.ElapsedMilliseconds);
        }
    }
}

public class ScriptGlobals
{
    public ScriptContext Ctx { get; set; } = null!;
}

public record ScriptResult(bool Success, string Output, string? Error, long ElapsedMs);
