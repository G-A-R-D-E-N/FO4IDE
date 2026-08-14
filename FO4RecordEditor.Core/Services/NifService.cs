using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using JsonConvert = Newtonsoft.Json.JsonConvert;

namespace FO4RecordEditor.Services;

/// <summary>
/// Authors / repairs / inspects Fallout 4 NIFs by shelling out to niftool.exe (our self-contained
/// nifly-based CLI), replacing NifSkope in the Blender -> NIF -> record -> game pipeline. Same
/// process pattern as PapyrusService: run the exe, capture stdout+stderr concurrently, timeout,
/// return the tool's text (a leading 'RESULT:' line or JSON) straight back to the AI.
/// </summary>
public static class NifService
{
    private static string? ResolveExe() => ToolPaths.Niftool();

    public static string Import(string objPath, string outNif, string material,
        string texDiffuse, string texNormal, bool collision, bool fromBlender)
    {
        objPath = Clean(objPath);
        outNif = Clean(outNif);
        if (string.IsNullOrWhiteSpace(objPath) || string.IsNullOrWhiteSpace(outNif))
            return "Provide 'obj_path' and 'out_nif'.";
        if (!File.Exists(objPath)) return $"OBJ not found: {objPath}";

        var args = new List<string> { "import", objPath, outNif };
        if (!string.IsNullOrWhiteSpace(material)) { args.Add("--material"); args.Add(material.Trim()); }
        if (!string.IsNullOrWhiteSpace(texDiffuse)) { args.Add("--tex-diffuse"); args.Add(texDiffuse.Trim()); }
        if (!string.IsNullOrWhiteSpace(texNormal)) { args.Add("--tex-normal"); args.Add(texNormal.Trim()); }
        if (collision) { args.Add("--collision"); args.Add("box"); }
        if (fromBlender) args.Add("--from-blender");
        return Run(args);
    }

    public static string Inspect(string nifPath)
    {
        nifPath = Clean(nifPath);
        if (string.IsNullOrWhiteSpace(nifPath)) return "Provide 'nif_path'.";
        if (!File.Exists(nifPath)) return $"NIF not found: {nifPath}";
        return Run(new List<string> { "inspect", nifPath });
    }

    public static string Geo(string nifPath)
    {
        nifPath = Clean(nifPath);
        if (string.IsNullOrWhiteSpace(nifPath)) return "Provide 'nif_path'.";
        if (!File.Exists(nifPath)) return $"NIF not found: {nifPath}";
        return Run(new List<string> { "geo", nifPath });
    }

    /// <summary>Live progress for an in-flight GeoBatch call -- polled by CellInterop.
    /// GetGeometryBatchProgress so the UI can show a real N/total bar instead of a spinner. Reset at
    /// the start of each batch; safe for a single Cell Viewer session (the only current caller), not
    /// designed for multiple concurrent batches.</summary>
    public static int GeoBatchDone;
    public static int GeoBatchTotal;

    /// <summary>
    /// Batch-resolve+convert unique Data-relative NIF paths (e.g. "Clutter\Rock01.nif") to geometry
    /// JSON, for the cell viewer. One resolve+niftool call per UNIQUE path, not per placed reference
    /// -- a mesh placed 40 times in a cell converts once, not 40 times -- run in parallel with a
    /// capped degree since each call is a real child process. Keyed by the ORIGINAL relative path the
    /// caller passed in, so the frontend can match each placed reference back to its geometry without
    /// needing to know how the path was actually resolved (loose vs. extracted from which archive).
    /// </summary>
    public static string GeoBatch(IEnumerable<string> relModelPaths)
    {
        var unique = relModelPaths
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var results = new ConcurrentDictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        var degree = Math.Max(1, Math.Min(Environment.ProcessorCount, 8));

        GeoBatchDone = 0;
        GeoBatchTotal = unique.Count;

        Parallel.ForEach(unique, new ParallelOptions { MaxDegreeOfParallelism = degree }, rel =>
        {
            var resolved = TextureService.ResolveNif(rel);
            if (resolved == null)
            {
                // Previously silent -- a batch of hundreds of unique meshes with a handful of
                // failures gave no way to find WHICH ones without a debugger attached. A single
                // widely-reused failed mesh (a common wall/floor tile in a big cell) can look like
                // "many walls just don't show" even when the batch's own failure count is tiny.
                DebugLog.Info("Nif.GeoBatch", $"Not found (loose or in any indexed archive): {rel}");
                results[rel] = new { error = "Not found (loose or in any indexed archive)." };
                Interlocked.Increment(ref GeoBatchDone);
                return;
            }

            var raw = Geo(resolved);
            // niftool's geo command emits raw JSON on success; anything else (a "NIF not found"/
            // "niftool.exe not found." line, a parse failure inside niftool) is plain text. Embed
            // success as JRaw so it nests as a real object in the batch result instead of a
            // JSON-string-inside-a-JSON-string that the frontend would have to double-parse.
            var ok = raw.TrimStart().StartsWith('{');
            if (!ok) DebugLog.Info("Nif.GeoBatch", $"niftool geo failed for {rel} (resolved: {resolved}): {raw}");
            results[rel] = ok ? new Newtonsoft.Json.Linq.JRaw(raw) : new { error = raw };
            Interlocked.Increment(ref GeoBatchDone);
        });

        return JsonConvert.SerializeObject(new { count = unique.Count, geometry = results });
    }

    public static string Verify(string nifPath)
    {
        nifPath = Clean(nifPath);
        if (string.IsNullOrWhiteSpace(nifPath)) return "Provide 'nif_path'.";
        if (!File.Exists(nifPath)) return $"NIF not found: {nifPath}";
        return Run(new List<string> { "verify", nifPath });
    }

    public static string Fix(string nifPath, string outNif)
    {
        nifPath = Clean(nifPath);
        outNif = Clean(outNif);
        if (string.IsNullOrWhiteSpace(nifPath) || string.IsNullOrWhiteSpace(outNif))
            return "Provide 'nif_path' and 'out_nif'.";
        if (!File.Exists(nifPath)) return $"NIF not found: {nifPath}";
        return Run(new List<string> { "fix", nifPath, outNif });
    }

    /// <summary>Dump a NIF's curated editable property tree (Nodes / Shapes / Extra) as JSON.</summary>
    public static string Tree(string nifPath)
    {
        nifPath = Clean(nifPath);
        if (string.IsNullOrWhiteSpace(nifPath)) return "Provide 'nif_path'.";
        if (!File.Exists(nifPath)) return $"NIF not found: {nifPath}";
        return Run(new List<string> { "tree", nifPath });
    }

    /// <summary>Apply a JSON array of field edits (from Edit mode) and save. outNif blank = save in
    /// place. Writes to a sibling temp first, then swaps over the target so a mid-save failure can't
    /// corrupt the original.</summary>
    public static string ApplyEdits(string nifPath, string editsJson, string outNif = "")
    {
        nifPath = Clean(nifPath);
        outNif = Clean(outNif);
        if (string.IsNullOrWhiteSpace(nifPath)) return "Provide 'nif_path'.";
        if (!File.Exists(nifPath)) return $"NIF not found: {nifPath}";
        if (string.IsNullOrWhiteSpace(editsJson)) return "No edits to apply.";

        var target = string.IsNullOrWhiteSpace(outNif) ? nifPath : outNif;
        var editsPath = Path.Combine(Path.GetTempPath(), "niftool_edits_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".json");
        var writeTmp = target + ".niftmp";
        try
        {
            File.WriteAllText(editsPath, editsJson, new UTF8Encoding(false));
            var res = Run(new List<string> { "set", nifPath, writeTmp, editsPath });
            if (res.Contains("set OK") && File.Exists(writeTmp))
            {
                File.Copy(writeTmp, target, true);   // swap the successful save over the target
                return res + $"\nSaved: {target}";
            }
            return res;
        }
        catch (Exception ex) { return "Failed to apply edits: " + ex.Message; }
        finally
        {
            try { if (File.Exists(writeTmp)) File.Delete(writeTmp); } catch { }
            try { if (File.Exists(editsPath)) File.Delete(editsPath); } catch { }
        }
    }

    private static string Clean(string s) => (s ?? "").Trim().Trim('"');

    private static string Run(List<string> args)
    {
        var exe = ResolveExe();
        if (exe == null)
            return "niftool.exe not found. Build it with `xmake` in E:\\F4SE OG\\Tools\\PluginEditTool\\tools\\niftool, " +
                   "or set the NIFTOOL_PATH environment variable to its full path.";

        var psi = new ProcessStartInfo
        {
            FileName = exe,
            WorkingDirectory = Path.GetDirectoryName(exe) ?? ".",
            // niftool emits UTF-8 (nlohmann JSON); decode as UTF-8 so non-ASCII paths/labels don't mangle.
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        try
        {
            var run = ProcessRunner.Run(psi, TimeSpan.FromSeconds(120));
            if (!run.Started) return "Failed to start niftool.exe.";
            if (run.TimedOut) return "niftool timed out after 120s (killed).";

            return string.IsNullOrWhiteSpace(run.Combined)
                ? $"niftool exited {run.ExitCode} with no output."
                : run.Combined;
        }
        catch (Exception ex)
        {
            return $"Failed to run niftool: {ex.Message}";
        }
    }
}
