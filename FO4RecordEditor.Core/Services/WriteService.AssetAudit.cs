using System.Collections;
using System.IO;
using System.Reflection;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Archives;

namespace FO4RecordEditor.Services;

/// <summary>
/// xEdit parity: cross-plugin asset usage audit (#61).
/// </summary>
public static partial class WriteService
{
    private static readonly HashSet<string> AssetAuditExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".nif", ".dds", ".wav", ".xwm", ".fuz", ".bgsm", ".bgem", ".hkx", ".swf", ".pex", ".seq",
    };

    private static string NormalizeAssetPath(string p) => p.Replace('/', '\\').TrimStart('\\');

    // Generic reflection walk, not a per-record-type switch. Verified before writing this that
    // Mutagen's own IAssetLinkContainer/EnumerateAssetLinks does NOT cover this: in this Fallout4
    // record set it is implemented ONLY by Cell/CellBlock/CellSubBlock/Worldspace/WorldspaceBlock/
    // WorldspaceSubBlock (grep-checked, 6 types total) -- i.e. placed-reference assets, not every
    // record type's own Model/Icon fields. WEAP/ARMO/STAT/etc. don't implement it at all, so a
    // reflection walk is the only way to catch every record type's asset-shaped string fields.
    private static void CollectAssetPaths(object? obj, HashSet<string> found, int depth, int maxDepth)
    {
        if (obj == null || depth > maxDepth) return;

        if (obj is string s)
        {
            var ext = Path.GetExtension(s);
            if (ext.Length > 0 && AssetAuditExtensions.Contains(ext)) found.Add(NormalizeAssetPath(s));
            return;
        }

        if (obj is IEnumerable enumerable)
        {
            foreach (var item in enumerable) CollectAssetPaths(item, found, depth + 1, maxDepth);
            return;
        }

        var type = obj.GetType();
        if (type.Namespace == null || !type.Namespace.StartsWith("Mutagen", StringComparison.Ordinal)) return;

        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (prop.GetIndexParameters().Length > 0) continue;
            object? val;
            try { val = prop.GetValue(obj); } catch { continue; }
            CollectAssetPaths(val, found, depth + 1, maxDepth);
        }
    }

    /// <summary>
    /// xEdit's "List used meshes" / "Output used assets filenames" family. Walks every property of a
    /// plugin's own (native) records via reflection, collecting string values whose extension matches
    /// a known FO4 asset type, then diffs that set against what the plugin's own on-disk folder
    /// actually ships (loose files + any BA2 sitting beside it -- the same folder FindPluginPath
    /// already resolves for this plugin elsewhere in this file). Reports orphans (shipped but
    /// unreferenced -- candidates for trimming a repack) and dangling (referenced but not found in
    /// THIS plugin's own folder -- NOT necessarily broken, see the caveat in the result).
    /// </summary>
    public static string AuditAssetUsage(string plugin, object? env, int recordLimit = 3000)
    {
        var mod = EnsureOpen(plugin, env, out var msg); if (mod == null) return msg;
        var (name, _) = NormalizePlugin(plugin);

        var native = mod.EnumerateMajorRecords().Where(r => r.FormKey.ModKey == mod.ModKey).Take(recordLimit).ToList();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rec in native) CollectAssetPaths(rec, used, 0, 5);

        string? pluginDir = null;
        try { pluginDir = Path.GetDirectoryName(FindPluginPath(name, env)); } catch { }
        if (pluginDir == null || !Directory.Exists(pluginDir))
        {
            var sample = string.Join(", ", used.Take(20)) + (used.Count > 20 ? ", ..." : "");
            return $"Found {used.Count} referenced asset path(s) in {name}'s {native.Count} native record(s), but " +
                   $"could not locate '{name}' on disk to check what's actually shipped alongside it: {sample}";
        }

        var shipped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var f in Directory.EnumerateFiles(pluginDir, "*", SearchOption.AllDirectories))
            {
                if (!AssetAuditExtensions.Contains(Path.GetExtension(f))) continue;
                shipped.Add(NormalizeAssetPath(Path.GetRelativePath(pluginDir, f)));
            }
        }
        catch (Exception ex) { DebugLog.Exception("AssetAudit.EnumLoose", ex); }

        // One-shot, not the session-cached scan TextureService keeps for hot lookups -- this runs
        // once per audit call against a single known folder, not a repeated resolution path.
        try
        {
            foreach (var archive in Directory.EnumerateFiles(pluginDir, "*.ba2", SearchOption.AllDirectories))
            {
                try
                {
                    var reader = Archive.CreateReader(GameRelease.Fallout4, new Noggog.FilePath(archive));
                    foreach (var f in reader.Files)
                    {
                        var p = f.Path?.ToString();
                        if (string.IsNullOrEmpty(p) || !AssetAuditExtensions.Contains(Path.GetExtension(p))) continue;
                        shipped.Add(NormalizeAssetPath(p));
                    }
                }
                catch (Exception ex) { DebugLog.Exception("AssetAudit.ReadBa2:" + Path.GetFileName(archive), ex); }
            }
        }
        catch (Exception ex) { DebugLog.Exception("AssetAudit.EnumBa2", ex); }

        var orphans = shipped.Except(used, StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
        var dangling = used.Except(shipped, StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Asset audit for '{name}' ({native.Count} native record(s), capped at {recordLimit}):");
        sb.AppendLine($"  Referenced: {used.Count} distinct asset path(s).");
        sb.AppendLine($"  Shipped in {pluginDir} (loose + BA2): {shipped.Count} asset file(s).");
        sb.AppendLine($"  Orphaned (shipped, not referenced by this plugin's own records): {orphans.Count}");
        foreach (var o in orphans.Take(30)) sb.AppendLine($"    - {o}");
        if (orphans.Count > 30) sb.AppendLine($"    ... and {orphans.Count - 30} more");
        // Split "not in this plugin's own folder" into "served by something else in the load order"
        // (expected -- a shared base-game or other-mod asset) and "served by nothing at all" (the
        // actually broken kind). #65's AssetResolver is what makes this distinction possible; before
        // it, every dangling entry had to be reported with a caveat and triaged by hand.
        var elsewhere = new List<string>();
        var missing = new List<string>();
        foreach (var d in dangling) (AssetResolver.Exists(d) ? elsewhere : missing).Add(d);

        sb.AppendLine($"  Referenced but not shipped here, served elsewhere in the load order: {elsewhere.Count} (expected)");
        foreach (var d in elsewhere.Take(10)) sb.AppendLine($"    - {d}");
        if (elsewhere.Count > 10) sb.AppendLine($"    ... and {elsewhere.Count - 10} more");
        sb.AppendLine($"  MISSING -- referenced and served by nothing, loose or packed: {missing.Count}");
        foreach (var d in missing.Take(30)) sb.AppendLine($"    - {d}");
        if (missing.Count > 30) sb.AppendLine($"    ... and {missing.Count - 30} more");
        sb.AppendLine("  Load-order resolution needs a modlist loaded ('Open MO2') or a Data folder set; " +
                       "with neither, everything lands in MISSING because there is nowhere to look. " +
                       "resolve_asset reports which container serves any one path.");
        return sb.ToString();
    }
}
