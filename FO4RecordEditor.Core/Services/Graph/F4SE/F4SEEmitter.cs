using System;
using System.Collections.Generic;
using System.Linq;

namespace FO4RecordEditor.Services.Graph.F4SE;


public sealed class F4SEEmitOptions
{







    public IReadOnlyDictionary<string, string> Existing { get; set; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);


    public bool EmitDespiteErrors { get; set; }
}


public sealed record F4SEEmitResult
{
    public IReadOnlyList<EmittedFile> Files { get; init; } = Array.Empty<EmittedFile>();

    public IReadOnlyList<GraphDiagnostic> Diagnostics { get; init; } = Array.Empty<GraphDiagnostic>();


    public IReadOnlyList<string> OrphanedBodies { get; init; } = Array.Empty<string>();

    public IEnumerable<GraphDiagnostic> Errors =>
        Diagnostics.Where(d => d.Severity == GraphSeverity.Error);

    public bool Success => Files.Count > 0 && !Errors.Any();

    public EmittedFile? File(string relativePath) =>
        Files.FirstOrDefault(f => string.Equals(f.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase));
}













public sealed class F4SEEmitter
{
    public F4SEEmitResult Emit(PluginBinding plugin, F4SEEmitOptions? options = null)
    {
        options ??= new F4SEEmitOptions();

        var diagnostics = F4SEBindingValidator.Validate(plugin).ToList();
        if (diagnostics.Any(d => d.Severity == GraphSeverity.Error) && !options.EmitDespiteErrors)
            return new F4SEEmitResult { Diagnostics = diagnostics };

        var structNames = plugin.AllStructs.Select(s => s.Name).ToList();
        var cpp = new F4SECppEmitter(structNames);
        var psc = new F4SEPapyrusHeaderEmitter(plugin);
        var shell = new F4SEPluginEmitter();

        var files = new List<EmittedFile>();
        var orphans = new List<string>();

        foreach (var module in plugin.Modules)
        {
            Add($"src/{F4SECppEmitter.HeaderFileName(plugin, module)}", cpp.EmitHeader(plugin, module));
            Add($"src/{F4SECppEmitter.SourceFileName(plugin, module)}", cpp.EmitSource(plugin, module));
            Add(F4SEPapyrusHeaderEmitter.ScriptFileName(module), psc.Emit(plugin, module));
        }

        Add($"src/{plugin.Name}Registrations.h", cpp.EmitRegistrationsHeader(plugin));
        Add($"src/{plugin.Name}Registrations.cpp", cpp.EmitRegistrationsSource(plugin));
        Add("src/main.cpp", shell.EmitMain(plugin));
        Add("CMakeLists.txt", shell.EmitCMakeLists(plugin));
        Add("README.md", shell.EmitReadme(plugin));

        return new F4SEEmitResult
        {
            Files = files,
            Diagnostics = diagnostics,
            OrphanedBodies = orphans,
        };

        void Add(string path, string text)
        {
            options.Existing.TryGetValue(path, out var previous);
            if (previous != null)
            {
                orphans.AddRange(F4SERegionMerge.Orphaned(text, previous).Select(name => $"{path}: {name}"));
                text = F4SERegionMerge.Merge(text, previous);
            }
            files.Add(new EmittedFile(path, text));
        }
    }









    public static CrossCheckResult RoundTrip(PluginBinding plugin, F4SEEmitResult result)
    {
        var extractor = new F4SERegistrationExtractor();
        var structNames = plugin.AllStructs.Select(s => s.Name).ToList();

        var recovered = result.Files
            .Where(f => f.RelativePath.EndsWith(".cpp", StringComparison.OrdinalIgnoreCase))
            .SelectMany(f => extractor.Extract(f.Text, f.RelativePath, structNames).Natives)
            .ToList();

        var psc = new F4SEPapyrusHeaderEmitter(plugin);
        var declared = plugin.Modules.SelectMany(psc.DeclarationsOf).ToList();

        return F4SECrossCheck.Compare(recovered, declared);
    }
}
