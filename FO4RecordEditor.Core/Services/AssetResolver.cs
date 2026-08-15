using System.Diagnostics;
using System.IO;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Archives;
using Newtonsoft.Json;

namespace FO4RecordEditor.Services;

public sealed record AssetHit(string Kind, string Container, string Path, string InnerPath, long Size)
{
    public string Provider { get; init; } = string.Empty;
}

public static class AssetResolver
{
    private static readonly object _lock = new();

    private static readonly Dictionary<string, Dictionary<string, string>> _archiveIndex =
        new(StringComparer.OrdinalIgnoreCase);
    private static string[]? _sessionRoots;

    private static string Norm(string p) => (p ?? "").Replace('/', '\\').Trim().Trim('"').TrimStart('\\');

    private static string NativeRelativePath(string p) => Norm(p).Replace('\\', Path.DirectorySeparatorChar);

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
                foreach (var archive in Directory.EnumerateFiles(root, "*.ba2", SearchOption.AllDirectories)
                             .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
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

    public static List<AssetHit> ResolveAll(string relPath, int limit = 25)
    {
        var rel = Norm(relPath);
        var nativeRel = NativeRelativePath(rel);
        var hits = new List<AssetHit>();
        if (rel.Length == 0) return hits;

        foreach (var root in Roots())
        {
            if (string.IsNullOrWhiteSpace(root)) continue;
            var provider = Path.GetFileName(root.TrimEnd('\\', '/'));

            string full;
            try { full = Path.Combine(root, nativeRel); } catch { continue; }
            try
            {
                if (File.Exists(full))
                {
                    long size = 0;
                    try { size = new FileInfo(full).Length; } catch { }
                    hits.Add(new AssetHit("loose", provider, full, "", size) { Provider = provider });
                    if (hits.Count >= limit) return hits;
                    continue;
                }
            }
            catch (Exception ex) { DebugLog.Exception("Asset.Loose", ex); }

            if (ArchiveIndexFor(root).TryGetValue(rel, out var archivePath))
            {
                hits.Add(new AssetHit("archive", Path.GetFileName(archivePath), archivePath, rel, 0)
                {
                    Provider = provider
                });
                if (hits.Count >= limit) return hits;
            }
        }
        return hits;
    }

    public static AssetHit? Resolve(string relPath) => ResolveAll(relPath, 1).FirstOrDefault();

    public static bool Exists(string relPath) => Resolve(relPath) != null;

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
        sb.AppendLine($"  Providers: {hits.Count}; ambiguous: {(hits.Count > 1 ? "true" : "false")}.");
        sb.AppendLine($"  Winning mod: {win.Provider}.");
        sb.AppendLine($"  Winner: {win.Path}" + (win.InnerPath.Length > 0 ? $" [{win.InnerPath}]" : "")
                      + (win.Size > 0 ? $" ({win.Size} bytes)" : ""));
        if (hits.Count > 1)
        {
            sb.AppendLine($"  Also present in {hits.Count - 1} lower-priority mod(s), highest priority first:");
            foreach (var h in hits.Skip(1))
                sb.AppendLine($"    {h.Provider}: {h.Kind} {h.Container} ({h.Path})");
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
