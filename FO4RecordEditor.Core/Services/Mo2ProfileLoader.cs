using System.IO;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Fallout4;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Order;
using Noggog;

namespace FO4RecordEditor.Services;

/// <summary>
/// Loads a Mod Organizer 2 modlist by reading its profile + mod folders **directly from disk**,
/// without running inside MO2's virtual file system. The editor cannot run under usvfs (the .NET
/// runtime dies on injection), so instead of relying on the VFS we reconstruct the same load
/// order MO2 would present:
///   - profiles/&lt;profile&gt;/plugins.txt  -> the plugin load order (lines starting with '*')
///   - profiles/&lt;profile&gt;/modlist.txt  -> mod priority (top = highest)
///   - mods/&lt;mod&gt;/, overwrite/, and the game Data folder -> where each plugin file lives
/// The result is a real Mutagen load order + link cache, wrapped so the rest of the app consumes
/// it exactly like a GameEnvironmentState (LoadOrder / LinkCache / DataFolderPath).
/// </summary>
public static class Mo2ProfileLoader
{
    // Every plugin in the load order is an mmap overlay. Record data stays off the managed heap, so
    // the load order costs almost nothing regardless of how big the modlist is.
    //
    // This used to full-read anything at or below a size threshold, so that those files carried no
    // open handle and save_plugin could File.Replace them in place. That was backwards on memory: a
    // full read materializes every record as a managed object, roughly 9x the file on disk, and it
    // was paid for the WHOLE load order at startup. Measured on a real 661-plugin modlist:
    //
    //   threshold   full-read   managed heap   RSS     load time
    //   1 MB          616        +489 MB      +646 MB    3.5 s
    //   64 KB         436         +45 MB      +157 MB    1.4 s
    //   all overlay     0          +5 MB       +29 MB    0.05 s
    //
    // The inversion is the point: at 1 MB the 616 small plugins were 53 MB on disk but essentially
    // all of the memory, while the 44 large ones -- 655 MB on disk, 92% of the bytes -- cost almost
    // nothing as overlays.
    //
    // In-place saving is preserved a different way: TryReplaceLoadedPluginFile disposes just the one
    // plugin's overlay to release its handle, swaps the new file in, and reopens it. Reading a whole
    // load order into RAM to keep one file writable was paying for 661 plugins to save one.
    //
    // Note a startup full read was never what made a plugin editable: WriteService.EnsureOpen does
    // its own CreateFromBinary when a plugin is opened for editing, whatever the load order did.

    /// <summary>Minimal env shape the rest of the app accesses via dynamic.</summary>
    public sealed class Mo2GameEnvironment
    {
        public required ILoadOrderGetter<IModListingGetter<IFallout4ModGetter>> LoadOrder { get; init; }

        /// <summary>
        /// Settable rather than init-only because <see cref="TryReplaceLoadedPluginFile"/> has to
        /// rebuild it: an immutable link cache captures the mod instances it was built from, so once
        /// a plugin's overlay is disposed to release its file handle, the old cache is holding a
        /// disposed overlay and must be replaced before anything reads through it again.
        /// </summary>
        public required ILinkCache LinkCache { get; set; }
        public required DirectoryPath DataFolderPath { get; init; }

        /// <summary>
        /// Plugin file name -> the REAL on-disk path each one was actually loaded from (an MO2 mod
        /// folder, the overwrite folder, or the game Data folder -- whichever won by priority). The
        /// standard Mutagen ModListing this env's LoadOrder entries wrap has no Path property, and
        /// DataFolderPath here is just the vanilla game Data folder, so without this map
        /// WriteService.FindPluginPath had no way to re-locate an MO2-resolved plugin for
        /// open_plugin/save_plugin/the Masters panel -- every one of them failed with "could not
        /// locate on disk" for every MO2-managed mod, which is effectively all of them. Read-only
        /// browsing (list_plugins/get_record/...) never hit this: it only needs the mod already
        /// loaded into memory, which the LoadOrder entries do carry correctly.
        /// </summary>
        public required IReadOnlyDictionary<string, string> PluginPaths { get; init; }
    }

    public sealed record Mo2Info(string InstancePath, string Profile, string GameDataFolder, string OverwriteFolder);

    /// <summary>
    /// Plugins from the last <see cref="Load"/> that were found on disk but could not be parsed, with
    /// the reason. They are NOT in the load order, so anything derived from it -- conflict scans,
    /// patches, master lists -- is computed as though they do not exist.
    /// </summary>
    /// <remarks>
    /// Exposed separately from the summary string so the GUI and the MCP layer can surface this as a
    /// real warning rather than a sentence a user may never read. Previously these were folded into
    /// the "could not be resolved" count with no reason attached, which made a corrupt plugin
    /// indistinguishable from a missing one.
    /// </remarks>
    public static IReadOnlyList<(string name, string reason)> FailedToLoad { get; private set; } =
        Array.Empty<(string, string)>();

    /// <summary>
    /// Swap <paramref name="tempPath"/> in over <paramref name="targetPath"/> for a plugin that this
    /// environment currently holds open, releasing and reopening just that one plugin's overlay.
    /// Returns false (with <paramref name="error"/> set) if the swap could not be done, in which case
    /// the caller should fall back to its own write path -- nothing is left half-applied.
    /// </summary>
    /// <remarks>
    /// Every plugin in the load order is an mmap overlay, and a mapped file cannot be replaced on
    /// Windows while the mapping is live. Rather than keep the whole load order in RAM so that files
    /// stay writable, this releases exactly the one plugin being saved.
    /// <para>
    /// The ordering matters and is not incidental. <c>LoadOrder.Set</c> disposes the listing it
    /// replaces (the load order is constructed with <c>disposeItems: true</c>), which is what actually
    /// closes the handle -- so the old overlay is dropped BEFORE the file is touched, and the new one
    /// is opened only after. Between those two points the plugin's slot holds no mod.
    /// </para>
    /// <para>
    /// The link cache must then be rebuilt, because an immutable link cache captures the mod
    /// instances it was constructed from: leaving the old one in place would leave every subsequent
    /// resolve reading through a disposed overlay. Both holders are updated --
    /// <see cref="MutagenLoader.LinkCache"/> and this environment's own property -- and every reader
    /// in the app fetches one of those at point of use rather than caching its own copy, so they all
    /// pick up the replacement. The mod index for the plugin is invalidated for the same reason: its
    /// entries point at records owned by the overlay that just went away.
    /// </para>
    /// <para>
    /// Callers must have finished writing <paramref name="tempPath"/> first. This is deliberately not
    /// thread-safe: it is driven by an explicit user save, and a concurrent read during the swap
    /// window would be reading a load order mid-mutation regardless of what this method did.
    /// </para>
    /// </remarks>
    public static bool TryReplaceLoadedPluginFile(
        object? env, string pluginName, string tempPath, string targetPath, out string error)
    {
        error = "";
        if (env is not Mo2GameEnvironment mo2)
        {
            error = "no MO2 environment loaded";
            return false;
        }
        // Set() is on the concrete LoadOrder, not the getter interface the env exposes.
        if (mo2.LoadOrder is not LoadOrder<IModListingGetter<IFallout4ModGetter>> lo)
        {
            error = "load order is not mutable";
            return false;
        }

        ModKey modKey;
        try { modKey = ModKey.FromNameAndExtension(pluginName); }
        catch (Exception ex) { error = $"'{pluginName}' is not a valid plugin name: {ex.Message}"; return false; }

        if (!lo.TryGetValue(modKey, out var existing) || existing.Mod == null)
        {
            error = $"'{pluginName}' is not currently loaded in the environment";
            return false;
        }

        // Drop the overlay (Set disposes what it replaces) so the file handle is gone before the swap.
        lo.Set(new ModListing<IFallout4ModGetter>(modKey, mod: null, enabled: existing.Enabled, ghostSuffix: existing.GhostSuffix));
        MutagenLoader.InvalidateModIndex(pluginName);

        try
        {
            if (File.Exists(targetPath)) File.Replace(tempPath, targetPath, null);
            else File.Move(tempPath, targetPath);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            // Put the plugin back from whatever is still on disk, so a failed swap does not leave the
            // environment permanently missing it.
            TryReopen(lo, modKey, targetPath, existing);
            RebuildLinkCache(mo2, lo);
            return false;
        }

        TryReopen(lo, modKey, targetPath, existing);
        RebuildLinkCache(mo2, lo);
        return true;
    }

    private static void TryReopen(
        LoadOrder<IModListingGetter<IFallout4ModGetter>> lo, ModKey modKey, string path,
        IModListingGetter<IFallout4ModGetter> previous)
    {
        try
        {
            var reopened = Fallout4Mod.CreateFromBinaryOverlay(ModPath.FromPath(path), Fallout4Release.Fallout4);
            lo.Set(new ModListing<IFallout4ModGetter>(modKey, reopened, enabled: previous.Enabled, ghostSuffix: previous.GhostSuffix));
        }
        catch
        {
            // Leave the empty listing in place rather than throw out of a save that already succeeded
            // on disk. The plugin is on disk correctly; only this session's view of it is stale, and
            // reloading the modlist restores it.
        }
    }

    private static void RebuildLinkCache(Mo2GameEnvironment mo2, LoadOrder<IModListingGetter<IFallout4ModGetter>> lo)
    {
        var rebuilt = lo.ToImmutableLinkCache<IFallout4Mod, IFallout4ModGetter>();
        mo2.LinkCache = rebuilt;
        MutagenLoader.LinkCache = rebuilt;
    }

    /// <summary>Reads ModOrganizer.ini at the instance root to find the active profile + game Data folder.</summary>
    public static Mo2Info ReadInstanceInfo(string instancePath)
    {
        string profile = "Default";
        string gamePath = "";
        var ini = Path.Combine(instancePath, "ModOrganizer.ini");
        if (File.Exists(ini))
        {
            foreach (var raw in File.ReadAllLines(ini))
            {
                var line = raw.Trim();
                if (line.StartsWith("selected_profile=", StringComparison.OrdinalIgnoreCase))
                    profile = Unwrap(line);
                else if (line.StartsWith("gamePath=", StringComparison.OrdinalIgnoreCase))
                    gamePath = Unwrap(line);
            }
        }

        // gamePath points at the game install root; plugins live in its Data subfolder.
        var dataFolder = string.IsNullOrWhiteSpace(gamePath) ? "" : Path.Combine(gamePath, "Data");

        // A portable MO2 instance keeps the game beside itself, and an instance copied between
        // machines or drives keeps the old gamePath. Falling back to that layout means a moved
        // instance still finds its base masters instead of loading mods against nothing.
        if (dataFolder.Length == 0 || !Directory.Exists(dataFolder))
        {
            var portable = Path.Combine(instancePath, "Stock Folder", "Data");
            if (Directory.Exists(portable)) dataFolder = portable;
        }
        return new Mo2Info(instancePath, profile, dataFolder, ResolveOverwriteFolder(instancePath));
    }

    /// <summary>The instance's real overwrite folder, honoring ModOrganizer.ini's base_directory /
    /// overwrite_directory (with the %BASE_DIR% placeholder). Defaults to &lt;instance&gt;\overwrite --
    /// correct for portable instances and for global instances whose ini sets explicit paths.</summary>
    public static string ResolveOverwriteFolder(string instancePath)
    {
        string baseDir = "", overwriteDir = "";
        var ini = Path.Combine(instancePath, "ModOrganizer.ini");
        if (File.Exists(ini))
        {
            foreach (var raw in File.ReadAllLines(ini))
            {
                var line = raw.Trim();
                if (line.StartsWith("base_directory=", StringComparison.OrdinalIgnoreCase))
                    baseDir = Unwrap(line);
                else if (line.StartsWith("overwrite_directory=", StringComparison.OrdinalIgnoreCase))
                    overwriteDir = Unwrap(line);
            }
        }

        if (string.IsNullOrWhiteSpace(baseDir)) baseDir = instancePath;
        baseDir = baseDir.Replace("%BASE_DIR%", instancePath);

        var ow = string.IsNullOrWhiteSpace(overwriteDir)
            ? Path.Combine(baseDir, "overwrite")
            : overwriteDir.Replace("%BASE_DIR%", baseDir);

        ow = ow.Replace('/', Path.DirectorySeparatorChar);
        if (!Path.IsPathRooted(ow)) ow = Path.Combine(baseDir, ow);
        try { return Path.GetFullPath(ow); } catch { return ow; }
    }

    // ModOrganizer.ini values are stored as: key=@ByteArray(value)  with '\' escaped as '\\'.
    private static string Unwrap(string line)
    {
        var v = line[(line.IndexOf('=') + 1)..].Trim();
        const string prefix = "@ByteArray(";
        if (v.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && v.EndsWith(")"))
            v = v[prefix.Length..^1];
        return NativePath(v.Replace("\\\\", "\\"));
    }

    /// <summary>
    /// Turn a Windows or Wine path from ModOrganizer.ini into one this OS can open.
    ///
    /// MO2 writes gamePath as a Windows path. Read on Linux, "Z:\media\ricky\..." is not a path at
    /// all, so the game install never resolved and the base masters silently never loaded: the whole
    /// of Fallout4.esm and the DLCs were absent from the load order while 651 mod plugins loaded
    /// fine. Everything downstream inherited that: references into the base game looked dangling,
    /// conflict matrices omitted the originating master, and searches missed vanilla FormIDs.
    ///
    /// Wine maps Z: to the filesystem root; other drive letters live under the prefix's drive_&lt;x&gt;.
    /// </summary>
    private static string NativePath(string p)
    {
        if (string.IsNullOrWhiteSpace(p) || OperatingSystem.IsWindows()) return p;

        if (p.Length >= 2 && char.IsLetter(p[0]) && p[1] == ':')
        {
            var rest = p[2..].Replace('\\', '/').TrimStart('/');
            var drive = char.ToLowerInvariant(p[0]);
            if (drive == 'z') return "/" + rest;
            var prefix = Environment.GetEnvironmentVariable("WINEPREFIX")
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".wine");
            return Path.Combine(prefix, "drive_" + drive, rest);
        }
        return p.Replace('\\', '/');
    }

    /// <summary>
    /// Build the environment for an MO2 instance + profile. Returns the env (store on
    /// ShellViewModel.GameEnvironment) and the ordered list of loaded plugin filenames.
    /// </summary>
    public static (object env, List<string> plugins) Load(
        string instancePath, string profile, string gameDataFolder,
        IProgress<(string message, double? percent)>? progress = null)
    {
        progress?.Report(("Reading MO2 profile...", 0));

        var profileDir = Path.Combine(instancePath, "profiles", profile);
        var pluginsTxt = Path.Combine(profileDir, "plugins.txt");
        var modlistTxt = Path.Combine(profileDir, "modlist.txt");
        if (!File.Exists(pluginsTxt))
            throw new FileNotFoundException($"plugins.txt not found for profile '{profile}'", pluginsTxt);

        // Ordered load order (top = loaded first). Lines starting with '*' are enabled plugins.
        var loadOrder = File.ReadAllLines(pluginsTxt)
            .Select(l => l.Trim())
            .Where(l => l.StartsWith('*'))
            .Select(l => l[1..].Trim())
            .Where(l => l.Length > 0)
            .ToList();

        // Fallout 4 force-loads the base masters (Fallout4.esm + the official DLCs) -- they are
        // NOT written to plugins.txt. Without them the link cache is missing the entire base game:
        // vanilla records don't appear and FormLinks into them never resolve to names. Prepend the
        // implicit listings (in canonical order) that aren't already present; missing DLCs are
        // simply skipped later when their file isn't found on disk.
        var present = new HashSet<string>(loadOrder, StringComparer.OrdinalIgnoreCase);
        var implicitMasters = Mutagen.Bethesda.Plugins.Implicits.Get(GameRelease.Fallout4).Listings
            .Select(m => m.FileName.String)
            .Where(m => !present.Contains(m))
            .ToList();
        loadOrder = implicitMasters.Concat(loadOrder).ToList();

        // Enabled mods in priority order (modlist.txt top = highest). '+' enabled, '-' disabled,
        // separators end with '_separator', '*' markers are unmanaged (game Data) -- skip those.
        var modsHighToLow = File.Exists(modlistTxt)
            ? File.ReadAllLines(modlistTxt)
                .Select(l => l.Trim())
                .Where(l => l.StartsWith('+'))
                .Select(l => l[1..].Trim())
                .Where(l => l.Length > 0 && !l.EndsWith("_separator", StringComparison.OrdinalIgnoreCase))
                .ToList()
            : new List<string>();

        progress?.Report(($"Resolving {loadOrder.Count} plugins across {modsHighToLow.Count} mods...", 5));

        // Map plugin filename -> real path, honoring MO2 priority. Build from lowest priority to
        // highest so higher-priority sources overwrite: game Data (lowest) -> mods bottom->top ->
        // overwrite (highest).
        var resolved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        void Index(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return;
            foreach (var f in Directory.EnumerateFiles(folder))
            {
                var ext = Path.GetExtension(f);
                if (ext.Equals(".esp", StringComparison.OrdinalIgnoreCase) ||
                    ext.Equals(".esm", StringComparison.OrdinalIgnoreCase) ||
                    ext.Equals(".esl", StringComparison.OrdinalIgnoreCase))
                    resolved[Path.GetFileName(f)] = f;
            }
        }

        Index(gameDataFolder);
        var modsDir = Path.Combine(instancePath, "mods");
        for (int i = modsHighToLow.Count - 1; i >= 0; i--)   // bottom -> top so top wins
            Index(Path.Combine(modsDir, modsHighToLow[i]));
        Index(ResolveOverwriteFolder(instancePath));   // honors ModOrganizer.ini base/overwrite paths

        // Load each plugin (in load order). Patch-sized plugins are read FULLY into memory so they
        // are not memory-mapped -- the file stays unlocked, so the AI/editor can save edits straight
        // back over it. Big masters (Fallout4.esm, DLCs, large mods) stay as lazy binary overlays
        // (fast, low memory); you don't edit those in place anyway.
        var listings = new List<IModListingGetter<IFallout4ModGetter>>();
        var loaded = new List<string>();
        var missing = new List<string>();
        // Found on disk but failed to parse -- kept apart from `missing` so the summary can say
        // which plugins are absent from the load order and why, instead of dropping them quietly.
        var failedToLoad = new List<(string name, string reason)>();
        var pluginPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        MutagenLoader.PluginSourcePaths.Clear();   // a new profile can resolve the same name elsewhere
        for (int i = 0; i < loadOrder.Count; i++)
        {
            var name = loadOrder[i];
            double pct = 5 + (double)(i + 1) / loadOrder.Count * 90;
            progress?.Report(($"Loading {name} ({i + 1}/{loadOrder.Count})...", pct));

            if (!resolved.TryGetValue(name, out var path)) { missing.Add(name); continue; }
            try
            {
                var modKey = ModKey.FromNameAndExtension(name);
                long size = 0;
                try { size = new FileInfo(path).Length; } catch { }

                // An overlay keeps an open handle on its plugin file for as long as the environment
                // lives, and nothing tears the environment down -- which is why saving over a loaded
                // plugin falls back to writing a .new file beside it. (Measured, not assumed: loading
                // Fallout4.esm as an overlay shows an open fd for it in /proc/self/fd, gone after
                // Dispose; a full read shows none.) ReadLargePluginsIntoMemory lifts the size limit so
                // everything is read into memory instead, trading RAM for saving in place. With the
                // setting off this evaluates to exactly the previous expression.
                IFallout4ModGetter mod = ToolPaths.ReadLargePluginsIntoMemory
                    ? Fallout4Mod.CreateFromBinary(ModPath.FromPath(path), Fallout4Release.Fallout4)      // opt-in: RAM for lock-free files
                    : Fallout4Mod.CreateFromBinaryOverlay(ModPath.FromPath(path), Fallout4Release.Fallout4); // mmap, off the managed heap
                _ = size;

                listings.Add(new ModListing<IFallout4ModGetter>(modKey, mod, enabled: true, ghostSuffix: string.Empty));
                loaded.Add(name);
                pluginPaths[name] = path;
                MutagenLoader.PluginSourcePaths[name] = path;   // keys this plugin's persisted counts (#87)
            }
            catch (Exception ex)
            {
                // A plugin that WAS found but would not parse is a different problem from one that
                // was never found, and used to be reported as the same "could not be resolved" line
                // with no reason. That silently drops real plugins from the load order: on the
                // reference modlist three of them (a truncated subrecord, a bad FLTV length, a
                // duplicate FormKey in a Grass group) vanished with nothing said, so the user was
                // patching against a load order that did not contain them.
                failedToLoad.Add((name, $"{ex.GetType().Name}: {ex.Message.Split('\n')[0].Trim()}"));
            }
        }

        var lo = new LoadOrder<IModListingGetter<IFallout4ModGetter>>(listings, disposeItems: true);
        var linkCache = lo.ToImmutableLinkCache<IFallout4Mod, IFallout4ModGetter>();
        MutagenLoader.LinkCache = linkCache;   // lets the field walker resolve FormLinks to names

        // Record each plugin's ESL ("small master") flag so the save path can master-map custom
        // ActorValue condition params correctly (0xFE light-master encoding for ESL masters).
        MutagenLoader.MasterIsEsl.Clear();
        foreach (var l in listings)
            if (l.Mod != null) MutagenLoader.MasterIsEsl[l.ModKey.FileName.String] = l.Mod.IsSmallMaster;

        var summary = $"Loaded {loaded.Count} plugins from MO2 profile '{profile}'."
            + (missing.Count > 0
                ? $" {missing.Count} could not be resolved (e.g. {string.Join(", ", missing.Take(5))})."
                : "")
            + (failedToLoad.Count > 0
                ? $" WARNING: {failedToLoad.Count} plugin(s) were found but FAILED TO LOAD and are NOT in the "
                  + "load order, so conflicts and patches will not account for them: "
                  + string.Join("; ", failedToLoad.Take(5).Select(f => $"{f.name} ({f.reason})"))
                  + (failedToLoad.Count > 5 ? $"; and {failedToLoad.Count - 5} more." : ".")
                : "");

        FailedToLoad = failedToLoad;
        progress?.Report((summary, 100));

        // So texture/mesh lookups (TextureService.ResolveDds/ResolveNif) search this ACTUAL modlist's
        // mods\ and overwrite\ folders, not whatever (if anything) is saved in settings.json.
        TextureService.SetSessionRoots(new[] { gameDataFolder, modsDir, ResolveOverwriteFolder(instancePath) });

        // #65: the general asset resolver needs each MOD as its own data root, in modlist priority --
        // "mods\Foo\Meshes\x.nif" is not reachable by treating the mods\ parent as a data root, which
        // is why TextureService's roots above cannot answer a loose-file question.
        AssetResolver.SetSessionDataRoots(AssetResolver.Mo2DataRoots(
            instancePath, modsHighToLow, ResolveOverwriteFolder(instancePath), gameDataFolder));

        var env = new Mo2GameEnvironment
        {
            LoadOrder = lo,
            LinkCache = linkCache,
            DataFolderPath = new DirectoryPath(gameDataFolder),
            PluginPaths = pluginPaths,
        };
        return (env, loaded);
    }
}
