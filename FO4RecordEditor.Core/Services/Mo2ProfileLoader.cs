using System.IO;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Fallout4;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Order;
using Noggog;

namespace FO4RecordEditor.Services;












public static class Mo2ProfileLoader
{

























    public sealed class Mo2GameEnvironment
    {
        public required ILoadOrderGetter<IModListingGetter<IFallout4ModGetter>> LoadOrder { get; init; }







        public required ILinkCache LinkCache { get; set; }
        public required DirectoryPath DataFolderPath { get; init; }












        public required IReadOnlyDictionary<string, string> PluginPaths { get; init; }
    }

    public sealed record Mo2Info(string InstancePath, string Profile, string GameDataFolder, string OverwriteFolder);












    public static IReadOnlyList<(string name, string reason)> FailedToLoad { get; private set; } =
        Array.Empty<(string, string)>();
































    public static bool TryReplaceLoadedPluginFile(
        object? env, string pluginName, string tempPath, string targetPath, out string error)
    {
        error = "";
        if (env is not Mo2GameEnvironment mo2)
        {
            error = "no MO2 environment loaded";
            return false;
        }

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



        }
    }

    private static void RebuildLinkCache(Mo2GameEnvironment mo2, LoadOrder<IModListingGetter<IFallout4ModGetter>> lo)
    {
        var rebuilt = lo.ToImmutableLinkCache<IFallout4Mod, IFallout4ModGetter>();
        mo2.LinkCache = rebuilt;
        MutagenLoader.LinkCache = rebuilt;
    }


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


        var dataFolder = string.IsNullOrWhiteSpace(gamePath) ? "" : Path.Combine(gamePath, "Data");




        if (dataFolder.Length == 0 || !Directory.Exists(dataFolder))
        {
            var portable = Path.Combine(instancePath, "Stock Folder", "Data");
            if (Directory.Exists(portable)) dataFolder = portable;
        }
        return new Mo2Info(instancePath, profile, dataFolder, ResolveOverwriteFolder(instancePath));
    }




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


    private static string Unwrap(string line)
    {
        var v = line[(line.IndexOf('=') + 1)..].Trim();
        const string prefix = "@ByteArray(";
        if (v.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && v.EndsWith(")"))
            v = v[prefix.Length..^1];
        return NativePath(v.Replace("\\\\", "\\"));
    }












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


        var loadOrder = File.ReadAllLines(pluginsTxt)
            .Select(l => l.Trim())
            .Where(l => l.StartsWith('*'))
            .Select(l => l[1..].Trim())
            .Where(l => l.Length > 0)
            .ToList();






        var present = new HashSet<string>(loadOrder, StringComparer.OrdinalIgnoreCase);
        var implicitMasters = Mutagen.Bethesda.Plugins.Implicits.Get(GameRelease.Fallout4).Listings
            .Select(m => m.FileName.String)
            .Where(m => !present.Contains(m))
            .ToList();
        loadOrder = implicitMasters.Concat(loadOrder).ToList();



        var modsHighToLow = File.Exists(modlistTxt)
            ? File.ReadAllLines(modlistTxt)
                .Select(l => l.Trim())
                .Where(l => l.StartsWith('+'))
                .Select(l => l[1..].Trim())
                .Where(l => l.Length > 0 && !l.EndsWith("_separator", StringComparison.OrdinalIgnoreCase))
                .ToList()
            : new List<string>();

        progress?.Report(($"Resolving {loadOrder.Count} plugins across {modsHighToLow.Count} mods...", 5));




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
        for (int i = modsHighToLow.Count - 1; i >= 0; i--)
            Index(Path.Combine(modsDir, modsHighToLow[i]));
        Index(ResolveOverwriteFolder(instancePath));





        var listings = new List<IModListingGetter<IFallout4ModGetter>>();
        var loaded = new List<string>();
        var missing = new List<string>();


        var failedToLoad = new List<(string name, string reason)>();
        var pluginPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        MutagenLoader.PluginSourcePaths.Clear();
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








                IFallout4ModGetter mod = ToolPaths.ReadLargePluginsIntoMemory
                    ? Fallout4Mod.CreateFromBinary(ModPath.FromPath(path), Fallout4Release.Fallout4)
                    : Fallout4Mod.CreateFromBinaryOverlay(ModPath.FromPath(path), Fallout4Release.Fallout4);
                _ = size;

                listings.Add(new ModListing<IFallout4ModGetter>(modKey, mod, enabled: true, ghostSuffix: string.Empty));
                loaded.Add(name);
                pluginPaths[name] = path;
                MutagenLoader.PluginSourcePaths[name] = path;
            }
            catch (Exception ex)
            {






                failedToLoad.Add((name, $"{ex.GetType().Name}: {ex.Message.Split('\n')[0].Trim()}"));
            }
        }

        var lo = new LoadOrder<IModListingGetter<IFallout4ModGetter>>(listings, disposeItems: true);
        var linkCache = lo.ToImmutableLinkCache<IFallout4Mod, IFallout4ModGetter>();
        MutagenLoader.LinkCache = linkCache;



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



        TextureService.SetSessionRoots(new[] { gameDataFolder, modsDir, ResolveOverwriteFolder(instancePath) });




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
