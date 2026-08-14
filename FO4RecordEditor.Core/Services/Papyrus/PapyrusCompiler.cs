using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FO4RecordEditor.Services.Papyrus;

/// <summary>What one compile produced.</summary>
public sealed class PapyrusCompileResult
{
    internal PapyrusCompileResult(
        PapyrusScript? script, PexFile? pex, IReadOnlyList<PapyrusDiagnostic> diagnostics, bool sourcesComplete)
    {
        Script = script;
        Pex = pex;
        Diagnostics = diagnostics;
        SourcesComplete = sourcesComplete;
    }

    public PapyrusScript? Script { get; }

    /// <summary>The compiled object, or null when anything was reported as an error.</summary>
    public PexFile? Pex { get; }

    public IReadOnlyList<PapyrusDiagnostic> Diagnostics { get; }

    /// <summary>
    /// False when a parent script, an import or a named type was not on the roots.
    /// </summary>
    /// <remarks>
    /// The resolver and the type checker stay quiet in that case, deliberately -- see
    /// <see cref="PapyrusResolution.BaseChainComplete"/>. The back end does not have that luxury,
    /// because an unresolved callee has unknown arity once optional parameters exist. So a compile
    /// with incomplete sources usually fails at code generation, and this says why rather than
    /// leaving the caller to infer it from the message.
    /// </remarks>
    public bool SourcesComplete { get; }

    public bool Success => Pex != null;

    public IEnumerable<PapyrusDiagnostic> Errors =>
        Diagnostics.Where(d => d.Severity == PapyrusSeverity.Error);
}

/// <summary>Options for a compile. Roots are the import path, in priority order.</summary>
public sealed class PapyrusCompileOptions
{
    /// <summary>Extra source roots, searched before whatever the index already holds.</summary>
    public IList<string> ImportRoots { get; } = new List<string>();

    /// <summary>
    /// An <c>Institute_Papyrus_Flags.flg</c> to read the user-flag table from. Null looks for one
    /// under the roots and otherwise uses the built-in Fallout 4 table.
    /// </summary>
    public string? FlagFile { get; set; }

    public bool EmitDebugInfo { get; set; } = true;

    /// <summary>See <see cref="PapyrusCodeGenOptions.EmitDebugOnlyCode"/>.</summary>
    public bool EmitDebugOnlyCode { get; set; } = true;

    /// <summary>See <see cref="PapyrusCodeGenOptions.EmitBetaOnlyCode"/>.</summary>
    public bool EmitBetaOnlyCode { get; set; } = true;

    public string UserName { get; set; } = "";

    public string ComputerName { get; set; } = "";

    /// <summary>
    /// Unix seconds stamped into the header. Zero, the default, keeps output reproducible, which is
    /// what makes a byte-level diff between two compiles of the same source meaningful.
    /// </summary>
    public long CompilationTime { get; set; }
}

/// <summary>
/// Source to <c>.pex</c>, with no Creation Kit involved.
/// </summary>
/// <remarks>
/// The one entry point that ties the whole subsystem together: parse, index, resolve, type check,
/// generate, write. Each of those is separately testable and separately measured; this exists so a
/// caller does not have to know the order.
/// <para>
/// It refuses on the first stage that reports an error rather than pressing on, because a later
/// stage's output would then be built on a tree it has already been told is wrong. That is the same
/// reason <see cref="PapyrusCodeGenerator"/> refuses a call it cannot resolve: a wrong <c>.pex</c>
/// is worse than none, since nothing downstream will tell you it is wrong until the game runs it.
/// </para>
/// </remarks>
public sealed class PapyrusCompiler
{
    private readonly PapyrusScriptIndex _index;

    public PapyrusCompiler(PapyrusScriptIndex index) =>
        _index = index ?? throw new ArgumentNullException(nameof(index));

    /// <summary>An index over <paramref name="roots"/>, in the order given.</summary>
    public static PapyrusScriptIndex IndexFor(IEnumerable<string> roots)
    {
        var index = new PapyrusScriptIndex();
        foreach (var root in roots) index.AddRoot(root);
        return index;
    }

    public PapyrusCompileResult CompileFile(string path, PapyrusCompileOptions? options = null)
    {
        options ??= new PapyrusCompileOptions();
        foreach (var root in options.ImportRoots) _index.AddRoot(root);

        PapyrusScript script;
        try
        {
            script = PapyrusParser.ParseFile(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new PapyrusCompileResult(null, null, new[]
            {
                new PapyrusDiagnostic(
                    PapyrusDiagnosticCodes.CannotEmit, PapyrusSeverity.Error,
                    $"Could not read the source: {ex.Message}", default, path),
            }, sourcesComplete: false);
        }

        return Compile(script, options, Path.GetFullPath(path));
    }

    public PapyrusCompileResult Compile(
        PapyrusScript script, PapyrusCompileOptions? options = null, string? sourceFileName = null)
    {
        if (script == null) throw new ArgumentNullException(nameof(script));
        options ??= new PapyrusCompileOptions();

        var diagnostics = new List<PapyrusDiagnostic>(script.Diagnostics);
        if (script.HasErrors) return new PapyrusCompileResult(script, null, diagnostics, sourcesComplete: false);

        // Before resolution on purpose: a duplicate name makes the symbol table ambiguous, so
        // resolving first would bury the real problem under its own consequences.
        diagnostics.AddRange(PapyrusDeclarationCheck.Check(script));
        if (diagnostics.Any(d => d.Severity == PapyrusSeverity.Error))
            return new PapyrusCompileResult(script, null, diagnostics, sourcesComplete: false);

        var resolution = new PapyrusResolver(_index).Resolve(script);
        diagnostics.AddRange(resolution.Diagnostics);

        var checker = new PapyrusTypeChecker(_index);
        diagnostics.AddRange(checker.Check(resolution));

        if (diagnostics.Any(d => d.Severity == PapyrusSeverity.Error))
            return new PapyrusCompileResult(script, null, diagnostics, resolution.BaseChainComplete);

        var flagFile = options.FlagFile ?? PapyrusUserFlagTable.FindFlagFile(_index.Roots);
        var generator = new PapyrusCodeGenerator(_index);
        var pex = generator.Generate(
            script,
            resolution,
            new PapyrusCodeGenOptions
            {
                SourceFileName = sourceFileName ?? script.FilePath ?? script.Name + ".psc",
                UserName = options.UserName,
                ComputerName = options.ComputerName,
                CompilationTime = options.CompilationTime,
                EmitDebugInfo = options.EmitDebugInfo,
                EmitDebugOnlyCode = options.EmitDebugOnlyCode,
                EmitBetaOnlyCode = options.EmitBetaOnlyCode,
                UserFlags = PapyrusUserFlagTable.FromFileOrDefault(flagFile),
            },
            out var codegenDiagnostics);

        diagnostics.AddRange(codegenDiagnostics);
        return new PapyrusCompileResult(script, pex, diagnostics, resolution.BaseChainComplete);
    }

    /// <summary>Compiles <paramref name="sourcePath"/> and writes the result beside it or to <paramref name="outputPath"/>.</summary>
    public PapyrusCompileResult CompileToFile(
        string sourcePath, string? outputPath = null, PapyrusCompileOptions? options = null)
    {
        var result = CompileFile(sourcePath, options);
        if (result.Pex == null) return result;

        outputPath ??= Path.ChangeExtension(sourcePath, ".pex");
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        result.Pex.WriteFile(outputPath);
        return result;
    }
}
