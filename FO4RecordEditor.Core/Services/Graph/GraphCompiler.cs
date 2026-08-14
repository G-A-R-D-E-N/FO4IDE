using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FO4RecordEditor.Services.Papyrus;

namespace FO4RecordEditor.Services.Graph;

public sealed class GraphCompileOptions
{
    public PapyrusCompileOptions Papyrus { get; } = new();

    public PapyrusSourceWriterOptions Writer { get; } = new();

    public bool StopAfterSource { get; set; }

    public bool TreatPapyrusErrorsAsInternalFaults { get; set; }
}

public sealed record GraphCompileResult
{

    public string? Source { get; init; }

    public GraphSourceMap? SourceMap { get; init; }

    public IrScript? Ir { get; init; }

    public PexFile? Pex { get; init; }

    public IReadOnlyList<GraphDiagnostic> Diagnostics { get; init; } = Array.Empty<GraphDiagnostic>();

    public IReadOnlyList<PapyrusDiagnostic> PapyrusDiagnostics { get; init; } =
        Array.Empty<PapyrusDiagnostic>();

    public bool SourcesComplete { get; init; } = true;

    public bool Success => Pex != null;

    public IEnumerable<GraphDiagnostic> Errors =>
        Diagnostics.Where(d => d.Severity == GraphSeverity.Error);
}

public sealed class GraphCompiler
{
    private readonly PapyrusScriptIndex _index;
    private readonly NodePalette _palette;

    public GraphCompiler(PapyrusScriptIndex index, NodePalette? palette = null)
    {
        _index = index ?? throw new ArgumentNullException(nameof(index));
        _palette = palette ?? new NodePalette(index);
    }

    public static PapyrusScriptIndex IndexFor(IEnumerable<string> roots) =>
        PapyrusCompiler.IndexFor(roots);

    public NodePalette Palette => _palette;

    public GraphValidation Validate(GraphDocument document) =>
        new GraphValidator(_index, _palette).Validate(document);

    public GraphCompileResult Compile(GraphDocument document, GraphCompileOptions? options = null)
    {
        options ??= new GraphCompileOptions();

        var validation = Validate(document);
        var problems = validation.Diagnostics.ToList();
        if (!validation.Ok)
            return new GraphCompileResult { Diagnostics = problems, SourcesComplete = validation.SourcesComplete };

        var owner = OwnerScriptFor(document);
        var types = new GraphTypeResolver(_index, document.Header.ScriptName, document.Header.Extends);
        var linearizer = new GraphLinearizer(document, validation, types, owner);

        var ir = linearizer.Lower(_index);
        problems.AddRange(linearizer.Diagnostics);

        if (ir == null || problems.Any(d => d.Severity == GraphSeverity.Error))
        {
            return new GraphCompileResult
            {
                Diagnostics = problems,
                Ir = ir,
                SourcesComplete = types.SourcesComplete,
            };
        }

        var source = new PapyrusSourceWriter(options.Writer).Write(ir, out var map);
        if (options.StopAfterSource)
        {
            return new GraphCompileResult
            {
                Source = source,
                SourceMap = map,
                Ir = ir,
                Diagnostics = problems,
                SourcesComplete = types.SourcesComplete,
            };
        }

        var fileName = document.Header.ScriptName + ".psc";
        PublishForSelfReference(document.Header.ScriptName, source);

        var parsed = PapyrusParser.Parse(source, fileName);
        var compiled = new PapyrusCompiler(_index).Compile(parsed, options.Papyrus, fileName);

        var papyrusDiagnostics = compiled.Diagnostics.ToList();
        problems.AddRange(papyrusDiagnostics.Select(d => MapBack(d, map, options)));

        return new GraphCompileResult
        {
            Source = source,
            SourceMap = map,
            Ir = ir,
            Pex = compiled.Pex,
            Diagnostics = problems,
            PapyrusDiagnostics = papyrusDiagnostics,
            SourcesComplete = types.SourcesComplete && compiled.SourcesComplete,
        };
    }

    public GraphCompileResult CompileFile(string graphPath, GraphCompileOptions? options = null)
    {
        if (!GraphDocumentJson.TryDeserialize(File.ReadAllText(graphPath), out var document, out var error))
            return new GraphCompileResult { Diagnostics = new[] { error! } };

        return Compile(document!, options);
    }

    public GraphCompileResult CompileToFile(
        GraphDocument document, string pscPath, string? pexPath = null, GraphCompileOptions? options = null)
    {
        var result = Compile(document, options);
        if (result.Source == null) return result;

        var directory = Path.GetDirectoryName(pscPath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(pscPath, result.Source);

        if (result.Pex == null) return result;
        result.Pex.WriteFile(pexPath ?? Path.ChangeExtension(pscPath, ".pex"));
        return result;
    }

    private readonly Dictionary<string, string> _scratchRoots =
        new(StringComparer.OrdinalIgnoreCase);

    private void PublishForSelfReference(string scriptName, string source)
    {
        if (string.IsNullOrWhiteSpace(scriptName)) return;

        try
        {
            var fileName = scriptName.Replace(':', '_') + ".psc";

            if (_scratchRoots.TryGetValue(scriptName, out var known))
            {
                var existing = Path.Combine(known, fileName);
                File.WriteAllText(existing, source);
                _index.Invalidate(existing);
                return;
            }

            var root = Path.Combine(
                Path.GetTempPath(), "fo4re-graph-scratch", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);

            var path = Path.Combine(root, fileName);
            File.WriteAllText(path, source);

            _index.AddRoot(root);
            _scratchRoots[scriptName] = root;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {

        }
    }

    private PapyrusScript? OwnerScriptFor(GraphDocument document)
    {
        if (string.IsNullOrWhiteSpace(document.Header.ScriptName)) return null;

        var header = "Scriptname " + document.Header.ScriptName;
        if (!string.IsNullOrWhiteSpace(document.Header.Extends))
            header += " extends " + document.Header.Extends;

        var imports = string.Concat(document.Header.Imports.Select(i => "\nImport " + i));
        return PapyrusParser.Parse(header + imports + "\n", document.Header.ScriptName + ".psc");
    }

    private static GraphDiagnostic MapBack(
        PapyrusDiagnostic diagnostic, GraphSourceMap map, GraphCompileOptions options)
    {
        var severity = diagnostic.Severity == PapyrusSeverity.Error
            ? GraphSeverity.Error
            : GraphSeverity.Warning;

        var entry = map.Find(diagnostic.Span.Start)
                    ?? map.FindByLine(diagnostic.Span.Line)
                    ?? map.FunctionAt(diagnostic.Span.Start);

        if (entry != null)
        {
            var note = options.TreatPapyrusErrorsAsInternalFaults && severity == GraphSeverity.Error
                ? " The graph validator should have caught this."
                : "";

            return new GraphDiagnostic
            {
                Code = GraphDiagnosticCodes.InternalEmitterFault,
                Severity = severity,
                Message = diagnostic.Message + note,
                NodeId = entry.Value.NodeId,
                PinId = entry.Value.PinId,
                SourceLine = diagnostic.Span.Line,
                PapyrusCode = diagnostic.Code,
            };
        }

        return new GraphDiagnostic
        {
            Code = GraphDiagnosticCodes.InternalEmitterFault,
            Severity = severity,
            Message = $"{diagnostic.Message} (generated line {diagnostic.Span.Line}, "
                      + "which no node claims: this is a defect in the compiler)",
            SourceLine = diagnostic.Span.Line,
            PapyrusCode = diagnostic.Code,
        };
    }
}
