using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FO4RecordEditor.Services.Papyrus;

namespace FO4RecordEditor.Services.Graph;

/// <summary>Options for one graph compile.</summary>
public sealed class GraphCompileOptions
{
    public PapyrusCompileOptions Papyrus { get; } = new();

    public PapyrusSourceWriterOptions Writer { get; } = new();

    /// <summary>Stop once source exists, for the canvas preview.</summary>
    public bool StopAfterSource { get; set; }

    /// <summary>
    /// Treat a surviving Papyrus error as a defect in this compiler rather than in the graph.
    /// </summary>
    /// <remarks>
    /// If the validator did its job, the generated source produces no errors at all, so any that
    /// survive are a validator gap. Turning this on in the test suite makes the whole suite a
    /// ratchet on validator completeness; it stays off in production, where the author still needs
    /// the message.
    /// </remarks>
    public bool TreatPapyrusErrorsAsInternalFaults { get; set; }
}

/// <summary>What one graph compile produced.</summary>
public sealed record GraphCompileResult
{
    /// <summary>
    /// The generated source, present even on failure.
    /// </summary>
    /// <remarks>
    /// Populated whenever emission got that far, specifically so the canvas can show the source with
    /// the failure marked. Without it, debugging the emitter is guesswork.
    /// </remarks>
    public string? Source { get; init; }

    public GraphSourceMap? SourceMap { get; init; }

    public IrScript? Ir { get; init; }

    public PexFile? Pex { get; init; }

    public IReadOnlyList<GraphDiagnostic> Diagnostics { get; init; } = Array.Empty<GraphDiagnostic>();

    /// <summary>The unmapped Papyrus diagnostics, kept for debugging the mapping itself.</summary>
    public IReadOnlyList<PapyrusDiagnostic> PapyrusDiagnostics { get; init; } =
        Array.Empty<PapyrusDiagnostic>();

    public bool SourcesComplete { get; init; } = true;

    public bool Success => Pex != null;

    public IEnumerable<GraphDiagnostic> Errors =>
        Diagnostics.Where(d => d.Severity == GraphSeverity.Error);
}

/// <summary>
/// Graph to script to compiled object.
/// </summary>
/// <remarks>
/// Mirrors <see cref="PapyrusCompiler"/>'s shape and inherits its stance: refuse at the first stage
/// that reports an error rather than press on, because a later stage would then be working from a
/// tree it has already been told is wrong.
/// </remarks>
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

    /// <summary>
    /// Diagnostics only, with no emission.
    /// </summary>
    /// <remarks>
    /// Shares its stages verbatim with <see cref="Compile"/>, so live validation on the canvas can
    /// never disagree with what a compile would say.
    /// </remarks>
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

    /// <summary>Compiles and writes both the source and the compiled object.</summary>
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

    /// <summary>
    /// Puts the generated source where the index can find it, so the script can refer to its own type.
    /// </summary>
    /// <remarks>
    /// <c>PapyrusScriptIndex</c> resolves from disk roots and has no in-memory injection, so a
    /// script that is not on a root cannot be resolved even while it is being compiled. That
    /// matters as soon as a graph passes <c>Self</c> somewhere its base type is wanted: the checker
    /// asks whether the script inherits from the parameter's type, the index cannot find the
    /// script, and a perfectly good call is refused.
    /// <para>
    /// One scratch directory per script name, because <c>AddRoot</c> snapshots a directory's
    /// contents into its name map and returns early the second time it is given the same root. The
    /// file therefore has to exist before the root is added; writing into an already-added root
    /// would leave the script unfindable. Later compiles of the same script rewrite the file and
    /// invalidate the parse cache, which is enough.
    /// </para>
    /// <para>
    /// The file is a build artefact, not output: the caller still decides where real output goes.
    /// </para>
    /// </remarks>
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
            // Losing this only costs self-type resolution, which the compiler will then report in
            // its own terms. It is not worth failing an otherwise good compile over.
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

    /// <summary>
    /// Attributes a Papyrus diagnostic to the node that produced the offending source.
    /// </summary>
    /// <remarks>
    /// Four steps, narrowing to widening. The last one has to be loud: a compiler error that mapped
    /// to nothing and got dropped would present to the author as a failure with no reason given,
    /// which is the worst outcome this subsystem could produce.
    /// </remarks>
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
