using System.Collections;
using System.IO;
using System.Reflection;
using FO4RecordEditor.Models;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Fallout4;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Environments;

namespace FO4RecordEditor.Services;

public static partial class MutagenLoader
{

    private static readonly HashSet<string> _skipProps = new(StringComparer.OrdinalIgnoreCase)
    {
        "Registration", "IsCompressed", "IsDeleted", "FormVersion",
        "VersionControl", "StaticRegistration", "ProtocolDefinition",
        "CustomData", "BinaryWriteTranslator", "RecordType", "ContainedFormLinks",

        "Fallout4MajorRecordFlags", "MajorRecordFlags", "MajorRecordFlagsRaw",
        "Version2", "MajorFlags", "IsNull", "FormVersion2",
    };

    private static readonly HashSet<Type> _leafTypes = new()
    {
        typeof(string), typeof(bool), typeof(byte), typeof(sbyte),
        typeof(short), typeof(ushort), typeof(int), typeof(uint),
        typeof(long), typeof(ulong), typeof(float), typeof(double),
    };

    public static readonly System.Collections.Concurrent.ConcurrentDictionary<string, object> LooseMods = new(StringComparer.OrdinalIgnoreCase);

    public static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> LooseModPaths = new(StringComparer.OrdinalIgnoreCase);

    public static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> PluginSourcePaths = new(StringComparer.OrdinalIgnoreCase);

    public static readonly System.Collections.Concurrent.ConcurrentDictionary<string, object> EditableMods = new(StringComparer.OrdinalIgnoreCase);

    public static Mutagen.Bethesda.Plugins.Cache.ILinkCache? LinkCache;

    public static readonly System.Collections.Generic.Dictionary<string, bool> MasterIsEsl =
        new(System.StringComparer.OrdinalIgnoreCase);

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, PropertyInfo?> _nameProp = new();

    public static string DescribeFormKey(object? envObj, string formKeyStr)
    {
        if (!Mutagen.Bethesda.Plugins.FormKey.TryFactory(formKeyStr, out var fk)) return formKeyStr;
        if (fk.IsNull) return "Null";
        try
        {
            var cache = LinkCache;
            if (cache != null && cache.TryResolve<IMajorRecordGetter>(fk, out var rec))
            {
                var eid = rec.EditorID ?? "";
                if (eid.Length > 0) return $"{eid} [{fk}]";
            }
        }
        catch { }
        return formKeyStr;
    }

    public static string FormatFormLink(Mutagen.Bethesda.Plugins.IFormLinkIdentifier fli)
    {
        var fk = fli.FormKey;
        if (fk.IsNull) return "Null";
        var fkStr = fk.ToString();

        var cache = LinkCache;
        if (cache == null) return fkStr;
        try
        {
            if (!cache.TryResolve<IMajorRecordGetter>(fk, out var rec)) return fkStr;

            var eid = rec.EditorID ?? "";
            string name = rec is Mutagen.Bethesda.Plugins.Aspects.INamedGetter named ? named.Name ?? "" : "";
            if (name.Length == 0)
            {
                var np = _nameProp.GetOrAdd(rec.GetType(), t => t.GetProperty("Name"));
                if (np != null) name = np.GetValue(rec)?.ToString() ?? "";
            }

            var label = (eid, name) switch
            {
                ("", "") => "",
                (_, "")  => eid,
                ("", _)  => $"\"{name}\"",
                _        => $"{eid} \"{name}\"",
            };
            return label.Length > 0 ? $"{label} [{fkStr}]" : fkStr;
        }
        catch { return fkStr; }
    }

    private static readonly Lazy<List<(string Name, Type Type)>> _fo4Records = new(() =>
    {
        var list = new List<(string, Type)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var major = typeof(Mutagen.Bethesda.Plugins.Records.IMajorRecordGetter);
        Type[] types;
        try { types = typeof(Mutagen.Bethesda.Fallout4.IFallout4ModGetter).Assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray()!; }
        foreach (var t in types)
        {
            if (t is null || !t.IsClass || t.IsAbstract) continue;
            if (!major.IsAssignableFrom(t)) continue;
            if (seen.Add(t.Name)) list.Add((t.Name, t));
        }
        return list;
    });

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, string> _refTypesCache = new();

    private static (string? Display, string? Csv) FormLinkInfo(Type t)
    {
        foreach (var cand in new[] { t }.Concat(t.GetInterfaces()))
        {
            if (!cand.IsGenericType) continue;
            var args = cand.GetGenericArguments();
            if (args.Length != 1) continue;
            var arg = args[0];
            if (!typeof(Mutagen.Bethesda.Plugins.Records.IMajorRecordGetter).IsAssignableFrom(arg)) continue;

            var n = arg.Name;
            if (n.StartsWith("I", StringComparison.Ordinal)) n = n[1..];
            if (n.EndsWith("Getter", StringComparison.Ordinal)) n = n[..^6];

            var csv = _refTypesCache.GetOrAdd(arg, target =>
                string.Join(",", _fo4Records.Value
                    .Where(r => target.IsAssignableFrom(r.Type))
                    .Select(r => r.Name)
                    .OrderBy(x => x, StringComparer.Ordinal)));
            return (n.Length > 0 ? n : null, string.IsNullOrEmpty(csv) ? null : csv);
        }
        return (null, null);
    }

    private static readonly Lazy<Dictionary<string, Type>> _getterIfaceBySig = new(() =>
    {
        var map = new Dictionary<string, Type>(StringComparer.Ordinal);
        var major = typeof(Mutagen.Bethesda.Plugins.Records.IMajorRecordGetter);
        Type[] types;
        try { types = typeof(Mutagen.Bethesda.Fallout4.IFallout4ModGetter).Assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray()!; }
        foreach (var t in types)
        {
            if (t is null || !t.IsInterface || !major.IsAssignableFrom(t)) continue;
            var n = t.Name;
            if (!n.StartsWith("I", StringComparison.Ordinal) || !n.EndsWith("Getter", StringComparison.Ordinal)) continue;
            map[n[1..^6]] = t;
        }
        return map;
    });

    internal sealed class ModIndex
    {

        public Dictionary<string, int> Counts = new(StringComparer.Ordinal);

        public readonly System.Collections.Concurrent.ConcurrentDictionary<string, List<Mutagen.Bethesda.Plugins.Records.IMajorRecordGetter>> BySig =
            new(StringComparer.Ordinal);

        public Dictionary<FormKey, Mutagen.Bethesda.Plugins.Records.IMajorRecordGetter>? ByFormKey;

        public long RetainedRecords;

        public object? Source;

        public long LastAccess;
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, ModIndex> _modIndexCache = new(StringComparer.OrdinalIgnoreCase);
    private static long _modIndexAccessTick;
    private static readonly object _modIndexEvictLock = new();

    public static int MaxCachedModIndexes = 64;

    public static long MaxCachedIndexRecords = 500_000;

    private static object? ResolveMod(string modName, object? envObj)
    {
        if (EditableMods.TryGetValue(modName, out var editable)) return editable;

        if (envObj != null)
        {
            dynamic env = envObj;
            foreach (var l in ((IEnumerable)env.LoadOrder.ListedOrder).Cast<dynamic>())
            {
                if (l.Mod != null &&
                    string.Equals((string)l.ModKey.FileName.String, modName, StringComparison.OrdinalIgnoreCase))
                    return l.Mod;
            }
        }
        return LooseMods.TryGetValue(modName, out var loose) ? loose : null;
    }

    private static IEnumerable<(string name, object mod)> AllLoadedMods(object? envObj)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in EditableMods)
            if (seen.Add(kv.Key)) yield return (kv.Key, kv.Value);

        if (envObj != null)
        {
            dynamic env = envObj;
            foreach (var l in ((IEnumerable)env.LoadOrder.ListedOrder).Cast<dynamic>())
            {
                if (l.Mod == null) continue;
                var name = (string)l.ModKey.FileName.String;
                if (seen.Add(name)) yield return (name, (object)l.Mod);
            }
        }
        foreach (var kv in LooseMods)
            if (seen.Add(kv.Key)) yield return (kv.Key, kv.Value);
    }

    private static ModIndex GetModIndex(object mod, string modName, Action<string>? progress = null)
    {

        if (_modIndexCache.TryGetValue(modName, out var cached) && ReferenceEquals(cached.Source, mod))
        {
            cached.LastAccess = System.Threading.Interlocked.Increment(ref _modIndexAccessTick);
            return cached;
        }

        var idx = new ModIndex { Source = mod };
        if (mod is Mutagen.Bethesda.Fallout4.IFallout4ModGetter f4mod)
        {

            var cacheKey = CountsCacheKeyFor(modName);
            if (cacheKey != null && RecordCountCache.TryGet(cacheKey, out var persisted))
            {
                idx.Counts = persisted;
                return StoreModIndex(modName, idx);
            }

            int count = 0;
            foreach (var rec in f4mod.EnumerateMajorRecords())
            {
                var sig = rec.Registration.Name;
                idx.Counts[sig] = idx.Counts.TryGetValue(sig, out var n) ? n + 1 : 1;
                if (++count % 5000 == 0) progress?.Invoke($"Indexing {modName}: {count} records...");
            }

            if (cacheKey != null) RecordCountCache.Put(cacheKey, idx.Counts);
        }
        return StoreModIndex(modName, idx);
    }

    private static void RegisterPluginSourcePath(string modName, string dataFolder)
    {
        try
        {
            var path = System.IO.Path.Combine(dataFolder, modName);
            if (System.IO.File.Exists(path)) PluginSourcePaths[modName] = path;
        }
        catch { }
    }

    public static string? CountsCacheKeyFor(string modName)
    {
        if (EditableMods.ContainsKey(modName)) return null;
        if (LooseModPaths.TryGetValue(modName, out var loose)) return loose;
        return PluginSourcePaths.TryGetValue(modName, out var path) ? path : null;
    }

    private static List<Mutagen.Bethesda.Plugins.Records.IMajorRecordGetter> RecordsOfSig(ModIndex idx, string sig)
    {
        if (idx.BySig.TryGetValue(sig, out var cached)) return cached;
        if (!idx.Counts.ContainsKey(sig)) return [];

        var list = new List<Mutagen.Bethesda.Plugins.Records.IMajorRecordGetter>();
        if (idx.Source is Mutagen.Bethesda.Fallout4.IFallout4ModGetter f4mod
            && _getterIfaceBySig.Value.TryGetValue(sig, out var iface))
        {
            foreach (Mutagen.Bethesda.Plugins.Records.IMajorRecordGetter rec in f4mod.EnumerateMajorRecords(iface))
                if (string.Equals(rec.Registration.Name, sig, StringComparison.Ordinal))
                    list.Add(rec);
        }

        var stored = idx.BySig.GetOrAdd(sig, list);
        if (ReferenceEquals(stored, list))
        {
            System.Threading.Interlocked.Add(ref idx.RetainedRecords, list.Count);
            EvictModIndexLru();
        }
        return stored;
    }

    private static Dictionary<FormKey, Mutagen.Bethesda.Plugins.Records.IMajorRecordGetter> RecordsByFormKey(ModIndex idx)
    {
        if (idx.ByFormKey is { } built) return built;

        var map = new Dictionary<FormKey, Mutagen.Bethesda.Plugins.Records.IMajorRecordGetter>();
        if (idx.Source is Mutagen.Bethesda.Fallout4.IFallout4ModGetter f4mod)
            foreach (var rec in f4mod.EnumerateMajorRecords())
                map[rec.FormKey] = rec;

        idx.ByFormKey = map;
        System.Threading.Interlocked.Add(ref idx.RetainedRecords, map.Count);
        EvictModIndexLru();
        return map;
    }

    private static IEnumerable<(string sig, Mutagen.Bethesda.Plugins.Records.IMajorRecordGetter rec)> StreamRecords(ModIndex idx)
    {
        if (idx.Source is not Mutagen.Bethesda.Fallout4.IFallout4ModGetter f4mod) yield break;

        var alreadyServed = new HashSet<string>(idx.BySig.Keys, StringComparer.Ordinal);

        foreach (var sig in alreadyServed)
            if (idx.BySig.TryGetValue(sig, out var cached))
                foreach (var r in cached)
                    yield return (sig, r);

        foreach (var rec in f4mod.EnumerateMajorRecords())
        {
            var sig = rec.Registration.Name;
            if (alreadyServed.Contains(sig)) continue;
            yield return (sig, rec);
        }
    }

    private static Mutagen.Bethesda.Plugins.Records.IMajorRecordGetter? LookupByFormKey(
        ModIndex idx, FormKey formKey, string? sigHint)
    {
        if (!string.IsNullOrEmpty(sigHint) && idx.Counts.ContainsKey(sigHint))
        {
            foreach (var rec in RecordsOfSig(idx, sigHint))
                if (rec.FormKey == formKey) return rec;
            return null;
        }
        return RecordsByFormKey(idx).TryGetValue(formKey, out var r) ? r : null;
    }

    private static ModIndex StoreModIndex(string modName, ModIndex idx)
    {
        idx.LastAccess = System.Threading.Interlocked.Increment(ref _modIndexAccessTick);
        _modIndexCache[modName] = idx;
        EvictModIndexLru();
        return idx;
    }

    private static void EvictModIndexLru()
    {
        lock (_modIndexEvictLock)
        {
            while (true)
            {
                long retained = 0;
                foreach (var kv in _modIndexCache) retained += kv.Value.RetainedRecords;
                if (_modIndexCache.Count <= MaxCachedModIndexes && retained <= MaxCachedIndexRecords) break;

                string? victim = null;
                long oldest = long.MaxValue;
                foreach (var kv in _modIndexCache)
                {
                    if (EditableMods.ContainsKey(kv.Key)) continue;
                    if (kv.Value.LastAccess < oldest) { oldest = kv.Value.LastAccess; victim = kv.Key; }
                }
                if (victim == null) break;
                _modIndexCache.TryRemove(victim, out _);
            }
        }
    }

    public static void InvalidateModIndex(string modName) => _modIndexCache.TryRemove(modName, out _);

    public static void ReleaseLooseMod(string modName)
    {
        InvalidateModIndex(modName);
        if (!LooseMods.TryRemove(modName, out var mod)) return;
        if (EditableMods.TryGetValue(modName, out var editable) && ReferenceEquals(editable, mod)) return;
        (mod as IDisposable)?.Dispose();
    }

    public static void ReplaceLooseMod(string modName, object mod)
    {
        if (LooseMods.TryGetValue(modName, out var prev) && !ReferenceEquals(prev, mod))
            ReleaseLooseMod(modName);
        LooseMods[modName] = mod;
        InvalidateModIndex(modName);
    }

    public static int ModIndexCacheCount => _modIndexCache.Count;
    public static bool ModIndexCacheContains(string modName) => _modIndexCache.ContainsKey(modName);
    public static void ClearModIndexCacheForTest() => _modIndexCache.Clear();

    public static void SeedModIndexForTest(string modName, object source, long retainedRecords = 0)
        => StoreModIndex(modName, new ModIndex { Source = source, RetainedRecords = retainedRecords });

    public static long ModIndexRetainedRecords =>
        _modIndexCache.Values.Sum(v => v.RetainedRecords);

    public static RecordNode LoadEsp(string espPath, IProgress<(string message, double? percent)>? progress = null)
    {
        var fileName = Path.GetFileName(espPath);
        var root = new RecordNode { Key = fileName, FilePath = espPath };
        progress?.Report(($"Opening {fileName}...", 0));

        var modPath = ModPath.FromPath(espPath);
        var mod = Fallout4Mod.CreateFromBinaryOverlay(modPath, Fallout4Release.Fallout4);

        ReplaceLooseMod(fileName, mod);
        LooseModPaths[fileName] = espPath;

        var dummy = new RecordNode { Key = "Loading...", Parent = root };
        AddLeafInit(dummy, "_NeedsGroups", fileName);
        root.Children.Add(dummy);

        progress?.Report(($"Loaded loose plugin {fileName}.", 100));
        return root;
    }

    public static RecordNode MakeLazyNode(string pluginName, string? filePath = null)
    {
        var root = new RecordNode { Key = pluginName, FilePath = filePath };
        var dummy = new RecordNode { Key = "Loading...", Parent = root };
        AddLeafInit(dummy, "_NeedsGroups", pluginName);
        root.Children.Add(dummy);
        return root;
    }

    public static (object env, List<string> plugins) BuildEnvironment(
        IProgress<(string message, double? percent)>? progress = null,
        string? dataFolderOverride = null)
    {
        progress?.Report(("Initializing Game Environment...", 0));
        _modIndexCache.Clear();
        var builder = GameEnvironment.Typical.Builder<IFallout4Mod, IFallout4ModGetter>(GameRelease.Fallout4);
        if (!string.IsNullOrWhiteSpace(dataFolderOverride))
        {
            progress?.Report(($"Using data folder: {dataFolderOverride}", null));
            builder = builder.WithTargetDataFolder(new Noggog.DirectoryPath(dataFolderOverride));
        }
        IGameEnvironment<IFallout4Mod, IFallout4ModGetter> env;
        try
        {
            env = builder.Build();
        }
        catch (System.Exception ex)
        {

            throw TranslateEnvironmentError(ex);
        }
        LinkCache = env.LinkCache;

        var plugins = new List<string>();
        MasterIsEsl.Clear();
        PluginSourcePaths.Clear();
        foreach (var l in env.LoadOrder.ListedOrder)
            if (l.Mod != null)
            {
                plugins.Add(l.ModKey.FileName.String);
                MasterIsEsl[l.ModKey.FileName.String] = l.Mod.IsSmallMaster;
                RegisterPluginSourcePath(l.ModKey.FileName.String, env.DataFolderPath.Path);
            }

        try { TextureService.SetSessionRoots(new[] { env.DataFolderPath.Path }); } catch { }
        try { AssetResolver.SetSessionDataRoots(new[] { env.DataFolderPath.Path }); } catch { }

        progress?.Report(($"Found {plugins.Count} plugins.", null));
        return (env, plugins);
    }

    public static (RecordNode root, object env) LoadEnvironment(IProgress<(string message, double? percent)>? progress = null)
    {
        progress?.Report(("Initializing Game Environment...", 0));
        _modIndexCache.Clear();
        var env = GameEnvironment.Typical.Builder<IFallout4Mod, IFallout4ModGetter>(GameRelease.Fallout4).Build();

        var root = new RecordNode { Key = "Load Order" };
        var listed = env.LoadOrder.ListedOrder.ToList();
        PluginSourcePaths.Clear();
        foreach (var l in listed)
            if (l.Mod != null) RegisterPluginSourcePath(l.ModKey.FileName.String, env.DataFolderPath.Path);

        for (int i = 0; i < listed.Count; i++)
        {
            var modListing = listed[i];
            var mod = modListing.Mod;
            if (mod == null) continue;

            double percent = (double)(i + 1) / listed.Count * 100;
            progress?.Report(($"Loading {mod.ModKey.FileName} ({i + 1}/{listed.Count})...", percent));

            var modNode = new RecordNode { Key = mod.ModKey.FileName.String, Parent = root };

            var dummy = new RecordNode { Key = "Loading...", Parent = modNode };
            AddLeafInit(dummy, "_NeedsGroups", mod.ModKey.FileName.String);
            modNode.Children.Add(dummy);

            root.Children.Add(modNode);
        }

        progress?.Report(($"Done -- Loaded {listed.Count} plugins.", null));
        return (root, env);
    }

    public static List<RecordNode> GetGroups(string modName, object? envObj, RecordNode pluginNode, Action<string>? progress = null)
    {
        var mod = ResolveMod(modName, envObj);
        if (mod == null) return [];

        progress?.Invoke("Enumerating records...");
        var idx = GetModIndex(mod, modName, progress);

        var result = new List<RecordNode>();

        foreach (var sig in idx.Counts.Keys.OrderBy(g => g))
        {
            var grpNode = new RecordNode { Key = sig, Parent = pluginNode };
            var grpDummy = new RecordNode { Key = "Loading...", Parent = grpNode };
            AddLeafInit(grpDummy, "_NeedsRecords", $"{modName}|{sig}");
            grpNode.Children.Add(grpDummy);
            result.Add(grpNode);
        }
        if (result.Count == 0)
        {
            var noneNode = new RecordNode { Key = "(No records)", Parent = pluginNode };
            result.Add(noneNode);
        }
        return result;
    }

    public static List<RecordNode> GetRecords(string modName, string sig, object? envObj, RecordNode groupNode, Action<string>? progress = null)
    {
        var mod = ResolveMod(modName, envObj);
        if (mod == null) return [];

        progress?.Invoke("Enumerating records...");
        var idx = GetModIndex(mod, modName, progress);

        var result = new List<RecordNode>();
        var recs = RecordsOfSig(idx, sig);
        if (recs.Count == 0) return result;

        foreach (var rec in recs)
        {
            string editorId = rec.EditorID ?? "";
            var recNode = new RecordNode
            {
                Key = !string.IsNullOrEmpty(editorId) ? editorId : rec.FormKey.ToString(),
                Parent = groupNode,
                IsRecordNode = true,
            };
            AddLeafInit(recNode, "FormKey", rec.FormKey.ToString());
            AddLeafInit(recNode, "EditorID", editorId);
            AddLeafInit(recNode, "Type", sig);
            AddLeafInit(recNode, "_HasData", "False");
            recNode.ConflictStatus = ConflictState.GetStatus(modName, rec.FormKey);
            result.Add(recNode);
        }
        return result;
    }

    public static void PopulateNode(RecordNode node, object? envObj, string modName = "")
    {

        var flag = node.GetChild("_HasData");
        if (flag != null) node.Children.Remove(flag);

        var fkStr = node.GetValue("FormKey");
        if (fkStr == null || !FormKey.TryFactory(fkStr, out var formKey)) return;

        if (string.IsNullOrEmpty(modName)) modName = formKey.ModKey.FileName.String;

        var mod = ResolveMod(modName, envObj);
        if (mod == null) return;

        var idx = GetModIndex(mod, modName);
        var rec = LookupByFormKey(idx, formKey, node.GetValue("Type"));
        if (rec != null) WalkObject(rec, node, 0, 4, modName);
    }

    public static List<string> PopulateNodeAllVersions(RecordNode node, object? envObj)
    {
        var flag = node.GetChild("_HasData");
        if (flag != null) node.Children.Remove(flag);

        var fkStr = node.GetValue("FormKey");
        if (fkStr == null || !FormKey.TryFactory(fkStr, out var fk)) return new();

        var versions = GetRecordContexts(envObj, fk);

        if (versions.Count == 0)
        {

            var modName = node.Parent?.Parent?.Key ?? fk.ModKey.FileName.String;
            var mod = ResolveMod(modName, envObj);
            var rec = mod == null ? null : LookupByFormKey(GetModIndex(mod, modName), fk, node.GetValue("Type"));
            if (rec != null) WalkObject(rec, node, 0, 4, modName);
            return new List<string> { modName };
        }

        foreach (var (plugin, rec) in versions)
            WalkObject(rec, node, 0, 4, plugin);
        return versions.Select(v => v.plugin).ToList();
    }

    public static List<RecordNode> BuildPopulatedFields(string formKeyStr, object? envObj, string modName, string? sig = null)
    {
        var temp = new RecordNode { Key = "temp" };
        if (!FormKey.TryFactory(formKeyStr, out var formKey)) return [];
        if (string.IsNullOrEmpty(modName)) modName = formKey.ModKey.FileName.String;

        var mod = ResolveMod(modName, envObj);
        if (mod == null) return [];

        var idx = GetModIndex(mod, modName);
        var rec = LookupByFormKey(idx, formKey, sig);
        if (rec != null)
            WalkObject(rec, temp, 0, 4, modName);

        return temp.Children.ToList();
    }

    public static Mutagen.Bethesda.Plugins.Records.IMajorRecordGetter? GetRecordVersion(
        object? envObj, string plugin, FormKey fk)
    {
        foreach (var (p, rec) in GetRecordContexts(envObj, fk))
            if (string.Equals(p, plugin, StringComparison.OrdinalIgnoreCase)) return rec;
        return null;
    }

    public static List<string> GetConflictingPlugins(object? envObj, FormKey fk) =>
        GetRecordContexts(envObj, fk).Select(c => c.plugin).ToList();

    public sealed record RefByDto(string Plugin, string FormKey, string EditorID, string Type);

    public static List<RefByDto> GetReferencedBy(object? envObj, string formKeyStr, int cap = 500)
    {
        var result = new List<RefByDto>();
        if (envObj == null || !FormKey.TryFactory(formKeyStr, out var target)) return result;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, mod) in AllLoadedMods(envObj))
        {
            if (mod is not Mutagen.Bethesda.Fallout4.IFallout4ModGetter f4) continue;

            try
            {
                foreach (var rec in f4.EnumerateMajorRecords())
                {
                    if (rec.FormKey == target) continue;
                    bool refs = false;
                    foreach (var link in rec.EnumerateFormLinks())
                        if (link.FormKey == target) { refs = true; break; }
                    if (!refs) continue;

                    if (!seen.Add(name + "|" + rec.FormKey)) continue;
                    result.Add(new RefByDto(name, rec.FormKey.ToString(), rec.EditorID ?? "", rec.Registration.Name));
                    if (result.Count >= cap) return result;
                }
            }
            catch (Exception ex)
            {
                DebugLog.Exception($"GetReferencedBy: skipped {name}", ex);
            }
        }
        return result;
    }

    public sealed record ProblemDto(string Severity, string Description);
    public sealed record DuplicateFormIdDto(uint RawFormId, int Count, IReadOnlyList<string> RecordTypes);

    private sealed record DuplicateIntegrityResult(
        IReadOnlyList<DuplicateFormIdDto> Duplicates,
        string? Error);

    private static string? ResolveDuplicateScanPath(string plugin, object? envObj)
    {
        if (EditableMods.ContainsKey(plugin) &&
            WriteService.TryGetSourcePath(plugin, out var editablePath) &&
            File.Exists(editablePath))
            return editablePath;

        if (envObj != null)
        {
            try
            {
                dynamic dynEnv = envObj;
                IReadOnlyDictionary<string, string> pluginPaths = dynEnv.PluginPaths;
                if (pluginPaths.TryGetValue(plugin, out var envPath) && File.Exists(envPath))
                    return envPath;
            }
            catch { }
        }

        if (PluginSourcePaths.TryGetValue(plugin, out var sourcePath) && File.Exists(sourcePath))
            return sourcePath;
        if (LooseModPaths.TryGetValue(plugin, out var loosePath) && File.Exists(loosePath))
            return loosePath;
        return null;
    }

    private static DuplicateIntegrityResult ConvertDuplicateScan(DuplicateFormIdScanner.Result scan) =>
        new(
            scan.Duplicates
                .Select(d => new DuplicateFormIdDto(d.RawFormId, d.Count, d.RecordTypes))
                .ToArray(),
            scan.Error);

    private static DuplicateIntegrityResult ScanDuplicateFormIds(string plugin, object? envObj)
    {
        var path = ResolveDuplicateScanPath(plugin, envObj);
        return path == null
            ? new DuplicateIntegrityResult([], null)
            : ConvertDuplicateScan(DuplicateFormIdScanner.Scan(path));
    }

    private static bool TryGetCachedDuplicateFormIds(
        string plugin,
        object? envObj,
        out DuplicateIntegrityResult result)
    {
        result = new DuplicateIntegrityResult([], null);
        var path = ResolveDuplicateScanPath(plugin, envObj);
        if (path == null) return true;
        if (!DuplicateFormIdScanner.TryGetCached(path, out var cached))
        {
            DuplicateFormIdScanner.QueueScan(path);
            return false;
        }
        result = ConvertDuplicateScan(cached);
        return true;
    }

    public static IReadOnlyList<DuplicateFormIdDto> GetDuplicateFormIds(string plugin) =>
        ScanDuplicateFormIds(plugin, envObj: null).Duplicates;

    private static uint ToFileLocalFormId(IFallout4ModGetter mod, FormKey formKey)
    {
        var objectId = formKey.ID & 0x00FFFFFFu;
        if (formKey.ModKey.Equals(mod.ModKey))
            return ((uint)mod.MasterReferences.Count << 24) | objectId;

        for (var i = 0; i < mod.MasterReferences.Count; i++)
            if (mod.MasterReferences[i].Master.Equals(formKey.ModKey))
                return ((uint)i << 24) | objectId;

        return uint.MaxValue;
    }

    private static string DuplicateLabel(DuplicateFormIdDto duplicate) =>
        $"DUPLICATE FORMID {duplicate.RawFormId:X8} appears {duplicate.Count} times " +
        $"({string.Join(", ", duplicate.RecordTypes)}); Fallout 4 and this loader keep the last occurrence.";

    private static string? DuplicateIntegrityBlock(string plugin, object? envObj)
    {
        var scan = ScanDuplicateFormIds(plugin, envObj);
        if (scan.Duplicates.Count == 0 && scan.Error == null) return null;

        var sb = new System.Text.StringBuilder();
        if (scan.Error != null)
        {
            sb.AppendLine($"{plugin}: duplicate FormID scan failed: {scan.Error}");
            return sb.ToString();
        }

        var extras = scan.Duplicates.Sum(d => d.Count - 1);
        sb.AppendLine($"{plugin}: {scan.Duplicates.Count} duplicate FormID group(s), {extras} extra record(s).");
        foreach (var duplicate in scan.Duplicates.Take(80))
            sb.AppendLine("  " + DuplicateLabel(duplicate));
        if (scan.Duplicates.Count > 80)
            sb.AppendLine($"  ... and {scan.Duplicates.Count - 80} more duplicate FormID group(s)");
        return sb.ToString();
    }

    public static List<ProblemDto> GetRecordProblems(object? envObj, string formKeyStr)
    {
        var result = new List<ProblemDto>();
        if (envObj == null || !FormKey.TryFactory(formKeyStr, out var fk)) return result;

        var ctx = GetRecordContexts(envObj, fk);
        if (ctx.Count == 0) return result;
        var winnerPlugin = ctx[^1].plugin;
        var rec = ctx[^1].rec;

        if (ResolveMod(winnerPlugin, envObj) is IFallout4ModGetter winnerMod)
        {
            var rawFormId = ToFileLocalFormId(winnerMod, rec.FormKey);
            if (TryGetCachedDuplicateFormIds(winnerPlugin, envObj, out var duplicateScan))
            {
                if (duplicateScan.Error != null)
                    result.Add(new ProblemDto("Error",
                        $"Duplicate FormID scan failed for {winnerPlugin}: {duplicateScan.Error}"));
                var duplicate = duplicateScan.Duplicates
                    .FirstOrDefault(d => d.RawFormId == rawFormId);
                if (duplicate != null)
                    result.Add(new ProblemDto("Error", DuplicateLabel(duplicate)));
            }
            else
            {
                result.Add(new ProblemDto("Warning",
                    $"Duplicate FormID scan for {winnerPlugin} is running in the background; refresh Problems."));
            }
        }

        if (rec.IsDeleted)
            result.Add(new ProblemDto("Error", "Record is flagged DELETED -- prefer a disable/override; deletions can crash the game."));

        var loaded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, _) in AllLoadedMods(envObj)) loaded.Add(name);

        var seen = new HashSet<FormKey>();
        foreach (var link in rec.EnumerateFormLinks())
        {
            if (link.FormKey.IsNull || !seen.Add(link.FormKey)) continue;
            var owner = link.FormKey.ModKey.FileName.String;
            if (!loaded.Contains(owner))
                result.Add(new ProblemDto("Error",
                    $"Reference {link.FormKey} points into {owner}, which is not in the load order (crash risk)."));
        }
        return result;
    }

    private static readonly Dictionary<string, string> _sigToClass = new(StringComparer.OrdinalIgnoreCase)
    {
        ["COBJ"] = "ConstructibleObject", ["WEAP"] = "Weapon", ["ARMO"] = "Armor", ["ALCH"] = "Ingestible",
        ["MISC"] = "MiscItem", ["FLST"] = "FormList", ["LVLI"] = "LeveledItem", ["AMMO"] = "Ammunition",
    };

    public static List<(FormKey fk, string editorId, string sig)> EnumerateOverrides(
        object? envObj, string plugin, string? sig = null)
    {
        var result = new List<(FormKey, string, string)>();
        if (ResolveMod(plugin, envObj) is not Mutagen.Bethesda.Plugins.Records.IModGetter mod) return result;

        string? wantClass = sig == null ? null
            : (_sigToClass.TryGetValue(sig, out var c) ? c : sig);

        foreach (var rec in mod.EnumerateMajorRecords())
        {
            if (rec.FormKey.ModKey == mod.ModKey) continue;
            var rsig = rec.Registration.Name;
            if (wantClass != null && !string.Equals(rsig, wantClass, StringComparison.OrdinalIgnoreCase)) continue;
            result.Add((rec.FormKey, rec.EditorID ?? "", rsig));
        }
        return result;
    }

    public static bool CobjUsesComponent(Mutagen.Bethesda.Plugins.Records.IMajorRecordGetter rec, FormKey component)
    {
        if (rec is not Mutagen.Bethesda.Fallout4.IConstructibleObjectGetter cobj || cobj.Components == null) return false;
        foreach (var c in cobj.Components)
            if (c.Component.FormKey == component) return true;
        return false;
    }

    public static List<(string plugin, Mutagen.Bethesda.Plugins.Records.IMajorRecordGetter rec)>
        GetRecordContexts(object? envObj, FormKey fk)
    {
        var ordered = new List<(string plugin, Mutagen.Bethesda.Plugins.Records.IMajorRecordGetter rec)>();

        if (envObj != null)
        {
            try
            {
                var lc = (Mutagen.Bethesda.Plugins.Cache.ILinkCache)((dynamic)envObj).LinkCache;

                var contexts = lc.ResolveAllSimpleContexts<IMajorRecordGetter>(fk).ToList();
                contexts.Reverse();
                foreach (var ctx in contexts)
                    ordered.Add((ctx.ModKey.FileName.String, ctx.Record));
            }
            catch (Exception ex)
            {

                DebugLog.Exception($"GetRecordContexts({fk})", ex);
            }
        }

        foreach (var kv in EditableMods)
        {
            try
            {
                var idx = GetModIndex(kv.Value, kv.Key);
                if (!RecordsByFormKey(idx).TryGetValue(fk, out var rec)) continue;
                int at = ordered.FindIndex(o => string.Equals(o.plugin, kv.Key, StringComparison.OrdinalIgnoreCase));
                if (at >= 0) ordered[at] = (kv.Key, rec);
                else ordered.Add((kv.Key, rec));
            }
            catch { }
        }
        return ordered;
    }

    public static ConflictMatrix? BuildConflictMatrix(object? envObj, string formKeyStr)
    {
        if (envObj == null || !FormKey.TryFactory(formKeyStr, out var fk)) return null;

        var versions = GetRecordContexts(envObj, fk);
        if (versions.Count == 0) return null;

        var root = new RecordNode { Key = "root" };
        foreach (var (plugin, rec) in versions)
            WalkObject(rec, root, 0, 6, plugin);

        var plugins = versions.Select(v => v.plugin).ToList();
        var rows = new List<ConflictFieldRow>();
        FlattenConflictRows(root, "", 0, plugins, rows);

        var identityKeys = new[] { "EditorID", "TitleString", "FormKey", "Description", "Type", "_HasData" };
        var orderedRows = rows.OrderBy(r => {
            int idx = Array.IndexOf(identityKeys, r.Field);
            return idx == -1 ? 999 : idx;
        }).ToList();

        string level;
        if (plugins.Count <= 1) level = "onlyone";
        else if (orderedRows.Any(r => r.Severity == "critical")) level = "critical";
        else if (orderedRows.Any(r => r.Severity == "conflict")) level = "conflict";
        else if (orderedRows.Any(r => r.Severity == "override")) level = "override";
        else level = "noconflict";

        var last = versions[^1];
        return new ConflictMatrix
        {
            FormKey = fk.ToString(),
            EditorID = last.rec.EditorID ?? "",
            Type = last.rec.Registration.Name,
            Plugins = plugins,
            Winner = last.plugin,
            Rows = orderedRows,
            Level = level,
        };
    }

    private static string MakeDisplayLabel(string key, string parentKey)
    {
        if (key.Length > 1 && key[0] == '[' && parentKey != "root")
            return $"{Rendering.FriendlyNames.Singular(parentKey)} {key}";
        return Rendering.FriendlyNames.Label(key);
    }

    private static string GroupOf(string path)
    {
        int dot = path.IndexOf('.');
        int brk = path.IndexOf('[');
        int cut = dot < 0 ? brk : brk < 0 ? dot : Math.Min(dot, brk);
        return cut <= 0 ? "" : path[..cut];
    }

    private static string ClassifyKind(string editKind, string key, IReadOnlyList<string> values)
    {
        if (editKind == "Bool") return "Flag";
        if (editKind == "Ref") return "FormID";
        if (key.EndsWith("Flags", StringComparison.OrdinalIgnoreCase) ||
            key.EndsWith("Flag", StringComparison.OrdinalIgnoreCase)) return "Flag";
        foreach (var v in values)
            if (v.Length > 2 && v[0] == '[' && v.Contains(':')) return "FormID";
        return "Value";
    }

    private static void FlattenConflictRows(RecordNode node, string path, int depth, List<string> plugins, List<ConflictFieldRow> rows)
    {
        foreach (var child in node.Children)
        {
            string sep = child.Key.StartsWith("[") ? "" : (path.Length == 0 ? "" : ".");
            string p = path + sep + child.Key;
            bool hasKids = child.Children.Count > 0;

            if (child.Values.Count > 0 || hasKids)
            {
                var vals = plugins.Select(pl => child.Values.TryGetValue(pl, out var v) ? v : "").ToList();
                var present = vals.Where(v => v.Length > 0).ToList();
                bool anyMissing = present.Count < plugins.Count;

                bool differs = present.Select(CanonValue).Distinct().Count() > 1 || (present.Count > 0 && anyMissing);
                var editKindString = child.EditKind.ToString();
                var (statuses, severity) = ClassifyRow(vals, editKindString);
                var group = GroupOf(p);
                rows.Add(new ConflictFieldRow
                {
                    Field = p, DisplayLabel = MakeDisplayLabel(child.Key, node.Key), Level = depth, Values = vals, Differs = differs,
                    Statuses = statuses, Severity = severity,
                    IsSummary = child.IsSummary, HasChildren = hasKids,
                    EditKind = editKindString, EnumOptions = child.EnumOptions,
                    Kind = ClassifyKind(editKindString, child.Key, present),
                    Group = group, GroupLabel = group.Length == 0 ? "" : Rendering.FriendlyNames.Label(group),
                    RefType = child.RefType, RefTypes = child.RefTypes,
                });
            }

            FlattenConflictRows(child, p, depth + 1, plugins, rows);
        }
    }

    public static string CanonValue(string v)
    {
        if (double.TryParse(v, System.Globalization.NumberStyles.Float | System.Globalization.NumberStyles.AllowThousands,
                System.Globalization.CultureInfo.InvariantCulture, out var d))
            return d.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        return v;
    }

    public static (string[] statuses, string severity) ClassifyRow(IReadOnlyList<string> vals, string editKind)
    {
        int n = vals.Count;
        var statuses = new string[n];

        var present = new List<int>();
        for (int i = 0; i < n; i++) if (!string.IsNullOrEmpty(vals[i])) present.Add(i);

        if (present.Count == 0) { for (int i = 0; i < n; i++) statuses[i] = "notdefined"; return (statuses, "none"); }

        int masterIdx = present[0];
        int winnerIdx = present[^1];
        string Canon(int i) => CanonValue(vals[i]);
        bool Eq(int a, int b) => string.Equals(Canon(a), Canon(b), StringComparison.Ordinal);

        var distinct = new HashSet<string>(StringComparer.Ordinal);
        foreach (var i in present) distinct.Add(Canon(i));
        bool anyMissing = present.Count < n;
        bool differs = distinct.Count > 1 || (present.Count > 0 && anyMissing);

        for (int i = 0; i < n; i++)
        {
            if (string.IsNullOrEmpty(vals[i])) { statuses[i] = "notdefined"; continue; }
            if (present.Count == 1) { statuses[i] = "only"; continue; }
            if (i == masterIdx) { statuses[i] = "master"; continue; }
            if (Eq(i, masterIdx)) { statuses[i] = "identical"; continue; }
            if (i == winnerIdx) { statuses[i] = "win"; continue; }
            statuses[i] = Eq(i, winnerIdx) ? "override" : "lose";
        }

        string severity;
        if (!differs) severity = "none";
        else
        {

            bool refBrokenWinner = string.Equals(editKind, "Ref", StringComparison.OrdinalIgnoreCase)
                && LooksNullRef(vals[winnerIdx]) && present.Any(i => !LooksNullRef(vals[i]));
            if (refBrokenWinner) severity = "critical";
            else if (distinct.Count >= 3) severity = "conflict";
            else severity = "override";
        }
        return (statuses, severity);
    }

    private static bool LooksNullRef(string v) =>
        string.IsNullOrEmpty(v) || v.Equals("Null", StringComparison.OrdinalIgnoreCase)
        || v.Equals("None", StringComparison.OrdinalIgnoreCase) || v.StartsWith("000000:", StringComparison.OrdinalIgnoreCase);

    public static IReadOnlyList<string> QueryLoadedPlugins(object? envObj)
    {
        var names = new List<string>();
        if (envObj != null)
        {
            dynamic env = envObj;
            foreach (var l in ((IEnumerable)env.LoadOrder.ListedOrder).Cast<dynamic>())
                if (l.Mod != null) names.Add((string)l.ModKey.FileName.String);
        }
        foreach (var k in LooseMods.Keys)
            if (!names.Contains(k, StringComparer.OrdinalIgnoreCase)) names.Add(k);
        return names;
    }

    public static IReadOnlyList<string> QueryRecordTypes(object? envObj, string plugin)
    {
        var mod = ResolveMod(plugin, envObj);
        if (mod == null) return [];

        return GetModIndex(mod, plugin).Counts
            .OrderBy(kv => kv.Key)
            .Select(kv => $"{kv.Key} ({kv.Value})")
            .ToList();
    }

    public static IReadOnlyList<(string formKey, string editorId)> QueryRecordsOfType(
        object? envObj, string plugin, string sig, int limit = 100, int offset = 0)
    {
        var mod = ResolveMod(plugin, envObj);
        if (mod == null) return [];
        var idx = GetModIndex(mod, plugin);
        var recs = RecordsOfSig(idx, sig);
        return recs.Skip(Math.Max(0, offset)).Take(limit).Select(r => (r.FormKey.ToString(), r.EditorID ?? "")).ToList();
    }

    public static int CountRecordsOfType(object? envObj, string plugin, string sig)
    {
        var mod = ResolveMod(plugin, envObj);
        if (mod == null) return 0;
        var idx = GetModIndex(mod, plugin);
        return idx.Counts.TryGetValue(sig, out var n) ? n : 0;
    }

    public static string QueryRecordSummaries(object? envObj, string plugin, string sig, int limit, int offset = 0)
    {
        var mod = ResolveMod(plugin, envObj);
        if (mod == null) return ToolError.Fail($"Plugin '{plugin}' not found.");
        var idx = GetModIndex(mod, plugin);
        var recs = RecordsOfSig(idx, sig);
        if (recs.Count == 0)
            return $"No records of type '{sig}' in {plugin}.";

        var take = recs.Skip(Math.Max(0, offset)).Take(limit).ToList();
        if (take.Count == 0) return $"No records of type '{sig}' in {plugin} at offset {offset}.";

        var rec0 = take[0];
        var colProps = DiscoverGridColumns(rec0, 8);

        const int EW = 36, VW = 26, Budget = 6800;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"{sig} in {plugin}: {recs.Count} total record(s).");

        var head = "  " + "EditorID [FormKey]".PadRight(EW);
        foreach (var p in colProps) head += " | " + SumTrunc(p.Name, VW).PadRight(VW);
        sb.AppendLine(head.TrimEnd());
        sb.AppendLine("  " + new string('-', Math.Min(EW + colProps.Count * (VW + 3), 200)));

        int emitted = 0;
        foreach (var rec in take)
        {
            if (sb.Length > Budget) break;
            var hdr = (rec.EditorID ?? "(no-eid)") + " [" + rec.FormKey + "]";
            var line = "  " + SumTrunc(hdr, EW).PadRight(EW);
            foreach (var prop in colProps)
            {
                string cell;
                try
                {
                    var v = prop.GetValue(rec);
                    cell = v switch
                    {
                        null => "",
                        Mutagen.Bethesda.Plugins.IFormLinkIdentifier fli => FormatFormLink(fli),
                        IEnumerable ie when v.GetType() != typeof(string) =>
                            "[" + SumCountEnumerable(ie) + " items]",
                        _ => v.ToString() ?? "",
                    };
                }
                catch { cell = "?"; }
                line += " | " + SumTrunc(cell, VW).PadRight(VW);
            }
            sb.AppendLine(line.TrimEnd());
            emitted++;
        }

        if (emitted < recs.Count)
            sb.AppendLine($"  ... showed {emitted} of {recs.Count}. Narrow with search_records (by EditorID/FormKey) " +
                          "or get_record for one record's full dump; raise 'limit' only if you truly need more rows.");

        return sb.ToString();
    }

    private static List<PropertyInfo> DiscoverGridColumns(object rec0, int max)
    {

        var scalars = new List<PropertyInfo>();
        var collections = new List<PropertyInfo>();
        foreach (var prop in rec0.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (_skipProps.Contains(prop.Name)) continue;
            if (prop.Name is "EditorID" or "FormKey") continue;
            if (prop.GetIndexParameters().Length > 0) continue;
            object? v; try { v = prop.GetValue(rec0); } catch { continue; }
            if (v == null) continue;
            var vt = v.GetType();
            if (_leafTypes.Contains(vt) || vt.IsEnum || v is Mutagen.Bethesda.Plugins.IFormLinkIdentifier)
                scalars.Add(prop);
            else if (v is IEnumerable && vt != typeof(string))
                collections.Add(prop);
        }
        var colProps = scalars.Take(max).ToList();
        if (colProps.Count < max) colProps.AddRange(collections.Take(max - colProps.Count));
        return colProps;
    }

    private static string SumTrunc(string? s, int max) =>
        string.IsNullOrEmpty(s) ? "" : s.Length <= max ? s : s[..(max - 3)] + "...";

    private static int SumCountEnumerable(IEnumerable ie)
    {
        if (ie is ICollection c) return c.Count;
        int n = 0; foreach (var _ in ie) n++; return n;
    }

    public static IReadOnlyList<Mutagen.Bethesda.Plugins.Records.IMajorRecordGetter> GetRecordsForBatch(
        object? envObj, string plugin, string sig)
    {
        var mod = ResolveMod(plugin, envObj);
        if (mod == null) return [];
        var idx = GetModIndex(mod, plugin);
        return RecordsOfSig(idx, sig);
    }

    public static IReadOnlyList<(string formKey, string editorId, string type)> QuerySearch(
        object? envObj, string plugin, string query, int limit = 50, int offset = 0)
    {
        var mod = ResolveMod(plugin, envObj);
        if (mod == null) return [];
        var idx = GetModIndex(mod, plugin);
        var hits = new List<(string, string, string)>();
        int skipped = 0;
        foreach (var (sig, r) in StreamRecords(idx))
        {
            var eid = r.EditorID ?? "";
            if (eid.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                r.FormKey.ToString().Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                if (skipped < offset) { skipped++; continue; }
                hits.Add((r.FormKey.ToString(), eid, sig));
                if (hits.Count >= limit) return hits;
            }
        }
        return hits;
    }

    public sealed record SearchHit(string FormKey, string EditorID, string Type, string Plugin, string Name = "");

    public static List<SearchHit> SearchAllRecords(object? envObj, string query, string? typeFilter = null, int limit = 200)
    {
        if (envObj == null) return new List<SearchHit>();
        query ??= "";

        var hits = new Dictionary<FormKey, SearchHit>();

        var filterSet = string.IsNullOrWhiteSpace(typeFilter)
            ? null
            : new HashSet<string>(
                typeFilter.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                StringComparer.OrdinalIgnoreCase);

        const int AccumulationCap = 5000;

        foreach (var (name, mod) in AllLoadedMods(envObj))
        {

            try
            {
                var idx = GetModIndex(mod, name);
                foreach (var (sig, r) in StreamRecords(idx))
                {
                    if (filterSet != null && !filterSet.Contains(sig)) continue;
                    {
                        var eid = r.EditorID ?? "";
                        string dispName = "";

                        try { dispName = r is Mutagen.Bethesda.Plugins.Aspects.INamedGetter nm ? (nm.Name ?? "") : ""; }
                        catch { }
                        if (query.Length > 0 &&
                            !eid.Contains(query, StringComparison.OrdinalIgnoreCase) &&
                            !dispName.Contains(query, StringComparison.OrdinalIgnoreCase) &&
                            !r.FormKey.ToString().Contains(query, StringComparison.OrdinalIgnoreCase)) continue;
                        hits[r.FormKey] = new SearchHit(r.FormKey.ToString(), eid, sig, name, dispName);
                        if (hits.Count >= AccumulationCap) goto done;
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLog.Exception($"SearchAllRecords: skipped {name}", ex);
            }
        }
        done:
        return hits.Values.Take(limit).ToList();
    }

    public static object? ResolveModPublic(string plugin, object? envObj) => ResolveMod(plugin, envObj);

    public static List<SearchHit> SearchCellRecords(object? envObj, string query, int limit = 25)
    {
        if (envObj == null) return new List<SearchHit>();
        query ??= "";

        var hits = new Dictionary<FormKey, SearchHit>();

        foreach (var (name, mod) in AllLoadedMods(envObj))
        {
            if (mod is not Mutagen.Bethesda.Fallout4.IFallout4ModGetter f4mod) continue;
            foreach (var cell in f4mod.EnumerateMajorRecords<Mutagen.Bethesda.Fallout4.ICellGetter>(throwIfUnknown: false))
            {
                var eid = cell.EditorID ?? "";
                var dispName = cell.Name?.String ?? "";
                if (query.Length > 0 &&
                    !eid.Contains(query, StringComparison.OrdinalIgnoreCase) &&
                    !dispName.Contains(query, StringComparison.OrdinalIgnoreCase) &&
                    !cell.FormKey.ToString().Contains(query, StringComparison.OrdinalIgnoreCase)) continue;
                hits[cell.FormKey] = new SearchHit(cell.FormKey.ToString(), eid, "CELL", name, dispName);
            }
        }
        return hits.Values.Take(limit).ToList();
    }

    public static List<SearchHit> SearchWorldspaceRecords(object? envObj, string query, int limit = 100)
    {
        if (envObj == null) return new List<SearchHit>();
        query ??= "";
        var hits = new Dictionary<FormKey, SearchHit>();

        foreach (var (name, mod) in AllLoadedMods(envObj))
        {
            if (mod is not Mutagen.Bethesda.Fallout4.IFallout4ModGetter f4mod) continue;
            foreach (var ws in f4mod.EnumerateMajorRecords<Mutagen.Bethesda.Fallout4.IWorldspaceGetter>(throwIfUnknown: false))
            {
                var eid = ws.EditorID ?? "";
                var dispName = ws.Name?.String ?? "";
                if (query.Length > 0 &&
                    !eid.Contains(query, StringComparison.OrdinalIgnoreCase) &&
                    !dispName.Contains(query, StringComparison.OrdinalIgnoreCase) &&
                    !ws.FormKey.ToString().Contains(query, StringComparison.OrdinalIgnoreCase)) continue;
                hits[ws.FormKey] = new SearchHit(ws.FormKey.ToString(), eid, "WRLD", name, dispName);
            }
        }
        return hits.Values.Take(limit).ToList();
    }

    public static string QueryRecordFields(object? envObj, string plugin, string formKeyOrEditorId)
    {

        var candidates = new List<(string name, object mod)>();
        var named = ResolveMod(plugin, envObj);
        if (named != null) candidates.Add((plugin, named));
        foreach (var m in AllLoadedMods(envObj))
        {
            bool alreadyNamed = candidates.Any(c => string.Equals(c.name, m.name, StringComparison.OrdinalIgnoreCase));
            if (alreadyNamed) continue;
            if (LooseMods.ContainsKey(m.name) || _modIndexCache.ContainsKey(m.name))
                candidates.Add(m);
        }

        FormKey.TryFactory(formKeyOrEditorId, out var fk);

        foreach (var (name, mod) in candidates)
        {
            var idx = GetModIndex(mod, name);
            Mutagen.Bethesda.Plugins.Records.IMajorRecordGetter? rec = null;
            foreach (var (_, r) in StreamRecords(idx))
            {
                if (!fk.IsNull ? r.FormKey == fk
                               : string.Equals(r.EditorID, formKeyOrEditorId, StringComparison.OrdinalIgnoreCase))
                { rec = r; break; }
            }
            if (rec == null) continue;

            var temp = new RecordNode { Key = rec.EditorID ?? rec.FormKey.ToString() };
            AddLeafInit(temp, "FormKey", rec.FormKey.ToString());
            AddLeafInit(temp, "EditorID", rec.EditorID ?? "");
            AddLeafInit(temp, "Type", rec.Registration.Name);
            AddLeafInit(temp, "SourcePlugin", name);
            WalkObject(rec, temp, 0, 4, name);

            var sb2 = new System.Text.StringBuilder();
            FlattenToText(temp, 0, sb2);
            return sb2.ToString();
        }

        var loaded = string.Join(", ", AllLoadedMods(envObj).Select(m => m.name));
        return $"(record '{formKeyOrEditorId}' not found in '{plugin}'. " +
               $"Loaded plugins: {(string.IsNullOrEmpty(loaded) ? "none" : loaded)}. " +
               $"Try search_records first, or use the exact plugin name from list_plugins.)";
    }

    public static string CheckPlugin(object? envObj, string plugin)
    {
        var mod = ResolveMod(plugin, envObj);
        if (mod == null) return $"(plugin '{plugin}' is not loaded)";
        if (mod is not Mutagen.Bethesda.Fallout4.IFallout4ModGetter f4) return "(not a Fallout 4 plugin)";

        var declared = new HashSet<ModKey> { f4.ModKey };
        foreach (var m in f4.MasterReferences) declared.Add(m.Master);

        var liveCache = envObj != null ? LinkCache : null;

        int count = 0, deleted = 0, broken = 0, typeCollisions = 0;
        var samples = new List<string>();
        foreach (var rec in f4.EnumerateMajorRecords())
        {
            count++;
            if (rec.IsDeleted)
            {
                deleted++;
                if (samples.Count < 50) samples.Add($"DELETED  {CheckLabel(rec)}");
            }
            foreach (var link in rec.EnumerateFormLinks())
            {
                if (link.FormKey.IsNull) continue;
                if (declared.Contains(link.FormKey.ModKey)) continue;
                broken++;
                if (samples.Count < 50) samples.Add($"REF TO UNDECLARED MASTER  {CheckLabel(rec)} -> {link.FormKey}");
            }

            if (envObj != null && !f4.ModKey.Equals(rec.FormKey.ModKey))
            {
                var masterPlugin = rec.FormKey.ModKey.FileName;
                var masterRec = GetRecordVersion(envObj, masterPlugin, rec.FormKey);
                if (masterRec != null)
                {
                    var thisType = rec.Registration.GetterType;
                    var masterType = masterRec.Registration.GetterType;
                    if (!masterType.IsAssignableFrom(thisType) && !thisType.IsAssignableFrom(masterType))
                    {
                        typeCollisions++;
                        var masterEdid = string.IsNullOrEmpty(masterRec.EditorID)
                            ? masterRec.FormKey.ToString() : masterRec.EditorID;
                        if (samples.Count < 50)
                            samples.Add($"TYPE COLLISION (game-breaking)  {CheckLabel(rec)} overrides " +
                                        $"{masterType.Name} '{masterEdid}' [{masterRec.FormKey}] " +
                                        $"in {masterPlugin}");
                    }
                }
            }
        }

        var duplicateScan = ScanDuplicateFormIds(plugin, envObj);
        var duplicateExtras = duplicateScan.Duplicates.Sum(d => d.Count - 1);
        foreach (var duplicate in duplicateScan.Duplicates)
            if (samples.Count < 50) samples.Add(DuplicateLabel(duplicate));
        if (duplicateScan.Error != null && samples.Count < 50)
            samples.Add($"DUPLICATE FORMID SCAN FAILED  {duplicateScan.Error}");

        var issueCount = deleted + broken + typeCollisions + duplicateScan.Duplicates.Count;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Checked {plugin}: {count} records, {deleted} deleted, {broken} reference(s) to undeclared masters, " +
                      $"{typeCollisions} type collision(s), {duplicateScan.Duplicates.Count} duplicate FormID group(s) " +
                      $"({duplicateExtras} extra record(s)).");
        if (issueCount == 0 && duplicateScan.Error == null)
        {
            sb.AppendLine("No issues found.");
            return sb.ToString();
        }

        sb.AppendLine(duplicateScan.Error == null ? "Issues:" : "Issues / incomplete checks:");
        foreach (var sample in samples) sb.AppendLine("  " + sample);
        var hidden = issueCount + (duplicateScan.Error == null ? 0 : 1) - samples.Count;
        if (hidden > 0) sb.AppendLine($"  ... and {hidden} more");
        return sb.ToString();
    }

    public static bool RemoveFromEditableMod(string plugin, Mutagen.Bethesda.Plugins.FormKey fk)
    {
        if (!EditableMods.TryGetValue(plugin, out var modObj)) return false;
        if (modObj is not Mutagen.Bethesda.Fallout4.Fallout4Mod mod) return false;
        return mod.ConstructibleObjects.Remove(fk);
    }

    private static string CheckLabel(Mutagen.Bethesda.Plugins.Records.IMajorRecordGetter r) =>
        $"{(string.IsNullOrEmpty(r.EditorID) ? r.FormKey.ToString() : r.EditorID)} [{r.FormKey}] ({r.Registration.Name})";

    private static readonly IReadOnlySet<string> VanillaMasters = ProtectedPlugins.VanillaMasters;

    private static bool IsVanillaRef(Mutagen.Bethesda.Plugins.FormKey fk) =>
        VanillaMasters.Contains(fk.ModKey.FileName.String);

    public static string ScanErrorsDeep(object? envObj, string plugin)
    {
        if (envObj == null) return "Deep error scan needs the loaded game environment. Use 'Load Env' first.";
        if (ResolveMod(plugin, envObj) is not Mutagen.Bethesda.Fallout4.IFallout4ModGetter mod)
            return $"(plugin '{plugin}' is not loaded)";

        var cache = (Mutagen.Bethesda.Plugins.Cache.ILinkCache)((dynamic)envObj).LinkCache;
        int dangling = 0, deleted = 0, count = 0;
        var samples = new List<string>();
        foreach (var rec in mod.EnumerateMajorRecords())
        {
            count++;
            if (rec.IsDeleted) { deleted++; if (samples.Count < 60) samples.Add($"DELETED  {CheckLabel(rec)}"); }
            foreach (var link in rec.EnumerateFormLinks())
            {
                if (link.FormKey.IsNull || IsVanillaRef(link.FormKey)) continue;
                if (!cache.TryResolveIdentifier<IMajorRecordGetter>(link.FormKey, out _))
                {
                    dangling++;
                    if (samples.Count < 60) samples.Add($"DANGLING REF (crash risk)  {CheckLabel(rec)} -> {link.FormKey}");
                }
            }
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Error scan of {plugin}: {count} records, {deleted} deleted, {dangling} unresolved reference(s).");
        if (deleted + dangling == 0) { sb.AppendLine("No crash-causing issues found."); return sb.ToString(); }
        sb.AppendLine("Issues:");
        foreach (var s in samples) sb.AppendLine("  " + s);
        if (deleted + dangling > samples.Count) sb.AppendLine($"  ... and {deleted + dangling - samples.Count} more");
        return sb.ToString();
    }

    public static string ScanBrokenRefs(object? envObj, string plugin)
    {
        if (envObj == null) return "scan_broken_refs needs the loaded game environment. Use 'Load Env' first.";
        if (ResolveMod(plugin, envObj) is not Mutagen.Bethesda.Fallout4.IFallout4ModGetter mod)
            return $"(plugin '{plugin}' is not loaded)";

        var cache = (Mutagen.Bethesda.Plugins.Cache.ILinkCache)((dynamic)envObj).LinkCache;
        var loadedPlugins = BuildLoadedPluginSet(envObj);
        var brokenRefs = ScanBrokenRefsInMod(cache, plugin, mod, loadedPlugins);
        var duplicates = DuplicateIntegrityBlock(plugin, envObj);
        if (brokenRefs == null && duplicates == null)
            return $"{plugin}: no broken references or duplicate FormIDs found.";
        if (brokenRefs == null) return duplicates!;
        if (duplicates == null) return brokenRefs;
        return brokenRefs.TrimEnd() + "\n\n" + duplicates;
    }

    private static HashSet<string> BuildLoadedPluginSet(object? envObj)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in EditableMods) set.Add(kv.Key);
        foreach (var kv in LooseMods) set.Add(kv.Key);
        if (envObj != null)
        {
            dynamic env = envObj;
            foreach (var l in ((IEnumerable)env.LoadOrder.ListedOrder).Cast<dynamic>())
                if (l.Mod != null) set.Add((string)l.ModKey.FileName.String);
        }
        return set;
    }

    private static string? ScanBrokenRefsInMod(
        Mutagen.Bethesda.Plugins.Cache.ILinkCache cache, string pluginName,
        Mutagen.Bethesda.Fallout4.IFallout4ModGetter mod,
        HashSet<string> loadedPlugins)
    {
        int count = 0, dangling = 0;
        var byMaster = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var samples = new List<string>();

        foreach (var rec in mod.EnumerateMajorRecords())
        {
            count++;
            foreach (var link in rec.EnumerateFormLinks())
            {
                var fk = link.FormKey;
                if (fk.IsNull || IsVanillaRef(fk)) continue;

                if (!loadedPlugins.Contains(fk.ModKey.FileName.String)) continue;
                if (!cache.TryResolveIdentifier<IMajorRecordGetter>(fk, out _))
                {
                    dangling++;
                    var master = fk.ModKey.FileName.String;
                    byMaster[master] = byMaster.TryGetValue(master, out var n) ? n + 1 : 1;
                    if (samples.Count < 80)
                        samples.Add($"  [{fk}] in {CheckLabel(rec)}");
                }
            }
        }

        if (dangling == 0) return null;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"{pluginName}  ({dangling} broken ref(s) across {count} records)");
        sb.AppendLine("  Broken refs by target plugin:");
        foreach (var kv in byMaster.OrderByDescending(x => x.Value))
            sb.AppendLine($"    {kv.Value,5}x  {kv.Key}");
        sb.AppendLine("  Sample broken refs:");
        foreach (var s in samples) sb.AppendLine(s);
        if (dangling > samples.Count) sb.AppendLine($"  ... and {dangling - samples.Count} more");
        return sb.ToString();
    }

    public static string ScanAllPluginsForBrokenRefs(object? envObj)
    {
        if (envObj == null) return "scan_all_plugins needs the loaded game environment. Use 'Load Env' first.";

        var cache = (Mutagen.Bethesda.Plugins.Cache.ILinkCache)((dynamic)envObj).LinkCache;
        var loadedPlugins = BuildLoadedPluginSet(envObj);
        var results = new System.Text.StringBuilder();
        int totalPlugins = 0, dirtyPlugins = 0, totalBroken = 0;

        foreach (var (name, modObj) in AllLoadedMods(envObj))
        {
            if (VanillaMasters.Contains(name)) continue;
            if (modObj is not Mutagen.Bethesda.Fallout4.IFallout4ModGetter mod) continue;
            totalPlugins++;
            var pluginResult = ScanBrokenRefsInMod(cache, name, mod, loadedPlugins);
            if (pluginResult == null) continue;
            dirtyPlugins++;
            var m = System.Text.RegularExpressions.Regex.Match(pluginResult, @"(\d+) broken ref");
            if (m.Success && int.TryParse(m.Groups[1].Value, out var n)) totalBroken += n;
            results.AppendLine(pluginResult);
        }

        if (dirtyPlugins == 0)
            return $"All {totalPlugins} non-vanilla plugins are clean -- no broken references found.";

        return $"=== Crash Risk Scan: {dirtyPlugins} of {totalPlugins} plugins have broken refs -- {totalBroken} total ===\n\n" +
               results.ToString();
    }

    public static string ScanConflicts(object? envObj, string plugin)
    {
        if (envObj == null) return "Conflict scan needs the loaded game environment. Use 'Load Env' first.";
        dynamic env = envObj;

        var owners = new Dictionary<FormKey, List<string>>();
        foreach (var l in (IEnumerable)env.LoadOrder.ListedOrder)
        {
            dynamic ld = l;
            if (ld.Mod == null) continue;
            string pname = ld.ModKey.FileName.String;
            if (ld.Mod is Mutagen.Bethesda.Fallout4.IFallout4ModGetter m)
                foreach (var rec in m.EnumerateMajorRecords())
                {
                    if (!owners.TryGetValue(rec.FormKey, out var lst)) { lst = new(); owners[rec.FormKey] = lst; }
                    if (!lst.Contains(pname)) lst.Add(pname);
                }
        }

        if (ResolveMod(plugin, envObj) is not Mutagen.Bethesda.Fallout4.IFallout4ModGetter mod)
            return $"(plugin '{plugin}' is not loaded)";

        int conflicts = 0;
        var samples = new List<string>();
        foreach (var rec in mod.EnumerateMajorRecords())
        {
            if (owners.TryGetValue(rec.FormKey, out var lst) && lst.Count > 1)
            {
                conflicts++;
                if (samples.Count < 60) samples.Add($"{CheckLabel(rec)}  <- {string.Join(", ", lst)}");
            }
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Conflict scan of {plugin}: {conflicts} record(s) also touched by other plugins.");
        if (conflicts == 0) { sb.AppendLine("No conflicts with other loaded plugins."); return sb.ToString(); }
        sb.AppendLine("Conflicts:");
        foreach (var s in samples) sb.AppendLine("  " + s);
        if (conflicts > samples.Count) sb.AppendLine($"  ... and {conflicts - samples.Count} more");
        return sb.ToString();
    }

    public static FormKey ResolveEditorIdToFormKey(object? envObj, string editorId)
    {
        foreach (var (name, mod) in AllLoadedMods(envObj))
        {
            if (!LooseMods.ContainsKey(name) && !_modIndexCache.ContainsKey(name)) continue;
            var idx = GetModIndex(mod, name);
            foreach (var (_, r) in StreamRecords(idx))
                if (string.Equals(r.EditorID, editorId, StringComparison.OrdinalIgnoreCase))
                    return r.FormKey;
        }
        return FormKey.Null;
    }

    public static FormKey ResolveId(object? envObj, string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return FormKey.Null;
        if (FormKey.TryFactory(id, out var fk)) return fk;
        var cache = LinkCache;
        if (cache != null && cache.TryResolve<Mutagen.Bethesda.Plugins.Records.IMajorRecordGetter>(id, out var rec)) return rec.FormKey;
        return ResolveEditorIdToFormKey(envObj, id);
    }

    private static void FlattenToText(RecordNode node, int depth, System.Text.StringBuilder sb)
    {
        foreach (var c in node.Children)
        {
            var indent = new string(' ', depth * 2);

            if (c.IsLeaf || c.IsSummary) sb.AppendLine($"{indent}{c.Key}: {c.Value}");
            else { sb.AppendLine($"{indent}{c.Key}:"); FlattenToText(c, depth + 1, sb); }
        }
    }

    [ThreadStatic]
    private static HashSet<object>? _visiting;

    private static HashSet<object> Visiting => _visiting ??= new HashSet<object>();

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, PropertyInfo[]> _propCache = new();

    [ThreadStatic] private static int _walkNodeCount;
    private const int MaxWalkNodes = 5000;

    private static void WalkObject(object obj, RecordNode parent, int depth, int maxDepth, string pluginName = "")
    {
        if (depth == 0) _walkNodeCount = 0;
        if (depth > maxDepth) return;
        if (_walkNodeCount >= MaxWalkNodes) return;
        if (obj == null!) return;

        var type = obj.GetType();

        if (_leafTypes.Contains(type) || type.IsEnum)
        {
            if (parent.IsLeaf) parent.Values[pluginName] = obj.ToString()!;
            return;
        }

        if (obj is not Mutagen.Bethesda.Plugins.Records.IMajorRecordGetter
            && obj is Mutagen.Bethesda.Plugins.IFormLinkIdentifier topLink)
        {
            if (parent.IsLeaf)
            {
                parent.Values[pluginName] = FormatFormLink(topLink);

                if (parent.EditKind != Models.FieldEditKind.Ref)
                {
                    parent.EditKind = Models.FieldEditKind.Ref;
                    (parent.RefType, parent.RefTypes) = FormLinkInfo(obj.GetType());
                }
            }
            return;
        }

        if (obj is Mutagen.Bethesda.Strings.ITranslatedStringGetter ts)
        {
            if (parent.IsLeaf) parent.Values[pluginName] = ts.String ?? ts.ToString() ?? "";
            return;
        }

        if (obj is FormKey || obj is ModKey)
        {
            if (parent.IsLeaf) parent.Values[pluginName] = obj.ToString() ?? "";
            return;
        }

        if (obj is Type || obj is System.Reflection.MemberInfo || obj is System.Reflection.Assembly
            || obj is System.Reflection.Module || obj is System.Reflection.ParameterInfo
            || (type.Namespace ?? "").StartsWith("System.Reflection", StringComparison.Ordinal))
        {
            if (parent.IsLeaf) parent.Values[pluginName] = obj.ToString() ?? "";
            return;
        }

        if (!Visiting.Add(obj)) return;
        try
        {
            var props = _propCache.GetOrAdd(type, t =>
                t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                 .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
                 .Where(p => !_skipProps.Contains(p.Name))
                 .ToArray());

            foreach (var prop in props)
            {
                object? val;
                try { val = prop.GetValue(obj); } catch { continue; }
                if (val == null) continue;

                var valType = val.GetType();

                if (val is Mutagen.Bethesda.Plugins.Records.IMajorRecordGetter) continue;

                if (_walkNodeCount >= MaxWalkNodes) break;

                var child = parent.GetChild(prop.Name);
                if (child == null)
                {
                    child = new RecordNode { Key = prop.Name, Parent = parent };
                    parent.Children.Add(child);
                    _walkNodeCount++;
                }

                if (_leafTypes.Contains(valType) || valType.IsEnum)
                {
                    child.Values[pluginName] = val.ToString()!;

                    if (valType == typeof(bool)) child.EditKind = Models.FieldEditKind.Bool;
                    else if (valType.IsEnum && child.EnumOptions == null)
                    {
                        child.EditKind = Models.FieldEditKind.Enum;
                        child.EnumOptions = Enum.GetNames(valType);
                    }
                }
                else if (val is string s)
                {
                    child.Values[pluginName] = s;
                }
                else if (val is Mutagen.Bethesda.Plugins.IFormLinkIdentifier fli)
                {
                    child.Values[pluginName] = FormatFormLink(fli);
                    child.EditKind = Models.FieldEditKind.Ref;
                    if (child.RefType == null)
                        (child.RefType, child.RefTypes) = FormLinkInfo(prop.PropertyType);
                }

                else if (val is Mutagen.Bethesda.Strings.ITranslatedStringGetter tstr)
                {
                    child.Values[pluginName] = tstr.String ?? tstr.ToString() ?? "";
                }
                else if (val is IEnumerable enumerable and not string)
                {

                    if (Services.Rendering.ElementRenderer.TryRenderByteBlob(enumerable, out var blob))
                    {
                        child.Values[pluginName] = blob;
                    }
                    else
                    {
                        var items = new List<object>();
                        foreach (var it in enumerable) if (it != null) items.Add(it);

                        if (items.Count > 1 && items.All(x =>
                                x is Mutagen.Bethesda.Plugins.IFormLinkIdentifier
                                && x is not Mutagen.Bethesda.Plugins.Records.IMajorRecordGetter))
                            items.Sort((a, b) => string.CompareOrdinal(
                                ((Mutagen.Bethesda.Plugins.IFormLinkIdentifier)a).FormKey.ToString(),
                                ((Mutagen.Bethesda.Plugins.IFormLinkIdentifier)b).FormKey.ToString()));

                        int i = 0;
                        foreach (var item in items)
                        {
                            if (_walkNodeCount >= MaxWalkNodes) break;
                            var entryKey = $"[{i}]";
                            var entry = child.GetChild(entryKey);
                            if (entry == null)
                            {
                                entry = new RecordNode { Key = entryKey, Parent = child };
                                child.Children.Add(entry);
                                _walkNodeCount++;
                            }
                            if (Services.Rendering.ElementRenderer.TryRenderLine(item, out var friendly))
                            {

                                entry.Values[pluginName] = friendly;
                                entry.IsSummary = true;
                            }
                            else
                            {
                                entry.Values[pluginName] = BuildSummary(item);
                            }
                            WalkObject(item, entry, depth + 1, maxDepth, pluginName);
                            i++;
                        }
                    }
                }

                else if (Services.Rendering.ElementRenderer.TryRenderLine(val, out var friendlyVal))
                {
                    child.Values[pluginName] = friendlyVal;
                }
                else
                {
                    WalkObject(val, child, depth + 1, maxDepth, pluginName);
                }
            }
        }
        finally
        {
            Visiting.Remove(obj);
        }
    }

    public static string FormatCondition(Mutagen.Bethesda.Fallout4.IConditionGetter cond)
    {
        var op = cond.CompareOperator switch
        {
            Mutagen.Bethesda.Fallout4.CompareOperator.EqualTo => "==",
            Mutagen.Bethesda.Fallout4.CompareOperator.NotEqualTo => "!=",
            Mutagen.Bethesda.Fallout4.CompareOperator.GreaterThan => ">",
            Mutagen.Bethesda.Fallout4.CompareOperator.GreaterThanOrEqualTo => ">=",
            Mutagen.Bethesda.Fallout4.CompareOperator.LessThan => "<",
            Mutagen.Bethesda.Fallout4.CompareOperator.LessThanOrEqualTo => "<=",
            _ => cond.CompareOperator.ToString(),
        };

        string rhs = cond switch
        {
            Mutagen.Bethesda.Fallout4.IConditionGlobalGetter g => FormatFormLink(g.ComparisonValue),
            Mutagen.Bethesda.Fallout4.IConditionFloatGetter f => f.ComparisonValue.ToString("0.###"),
            _ => "?",
        };

        var data = cond.Data;
        string fn = data is Mutagen.Bethesda.Fallout4.IFunctionConditionDataGetter fc
            ? fc.Function.ToString() : data.GetType().Name;

        var args = new List<string>();
        if (data is Mutagen.Bethesda.Fallout4.IFunctionConditionDataGetter fcd)
        {
            if (!fcd.ParameterOneRecord.FormKey.IsNull) args.Add(FormatFormLink(fcd.ParameterOneRecord));
            else if (fcd.ParameterOneNumber != 0) args.Add(fcd.ParameterOneNumber.ToString());
            if (!fcd.ParameterTwoRecord.FormKey.IsNull) args.Add(FormatFormLink(fcd.ParameterTwoRecord));
            else if (fcd.ParameterTwoNumber != 0) args.Add(fcd.ParameterTwoNumber.ToString());
        }

        var line = $"{fn}({string.Join(", ", args)}) {op} {rhs}";
        if (data.RunOnType != Mutagen.Bethesda.Fallout4.Condition.RunOnType.Subject)
        {
            line += $" on {data.RunOnType}";
            if (!data.Reference.FormKey.IsNull) line += $" {FormatFormLink(data.Reference)}";
        }
        return line;
    }

    private static string BuildSummary(object item)
    {
        if (item == null) return "";
        var type = item.GetType();
        if (_leafTypes.Contains(type) || type.IsEnum || item is string)
            return item.ToString() ?? "";

        if (item is Mutagen.Bethesda.Plugins.IFormLinkIdentifier fli)
            return fli.FormKey.IsNull ? "Null" : fli.FormKey.ToString();

        var parts = new List<string>();
        string[] summaryProps = { "Level", "Count", "Reference", "Magnitude", "Duration", "BaseEffect", "EditorID", "FormKey", "Value", "FunctionType", "Property", "Step", "Keyword" };

        var props = _propCache.GetOrAdd(type, t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanRead).ToArray());

        foreach (var pName in summaryProps)
        {
            var p = props.FirstOrDefault(x => x.Name == pName);
            if (p != null)
            {
                try
                {
                    var val = p.GetValue(item);
                    if (val != null)
                    {
                        if (val is Mutagen.Bethesda.Plugins.IFormLinkIdentifier f)
                        {
                            if (!f.FormKey.IsNull) parts.Add(f.FormKey.ToString());
                        }
                        else if (val is string s && !string.IsNullOrEmpty(s))
                        {
                            parts.Add(s);
                        }
                        else
                        {
                            parts.Add(val.ToString()!);
                        }
                    }
                } catch { }
            }
        }

        if (parts.Count > 0)
            return string.Join(" ", parts);

        return "";
    }

    private static void AddLeafInit(RecordNode parent, string key, string value, string pluginName = "")
    {
        var node = new RecordNode { Key = key, Parent = parent };
        node.Values[pluginName] = value;
        parent.Children.Add(node);
    }
}
