using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace FO4RecordEditor.Services.Graph;

public static class GraphToolService
{

    public static string Validate(string graphPath, string? imports = null)
    {
        if (!TryLoad(graphPath, out var document, out var failure)) return failure!;

        var compiler = new GraphCompiler(IndexFor(graphPath, imports));
        var validation = compiler.Validate(document!);

        var text = new StringBuilder();
        text.AppendLine(validation.Ok
            ? $"RESULT: OK. {document!.Nodes.Count} nodes, {document.Wires.Count} wires, no errors."
            : $"RESULT: {validation.Errors.Count()} error(s) in {Path.GetFileName(graphPath)}.");

        foreach (var diagnostic in validation.Diagnostics) text.AppendLine(Describe(diagnostic, document!));
        if (!validation.SourcesComplete)
            text.AppendLine("Note: some types are not on the import roots, so checking was incomplete.");

        return text.ToString().TrimEnd();
    }

    public static string Compile(
        string graphPath, string? output = null, string? imports = null, bool sourceOnly = false)
    {
        if (!TryLoad(graphPath, out var document, out var failure)) return failure!;

        var compiler = new GraphCompiler(IndexFor(graphPath, imports));
        var result = compiler.Compile(document!, new GraphCompileOptions { StopAfterSource = sourceOnly });

        var text = new StringBuilder();
        var errors = result.Errors.ToList();

        if (errors.Count > 0)
        {
            text.AppendLine($"RESULT: FAILED. {errors.Count} error(s) in {Path.GetFileName(graphPath)}.");
            foreach (var diagnostic in result.Diagnostics) text.AppendLine(Describe(diagnostic, document!));
            if (result.Source != null)
            {

                text.AppendLine().AppendLine("Generated source:").AppendLine(result.Source);
            }
            return text.ToString().TrimEnd();
        }

        var written = new List<string>();
        if (!string.IsNullOrWhiteSpace(output))
        {
            Directory.CreateDirectory(output);
            var name = document!.Header.ScriptName;

            var pscPath = Path.Combine(output, name + ".psc");
            File.WriteAllText(pscPath, result.Source!);
            written.Add(pscPath);

            if (result.Pex != null)
            {
                var pexPath = Path.Combine(output, name + ".pex");
                result.Pex.WriteFile(pexPath);
                written.Add(pexPath);
            }
        }

        text.AppendLine(sourceOnly
            ? $"RESULT: OK. Generated source for {document!.Header.ScriptName}."
            : $"RESULT: OK. Compiled {document!.Header.ScriptName}.");

        foreach (var path in written) text.AppendLine("Wrote " + path);
        foreach (var diagnostic in result.Diagnostics.Where(d => d.Severity == GraphSeverity.Warning))
            text.AppendLine(Describe(diagnostic, document));

        if (written.Count == 0 && result.Source != null)
            text.AppendLine().AppendLine(result.Source);

        return text.ToString().TrimEnd();
    }

    public static string SearchPalette(string query, string? imports = null, int limit = 30)
    {
        var compiler = new GraphCompiler(IndexFor(null, imports));
        var result = compiler.Palette.Search(query, limit <= 0 ? 30 : limit);

        var text = new StringBuilder();
        text.AppendLine($"RESULT: {result.Entries.Count} of {result.Total} match '{query}'.");
        foreach (var entry in result.Entries)
            text.AppendLine($"  {entry.Id}\n      {entry.Signature}");

        if (result.Truncated) text.AppendLine($"({result.Total - result.Entries.Count} more not shown.)");
        return text.ToString().TrimEnd();
    }

    public static string DescribeNode(string nodeType, string? imports = null)
    {
        var compiler = new GraphCompiler(IndexFor(null, imports));
        var definition = compiler.Palette.Find(nodeType);
        if (definition == null) return $"RESULT: FAILED. No node type '{nodeType}' on this palette.";

        var text = new StringBuilder();
        text.AppendLine($"RESULT: {definition.Id}");
        text.AppendLine($"  {definition.Title} ({definition.Kind}, {definition.Category})");
        if (definition.Summary != null) text.AppendLine("  " + definition.Summary);
        text.AppendLine(definition.IsPure
            ? "  Pure: evaluates inline, no control flow pins."
            : "  Sequenced: has control flow pins.");

        foreach (var pin in definition.Pins)
        {
            var kind = pin.Kind == PinKind.Exec ? "exec" : pin.Type?.ToString() ?? "value";
            var optional = pin.IsOptional ? $" (optional, default {pin.DeclaredDefault ?? "None"})" : "";
            text.AppendLine($"    {pin.Direction,-3} {pin.Id,-24} {kind}{optional}");
        }

        return text.ToString().TrimEnd();
    }

    private static bool TryLoad(string path, out GraphDocument? document, out string? failure)
    {
        document = null;
        failure = null;

        if (!File.Exists(path))
        {
            failure = $"RESULT: FAILED. No graph document at {path}.";
            return false;
        }

        if (GraphDocumentJson.TryDeserialize(File.ReadAllText(path), out document, out var error)) return true;

        failure = $"RESULT: FAILED. {error!.Message}";
        return false;
    }

    private static PapyrusScriptIndexRoots IndexFor(string? graphPath, string? imports) =>
        new(graphPath, imports);

    private static string Describe(GraphDiagnostic diagnostic, GraphDocument document)
    {
        var node = document.Node(diagnostic.NodeId);
        var where = diagnostic.NodeId == null
            ? ""
            : $" [node {diagnostic.NodeId}{(node == null ? "" : " " + node.Definition)}"
              + $"{(diagnostic.PinId == null ? "" : " pin " + diagnostic.PinId)}]";

        var related = diagnostic.RelatedNodes.Count == 0
            ? ""
            : " (also " + string.Join(", ", diagnostic.RelatedNodes) + ")";

        var severity = diagnostic.Severity == GraphSeverity.Error ? "error" : "warning";
        return $"  {severity} {diagnostic.Code}: {diagnostic.Message}{where}{related}";
    }
}

public sealed class PapyrusScriptIndexRoots
{
    private readonly Papyrus.PapyrusScriptIndex _index;

    public PapyrusScriptIndexRoots(string? graphPath, string? imports)
    {
        var roots = new List<string>();

        var folder = string.IsNullOrWhiteSpace(graphPath) ? null : Path.GetDirectoryName(graphPath);
        if (!string.IsNullOrWhiteSpace(folder)) roots.Add(folder);

        if (!string.IsNullOrWhiteSpace(imports))
        {
            roots.AddRange(imports.Split(
                ';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        roots.AddRange(ToolPaths.PapyrusBaseImports());
        _index = Papyrus.PapyrusCompiler.IndexFor(roots.Where(Directory.Exists).Distinct());
    }

    public static implicit operator Papyrus.PapyrusScriptIndex(PapyrusScriptIndexRoots roots) => roots._index;
}
