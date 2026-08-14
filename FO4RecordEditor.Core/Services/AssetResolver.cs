using System.Diagnostics;
using System.IO;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Archives;
using Newtonsoft.Json;

namespace FO4RecordEditor.Services;

/// <summary>One provider that can serve a game-relative asset path.</summary>
/// <param name="Kind">"loose" or "archive".</param>
/// <param name="Container">The mod folder (loose) or the BA2 file name (archive).</param>
/// <param name="Path">The real on-disk file (loose) or the archive's own path (archive).</param>
/// <param name="InnerPath">The entry inside the archive; empty for a loose hit.</param>
public sealed record AssetHit(string Kind, string Container, string Path, string InnerPath, long Size);

/// <summary>
/// xEdit parity: the Resource* family (#65) -- answer "does this game-relative asset path exist
/// anywhere in the load order, and which mod or BA2 actually serves it".
///
/// Why this is not just Mutagen's own ArchiveAssetProvider: GameEnvironmentState exposes an
/// AssetProvider that does loose-then-archive in the right order, but Mo2ProfileLoader's
/// Mo2GameEnvironment has no AssetProvider at all, and its DataFolderPath is only the vanilla game
/// Data folder. Building on env.AssetProvider would therefore give silently wrong answers for every
/// MO2 modlist -- which is how this tool is actually used. So resolution walks MO2's own mod
/// priority instead.
///
/// Why this is not TextureService: that index deliberately holds only .dds/.nif/.bgsm entries, and
/// its roots are the mods PARENT folder rather than each mod as its own data root, which is fine for
/// its archive index but cannot answer a loose-file question ("mods\Foo\Meshes\x.nif" is not
/// "mods\Meshes\x.nif"). This resolves arbitrary extensions against per-mod data roots.
///
/// Priority, highest first: MO2 overwrite, then mods top-to-bottom of modlist.txt, then the vanilla
/// game Data folder -- the same ordering Mo2ProfileLoader already uses to decide which copy of a
/// plugin file wins. Within one root a loose file beats that root's archives, matching the engine
/// with bInvalidateOlderFiles set (which every modlist runs with).
///
/// Loose lookups are a direct File.Exists per root -- no directory scan at all. Only archives are
/// indexed, lazily, the first time a root is consulted.
/// </summary>
public static class AssetResolver
{
    private static readonly object _lock = new();
    // root -> (data-relative entry path -> archive file path). Kept per root so a root that is never
    // consulted is never indexed, and so a hit reports WHICH root answered.
    private static readonly Dictionary<string, Dictionary<string, string>> _archiveIndex =
        new(StringComparer.OrdinalIgnoreCase);
    private static string[]? _sessionRoots;

    private static string Norm(string p) => (p ?? "").Replace('/', '\\').Trim().Trim('"').TrimStart('\\');

    /// <summary>Publish this session's data roots, highest priority FIRST. Called by the loaders
    /// right after a modlist loads, for the same reason TextureService.SetSessionRoots is: settings
    /// .json holds a default, not what actually loaded.</summary>
    public static void SetSessionDataRoots(IEnumerable<string> rootsHighToLow)
    {
        lock (_lock)
        {
            _sessionRoots = rootsHighToLow
                .Where(r => !string.IsNullOrWhiteSpace(r))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            _archiveIndex.Clear();
        }
    }

    /// <summary>Expand an MO2 instance into one data root per enabled mod, in modlist.txt priority
    /// order (top of the file = highest). Public so Mo2ProfileLoader can hand over the ordering it
    /// has already parsed, and so a caller with only an instance path can derive it.</summary>
    public static List<string> Mo2DataRoots(string instancePath, IReadOnlyList<string> modsHighToLow,
                                            string overwriteFolder, string gameDataFolder)
    {
        var roots = new List<string>();
        if (!string.IsNullOrWhiteSpace(overwriteFolder)) roots.Add(overwriteFolder);
        var modsDir = Path.Combine(instancePath, "mods");
        foreach (var m in modsHighToLow) roots.Add(Path.Combine(modsDir, m));
        if (!string.IsNullOrWhiteSpace(gameDataFolder)) roots.Add(gameDataFolder);
        return roots;
    }

    private static string[] Roots()
    {
        lock (_lock)
        {
            if (_sessionRoots != null) return _sessionRoots;
        }
        // No modlist loaded this session: fall back to settings.json's Data folder alone. Deliberately
        // NOT the bare mods\ parent -- that is not a data root, and treating it as one is exactly the
        // wrong-answer failure this class exists to avoid.
        var roots = new List<string>();
        try
        {
            var file = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "FO4RecordEditor", "settings.json");
            if (File.Exists(file))
            {
                var s = JsonConvert.DeserializeObject<AppSettings>(File.ReadAllText(file));
                if (s != null && !string.IsNullOrWhiteSpace(s.DataFolder)) roots.Add(s.DataFolder);
            }
        }
        catch (Exception ex) { DebugLog.Exception("Asset.Roots", ex); }
        return roots.ToArray();
    }

    private static Dictionary<string, string> ArchiveIndexFor(string root)
    {
        lock (_lock)
        {
            if (_archiveIndex.TryGetValue(root, out var cached)) return cached;
            var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _archiveIndex[root] = index;
            if (!Directory.Exists(root)) return index;

            var sw = Stopwatch.StartNew();
            try
            {
                foreach (var archive in Directory.EnumerateFiles(root, "*.ba2", SearchOption.AllDirectories))
                {
                    try
                    {
                        var reader = Archive.CreateReader(GameRelease.Fallout4, new Noggog.FilePath(archive));
                        foreach (var f in reader.Files)
                        {
                            var p = f.Path?.ToString();
                            if (string.IsNullOrEmpty(p)) continue;
                            var key = Norm(p);
                            if (!index.ContainsKey(key)) index[key] = archive;
                        }
                    }
                    catch (Exception ex) { DebugLog.Exception("Asset.ReadBa2:" + Path.GetFileName(archive), ex); }
                }
            }
            catch (Exception ex) { DebugLog.Exception("Asset.EnumBa2:" + root, ex); }
            if (index.Count > 0)
                DebugLog.Info("Asset.Index", $"Indexed {index.Count} archive entries under {root} in {sw.ElapsedMilliseconds}ms");
            return index;
        }
    }

    /// <summary>Every provider that serves <paramref name="relPath"/>, highest priority first. Empty
    /// means the path exists nowhere in the load order.</summary>
    public static List<AssetHit> ResolveAll(string relPath, int limit = 25)
    {
        var rel = Norm(relPath);
        var hits = new List<AssetHit>();
        if (rel.Length == 0) return hits;

        foreach (var root in Roots())
        {
            if (string.IsNullOrWhiteSpace(root)) continue;

            string full;
            try { full = Path.Combine(root, rel); } catch { continue; }
            try
            {
                if (File.Exists(full))
                {
                    long size = 0;
                    try { size = new FileInfo(full).Length; } catch { }
                    hits.Add(new AssetHit("loose", Path.GetFileName(root.TrimEnd('\\', '/')), full, "", size));
                    if (hits.Count >= limit) return hits;
                }
            }
            catch (Exception ex) { DebugLog.Exception("Asset.Loose", ex); }

            if (ArchiveIndexFor(root).TryGetValue(rel, out var archivePath))
            {
                hits.Add(new AssetHit("archive", Path.GetFileName(archivePath), archivePath, rel, 0));
                if (hits.Count >= limit) return hits;
            }
        }
        return hits;
    }

    /// <summary>The winning provider, or null.</summary>
    public static AssetHit? Resolve(string relPath) => ResolveAll(relPath, 1).FirstOrDefault();

    public static bool Exists(string relPath) => Resolve(relPath) != null;

    /// <summary>Materialize the winning copy as a real file on disk: the loose file itself, or an
    /// archive entry extracted to a cache path, so the caller can hand it to nif_inspect /
    /// bgsm_inspect / an external tool. Extraction reuses TextureService's cache directory rather
    /// than opening a second one.</summary>
    public static string? Materialize(AssetHit hit)
    {
        if (hit.Kind == "loose") return hit.Path;
        try
        {
            long stamp = 0;
            try { stamp = File.GetLastWriteTimeUtc(hit.Path).Ticks; } catch { }
            var name = "asset_" + Math.Abs(
                StringComparer.OrdinalIgnoreCase.GetHashCode(hit.Path + "|" + stamp + "|" + hit.InnerPath))
                + Path.GetExtension(hit.InnerPath);
            Directory.CreateDirectory(TextureService.TexCacheDir);
            var outPath = Path.Combine(TextureService.TexCacheDir, name);
            if (File.Exists(outPath)) return outPath;

            var reader = Archive.CreateReader(GameRelease.Fallout4, new Noggog.FilePath(hit.Path));
            foreach (var f in reader.Files)
            {
                if (!string.Equals(Norm(f.Path?.ToString() ?? ""), hit.InnerPath, StringComparison.OrdinalIgnoreCase))
                    continue;
                var tmp = outPath + ".tmp" + Guid.NewGuid().ToString("N")[..8];
                File.WriteAllBytes(tmp, f.GetBytes());
                File.Move(tmp, outPath, true);
                return outPath;
            }
        }
        catch (Exception ex) { DebugLog.Exception("Asset.Materialize", ex); }
        return null;
    }

    /// <summary>Tool-facing report for one path.</summary>
    public static string ResolveText(string relPath, bool extract, int limit = 25)
    {
        var rel = Norm(relPath);
        if (rel.Length == 0) return ToolError.Fail("Give a game-relative asset path, e.g. 'Meshes\\Clutter\\Rock01.nif'.");

        var roots = Roots();
        if (roots.Length == 0)
            return ToolError.Fail("No data roots known -- load a modlist ('Open MO2') or set a Data folder in settings first. " +
                                  "Without one, 'not found' would only mean 'nowhere to look'.");

        var hits = ResolveAll(rel, limit);
        var sb = new System.Text.StringBuilder();
        if (hits.Count == 0)
        {
            sb.AppendLine($"'{rel}' NOT found in any of the {roots.Length} data root(s) searched (loose files and BA2s).");
            sb.AppendLine("Checked, highest priority first: " + string.Join(", ", roots.Take(5).Select(r => Path.GetFileName(r.TrimEnd('\\', '/'))))
                          + (roots.Length > 5 ? $", ... ({roots.Length - 5} more)" : ""));
            return sb.ToString();
        }

        var win = hits[0];
        sb.AppendLine($"'{rel}' -> served by {win.Kind} '{win.Container}'.");
        sb.AppendLine($"  Winner: {win.Path}" + (win.InnerPath.Length > 0 ? $" [{win.InnerPath}]" : "")
                      + (win.Size > 0 ? $" ({win.Size} bytes)" : ""));
        if (hits.Count > 1)
        {
            sb.AppendLine($"  Also present in {hits.Count - 1} lower-priority source(s):");
            foreach (var h in hits.Skip(1)) sb.AppendLine($"    {h.Kind}: {h.Container} ({h.Path})");
        }
        if (extract)
        {
            var real = Materialize(win);
            sb.AppendLine(real != null
                ? $"  Extracted to: {real}"
                : "  Could not materialize the winning copy to a real file.");
        }
        return sb.ToString();
    }
}
