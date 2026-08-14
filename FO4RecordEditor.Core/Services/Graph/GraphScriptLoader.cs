using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FO4RecordEditor.Services.Papyrus;

namespace FO4RecordEditor.Services.Graph;










public static class GraphScriptLoader
{

    public sealed record Result(GraphDocument? Document, IReadOnlyList<GraphDiagnostic> Diagnostics, string? Failure)
    {

        public bool Success => Document != null;
    }


    public static bool IsScript(string path) =>
        Path.GetExtension(path) is { } extension
        && (extension.Equals(".psc", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".pex", StringComparison.OrdinalIgnoreCase));












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









    private static IReadOnlyList<string> RootsFor(string path, IEnumerable<string> roots)
    {
        var list = roots.Where(Directory.Exists).ToList();
        var folder = Path.GetDirectoryName(Path.GetFullPath(path));
        if (folder != null && !list.Contains(folder, StringComparer.OrdinalIgnoreCase)) list.Insert(0, folder);
        return list;
    }

    private static Result Failed(string message) =>
        new(null, Array.Empty<GraphDiagnostic>(), message);


    private static string StripResultLine(string decompiled)
    {
        var newline = decompiled.IndexOf('\n');
        return newline >= 0 && decompiled.StartsWith("RESULT:", StringComparison.Ordinal)
            ? decompiled[(newline + 1)..]
            : decompiled;
    }
}
