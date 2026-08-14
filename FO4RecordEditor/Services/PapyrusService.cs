using System.Diagnostics;
using System.IO;
using System.Text;
using FO4RecordEditor.Services.Papyrus;

namespace FO4RecordEditor.Services;

/// <summary>
/// Compiles Papyrus source (.psc -> .pex), so the AI can author a script's records AND compile the
/// script in one workflow.
/// </summary>
/// <remarks>
/// Two engines. <b>Built-in</b> is this tool's own compiler in
/// <c>FO4RecordEditor.Core/Services/Papyrus/</c> -- lexer, parser, script index, resolver, type
/// checker, code generator, <c>.pex</c> writer -- and needs no Creation Kit at all. <b>Creation
/// Kit</b> shells out to <c>PapyrusCompiler.exe</c>. <see cref="Engine.Auto"/>, the default, prefers
/// the CK when one is installed, so nothing about an existing setup changes, and falls back to the
/// built-in engine otherwise, which is the whole point of issue #78: the tool no longer stops at the
/// edge of what a CK-less machine can do.
/// <para>
/// The CK compiler implicitly adds its working directory as an import folder, so it is always run
/// from the compile's own work dir (the script's folder, or the namespace root when the file's
/// layout genuinely matches its ScriptName) rather than the host's CWD. Never point that at a large
/// tree: it walks every import root recursively. The built-in engine has no such hazard -- it
/// indexes lazily and parses on demand.
/// </para>
/// </remarks>
public static class PapyrusService
{
    private const string DefaultFlags = "Institute_Papyrus_Flags.flg";

    /// <summary>Which compiler runs.</summary>
    public enum Engine
    {
        /// <summary>The Creation Kit when it is installed, the built-in compiler when it is not.</summary>
        Auto,

        /// <summary>This tool's own compiler. No Creation Kit involved.</summary>
        BuiltIn,

        /// <summary>Shell out to <c>PapyrusCompiler.exe</c>.</summary>
        CreationKit,
    }

    /// <summary>Parses an engine name, defaulting to <see cref="Engine.Auto"/> for anything unrecognised.</summary>
    public static Engine ParseEngine(string? name) => (name ?? "").Trim().ToLowerInvariant() switch
    {
        "builtin" or "built-in" or "native" or "internal" => Engine.BuiltIn,
        "creationkit" or "creation-kit" or "ck" or "external" => Engine.CreationKit,
        _ => Engine.Auto,
    };

    public static string Compile(string source, string? output, string? imports, string? flags,
        bool all, bool optimize, bool release, string? compilerPath,
        string? engine = null, bool debugInfo = true)
    {
        if (string.IsNullOrWhiteSpace(source)) return "Provide 'source' (a .psc file or a folder of scripts).";
        source = source.Trim().Trim('"');
        bool isDir = Directory.Exists(source);
        bool isFile = File.Exists(source);
        if (!isDir && !isFile) return $"Source not found: {source}";

        var chosen = ParseEngine(engine);
        var exePath = string.IsNullOrWhiteSpace(compilerPath) ? ToolPaths.PapyrusCompiler() : compilerPath.Trim().Trim('"');
        bool haveCreationKit = !string.IsNullOrWhiteSpace(exePath) && File.Exists(exePath);

        if (chosen == Engine.BuiltIn || (chosen == Engine.Auto && !haveCreationKit))
        {
            // A .pas rejection is worth keeping identical between the engines; the built-in one
            // makes the same check for the same reason.
            var result = PapyrusAnalysisService.Compile(
                source, output, imports, release, debugInfo,
                string.IsNullOrWhiteSpace(flags) ? null : flags.Trim().Trim('"'));

            if (chosen == Engine.Auto)
            {
                result += Environment.NewLine + Environment.NewLine
                    + "No PapyrusCompiler.exe was found, so the built-in compiler was used. That is not a "
                    + "fallback to something lesser -- it is measured against the Creation Kit's own output on "
                    + "real scripts -- but if you meant to use the CK, point at it with compiler_path or "
                    + ToolPaths.Describe("papyrus") + ".";
            }
            if (optimize)
            {
                result += Environment.NewLine + Environment.NewLine
                    + "Note: optimize (-op) is a Creation Kit compiler switch and was ignored.";
            }
            return result;
        }

        // .pas is Papyrus ASSEMBLY, not source. PapyrusCompiler compiles .psc; .pas is assembled by
        // PapyrusAssembler.exe (a different tool). This is the usual "I compiled the decompiler's
        // Assembly-listing output" mistake -- point the user at the .psc path instead.
        if (isFile && source.EndsWith(".pas", StringComparison.OrdinalIgnoreCase))
            return "That's a Papyrus ASSEMBLY file (.pas), not source. PapyrusCompiler compiles .psc SOURCE.\n" +
                   "Fix: Decompile this script again with 'Assembly listing' UNCHECKED to get a .psc, then compile that.\n" +
                   "(.pas is only used by PapyrusAssembler.exe, a separate tool, and is not what you recompile.)";

        var exe = exePath;
        if (!haveCreationKit)
            return "PapyrusCompiler.exe not found, and engine='creationkit' was asked for explicitly. Pass " +
                   "compiler_path pointing at it, or " + ToolPaths.Describe("papyrus") + ". " +
                   "Or use engine='builtin', which needs no Creation Kit.";

        // Determine the compile target + working dir, handling namespaced scripts. The FO4 compiler
        // derives a script's object name from its path RELATIVE to an import root, so a namespaced
        // script "IHO:IHO_Foo" must live at <root>\IHO\IHO_Foo.psc AND be targeted by its object name
        // ("IHO:IHO_Foo"), not the bare filename. For a single namespaced file we read its ScriptName,
        // target the object name, and use the namespace's parent folder as the import root.
        string workDir, target;
        string? stagingDir = null;
        if (isDir)
        {
            // The compiler derives each script's object name from its path relative to an import root,
            // so namespaced scripts ("IHO:IHO_Foo") sitting FLAT in a folder fail to read. Stage every
            // .psc into a temp tree matching its declared namespace (IHO\IHO_Foo.psc), then compile
            // that -- makes both flat and already-structured folders work.
            stagingDir = StageNamespacedFolder(source);
            workDir = stagingDir ?? source;
            target = workDir;
        }
        else
        {
            string full = Path.GetFullPath(source);
            string fileDir = Path.GetDirectoryName(full)!;
            string objName = "";
            try { objName = ExtractScriptName(File.ReadAllText(full)); } catch { }
            // Ascend one folder per namespace segment to reach the import root -- but ONLY while the
            // folder being left is actually named after that segment. The declared ScriptName drives
            // the depth, so an unbounded ascent walks up as many levels as the script cares to claim
            // and hands the result to the compiler as both an import root and its working directory:
            // the same whole-tree enumeration the parent-folder import root used to cause, reachable
            // through a differently-shaped path. A layout that doesn't match was never going to
            // resolve anyway (the compiler looks for <root>\A\B\Foo.psc), so it failed after the
            // enumeration rather than instead of it -- this just fails fast and stays put.
            var segments = objName.Split(':', StringSplitOptions.RemoveEmptyEntries);
            int depth = segments.Length - 1;
            string root = fileDir;
            bool layoutMatches = depth > 0;
            for (int i = 0; i < depth; i++)
            {
                if (!string.Equals(Path.GetFileName(root), segments[depth - 1 - i], StringComparison.OrdinalIgnoreCase))
                { layoutMatches = false; break; }
                var up = Directory.GetParent(root)?.FullName;
                if (up == null) { layoutMatches = false; break; }
                root = up;
            }
            if (layoutMatches)
            {
                workDir = root;
                target = objName;
            }
            else
            {
                workDir = fileDir;
                target = Path.GetFileName(source);
            }
        }

        // Import roots: caller-supplied + base game scripts + the work dir.
        // The work dir's PARENT is deliberately NOT an import root. The compiler walks every import
        // root recursively, so a single .psc sitting anywhere shallow made it enumerate the parent's
        // whole tree -- for a script near the workspace root that is ~695k files, and it either
        // stalls or dies on a >260-char path long before it compiles anything. Sibling-folder
        // imports that used to work by accident now have to be passed explicitly via `imports`
        // (the tool's own parameter) or configured once in Settings > Papyrus base imports.
        var imp = new List<string>();
        if (!string.IsNullOrWhiteSpace(imports))
            imp.AddRange(imports.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                .Select(s => s.Trim('"')));
        foreach (var b in ToolPaths.PapyrusBaseImports()) imp.Add(b);
        imp.Add(workDir);
        var importArg = string.Join(";", imp.Distinct(StringComparer.OrdinalIgnoreCase));

        // Default output: the ORIGINAL source folder for a dir compile (not the temp staging dir).
        string outDir = !string.IsNullOrWhiteSpace(output) ? output.Trim().Trim('"') : (isDir ? source : workDir);
        try { Directory.CreateDirectory(outDir); } catch (Exception ex) { return $"Cannot create output dir '{outDir}': {ex.Message}"; }
        var args = new StringBuilder();
        args.Append('"').Append(target).Append('"');
        args.Append(" -f=\"").Append(string.IsNullOrWhiteSpace(flags) ? DefaultFlags : flags.Trim()).Append('"');
        args.Append(" -i=\"").Append(importArg).Append('"');
        args.Append(" -o=\"").Append(outDir).Append('"');
        if (all || isDir) args.Append(" -all");
        if (optimize) args.Append(" -op");
        if (release) args.Append(" -r");

        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = args.ToString(),
            WorkingDirectory = workDir,
        };

        var sb = new StringBuilder();
        try
        {
            // A batch of 90 scripts emits heavily on BOTH streams, so they must be drained
            // concurrently -- see ProcessRunner for why sequential reads deadlock.
            var run = ProcessRunner.Run(psi, TimeSpan.FromSeconds(300));
            if (!run.Started) return "Failed to start the Papyrus compiler process.";
            if (run.TimedOut) return "Papyrus compile timed out after 300s (killed).";

            // Not run.Combined: this parser splits on '\n' and must keep the untrimmed shape.
            var combined = (run.StdOut + "\n" + run.StdErr).Replace("\r", "");
            // Lead with a parse-friendly RESULT line (so Claude/the GUI can read success at a glance).
            var batch = combined.Split('\n').FirstOrDefault(l => l.Contains("Batch compile", StringComparison.OrdinalIgnoreCase));
            var mOk = System.Text.RegularExpressions.Regex.Match(batch ?? "", @"(\d+)\s+succeeded,\s+(\d+)\s+failed");
            if (mOk.Success)
                sb.AppendLine($"RESULT: {mOk.Groups[1].Value} succeeded, {mOk.Groups[2].Value} failed (output -> {outDir})");
            else
                sb.AppendLine($"RESULT: {(run.ExitCode == 0 ? "success" : "FAILED")} (output -> {outDir})");
            sb.AppendLine();
            sb.AppendLine($"compiler: {exe}");
            sb.AppendLine($"workdir : {workDir}");
            sb.AppendLine($"args    : {args}");
            sb.AppendLine();
            // Surface the lines that matter (errors + the summary) rather than the full per-file noise.
            var important = combined.Split('\n')
                .Where(l => l.Trim().Length > 0)
                .Where(l => l.Contains("error", StringComparison.OrdinalIgnoreCase)
                         || l.Contains("Compilation succeeded", StringComparison.OrdinalIgnoreCase)
                         || l.Contains("failed", StringComparison.OrdinalIgnoreCase)
                         || l.Contains("No output generated", StringComparison.OrdinalIgnoreCase)
                         || l.Contains("Batch compile", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (important.Count > 0) sb.AppendLine(string.Join("\n", important));
            else if (combined.Trim().Length > 0) sb.AppendLine(combined.Trim());
            sb.AppendLine();
            sb.AppendLine($"exit code: {run.ExitCode} ({(run.ExitCode == 0 ? "success" : "FAILED")})");
            if (run.ExitCode != 0)
            {
                var hint = DiagnoseDependencies(combined);
                if (hint.Length > 0) { sb.AppendLine(); sb.Append(hint); }
            }
            return sb.ToString();
        }
        catch (Exception ex) { return $"Failed to run the Papyrus compiler: {ex.Message}"; }
        finally
        {
            if (stagingDir != null) { try { Directory.Delete(stagingDir, true); } catch { } }
        }
    }

    // Stage every .psc under a folder into a temp tree whose layout matches each script's declared
    // namespace (IHO:IHO_Foo -> IHO\IHO_Foo.psc), so the compiler can resolve namespaced scripts even
    // when the input folder is flat. Returns the staging dir, or null if there's nothing to stage.
    private static string? StageNamespacedFolder(string folder)
    {
        string[] files;
        try { files = Directory.GetFiles(folder, "*.psc", SearchOption.AllDirectories); }
        catch { return null; }
        if (files.Length == 0) return null;

        var staging = Path.Combine(Path.GetTempPath(), "FO4RE_PapyrusStage_" + Guid.NewGuid().ToString("N").Substring(0, 10));
        try
        {
            foreach (var f in files)
            {
                string obj = "";
                try { obj = ExtractScriptName(File.ReadAllText(f)); } catch { }
                string rel = obj.Contains(':')
                    ? Path.Combine(obj.Split(':', StringSplitOptions.RemoveEmptyEntries)) + ".psc"
                    : Path.GetFileName(f);
                var dest = Path.Combine(staging, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(f, dest, overwrite: true);
            }
            return staging;
        }
        catch { try { Directory.Delete(staging, true); } catch { } return null; }
    }

    /// <summary>
    /// Decompile a compiled Papyrus .pex back to source (.psc) using the in-process FO4 decompiler
    /// (no external tool). 'source' is a single .pex or a folder. For a single file, the source text
    /// is returned inline unless write=true. For a folder, all .pex are decompiled and written to
    /// 'output' (default: the source folder) and a summary is returned. assembly=true emits a faithful
    /// bytecode disassembly instead of reconstructed source.
    /// </summary>
    public static string Decompile(string source, string? output, bool assembly, bool write)
    {
        if (string.IsNullOrWhiteSpace(source)) return "Provide 'source' (a .pex file or a folder of .pex).";
        source = source.Trim().Trim('"');
        string ext = assembly ? ".pas" : ".psc";

        if (File.Exists(source))
        {
            string text;
            try { text = PapyrusDecompiler.Decompile(source, assembly); }
            catch (Exception ex) { return $"Decompile failed for {Path.GetFileName(source)}: {ex.Message}"; }

            // Save when EITHER "write" is on OR an output folder was given (setting a folder = intent
            // to save). With neither, this is an inline preview only -- make that explicit so it's not
            // mistaken for a save that produced no file.
            bool doWrite = write || !string.IsNullOrWhiteSpace(output);
            if (!doWrite)
                return text + "\n\n; ---------------------------------------------------------------\n" +
                       "; NOT SAVED - shown in the OUTPUT pane only. Tick 'Write files to disk'\n" +
                       "; (or set an Output folder) to save this as a .psc.";
            string outDir = string.IsNullOrWhiteSpace(output) ? Path.GetDirectoryName(Path.GetFullPath(source))! : output.Trim().Trim('"');
            string outPath = NamespacedOutPath(outDir, text, Path.GetFileNameWithoutExtension(source), ext, assembly);
            try { File.WriteAllText(outPath, text); } catch (Exception ex) { return $"Cannot write '{outPath}': {ex.Message}"; }
            var preview = string.Join("\n", text.Replace("\r", "").Split('\n').Take(40));
            return $"SAVED -> {outPath}\n\n--- preview (first 40 lines) ---\n{preview}";
        }

        if (Directory.Exists(source))
        {
            // Recurse so a whole mod's Scripts folder (with .pex anywhere under it) decompiles fully.
            var files = Directory.GetFiles(source, "*.pex", SearchOption.AllDirectories);
            if (files.Length == 0) return $"RESULT: 0 decompiled. No .pex files found under {source}.";
            string outDir = string.IsNullOrWhiteSpace(output) ? source : output.Trim().Trim('"');
            try { Directory.CreateDirectory(outDir); } catch (Exception ex) { return $"Cannot create output dir '{outDir}': {ex.Message}"; }
            int ok = 0; var fails = new List<string>();
            foreach (var f in files)
            {
                try
                {
                    var text = PapyrusDecompiler.Decompile(f, assembly);
                    var outPath = NamespacedOutPath(outDir, text, Path.GetFileNameWithoutExtension(f), ext, assembly);
                    File.WriteAllText(outPath, text);
                    ok++;
                }
                catch (Exception ex) { fails.Add($"{Path.GetFileName(f)}: {ex.Message}"); }
            }
            var msg = $"RESULT: {ok}/{files.Length} decompiled -> {ext}\n" +
                      $"OUTPUT: {outDir}\n" +
                      "Namespaced scripts were written into namespace subfolders so they recompile as-is " +
                      "(point Compile at this folder with 'Compile all').";
            if (fails.Count > 0) msg += $"\nFailures ({fails.Count}):\n  " + string.Join("\n  ", fails.Take(20));
            return msg;
        }

        return $"Source not found: {source}";
    }

    // Compute the output path for decompiled text, placing namespaced scripts ("IHO:IHO_Foo") into
    // namespace subfolders (out\IHO\IHO_Foo.psc) so the result recompiles as-is. Assembly (.pas) and
    // un-namespaced scripts write flat. Creates the directory.
    private static string NamespacedOutPath(string outDir, string text, string fallbackBaseName, string ext, bool assembly)
    {
        string rel = fallbackBaseName + ext;
        if (!assembly)
        {
            var obj = ExtractScriptName(text);
            if (obj.Length > 0)
                rel = Path.Combine(obj.Split(':', StringSplitOptions.RemoveEmptyEntries)) + ext;
        }
        var full = Path.Combine(outDir, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        return full;
    }

    // Known FO4 Papyrus script-extender frameworks: (symbol substring, framework name, Nexus URL).
    // Matched against unresolved type/function names so a failed compile can point at the right mod.
    private static readonly (string match, string name, string url)[] KnownExtenders =
    {
        ("hudframework",      "HUDFramework",            "https://www.nexusmods.com/fallout4/mods/20309"),
        ("workshopframework", "Workshop Framework",      "https://www.nexusmods.com/fallout4/mods/35004"),
        ("wsfw",              "Workshop Framework",      "https://www.nexusmods.com/fallout4/mods/35004"),
        ("mcm",               "Mod Configuration Menu",  "https://www.nexusmods.com/fallout4/mods/21497"),
        ("baka",              "Baka Framework",          "https://www.nexusmods.com/fallout4/search/?gsearch=Baka+Framework&gsearchtype=mods"),
    };

    // When a compile fails on unresolved types/functions, identify the missing symbols and point the
    // user at the script extender that provides them (F4SE + vanilla base are already on the path, so a
    // remaining unresolved symbol almost always means another extender's Scripts\Source is missing).
    private static string DiagnoseDependencies(string compilerOutput)
    {
        var patterns = new[]
        {
            @"unknown type (\S+)",
            @"(\S+) is not a known user-defined script type",
            @"cannot convert to unknown type (\S+)",
            @"\b([A-Za-z_]\w*) is not a function or does not exist",
            @"[Ss]cript '?([A-Za-z_][\w:]*)'? (?:does not exist|could not be found|is not imported)",
        };
        var symbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pat in patterns)
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(compilerOutput, pat))
                if (m.Groups.Count > 1) symbols.Add(m.Groups[1].Value.Trim().Trim('"', '\''));
        if (symbols.Count == 0) return "";

        var sb = new StringBuilder();
        sb.AppendLine("--- DEPENDENCY HELP ---");
        sb.AppendLine("F4SE + vanilla base scripts are already on the import path. A remaining unresolved");
        sb.AppendLine("type/function usually means another script extender / framework is required:");
        sb.AppendLine("add that mod's Data\\Scripts\\Source folder to the Import roots and recompile.");
        sb.AppendLine();
        foreach (var sym in symbols.OrderBy(s => s))
        {
            var low = sym.ToLowerInvariant();
            var known = Array.Find(KnownExtenders, e => low.Contains(e.match));
            if (known.name != null)
                sb.AppendLine($"  - {sym}  ->  likely {known.name}.  Nexus: {known.url}");
            else if (low.Contains(':'))
                sb.AppendLine($"  - {sym}  ->  another script in the SAME mod (namespaced). Make sure its .psc/.pex is present in the source folder (some mods ship helper scripts as source-only, with no .pex to decompile).");
            else
                sb.AppendLine($"  - {sym}  ->  unknown source (a script extender plugin or framework). " +
                              $"Search Nexus: https://www.nexusmods.com/fallout4/search/?gsearch={Uri.EscapeDataString(sym)}&gsearchtype=mods");
        }
        return sb.ToString();
    }

    // Read the declared object name ("IHO:IHO_Foo") from a .psc / decompiled source. "" if not found.
    private static string ExtractScriptName(string text)
    {
        foreach (var raw in text.Split('\n'))
        {
            var l = raw.Trim();
            if (l.Length == 0 || l.StartsWith(";")) continue;
            if (l.StartsWith("ScriptName ", StringComparison.OrdinalIgnoreCase))
            {
                var toks = l.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (toks.Length >= 2) return toks[1].Trim();
            }
            break; // first meaningful line should be ScriptName; stop otherwise
        }
        return "";
    }
}
