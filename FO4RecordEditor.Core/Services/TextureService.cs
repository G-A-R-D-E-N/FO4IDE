using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Archives;
using Newtonsoft.Json;
using FO4RecordEditor.Services.Materials;

namespace FO4RecordEditor.Services;

/// <summary>
/// Resolves a NIF's game-relative texture path (e.g. "Textures\mod\thing_d.dds") to a real DDS and
/// converts it to a PNG data URL for the WebGL viewport -- via Texconv (DirectXTex), which decodes
/// every BCn format FO4 uses (BC1/3/5/7) that the browser's DDSLoader cannot. The DDS is found either
/// as a loose file (searched from a user root and the NIF's Data\ ancestors) OR extracted from a
/// Fallout 4 BA2 archive (Mutagen's Ba2Reader) -- most vanilla/mod textures ship packed. Converted
/// PNGs are cached by source path + write time so repeat views are instant.
/// </summary>
public static class TextureService
{
    private static string? ResolveTexconv() => ToolPaths.Texconv();

    /// <summary>Return a "data:image/png;base64,…" URL for a NIF texture slot, or "" if it can't be
    /// resolved/converted. nifPath anchors the loose-texture and archive search; textureRoot (optional)
    /// is a user-picked Data/Textures folder tried first.</summary>
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

    /// <summary>Same resolution as <see cref="GetTexturePngDataUrl"/> but returns the cached PNG's
    /// file path directly, for non-web consumers (e.g. the Godot prototype loading an Image straight
    /// off disk) that don't need a base64 data URL. Null if it can't be resolved/converted.</summary>
    public static string? GetTexturePngPath(string nifPath, string relTexPath, string textureRoot = "")
    {
        try
        {
            nifPath = (nifPath ?? "").Trim().Trim('"');
            relTexPath = (relTexPath ?? "").Trim().Trim('"');
            textureRoot = (textureRoot ?? "").Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(relTexPath)) return null;

            // A shape with no texture in its own BSShaderTextureSet can still have a real diffuse via
            // a linked .bgsm material file (BSLightingShaderProperty.rootMaterialName) -- the frontend
            // passes that path here indistinguishable from a normal texture request except by
            // extension, when the shape's own "textures" array came up empty. Resolve + parse it and
            // fall through to the same DDS pipeline below using its DiffuseTexture.
            if (relTexPath.EndsWith(".bgsm", StringComparison.OrdinalIgnoreCase))
            {
                var mat = ResolveAndParseBgsm(nifPath, relTexPath, textureRoot);
                if (mat == null) return null;
                var diffuse = (mat.DiffuseTexture ?? "").TrimEnd('\0'); // BGSM strings are NUL-terminated; an empty slot is "\0"
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

    /// <summary>Resolve and parse a shape's linked .bgsm, exposing every material field (normal map,
    /// glow map, emission flags/color) beyond just the diffuse texture <see cref="GetTexturePngPath"/>
    /// uses internally. Null if the file can't be found or fails to parse.</summary>
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

    // Find the DDS: a user-picked texture root first, then absolute path, then game-relative under any
    // ancestor of the NIF (Data\Textures\…), then alongside the NIF, and finally inside FO4 BA2 archives.
    private static string? ResolveDds(string nifPath, string rel, string textureRoot = "")
    {
        rel = rel.Replace('/', '\\').TrimStart('\\');
        if (Path.IsPathRooted(rel) && File.Exists(rel)) return rel;

        string? nifDir = null;
        try { nifDir = Path.GetDirectoryName(Path.GetFullPath(nifPath)); } catch { }

        var candidates = new List<string>();

        // 1) user-picked texture root (highest priority): handle root = Data\, root = Data\Textures\,
        //    or root = the exact folder holding the file.
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
            candidates.Add(Path.Combine(dir, "Textures", rel));  // when rel lacks a leading Textures\
            dir = Path.GetDirectoryName(dir);
        }
        if (nifDir != null) candidates.Add(Path.Combine(nifDir, Path.GetFileName(rel)));

        foreach (var c in candidates)
            if (File.Exists(c)) return c;

        // 2) not loose -- try to pull it out of a BA2 (vanilla + mods ship packed textures).
        return ResolveFromArchives(rel, nifDir, textureRoot);
    }

    // Same search strategy as ResolveDds, but for a shape's linked .bgsm material (real path
    // "Materials\<rootMaterialName>", verified against a real archive -- not assumed) instead of a
    // texture. Kept as a separate method rather than parameterizing ResolveDds: the loose-file
    // fallback subfolder name ("Textures" vs "Materials") differs and threading that through every
    // call site would be noisier than one small mirror function.
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

    // ---- BA2 archive resolution ------------------------------------------------------------------
    //
    // Lazy, session-cached, tiered: scan cheap NIF-local / user-root archives first (a mod's own BA2
    // usually sits beside its meshes), and only fall back to the big MO2 mods / game Data scan on a
    // miss. Each archive's .dds file table is indexed once (innerPath -> archive path); lookups after
    // that are dictionary hits, and the matched entry is extracted to a temp .dds for Texconv.

    private static readonly object _lock = new();
    private static readonly Dictionary<string, string> _index = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> _scannedArchives = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> _scannedRoots = new(StringComparer.OrdinalIgnoreCase);

    // Where ExtractFromArchive/ConvertToPng cache their outputs. Also consulted by AddDataRoots (see
    // its comment) to recognize when a "nifDir" is our OWN extraction cache, not a real Data tree.
    // internal (not private) so a test can verify the climb-skip without touching real files.
    public static readonly string TexCacheDir = Path.Combine(Path.GetTempPath(), "FO4RE_Tex");

    // Per-cache-key lock so concurrent conversions/extractions of the SAME source (very common: many
    // meshes in a cell share one underlying rock/dirt/wood texture, or the same archive entry) don't
    // race on the same deterministic output path. Keyed rather than one global lock so unrelated
    // textures still convert in parallel -- the Cell Viewer fires one fetch per unique (model,shape).
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, object> _keyLocks = new(StringComparer.Ordinal);
    private static object LockFor(string key) => _keyLocks.GetOrAdd(key, static _ => new object());
    private static string[]? _globalRoots;   // session roots if set, else settings-derived -- cached until SetSessionRoots invalidates it

    private static string Norm(string p) => p.Replace('/', '\\').TrimStart('\\');

    private static string? ResolveFromArchives(string rel, string? nifDir, string textureRoot)
        => ResolveFromArchives(rel, nifDir, textureRoot, "textures");

    // topFolder generalizes this for the .bgsm lookup (ResolveMaterialFile passes "materials") the
    // same way LookupKeys already generalizes for ResolveNif's "meshes".
    private static string? ResolveFromArchives(string rel, string? nifDir, string textureRoot, string topFolder)
    {
        try
        {
            var keys = LookupKeys(rel, topFolder);

            // Tier 1 -- cheap, NIF-local / user-root archives.
            var cheap = new List<string>();
            AddDataRoots(cheap, nifDir);
            if (!string.IsNullOrWhiteSpace(textureRoot)) AddDataRoots(cheap, textureRoot);
            var hit = ScanAndLookup(cheap, keys);
            if (hit != null) return hit;

            // Tier 2 -- the big scan: configured Data folder + MO2 mods tree.
            var hit2 = ScanAndLookup(GlobalRoots(), keys);
            if (hit2 != null) return hit2;
        }
        catch (Exception ex) { DebugLog.Exception("Texture.Archive", ex); }
        return null;
    }

    // A slot path is normally "Textures\...". Match that, and also a bare form (some NIFs omit the
    // leading Textures\), against the archives' Data-relative entries.
    private static List<string> LookupKeys(string rel) => LookupKeys(rel, "textures");

    // Generalized for ResolveNif: meshes use the "Meshes\..." top folder instead of "Textures\...".
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

    // Add a folder and any "Data" ancestor of it as BA2 search roots (BA2s live in a Data folder, but
    // a mod's loose meshes may also sit beside its BA2).
    // internal (not private) so a test can verify the climb-skip / stop-at-Data behavior directly --
    // it's pure string manipulation, no I/O, so this is a cheap deterministic unit test.
    public static void AddDataRoots(List<string> roots, string? path)
    {
        var dir = path;
        // A NIF resolved from OUR OWN BA2-extraction temp cache (used for every mesh the Cell Viewer
        // pulls out of an archive -- ResolveNif extracts to TexCacheDir so niftool has a real file to
        // read) has no ancestor "Data" folder to find. Climbing from it walked all the way to a drive
        // root (the 24-level guard), which then made EnsureRootScanned recursively enumerate the
        // ENTIRE drive for .ba2 files -- hitting inaccessible OS junction points (C:\Config.Msi,
        // C:\Users\Default User, C:\Users\<user>\Application Data, ...) that threw mid-enumeration and
        // aborted the WHOLE texture resolution for whatever call triggered it (see EnsureRootScanned's
        // now-caught foreach below for why that was fatal, not just slow). Confirmed from a real debug
        // log: five separate UnauthorizedAccessExceptions, one per ancestor level, each one silently
        // failing that texture and leaving its mesh flat gray. Tier 2 (GlobalRoots, the real
        // Data/mods trees) already covers everything a temp-cache path could hope to find, so skip
        // the climb entirely for it.
        if (dir != null && dir.StartsWith(TexCacheDir, StringComparison.OrdinalIgnoreCase)) return;
        for (int guard = 0; dir != null && guard < 24; guard++)
        {
            roots.Add(dir);
            if (string.Equals(Path.GetFileName(dir), "Data", StringComparison.OrdinalIgnoreCase)) break;
            dir = Path.GetDirectoryName(dir);
        }
    }

    // Set by Mo2ProfileLoader.Load/MutagenLoader.BuildEnvironment right after a modlist actually
    // loads, so resolution reflects what's LOADED this session -- not just settings.json. Nothing
    // ever wrote the picked MO2 instance path back to settings.json (Mo2InstancePath is read-only
    // config for a *default*, not session state), so before this every texture/mesh lookup outside
    // the vanilla Data folder silently failed for any interactively-opened MO2 modlist. Found while
    // building the cell viewer, which depends on this to find almost anything: nearly every placed
    // object in a real modlist lives in a mods\ folder, not the vanilla Data folder.
    private static string[]? _sessionRoots;

    public static void SetSessionRoots(IEnumerable<string> roots)
    {
        _sessionRoots = roots.Where(r => !string.IsNullOrWhiteSpace(r)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        _globalRoots = null;   // next GlobalRoots() call re-derives instead of returning a stale cache
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

    // Index every .dds entry of every BA2 under a root (recursive). One-time per root/archive.
    private static void EnsureRootScanned(string root)
    {
        root = (root ?? "").Trim();
        if (string.IsNullOrWhiteSpace(root) || !_scannedRoots.Add(root) || !Directory.Exists(root)) return;

        IEnumerable<string> archives;
        try { archives = Directory.EnumerateFiles(root, "*.ba2", SearchOption.AllDirectories); }
        catch (Exception ex) { DebugLog.Exception("Texture.EnumBa2", ex); return; }

        int added = 0;
        var sw = Stopwatch.StartNew();
        // Directory.EnumerateFiles(..., AllDirectories) is lazy: an inaccessible subdirectory (an OS
        // reparse-point junction like C:\Config.Msi, hit if `root` is ever a drive root or a user
        // profile folder) throws from the enumerator's MoveNext() -- i.e. INSIDE this loop, not at the
        // call above. Uncaught, that propagates out of EnsureRootScanned and aborts the caller's ENTIRE
        // resolution attempt (ResolveFromArchives' try/catch stops it there, silently failing that
        // texture). Caught here instead so one bad subdirectory only truncates THIS root's index
        // (partial results, not a dropped call) -- defense in depth alongside the AddDataRoots fix
        // that stops the drive-root climb from happening in the first place.
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
                        // Indexed together (one scan pass over each archive) rather than three separate
                        // scans -- the same BA2s hold all three, so a texture lookup, a mesh lookup
                        // (ResolveNif, for the cell viewer), and a linked-material lookup (a shape with
                        // no embedded texture whose BSLightingShaderProperty points at a .bgsm instead)
                        // share this one cache.
                        if (string.IsNullOrEmpty(p) ||
                            !(p.EndsWith(".dds", StringComparison.OrdinalIgnoreCase) ||
                              p.EndsWith(".nif", StringComparison.OrdinalIgnoreCase) ||
                              p.EndsWith(".bgsm", StringComparison.OrdinalIgnoreCase))) continue;
                        var key = Norm(p);
                        if (!_index.ContainsKey(key)) { _index[key] = archive; added++; }  // first (most-local) wins
                    }
                }
                catch (Exception ex) { DebugLog.Exception("Texture.ReadBa2:" + Path.GetFileName(archive), ex); }
            }
        }
        catch (Exception ex) { DebugLog.Exception("Texture.EnumBa2:" + root, ex); }
        if (added > 0)
            DebugLog.Info("Texture.Index", $"Indexed {added} DDS/NIF entries from BA2s under {root} in {sw.ElapsedMilliseconds}ms");
    }

    // Pull one entry out of a BA2 to a cached temp file (keyed by archive path+mtime+inner path).
    // Extension comes from the actual inner entry, not assumed -- this used to hardcode ".dds", which
    // silently corrupted mesh resolution (a .nif extracted and named "....dds" is a file niftool
    // will reject); caught while adding ResolveNif for the cell viewer, so fixed for both callers.
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

            // Same deterministic-key race as ConvertToPng: two threads extracting the same archive
            // entry (e.g. a shared texture referenced by many meshes) could both open the archive
            // reader and write the same outPath concurrently. Lock serializes it; the write-to-temp
            // + Move (not a direct WriteAllBytes) means a File.Exists check from another thread can
            // never observe a partially-written file even without the lock, as defense in depth.
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

    /// <summary>
    /// Resolve a Data-relative NIF path (a placed reference's base object Model.File, e.g.
    /// "Clutter\Rock01.nif") to a real file -- loose under the session's data/mods roots, or
    /// extracted from a BA2 to a cached temp file. Built for the cell viewer's batch geometry
    /// resolution (NifService), same search roots as ResolveDds (SetSessionRoots/GlobalRoots) but
    /// with no per-call anchor directory: a cell's placed objects are always Data-relative, there is
    /// no "the nif this texture belongs to" to anchor against the way single-mesh preview has.
    /// </summary>
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
        // Salt the cache key so PNGs from before Z-reconstruction, and from before the in-process
        // decoder replaced Texconv, aren't reused.
        var key = Hash(ddsPath.ToLowerInvariant() + "|" + stamp + "|v3");
        Directory.CreateDirectory(TexCacheDir);
        var pngPath = Path.Combine(TexCacheDir, key + ".png");
        if (File.Exists(pngPath)) return pngPath;

        var decoded = DecodeInProcess(ddsPath, pngPath, key);
        if (decoded != null) return decoded;

        var texconv = ResolveTexconv();
        if (texconv == null) return null;

        // Serialize conversions of the SAME source texture. `work` below is deterministic (derived
        // only from `key`), so without this lock two threads converting the same texture (common --
        // many meshes in a cell share one diffuse) raced: one thread's `finally` deleted the shared
        // work directory out from under the other mid-texconv (DirectoryNotFoundException on its
        // File.Copy), or both copied into the same pngPath at once (IOException "used by another
        // process"). Either failure returned null silently, so the mesh fell back to flat gray.
        // Confirmed from a real debug log showing both exact exceptions.
        lock (LockFor(key))
        {
            if (File.Exists(pngPath)) return pngPath; // someone else finished while we waited for the lock

            var work = Path.Combine(TexCacheDir, "w_" + key);
            Directory.CreateDirectory(work);
            try
            {
                // BC5 stores only R,G (a tangent-space normal map); decoded straight to RGBA its blue is
                // 0 and three.js lights it wrong. -reconstructz rebuilds Z = sqrt(1-x²-y²). Gate it to
                // BC5 so diffuse/other maps are untouched, and fall back if an older texconv lacks it.
                var produced = RunTexconv(texconv, ddsPath, work, IsBc5(ddsPath));
                if (produced == null) return null;
                File.Copy(produced, pngPath, true);
                return pngPath;
            }
            finally { try { Directory.Delete(work, true); } catch { } }
        }
    }

    /// <summary>
    /// Decode and encode without leaving the process: no temp .dds, no Texconv.exe launch, no temp
    /// .png read back. That is three quarters of what showing one texture used to cost, on a panel
    /// that fetches one per unique (mesh, shape).
    ///
    /// Returns null for anything the decoder does not handle -- BC6H, the signed BCn spellings, the
    /// float formats, 24bpp uncompressed -- and the Texconv path below takes over. None of those
    /// appear on a mesh; 24bpp is ENB/ReShade colour LUTs.
    ///
    /// One deliberate difference from Texconv: an sRGB-tagged texture keeps its stored values.
    /// Texconv converting to R8G8B8A8_UNORM also converts sRGB to linear, which darkened those
    /// textures in the viewport even though a byte-identical UNORM copy rendered correctly.
    /// </summary>
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
                // Write beside the target and move into place, so a reader never sees a half-written
                // PNG if two cells ask for the same texture at once.
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

    // Run texconv into an isolated dir; returns the produced PNG path, or null. When reconstructZ is
    // requested but yields nothing (old texconv rejects the flag), retries once without it.
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

        // Was: ReadToEnd() on stdout, then stderr, then WaitForExit(30s). texconv warning-spamming
        // stderr filled that pipe while we blocked on stdout, and the timeout sat after the reads
        // where it could never run -- so a chatty conversion hung the caller permanently.
        var run = ProcessRunner.Run(psi, TimeSpan.FromSeconds(30));
        if (!run.Started || run.TimedOut) return null;

        if (File.Exists(produced)) return produced;
        if (reconstructZ) { try { File.Delete(produced); } catch { } return RunTexconv(texconv, ddsPath, work, false); }
        return null;
    }

    // Read a DDS header far enough to tell if it is BC5 (ATI2 FourCC, or a DX10 header with
    // DXGI_FORMAT_BC5_UNORM/SNORM = 83/84). BC5 is FO4's normal-map format.
    private static bool IsBc5(string ddsPath)
    {
        try
        {
            using var fs = File.OpenRead(ddsPath);
            Span<byte> h = stackalloc byte[148];
            int n = fs.Read(h);
            if (n < 88 || BitConverter.ToUInt32(h.Slice(0, 4)) != 0x20534444u) return false;  // "DDS "
            uint fourCC = BitConverter.ToUInt32(h.Slice(84, 4));                                // ddspf.dwFourCC
            if (fourCC == 0x32495441u) return true;                                             // "ATI2" = BC5
            if (fourCC == 0x30315844u && n >= 132)                                              // "DX10" ext header
            {
                uint dxgi = BitConverter.ToUInt32(h.Slice(128, 4));
                return dxgi == 83 || dxgi == 84;                                                // BC5_UNORM / BC5_SNORM
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
