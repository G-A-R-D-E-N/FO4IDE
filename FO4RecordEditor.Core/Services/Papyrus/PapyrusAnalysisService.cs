using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace FO4RecordEditor.Services.Papyrus;

/// <summary>
/// Text-formatted front door to the Papyrus front end, for the MCP tools and the GUI panel.
/// </summary>
/// <remarks>
/// Everything here is source-only analysis: it reads .psc, it never runs the Creation Kit's
/// <c>PapyrusCompiler.exe</c>, and it never writes anything. So it answers "does this parse", "what
/// does this script declare" and "where is this declared" on a machine with no CK installed, which
/// is exactly the gap the compile path cannot cover today.
/// <para>
/// It is deliberately not a compiler and does not pretend to be one. <see cref="Check"/> reports
/// syntax errors, not type errors -- a script that passes here can still fail to compile for a
/// misspelled function name or a bad cast, and saying otherwise would be worse than saying nothing.
/// </para>
/// <para>
/// Lives in Core rather than beside <c>PapyrusService</c> so it builds and tests without WPF.
/// </para>
/// </remarks>
public static class PapyrusAnalysisService
{
    /// <summary>Cap on files reported individually, so a whole-mod check stays readable.</summary>
    private const int MaxReportedFiles = 200;

    // -----------------------------------------------------------------------------------------
    // JSON surface, for the editor panel.
    //
    // The text-returning methods above are shaped for an MCP tool result, which a model reads as
    // prose. An editor needs positions it can select on, so these return JSON and -- crucially --
    // take the buffer TEXT rather than a path. "Errors as you type" is about the unsaved buffer;
    // re-reading the file from disk would analyse the last save instead of what is on screen.
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// Parses buffer text and returns diagnostics and the outline together, as JSON.
    /// </summary>
    /// <remarks>
    /// One call, not two, because the panel wants both on every keystroke and parsing twice for
    /// them would double the work for nothing -- they come out of the same tree.
    /// </remarks>
    /// <param name="text">The editor buffer.</param>
    /// <param name="filePath">Path the buffer came from, used only to label diagnostics. May be null.</param>
    public static string AnalyzeJson(string text, string? filePath = null)
    {
        PapyrusScript script;
        try
        {
            script = PapyrusParser.Parse(text ?? string.Empty, filePath);
        }
        catch (Exception ex)
        {
            // The parser is not supposed to be able to throw, and the corpus sweep exists to keep
            // it that way. If it ever does, the panel should say so rather than go blank.
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

    /// <summary>
    /// Resolves the symbol at an offset in buffer text, as JSON. Drives both hover and go-to-definition.
    /// </summary>
    /// <remarks>
    /// <c>resolved: false</c> is a real answer, not a failure, and the panel is expected to show it
    /// as "no declaration found" rather than as an error. Phase 1 has no type checker; see
    /// <see cref="PapyrusSymbols.FindDefinition"/>.
    /// <para>
    /// <c>sameFile</c> tells the panel whether it can jump within the open buffer or has to open
    /// another file, which it cannot decide from the path alone when the buffer is unsaved.
    /// </para>
    /// </remarks>
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

        // An index is only needed to follow Extends and to resolve other scripts by name, both of
        // which want a real path. With an unsaved scratch buffer there is nothing to root it at, so
        // the lookup degrades to this script's own members rather than failing.
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

    /// <summary>
    /// Parses a .psc file or a folder of them and reports syntax, name and type diagnostics.
    /// </summary>
    /// <remarks>
    /// Syntax comes from the parser and needs nothing else. Names and types come from
    /// <see cref="PapyrusResolver"/> and <see cref="PapyrusTypeChecker"/>, which need the scripts a
    /// file refers to, so they are only as good as the roots they are given -- and they know it.
    /// <para>
    /// <b>Both stay silent when the sources were incomplete</b>, which is what makes their 100%-clean
    /// figure over the vanilla scripts mean anything: a script whose parent the index cannot find
    /// would otherwise report every inherited member as undefined. The summary counts those files
    /// separately rather than folding them into "clean", so a caller can tell "nothing wrong with it"
    /// from "could not tell".
    /// </para>
    /// </remarks>
    /// <param name="source">A .psc path, or a folder (recursed).</param>
    /// <param name="all">Unused for a single file; a folder is always recursed.</param>
    /// <param name="semantic">Also resolve names and check types, not just parse.</param>
    /// <param name="imports">Extra source roots, semicolon-separated. Base scripts are added automatically.</param>
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

            // A parser cannot throw here -- the corpus sweep exists to keep it that way -- but the
            // semantic layers walk into other files, so a bad one on the roots must not take the run
            // down with it.
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

    /// <summary>
    /// Compiles <c>.psc</c> to <c>.pex</c> in process, with no Creation Kit.
    /// </summary>
    /// <remarks>
    /// The front door to <see cref="PapyrusCompiler"/>, which is the thing issue #78 was for: the
    /// tool could read, understand and decompile Papyrus on a machine with no CK, but it could not
    /// produce a script. It can now.
    /// <para>
    /// <b>A refusal here is a result, not a crash.</b> The back end emits nothing it cannot justify:
    /// a callee with no declaration on the import roots has unknown arity the moment optional
    /// parameters exist, so it reports and writes no file rather than emitting a call of the right
    /// shape and the wrong length. That is almost always a missing <c>imports</c> root, and the
    /// summary says so instead of leaving the caller to guess.
    /// </para>
    /// </remarks>
    /// <param name="source">A <c>.psc</c> file, or a folder of them (recursed).</param>
    /// <param name="output">Where the <c>.pex</c> go. Default is the source folder.</param>
    /// <param name="imports">Extra source roots, semicolon-separated. Base scripts are added automatically.</param>
    /// <param name="release">
    /// Strips <c>DebugOnly</c> and <c>BetaOnly</c> calls, which is what the Creation Kit's <c>-r</c>
    /// does. Off by default: a call into excluded code is not compiled at all, and silently deleting
    /// an author's logging is the worse failure.
    /// </param>
    /// <param name="debugInfo">Line numbers, property groups and struct order. What a stack trace reads.</param>
    /// <param name="flagFile">An <c>Institute_Papyrus_Flags.flg</c>; null finds one or uses the built-in table.</param>
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

            // A namespaced script is written into namespace folders, the way the game loads it and
            // the way the Creation Kit writes it: MyNS:MyScript goes to <out>/MyNS/MyScript.pex.
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

            // Compiling without a Creation Kit still needs the vanilla base script SOURCES on the
            // import path. That is a much weaker requirement than needing PapyrusCompiler.exe -- they
            // are just .psc text, they are redistributed with plenty of modding resource packs, and
            // they are not Windows-only -- but a script that mentions Form or ObjectReference cannot
            // compile without them, and saying "no Creation Kit needed" without saying this would be
            // an overclaim.
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

    /// <summary>Lists everything a script declares: the outline behind go-to-symbol.</summary>
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

    /// <summary>
    /// Resolves the symbol at a source position, returning its declaration site and signature.
    /// </summary>
    /// <param name="source">The .psc file the position is in.</param>
    /// <param name="line">1-based line.</param>
    /// <param name="column">1-based column.</param>
    /// <param name="imports">
    /// Extra source roots to search, semicolon-separated. The file's own folder and the detected
    /// base-script roots are always included.
    /// </param>
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

    /// <summary>
    /// Builds an index rooted at the script's own folder, the caller's roots, and the base scripts.
    /// </summary>
    /// <remarks>
    /// Order matters and mirrors <c>PapyrusService.Compile</c>: caller-supplied roots first, then the
    /// detected base-script roots, so a mod's own copy of a base script shadows the vanilla one the
    /// same way it would at compile time.
    /// <para>
    /// The file's own folder goes first, but a namespaced script is not rooted at its own folder --
    /// <c>MyNS/MyScript.psc</c> is <c>MyNS:MyScript</c> relative to the folder *above* MyNS. The
    /// index's bare-name fallback covers that without needing to guess how many levels to ascend,
    /// which is the same trap the closed namespace-ascent fix was about.
    /// </para>
    /// </remarks>
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

    /// <summary>
    /// The roots a folder of scripts implies, before anything the caller adds.
    /// </summary>
    /// <remarks>
    /// The folder itself, plus its <c>Source/User</c> and <c>Source</c> ancestors when the path has
    /// them. That layout is what the whole toolchain writes -- a namespaced script lives at
    /// <c>Source/User/MyNS/MyScript.psc</c> and is <c>MyNS:MyScript</c> relative to
    /// <c>Source/User</c>, not to its own folder -- so rooting only at the file's folder makes a
    /// script resolve its siblings and nothing else. The index's bare-name fallback papers over some
    /// of that; these roots mean it does not have to.
    /// <para>
    /// It walks up rather than guessing a depth from the declared ScriptName, which is the trap the
    /// closed namespace-ascent fix was about: an unbounded ascent driven by a name the file chose
    /// hands an arbitrarily shallow folder to a recursive walk.
    /// </para>
    /// </remarks>
    public static IEnumerable<string> NaturalRootsFor(string folder)
    {
        // GetFullPath normalises an alternate separator (a caller on Windows may well hand us
        // forward slashes, and a JSON tool argument usually does) but it preserves a trailing one,
        // which would make the same root compare unequal to itself when added twice.
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

    /// <summary>Converts a 1-based line and column into a 0-based offset, or -1 if out of range.</summary>
    /// <remarks>
    /// Public because every caller that has a caret has it as a line and column and every query
    /// here wants an offset. Out of range returns -1 rather than clamping: a clamped position would
    /// silently answer about the wrong symbol.
    /// </remarks>
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

        // PapyrusFileWalk, not Directory.EnumerateFiles(RecurseSubdirectories). The framework's
        // recursive form skips Hidden and System by default, which drops everything under a dotted
        // directory -- a scripts tree inside a .git checkout, a worktree, or any dot-prefixed folder
        // vanishes silently -- and IgnoreInaccessible does not cover the IOException a Proton
        // prefix's /proc symlinks throw, which aborts the whole walk rather than one subtree.
        //
        // The index has always used the walk. Until this used it too, the two disagreed about which
        // files exist: a folder compile could report "0 files" on a tree the resolver reads happily.
        files.AddRange(PapyrusFileWalk.EnumerateFiles(source, "*.psc"));
        files.Sort(StringComparer.OrdinalIgnoreCase);
        return files;
    }
}
