using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Archives;
using Newtonsoft.Json;
using FO4RecordEditor.Services.Materials;

namespace FO4RecordEditor.Services;

public static class TextureService
{
    private static string? ResolveTexconv() => ToolPaths.Texconv();

    public static string GetTexturePngDataUrl(string nifPath, string relTexPath, string textureRoot = "")
    {
        var png = GetTexturePngPath(nifPath, relTexPath, textureRoot);
        if (png == null) return "";
        try
        {
            var bytes = File.ReadAllBytes(png);
            return "data:image/png;base64," + Convert.ToBase64String(bytes);
        }
        catch (Exception ex) { DebugLog.Exception("Texture.GetPng", ex); return ""; }
    }

    public static string? GetTexturePngPath(string nifPath, string relTexPath, string textureRoot = "")
    {
        try
        {
            nifPath = (nifPath ?? "").Trim().Trim('"');
            relTexPath = (relTexPath ?? "").Trim().Trim('"');
            textureRoot = (textureRoot ?? "").Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(relTexPath)) return null;

            if (relTexPath.EndsWith(".bgsm", StringComparison.OrdinalIgnoreCase))
            {
                var mat = ResolveAndParseBgsm(nifPath, relTexPath, textureRoot);
                if (mat == null) return null;
                var diffuse = (mat.DiffuseTexture ?? "").TrimEnd('\0');
                if (string.IsNullOrWhiteSpace(diffuse)) return null;
                relTexPath = diffuse;
            }

            var dds = ResolveDds(nifPath, relTexPath, textureRoot);
            if (dds == null) return null;

            var png = ConvertToPng(dds);
            return png != null && File.Exists(png) ? png : null;
        }
        catch (Exception ex) { DebugLog.Exception("Texture.GetPngPath", ex); return null; }
    }

    public static BgsmData? ResolveBgsm(string nifPath, string bgsmRelPath, string textureRoot = "")
        => ResolveAndParseBgsm((nifPath ?? "").Trim().Trim('"'), (bgsmRelPath ?? "").Trim().Trim('"'),
            (textureRoot ?? "").Trim().Trim('"'));

    private static BgsmData? ResolveAndParseBgsm(string nifPath, string bgsmRelPath, string textureRoot)
    {
        var bgsmFile = ResolveMaterialFile(nifPath, bgsmRelPath, textureRoot);
        if (bgsmFile == null) return null;
        try { return BgsmCodec.Parse(File.ReadAllBytes(bgsmFile)); }
        catch (Exception ex) { DebugLog.Exception("Texture.BgsmParse", ex); return null; }
    }

    private static string? ResolveDds(string nifPath, string rel, string textureRoot = "")
    {
        rel = rel.Replace('/', '\\').TrimStart('\\');
        if (Path.IsPathRooted(rel) && File.Exists(rel)) return rel;

        string? nifDir = null;
        try { nifDir = Path.GetDirectoryName(Path.GetFullPath(nifPath)); } catch { }

        var candidates = new List<string>();

        if (!string.IsNullOrWhiteSpace(textureRoot) && Directory.Exists(textureRoot))
        {
            candidates.Add(Path.Combine(textureRoot, rel));
            if (rel.StartsWith("textures\\", StringComparison.OrdinalIgnoreCase))
                candidates.Add(Path.Combine(textureRoot, rel.Substring("textures\\".Length)));
            candidates.Add(Path.Combine(textureRoot, "Textures", rel));
            candidates.Add(Path.Combine(textureRoot, Path.GetFileName(rel)));
        }

        var dir = nifDir;
        while (dir != null)
        {
            candidates.Add(Path.Combine(dir, rel));
            candidates.Add(Path.Combine(dir, "Textures", rel));
            dir = Path.GetDirectoryName(dir);
        }
        if (nifDir != null) candidates.Add(Path.Combine(nifDir, Path.GetFileName(rel)));

        foreach (var c in candidates)
            if (File.Exists(c)) return c;

        return ResolveFromArchives(rel, nifDir, textureRoot);
    }

    private static string? ResolveMaterialFile(string nifPath, string rel, string textureRoot = "")
    {
        rel = rel.Replace('/', '\\').TrimStart('\\');
        if (Path.IsPathRooted(rel) && File.Exists(rel)) return rel;

        string? nifDir = null;
        try { nifDir = Path.GetDirectoryName(Path.GetFullPath(nifPath)); } catch { }

        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(textureRoot) && Directory.Exists(textureRoot))
        {
            candidates.Add(Path.Combine(textureRoot, rel));
            if (rel.StartsWith("materials\\", StringComparison.OrdinalIgnoreCase))
                candidates.Add(Path.Combine(textureRoot, rel.Substring("materials\\".Length)));
            candidates.Add(Path.Combine(textureRoot, "Materials", rel));
        }

        var dir = nifDir;
        while (dir != null)
        {
            candidates.Add(Path.Combine(dir, rel));
            candidates.Add(Path.Combine(dir, "Materials", rel));
            dir = Path.GetDirectoryName(dir);
        }

        foreach (var c in candidates)
            if (File.Exists(c)) return c;

        return ResolveFromArchives(rel, nifDir, textureRoot, "materials");
    }

    private static readonly object _lock = new();
    private static readonly Dictionary<string, string> _index = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> _scannedArchives = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> _scannedRoots = new(StringComparer.OrdinalIgnoreCase);

    public static readonly string TexCacheDir = Path.Combine(Path.GetTempPath(), "FO4RE_Tex");

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, object> _keyLocks = new(StringComparer.Ordinal);
    private static object LockFor(string key) => _keyLocks.GetOrAdd(key, static _ => new object());
    private static string[]? _globalRoots;

    private static string Norm(string p) => p.Replace('/', '\\').TrimStart('\\');

    private static string? ResolveFromArchives(string rel, string? nifDir, string textureRoot)
        => ResolveFromArchives(rel, nifDir, textureRoot, "textures");

    private static string? ResolveFromArchives(string rel, string? nifDir, string textureRoot, string topFolder)
    {
        try
        {
            var keys = LookupKeys(rel, topFolder);

            var cheap = new List<string>();
            AddDataRoots(cheap, nifDir);
            if (!string.IsNullOrWhiteSpace(textureRoot)) AddDataRoots(cheap, textureRoot);
            var hit = ScanAndLookup(cheap, keys);
            if (hit != null) return hit;

            var hit2 = ScanAndLookup(GlobalRoots(), keys);
            if (hit2 != null) return hit2;
        }
        catch (Exception ex) { DebugLog.Exception("Texture.Archive", ex); }
        return null;
    }

    private static List<string> LookupKeys(string rel) => LookupKeys(rel, "textures");

    private static List<string> LookupKeys(string rel, string topFolder)
    {
        var norm = Norm(rel);
        var keys = new List<string> { norm };
        var prefix = topFolder + "\\";
        if (!norm.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            keys.Add(prefix + norm);
        return keys;
    }

    private static string? ScanAndLookup(IEnumerable<string> roots, List<string> keys)
    {
        lock (_lock)
        {
            foreach (var root in roots) EnsureRootScanned(root);
            foreach (var k in keys)
                if (_index.TryGetValue(k, out var archive)) return ExtractFromArchive(archive, k);
        }
        return null;
    }

    public static void AddDataRoots(List<string> roots, string? path)
    {
        var dir = path;

        if (dir != null && dir.StartsWith(TexCacheDir, StringComparison.OrdinalIgnoreCase)) return;
        for (int guard = 0; dir != null && guard < 24; guard++)
        {
            roots.Add(dir);
            if (string.Equals(Path.GetFileName(dir), "Data", StringComparison.OrdinalIgnoreCase)) break;
            dir = Path.GetDirectoryName(dir);
        }
    }

    private static string[]? _sessionRoots;

    public static void SetSessionRoots(IEnumerable<string> roots)
    {
        _sessionRoots = roots.Where(r => !string.IsNullOrWhiteSpace(r)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        _globalRoots = null;
    }

    private static string[] GlobalRoots()
    {
        if (_globalRoots != null) return _globalRoots;
        if (_sessionRoots != null) return _globalRoots = _sessionRoots;

        var roots = new List<string>();
        try
        {
            var file = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "FO4RecordEditor", "settings.json");
            if (File.Exists(file))
            {
                var s = JsonConvert.DeserializeObject<AppSettings>(File.ReadAllText(file));
                if (s != null)
                {
                    if (!string.IsNullOrWhiteSpace(s.DataFolder)) roots.Add(s.DataFolder);
                    if (!string.IsNullOrWhiteSpace(s.Mo2InstancePath))
                        roots.Add(Path.Combine(s.Mo2InstancePath, "mods"));
                }
            }
        }
        catch (Exception ex) { DebugLog.Exception("Texture.GlobalRoots", ex); }
        return _globalRoots = roots.ToArray();
    }

    private static void EnsureRootScanned(string root)
    {
        root = (root ?? "").Trim();
        if (string.IsNullOrWhiteSpace(root) || !_scannedRoots.Add(root) || !Directory.Exists(root)) return;

        IEnumerable<string> archives;
        try { archives = Directory.EnumerateFiles(root, "*.ba2", SearchOption.AllDirectories); }
        catch (Exception ex) { DebugLog.Exception("Texture.EnumBa2", ex); return; }

        int added = 0;
        var sw = Stopwatch.StartNew();

        try
        {
            foreach (var archive in archives)
            {
                if (!_scannedArchives.Add(archive)) continue;
                try
                {
                    var reader = Archive.CreateReader(GameRelease.Fallout4, new Noggog.FilePath(archive));
                    foreach (var f in reader.Files)
                    {
                        var p = f.Path?.ToString();

                        if (string.IsNullOrEmpty(p) ||
                            !(p.EndsWith(".dds", StringComparison.OrdinalIgnoreCase) ||
                              p.EndsWith(".nif", StringComparison.OrdinalIgnoreCase) ||
                              p.EndsWith(".bgsm", StringComparison.OrdinalIgnoreCase))) continue;
                        var key = Norm(p);
                        if (!_index.ContainsKey(key)) { _index[key] = archive; added++; }
                    }
                }
                catch (Exception ex) { DebugLog.Exception("Texture.ReadBa2:" + Path.GetFileName(archive), ex); }
            }
        }
        catch (Exception ex) { DebugLog.Exception("Texture.EnumBa2:" + root, ex); }
        if (added > 0)
            DebugLog.Info("Texture.Index", $"Indexed {added} DDS/NIF entries from BA2s under {root} in {sw.ElapsedMilliseconds}ms");
    }

    private static string? ExtractFromArchive(string archivePath, string innerKey)
    {
        try
        {
            long stamp = 0;
            try { stamp = File.GetLastWriteTimeUtc(archivePath).Ticks; } catch { }
            var key = Hash(archivePath.ToLowerInvariant() + "|" + stamp + "|" + innerKey);
            var ext = Path.GetExtension(innerKey);
            if (string.IsNullOrEmpty(ext)) ext = ".dds";
            Directory.CreateDirectory(TexCacheDir);
            var outPath = Path.Combine(TexCacheDir, "ba2_" + key + ext);
            if (File.Exists(outPath)) return outPath;

            lock (LockFor(key))
            {
                if (File.Exists(outPath)) return outPath;

                var reader = Archive.CreateReader(GameRelease.Fallout4, new Noggog.FilePath(archivePath));
                foreach (var f in reader.Files)
                {
                    if (!string.Equals(Norm(f.Path?.ToString() ?? ""), innerKey, StringComparison.OrdinalIgnoreCase))
                        continue;
                    var tmp = outPath + ".tmp" + Guid.NewGuid().ToString("N")[..8];
                    File.WriteAllBytes(tmp, f.GetBytes());
                    File.Move(tmp, outPath, true);
                    return outPath;
                }
            }
        }
        catch (Exception ex) { DebugLog.Exception("Texture.Extract", ex); }
        return null;
    }

    public static string? ResolveNif(string relPath)
    {
        relPath = (relPath ?? "").Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(relPath)) return null;
        var rel = relPath.Replace('/', '\\').TrimStart('\\');
        if (Path.IsPathRooted(rel) && File.Exists(rel)) return rel;

        var roots = GlobalRoots();
        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root)) continue;
            var direct = Path.Combine(root, rel);
            if (File.Exists(direct)) return direct;
            if (rel.StartsWith("meshes\\", StringComparison.OrdinalIgnoreCase))
            {
                var stripped = Path.Combine(root, rel.Substring("meshes\\".Length));
                if (File.Exists(stripped)) return stripped;
            }
            else
            {
                var prefixed = Path.Combine(root, "Meshes", rel);
                if (File.Exists(prefixed)) return prefixed;
            }
        }

        return ScanAndLookup(roots, LookupKeys(rel, "meshes"));
    }

    private static string? ConvertToPng(string ddsPath)
    {
        var stamp = File.GetLastWriteTimeUtc(ddsPath).Ticks;

        var key = Hash(ddsPath.ToLowerInvariant() + "|" + stamp + "|v3");
        Directory.CreateDirectory(TexCacheDir);
        var pngPath = Path.Combine(TexCacheDir, key + ".png");
        if (File.Exists(pngPath)) return pngPath;

        var decoded = DecodeInProcess(ddsPath, pngPath, key);
        if (decoded != null) return decoded;

        var texconv = ResolveTexconv();
        if (texconv == null) return null;

        lock (LockFor(key))
        {
            if (File.Exists(pngPath)) return pngPath;

            var work = Path.Combine(TexCacheDir, "w_" + key);
            Directory.CreateDirectory(work);
            try
            {

                var produced = RunTexconv(texconv, ddsPath, work, IsBc5(ddsPath));
                if (produced == null) return null;
                File.Copy(produced, pngPath, true);
                return pngPath;
            }
            finally { try { Directory.Delete(work, true); } catch { } }
        }
    }

    private static string? DecodeInProcess(string ddsPath, string pngPath, string key)
    {
        try
        {
            var bytes = File.ReadAllBytes(ddsPath);
            if (!Textures.DdsDecoder.CanDecode(Archives.DdsCodec.Parse(bytes, Path.GetFileName(ddsPath)).DxgiFormat))
                return null;

            var png = Textures.DdsDecoder.ToPng(bytes, reconstructZ: Textures.DdsDecoder.IsBc5(bytes));

            lock (LockFor(key))
            {
                if (File.Exists(pngPath)) return pngPath;

                var temp = pngPath + "." + Environment.CurrentManagedThreadId + ".part";
                File.WriteAllBytes(temp, png);
                File.Move(temp, pngPath, overwrite: true);
                return pngPath;
            }
        }
        catch (Exception ex)
        {
            DebugLog.Exception("Texture.DecodeInProcess", ex);
            return null;
        }
    }

    private static string? RunTexconv(string texconv, string ddsPath, string work, bool reconstructZ)
    {
        var produced = Path.Combine(work, Path.GetFileNameWithoutExtension(ddsPath) + ".png");

        var args = new List<string> { "-nologo", "-y", "-ft", "png", "-f", "R8G8B8A8_UNORM" };
        if (reconstructZ) args.Add("-reconstructz");
        args.AddRange(new[] { "-o", work, ddsPath });

        var psi = new ProcessStartInfo
        {
            FileName = texconv,
            WorkingDirectory = work,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        var run = ProcessRunner.Run(psi, TimeSpan.FromSeconds(30));
        if (!run.Started || run.TimedOut) return null;

        if (File.Exists(produced)) return produced;
        if (reconstructZ) { try { File.Delete(produced); } catch { } return RunTexconv(texconv, ddsPath, work, false); }
        return null;
    }

    private static bool IsBc5(string ddsPath)
    {
        try
        {
            using var fs = File.OpenRead(ddsPath);
            Span<byte> h = stackalloc byte[148];
            int n = fs.Read(h);
            if (n < 88 || BitConverter.ToUInt32(h.Slice(0, 4)) != 0x20534444u) return false;
            uint fourCC = BitConverter.ToUInt32(h.Slice(84, 4));
            if (fourCC == 0x32495441u) return true;
            if (fourCC == 0x30315844u && n >= 132)
            {
                uint dxgi = BitConverter.ToUInt32(h.Slice(128, 4));
                return dxgi == 83 || dxgi == 84;
            }
        }
        catch (Exception ex) { DebugLog.Exception("Texture.IsBc5", ex); }
        return false;
    }

    private static string Hash(string s)
    {
        var bytes = SHA1.HashData(Encoding.UTF8.GetBytes(s));
        var sb = new StringBuilder();
        foreach (var b in bytes) sb.Append(b.ToString("x2"));
        return sb.ToString().Substring(0, 16);
    }
}
