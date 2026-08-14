using System.Diagnostics;
using System.IO;
using System.Text;
using FO4RecordEditor.Services.Papyrus;

namespace FO4RecordEditor.Services;

public static class PapyrusService
{
    private const string DefaultFlags = "Institute_Papyrus_Flags.flg";

    public enum Engine
    {

        Auto,

        BuiltIn,

        CreationKit,
    }

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

        if (isFile && source.EndsWith(".pas", StringComparison.OrdinalIgnoreCase))
            return "That's a Papyrus ASSEMBLY file (.pas), not source. PapyrusCompiler compiles .psc SOURCE.\n" +
                   "Fix: Decompile this script again with 'Assembly listing' UNCHECKED to get a .psc, then compile that.\n" +
                   "(.pas is only used by PapyrusAssembler.exe, a separate tool, and is not what you recompile.)";

        var exe = exePath;
        if (!haveCreationKit)
            return "PapyrusCompiler.exe not found, and engine='creationkit' was asked for explicitly. Pass " +
                   "compiler_path pointing at it, or " + ToolPaths.Describe("papyrus") + ". " +
                   "Or use engine='builtin', which needs no Creation Kit.";

        string workDir, target;
        string? stagingDir = null;
        if (isDir)
        {

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

        var imp = new List<string>();
        if (!string.IsNullOrWhiteSpace(imports))
            imp.AddRange(imports.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                .Select(s => s.Trim('"')));
        foreach (var b in ToolPaths.PapyrusBaseImports()) imp.Add(b);
        imp.Add(workDir);
        var importArg = string.Join(";", imp.Distinct(StringComparer.OrdinalIgnoreCase));

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

            var run = ProcessRunner.Run(psi, TimeSpan.FromSeconds(300));
            if (!run.Started) return "Failed to start the Papyrus compiler process.";
            if (run.TimedOut) return "Papyrus compile timed out after 300s (killed).";

            var combined = (run.StdOut + "\n" + run.StdErr).Replace("\r", "");

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

    private static readonly (string match, string name, string url)[] KnownExtenders =
    {
        ("hudframework",      "HUDFramework",            "https://www.nexusmods.com/fallout4/mods/20309"),
        ("workshopframework", "Workshop Framework",      "https://www.nexusmods.com/fallout4/mods/35004"),
        ("wsfw",              "Workshop Framework",      "https://www.nexusmods.com/fallout4/mods/35004"),
        ("mcm",               "Mod Configuration Menu",  "https://www.nexusmods.com/fallout4/mods/21497"),
        ("baka",              "Baka Framework",          "https://www.nexusmods.com/fallout4/search/?gsearch=Baka+Framework&gsearchtype=mods"),
    };

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
            break;
        }
        return "";
    }
}
