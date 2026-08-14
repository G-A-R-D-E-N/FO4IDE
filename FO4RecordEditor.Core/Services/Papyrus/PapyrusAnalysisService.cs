using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace FO4RecordEditor.Services.Papyrus;


















public static class PapyrusAnalysisService
{

    private const int MaxReportedFiles = 200;



















    public static string AnalyzeJson(string text, string? filePath = null)
    {
        PapyrusScript script;
        try
        {
            script = PapyrusParser.Parse(text ?? string.Empty, filePath);
        }
        catch (Exception ex)
        {


            return JsonConvert.SerializeObject(new { error = ex.Message });
        }

        var payload = new
        {
            script = script.Name,
            extends = script.Extends,
            errorCount = script.Diagnostics.Count(d => d.Severity == PapyrusSeverity.Error),
            diagnostics = script.Diagnostics.Select(d => new
            {
                code = d.Code,
                severity = d.Severity == PapyrusSeverity.Error ? "error" : "warning",
                message = d.Message,
                line = d.Span.Line,
                column = d.Span.Column,
                start = d.Span.Start,
                length = d.Span.Length,
            }).ToList(),
            symbols = PapyrusSymbols.DocumentSymbols(script).Select(s => new
            {
                name = s.Name,
                kind = s.Kind.ToString(),
                signature = s.Signature,
                documentation = s.Documentation,
                container = s.Container,
                line = s.Span.Line,
                column = s.Span.Column,
                start = s.Span.Start,
                nameStart = s.NameSpan.Start,
                nameLength = s.NameSpan.Length,
                nameLine = s.NameSpan.Line,
            }).ToList(),
        };
        return JsonConvert.SerializeObject(payload);
    }













    public static string SymbolAtJson(string text, string? filePath, int offset, string? imports = null)
    {
        PapyrusScript script;
        try
        {
            script = PapyrusParser.Parse(text ?? string.Empty, filePath);
        }
        catch (Exception ex)
        {
            return JsonConvert.SerializeObject(new { error = ex.Message });
        }




        var index = string.IsNullOrWhiteSpace(filePath)
            ? new PapyrusScriptIndex()
            : BuildIndex(filePath!, imports);

        var symbol = PapyrusSymbols.FindDefinition(index, script, offset);
        if (symbol == null) return JsonConvert.SerializeObject(new { resolved = false });

        var sameFile = symbol.File == null
            || (filePath != null && string.Equals(
                Path.GetFullPath(symbol.File),
                Path.GetFullPath(filePath),
                StringComparison.OrdinalIgnoreCase));

        return JsonConvert.SerializeObject(new
        {
            resolved = true,
            name = symbol.Name,
            kind = symbol.Kind.ToString(),
            signature = symbol.Signature,
            documentation = symbol.Documentation,
            container = symbol.Container,
            file = symbol.File,
            sameFile,
            line = symbol.NameSpan.Line,
            column = symbol.NameSpan.Column,
            start = symbol.NameSpan.Start,
            length = symbol.NameSpan.Length,
        });
    }




















    public static string Check(string source, bool all = true, bool semantic = true, string? imports = null)
    {
        if (string.IsNullOrWhiteSpace(source)) return "No source given.";

        var files = ResolveSources(source, out var error);
        if (error != null) return error;
        if (files.Count == 0) return $"No .psc files found under '{source}'.";

        PapyrusScriptIndex? index = null;
        PapyrusResolver? resolver = null;
        PapyrusTypeChecker? checker = null;
        if (semantic)
        {
            index = new PapyrusScriptIndex();
            var start = Directory.Exists(source) ? source : Path.GetDirectoryName(Path.GetFullPath(source))!;
            foreach (var root in NaturalRootsFor(start)) index.AddRoot(root);
            if (!string.IsNullOrWhiteSpace(imports))
            {
                foreach (var root in imports!.Split(';', StringSplitOptions.RemoveEmptyEntries))
                    index.AddRoot(root.Trim().Trim('"'));
            }
            foreach (var root in ToolPaths.PapyrusBaseImports()) index.AddRoot(root);
            resolver = new PapyrusResolver(index);
            checker = new PapyrusTypeChecker(index);
        }

        var sb = new StringBuilder();
        var clean = 0;
        var syntaxErrors = 0;
        var semanticErrors = 0;
        var incompleteSources = 0;
        var reported = 0;

        foreach (var file in files)
        {
            PapyrusScript script;
            try
            {
                script = index?.ParseCached(file) ?? PapyrusParser.ParseFile(file);
                if (script == null) throw new IOException("could not be parsed");
            }
            catch (Exception ex)
            {
                syntaxErrors++;
                if (reported++ < MaxReportedFiles) sb.AppendLine($"{file}: could not be read: {ex.Message}");
                continue;
            }

            var errors = script.Diagnostics.Where(d => d.Severity == PapyrusSeverity.Error).ToList();
            if (errors.Count > 0)
            {
                syntaxErrors++;
                if (reported++ < MaxReportedFiles)
                {
                    foreach (var d in errors) sb.AppendLine(d.ToString());
                }
                continue;
            }

            if (resolver == null) { clean++; continue; }




            List<PapyrusDiagnostic> found;
            bool sourcesComplete;
            try
            {
                var resolution = resolver.Resolve(script);
                sourcesComplete = resolution.BaseChainComplete;
                found = resolution.Diagnostics.Concat(checker!.Check(resolution))
                    .Where(d => d.Severity == PapyrusSeverity.Error)
                    .ToList();
            }
            catch (Exception ex)
            {
                semanticErrors++;
                if (reported++ < MaxReportedFiles) sb.AppendLine($"{file}: {ex.GetType().Name}: {ex.Message}");
                continue;
            }

            if (!sourcesComplete) incompleteSources++;
            if (found.Count == 0) { clean++; continue; }

            semanticErrors++;
            if (reported++ >= MaxReportedFiles) continue;
            foreach (var d in found) sb.AppendLine(d.ToString());
        }

        var summary = new StringBuilder();
        summary.Append($"RESULT: {clean} clean, {syntaxErrors} with syntax errors");
        if (semantic) summary.Append($", {semanticErrors} with name or type errors");
        summary.AppendLine($", of {files.Count} file(s).");

        if (!semantic)
        {
            summary.AppendLine(
                "Syntax only. This does not resolve names or check types, so a clean result does not "
                + "guarantee the script compiles. Set semantic=true, or use compile_papyrus.");
        }
        else if (incompleteSources > 0)
        {
            summary.AppendLine(
                $"{incompleteSources} file(s) referred to scripts that are not on the roots, so name and type "
                + "reporting was switched off for those -- otherwise every inherited member reads as undefined. "
                + "They are neither confirmed clean nor confirmed broken. Add the missing root with 'imports'.");
        }

        if (reported > MaxReportedFiles) sb.AppendLine($"({reported - MaxReportedFiles} more file(s) not listed.)");

        var detail = sb.ToString().TrimEnd();
        return detail.Length == 0
            ? summary.ToString().TrimEnd()
            : summary.ToString().TrimEnd() + Environment.NewLine + Environment.NewLine + detail;
    }


























    public static string Compile(
        string source,
        string? output = null,
        string? imports = null,
        bool release = false,
        bool debugInfo = true,
        string? flagFile = null)
    {
        if (string.IsNullOrWhiteSpace(source)) return "No source given.";

        source = source.Trim().Trim('"');
        if (File.Exists(source) && source.EndsWith(".pas", StringComparison.OrdinalIgnoreCase))
        {
            return "That is a Papyrus ASSEMBLY listing (.pas), not source. Compile the .psc instead; "
                 + "decompile again with assembly=false to get one.";
        }

        var files = ResolveSources(source, out var error);
        if (error != null) return error;
        if (files.Count == 0) return $"No .psc files found under '{source}'.";

        var rootFolder = Directory.Exists(source) ? source : Path.GetDirectoryName(Path.GetFullPath(source))!;
        var outputFolder = string.IsNullOrWhiteSpace(output) ? rootFolder : output!.Trim().Trim('"');

        var index = new PapyrusScriptIndex();
        foreach (var root in NaturalRootsFor(rootFolder)) index.AddRoot(root);
        if (!string.IsNullOrWhiteSpace(imports))
        {
            foreach (var root in imports!.Split(';', StringSplitOptions.RemoveEmptyEntries))
                index.AddRoot(root.Trim().Trim('"'));
        }
        int baseRoots = 0;
        foreach (var root in ToolPaths.PapyrusBaseImports()) { index.AddRoot(root); baseRoots++; }

        var compiler = new PapyrusCompiler(index);
        var options = new PapyrusCompileOptions
        {
            EmitDebugInfo = debugInfo,
            EmitDebugOnlyCode = !release,
            EmitBetaOnlyCode = !release,
            FlagFile = flagFile,
        };

        var sb = new StringBuilder();
        int succeeded = 0, failed = 0, reported = 0, incompleteSources = 0;

        foreach (var file in files)
        {
            PapyrusCompileResult result;
            try
            {
                result = compiler.CompileFile(file, options);
            }
            catch (Exception ex)
            {
                failed++;
                if (reported++ < MaxReportedFiles) sb.AppendLine($"{file}: {ex.GetType().Name}: {ex.Message}");
                continue;
            }

            if (!result.Success)
            {
                failed++;
                if (!result.SourcesComplete) incompleteSources++;
                if (reported++ >= MaxReportedFiles) continue;
                foreach (var d in result.Errors.Take(10)) sb.AppendLine(d.ToString());
                continue;
            }



            var target = Path.Combine(
                outputFolder,
                Path.Combine(result.Script!.Name.Split(':', StringSplitOptions.RemoveEmptyEntries)) + ".pex");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                result.Pex!.WriteFile(target);
                succeeded++;
            }
            catch (Exception ex)
            {
                failed++;
                if (reported++ < MaxReportedFiles) sb.AppendLine($"{target}: could not be written: {ex.Message}");
            }
        }

        var summary = new StringBuilder();
        summary.AppendLine($"RESULT: {succeeded} succeeded, {failed} failed, of {files.Count} file(s) (output -> {outputFolder})");
        summary.AppendLine("engine : built-in (no Creation Kit)");
        if (release) summary.AppendLine("mode   : release; DebugOnly and BetaOnly calls are not compiled");
        if (reported > MaxReportedFiles) summary.AppendLine($"({reported - MaxReportedFiles} more file(s) not listed.)");

        if (incompleteSources > 0)
        {
            summary.AppendLine();
            summary.AppendLine(
                $"{incompleteSources} of the failures could not see every script they refer to. The back end "
                + "refuses rather than guessing: an unresolved callee has unknown arity once optional "
                + "parameters exist. Add the missing source root with 'imports' (semicolon-separated).");







            if (baseRoots == 0)
            {
                summary.AppendLine(
                    "No base-script root was detected on this machine at all, so every vanilla type "
                    + "(Form, ObjectReference, Actor, ...) is unknown. The built-in engine needs no "
                    + "PapyrusCompiler.exe, but it does need the vanilla base script SOURCES to resolve "
                    + "against. Point Settings > Papyrus base imports, or PAPYRUS_BASE_IMPORTS, at a "
                    + "folder of them.");
            }
        }

        var detail = sb.ToString().TrimEnd();
        return detail.Length == 0
            ? summary.ToString().TrimEnd()
            : summary.ToString().TrimEnd() + Environment.NewLine + Environment.NewLine + detail;
    }


    public static string Outline(string source)
    {
        if (string.IsNullOrWhiteSpace(source)) return "No source given.";
        if (!File.Exists(source)) return $"Not found: '{source}'.";

        PapyrusScript script;
        try
        {
            script = PapyrusParser.ParseFile(source);
        }
        catch (Exception ex)
        {
            return $"Could not read '{source}': {ex.Message}";
        }

        var symbols = PapyrusSymbols.DocumentSymbols(script);
        var sb = new StringBuilder();
        sb.AppendLine($"RESULT: {symbols.Count} symbol(s) in {Path.GetFileName(source)}.");
        var errors = script.Diagnostics.Count(d => d.Severity == PapyrusSeverity.Error);
        if (errors > 0) sb.AppendLine($"({errors} syntax error(s); the outline is what could still be read.)");
        sb.AppendLine();

        foreach (var symbol in symbols)
        {
            var where = $"({symbol.Span.Line},{symbol.Span.Column})";
            var container = symbol.Container == null ? string.Empty : $"  [{symbol.Container}]";
            sb.AppendLine($"{where,-12} {symbol.Kind,-12} {symbol.Signature}{container}");
        }
        return sb.ToString().TrimEnd();
    }











    public static string Definition(string source, int line, int column, string? imports = null)
    {
        if (string.IsNullOrWhiteSpace(source)) return "No source given.";
        if (!File.Exists(source)) return $"Not found: '{source}'.";

        string text;
        try
        {
            text = File.ReadAllText(source);
        }
        catch (Exception ex)
        {
            return $"Could not read '{source}': {ex.Message}";
        }

        var offset = OffsetOf(text, line, column);
        if (offset < 0) return $"Position ({line},{column}) is outside '{Path.GetFileName(source)}'.";

        var index = BuildIndex(source, imports);
        var script = index.ParseCached(source) ?? PapyrusParser.Parse(text, source);

        var symbol = PapyrusSymbols.FindDefinition(index, script, offset);
        if (symbol == null)
        {
            return "RESULT: not resolved." + Environment.NewLine + Environment.NewLine +
                   "No declaration could be found for that position. This front end resolves by name " +
                   "(locals, parameters, this script's members, its Extends chain, imports, and script " +
                   "names); it has no type checker, so a member reached through an expression whose type " +
                   "is not written down cannot be resolved. It returns nothing rather than guess.";
        }

        var sb = new StringBuilder();
        sb.AppendLine($"RESULT: {symbol.Kind} {symbol.Name}");
        sb.AppendLine(symbol.Signature);
        if (symbol.Container != null) sb.AppendLine($"in: {symbol.Container}");
        sb.AppendLine($"at: {symbol.File ?? "<same file>"}({symbol.NameSpan.Line},{symbol.NameSpan.Column})");
        if (!string.IsNullOrWhiteSpace(symbol.Documentation))
        {
            sb.AppendLine();
            sb.AppendLine(symbol.Documentation!.Trim());
        }
        return sb.ToString().TrimEnd();
    }















    public static PapyrusScriptIndex BuildIndex(string sourceFile, string? imports)
    {
        var index = new PapyrusScriptIndex();

        var folder = Path.GetDirectoryName(Path.GetFullPath(sourceFile));
        if (folder != null) index.AddRoot(folder);

        if (!string.IsNullOrWhiteSpace(imports))
        {
            foreach (var root in imports!.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                index.AddRoot(root.Trim());
            }
        }

        foreach (var root in ToolPaths.PapyrusBaseImports()) index.AddRoot(root);

        return index;
    }

















    public static IEnumerable<string> NaturalRootsFor(string folder)
    {



        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        yield return full;

        var parts = full.Split(Path.DirectorySeparatorChar);
        for (int i = parts.Length - 1; i > 0; i--)
        {
            if (!parts[i].Equals("Source", StringComparison.OrdinalIgnoreCase)) continue;

            var sourceRoot = string.Join(Path.DirectorySeparatorChar, parts.Take(i + 1));
            var user = Path.Combine(sourceRoot, "User");
            if (Directory.Exists(user)) yield return user;
            yield return sourceRoot;
            yield break;
        }
    }







    public static int OffsetOf(string text, int line, int column)
    {
        if (line < 1 || column < 1) return -1;

        var offset = 0;
        var currentLine = 1;
        while (currentLine < line)
        {
            var newline = text.IndexOf('\n', offset);
            if (newline < 0) return -1;
            offset = newline + 1;
            currentLine++;
        }

        var lineEnd = text.IndexOf('\n', offset);
        if (lineEnd < 0) lineEnd = text.Length;
        var target = offset + column - 1;
        return target > lineEnd ? -1 : target;
    }

    private static List<string> ResolveSources(string source, out string? error)
    {
        error = null;
        var files = new List<string>();

        if (File.Exists(source))
        {
            if (!source.EndsWith(".psc", StringComparison.OrdinalIgnoreCase))
            {
                error = $"'{Path.GetFileName(source)}' is not a .psc. This reads Papyrus source; " +
                        "use decompile_papyrus for a compiled .pex.";
                return files;
            }
            files.Add(source);
            return files;
        }

        if (!Directory.Exists(source))
        {
            error = $"Not found: '{source}'.";
            return files;
        }









        files.AddRange(PapyrusFileWalk.EnumerateFiles(source, "*.psc"));
        files.Sort(StringComparer.OrdinalIgnoreCase);
        return files;
    }
}
