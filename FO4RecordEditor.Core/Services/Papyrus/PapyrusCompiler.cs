using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FO4RecordEditor.Services.Papyrus;


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


    public PexFile? Pex { get; }

    public IReadOnlyList<PapyrusDiagnostic> Diagnostics { get; }











    public bool SourcesComplete { get; }

    public bool Success => Pex != null;

    public IEnumerable<PapyrusDiagnostic> Errors =>
        Diagnostics.Where(d => d.Severity == PapyrusSeverity.Error);
}


public sealed class PapyrusCompileOptions
{

    public IList<string> ImportRoots { get; } = new List<string>();





    public string? FlagFile { get; set; }

    public bool EmitDebugInfo { get; set; } = true;


    public bool EmitDebugOnlyCode { get; set; } = true;


    public bool EmitBetaOnlyCode { get; set; } = true;

    public string UserName { get; set; } = "";

    public string ComputerName { get; set; } = "";





    public long CompilationTime { get; set; }
}















public sealed class PapyrusCompiler
{
    private readonly PapyrusScriptIndex _index;

    public PapyrusCompiler(PapyrusScriptIndex index) =>
        _index = index ?? throw new ArgumentNullException(nameof(index));


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
