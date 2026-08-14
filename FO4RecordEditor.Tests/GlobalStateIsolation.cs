using FO4RecordEditor.Services;

namespace FO4RecordEditor.Tests;

/// <summary>
/// Snapshots MutagenLoader's process-global registries and link cache, and restores them on
/// dispose, so a test that loads an environment cannot leave that environment in the process
/// globals for the next test class. Load() mutates the globals by contract; without save/restore,
/// later classes resolve records against whatever the last loading class left behind. Assembly
/// parallelization is disabled (AssemblyConfig.cs), so per-test restore keeps the suite
/// deterministic.
/// </summary>
/// <remarks>
/// Deliberately not captured:
/// - TextureService/AssetResolver session roots: setters only, no getters; only asset-resolving
///   tests read them and those set their own roots.
/// - Mo2ProfileLoader.FailedToLoad: written and read only by Mo2StartupLoadTests (it is the
///   fixture under test there, not ambient state).
/// - RecordCountCache: keyed by plugin path, so synthetic temp paths can never collide with or
///   answer for a real plugin's entry.
/// - The per-mod index cache: a pure cache; dropping entries on restore is always safe (they
///   rebuild from the mod instance on demand), so restore = clear, not an exact snapshot.
/// </remarks>
public sealed class GlobalStateIsolation : IDisposable
{
    private readonly Mutagen.Bethesda.Plugins.Cache.ILinkCache? _linkCache;
    private readonly KeyValuePair<string, object>[] _looseMods;
    private readonly KeyValuePair<string, string>[] _looseModPaths;
    private readonly KeyValuePair<string, string>[] _pluginSourcePaths;
    private readonly KeyValuePair<string, object>[] _editableMods;
    private readonly KeyValuePair<string, bool>[] _masterIsEsl;
    private readonly int _maxCachedModIndexes;
    private readonly long _maxCachedIndexRecords;

    public GlobalStateIsolation()
    {
        _linkCache = MutagenLoader.LinkCache;
        _looseMods = MutagenLoader.LooseMods.ToArray();
        _looseModPaths = MutagenLoader.LooseModPaths.ToArray();
        _pluginSourcePaths = MutagenLoader.PluginSourcePaths.ToArray();
        _editableMods = MutagenLoader.EditableMods.ToArray();
        _masterIsEsl = MutagenLoader.MasterIsEsl.ToArray();
        _maxCachedModIndexes = MutagenLoader.MaxCachedModIndexes;
        _maxCachedIndexRecords = MutagenLoader.MaxCachedIndexRecords;
    }

    public void Dispose()
    {
        MutagenLoader.LinkCache = _linkCache;
        Restore(MutagenLoader.LooseMods, _looseMods);
        Restore(MutagenLoader.LooseModPaths, _looseModPaths);
        Restore(MutagenLoader.PluginSourcePaths, _pluginSourcePaths);
        Restore(MutagenLoader.EditableMods, _editableMods);
        Restore(MutagenLoader.MasterIsEsl, _masterIsEsl);
        MutagenLoader.MaxCachedModIndexes = _maxCachedModIndexes;
        MutagenLoader.MaxCachedIndexRecords = _maxCachedIndexRecords;
        MutagenLoader.ClearModIndexCacheForTest();
        ConflictScanner.InvalidateCache();
    }

    // Works for both ConcurrentDictionary and plain Dictionary through IDictionary.
    private static void Restore<TKey, TValue>(
        IDictionary<TKey, TValue> dict,
        KeyValuePair<TKey, TValue>[] snapshot)
        where TKey : notnull
    {
        dict.Clear();
        foreach (var kv in snapshot) dict[kv.Key] = kv.Value;
    }
}
