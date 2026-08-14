using System;
using System.Collections.Generic;
using System.Linq;

namespace FO4RecordEditor.Services.Graph.F4SE;

/// <summary>Options for one emit.</summary>
public sealed class F4SEEmitOptions
{
    /// <summary>
    /// The previous contents of each file, keyed by relative path.
    /// </summary>
    /// <remarks>
    /// Supplied by the caller rather than read here, because the emitter does no I/O. Anything
    /// present is merged so hand-written bodies survive; anything absent is emitted fresh.
    /// </remarks>
    public IReadOnlyDictionary<string, string> Existing { get; set; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Emit even when validation reported an error. Off, and off is the right default.</summary>
    public bool EmitDespiteErrors { get; set; }
}

/// <summary>What one emit produced.</summary>
public sealed record F4SEEmitResult
{
    public IReadOnlyList<EmittedFile> Files { get; init; } = Array.Empty<EmittedFile>();

    public IReadOnlyList<GraphDiagnostic> Diagnostics { get; init; } = Array.Empty<GraphDiagnostic>();

    /// <summary>Preserved bodies whose function no longer exists, so nothing is lost silently.</summary>
    public IReadOnlyList<string> OrphanedBodies { get; init; } = Array.Empty<string>();

    public IEnumerable<GraphDiagnostic> Errors =>
        Diagnostics.Where(d => d.Severity == GraphSeverity.Error);

    public bool Success => Files.Count > 0 && !Errors.Any();

    public EmittedFile? File(string relativePath) =>
        Files.FirstOrDefault(f => string.Equals(f.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// The one entry point for turning a binding into an F4SE plugin's source tree.
/// </summary>
/// <remarks>
/// Returns text and writes nothing. That is what makes golden comparison and the emit then read back
/// round trip trivial, and it keeps every decision about where files land with the caller.
/// <para>
/// It refuses on a validation error rather than emitting something that could not build, which is
/// the same stance the Papyrus back end takes: a wrong artefact is worse than none, because nothing
/// downstream will say it is wrong until much later.
/// </para>
/// </remarks>
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

    /// <summary>
    /// Reads emitted C++ back and compares it to the declarations the Papyrus half carries.
    /// </summary>
    /// <remarks>
    /// The strongest check available without a C++ compiler. The two files come from one record but
    /// through two independent renderers, and the reader is a third independent implementation, so
    /// agreement across all three is real evidence rather than a tautology.
    /// </remarks>
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
