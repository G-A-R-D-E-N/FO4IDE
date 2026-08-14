using FO4RecordEditor.Services;

namespace FO4RecordEditor.Tests;

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

    private static void Restore<TKey, TValue>(
        IDictionary<TKey, TValue> dict,
        KeyValuePair<TKey, TValue>[] snapshot)
        where TKey : notnull
    {
        dict.Clear();
        foreach (var kv in snapshot) dict[kv.Key] = kv.Value;
    }
}
