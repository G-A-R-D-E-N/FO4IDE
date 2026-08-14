using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FO4RecordEditor.Services.Papyrus;

namespace FO4RecordEditor.Services.Graph;

/// <summary>
/// Everything between a Papyrus file on disk and <see cref="GraphLifter"/>: reading it, decompiling
/// it when it is compiled, and putting the right roots in front of the index.
/// </summary>
/// <remarks>
/// Separate from the lifter because the lifter's input is a parsed script and should stay that way,
/// and separate from the interop because the interop cannot be tested on a Linux checkout. The
/// desktop shell is a thin caller of this.
/// </remarks>
public static class GraphScriptLoader
{
    /// <summary>What a load produced: a document, or the reasons there is not one.</summary>
    public sealed record Result(GraphDocument? Document, IReadOnlyList<GraphDiagnostic> Diagnostics, string? Failure)
    {
        /// <summary>True when a document came back. A false with no failure means the lifter refused.</summary>
        public bool Success => Document != null;
    }

    /// <summary>Whether this extension is one <see cref="Load"/> can read at all.</summary>
    public static bool IsScript(string path) =>
        Path.GetExtension(path) is { } extension
        && (extension.Equals(".psc", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".pex", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Reads a <c>.psc</c> or <c>.pex</c> into a graph document.
    /// </summary>
    /// <param name="path">The script to read.</param>
    /// <param name="roots">The import roots. The script's own folder is added to them.</param>
    /// <remarks>
    /// The .pex path is the weaker of the two, and says so: the decompiler reconstructs declarations
    /// exactly but function bodies are best effort, so what comes back is what the compiled object
    /// says rather than what its author wrote. GraphLiftPexRoundTripTests is the measurement, and
    /// DecompilerSweep is the same question asked of the real corpus.
    /// </remarks>
    public static Result Load(string path, IEnumerable<string> roots)
    {
        if (!File.Exists(path)) return Failed("There is no file at " + path + ".");

        string text;
        if (Path.GetExtension(path).Equals(".pex", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                text = StripResultLine(PapyrusDecompiler.Decompile(path, assembly: false));
            }
            catch (Exception e)
            {
                return Failed(Path.GetFileName(path) + " could not be decompiled: " + e.Message);
            }

            // A body the decompiler could not structure comes back as an assembly listing on
            // purpose. That is not Papyrus, so lifting it would fail as a parse error somewhere in
            // the middle rather than saying what actually happened.
            if (text.Contains(".code", StringComparison.OrdinalIgnoreCase))
            {
                return Failed(Path.GetFileName(path)
                    + " has a function the decompiler could not turn back into Papyrus, so it came"
                    + " back as an assembly listing. Read it in the Papyrus panel instead.");
            }
        }
        else if (Path.GetExtension(path).Equals(".psc", StringComparison.OrdinalIgnoreCase))
        {
            try { text = File.ReadAllText(path); }
            catch (IOException e) { return Failed(Path.GetFileName(path) + " could not be read: " + e.Message); }
        }
        else
        {
            return Failed(Path.GetFileName(path) + " is not a .psc or a .pex.");
        }

        var parsed = PapyrusParser.Parse(text, Path.GetFileName(path));
        var lifted = new GraphLifter(GraphCompiler.IndexFor(RootsFor(path, roots))).Lift(parsed);
        return new Result(lifted.Success ? lifted.Document : null, lifted.Diagnostics, null);
    }

    /// <summary>
    /// The import roots, with the script's own folder in front.
    /// </summary>
    /// <remarks>
    /// Without it a script that passes Self where its base type is wanted cannot be checked, because
    /// nothing on the roots says what this script extends. The same publishing step every compile
    /// path here already does.
    /// </remarks>
    private static IReadOnlyList<string> RootsFor(string path, IEnumerable<string> roots)
    {
        var list = roots.Where(Directory.Exists).ToList();
        var folder = Path.GetDirectoryName(Path.GetFullPath(path));
        if (folder != null && !list.Contains(folder, StringComparer.OrdinalIgnoreCase)) list.Insert(0, folder);
        return list;
    }

    private static Result Failed(string message) =>
        new(null, Array.Empty<GraphDiagnostic>(), message);

    /// <summary>The decompiler prefixes a RESULT: line for its prose callers.</summary>
    private static string StripResultLine(string decompiled)
    {
        var newline = decompiled.IndexOf('\n');
        return newline >= 0 && decompiled.StartsWith("RESULT:", StringComparison.Ordinal)
            ? decompiled[(newline + 1)..]
            : decompiled;
    }
}
