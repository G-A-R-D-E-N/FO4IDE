using Loqui;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Binary.Overlay;
using Mutagen.Bethesda.Plugins.Binary.Translations;
using Mutagen.Bethesda.Plugins.Exceptions;
using Mutagen.Bethesda.Plugins.Internals;
using Noggog;
using System.Collections;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Mutagen.Bethesda.Plugins.Assets;
using Mutagen.Bethesda.Plugins.Binary.Streams;

namespace Mutagen.Bethesda.Plugins.Records;

public abstract class AGroup<TMajor> : IEnumerable<TMajor>, IGroup<TMajor>
    where TMajor : class, IMajorRecordInternal
{
    private static readonly ILoquiRegistration _registration = LoquiRegistration.GetRegister(typeof(TMajor));

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    protected abstract ICache<TMajor, FormKey> ProtectedCache { get; }

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    internal ICache<TMajor, FormKey> InternalCache => ProtectedCache;

    public IEnumerable<TMajor> Records => ProtectedCache.Items;

    IEnumerable<IMajorRecordGetter> IGroupGetter.Records => Records;
    IEnumerable<ILoquiObject> IGroupCommonGetter.Records => Records;
    IEnumerable<IMajorRecord> IGroup.Records => Records;
    IEnumerable<IMajorRecord> IGroup<TMajor>.Records => Records;

    public int Count => ProtectedCache.Count;

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    public IMod SourceMod { get; private set; }

    IReadOnlyCache<TMajor, FormKey> IGroupGetter<TMajor>.RecordCache => InternalCache;

    public ICache<TMajor, FormKey> RecordCache => InternalCache;

    IReadOnlyCache<IMajorRecordGetter, FormKey> IGroupGetter.RecordCache => RecordCache;

    public IEnumerable<FormKey> FormKeys => InternalCache.Keys;

    public TMajor this[FormKey key] => InternalCache[key];

    IMajorRecordGetter IGroupGetter.this[FormKey key] => this[key];

    public Type ContainedRecordType => typeof(TMajor);

    protected AGroup()
    {
        SourceMod = null!;
    }

    protected AGroup(IModGetter getter)
    {
        SourceMod = null!;
    }

    public AGroup(IMod mod)
    {
        SourceMod = mod;
    }

    public override string ToString()
    {
        return $"Group<{typeof(TMajor).Name}>({InternalCache.Count})";
    }

    public IEnumerator<TMajor> GetEnumerator()
    {
        return InternalCache.Items.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return InternalCache.GetEnumerator();
    }

    public void Add(TMajor record) => InternalCache.Add(record);

    public TMajor AddReturn(TMajor record)
    {
        InternalCache.Add(record);
        return record;
    }

    private TMajor ConfirmCorrectType(IMajorRecord record, string paramName)
    {
        if (record == null) throw new ArgumentNullException(paramName);
        if (record is not TMajor cast)
        {
            throw new ArgumentException(
                $"A record was added of the wrong type.  Expected {typeof(TMajor)}, but was given {record.GetType()}",
                paramName);
        }

        return cast;
    }

    public void AddUntyped(IMajorRecord record)
    {
        Add(ConfirmCorrectType(record, nameof(record)));
    }

    public void Set(TMajor record) => InternalCache.Set(record);

    public void SetUntyped(IMajorRecord record) => Set(ConfirmCorrectType(record, nameof(record)));

    public void Set(IEnumerable<TMajor> records) => InternalCache.Set(records);

    public void SetUntyped(IEnumerable<IMajorRecord> records) => SetUntyped(records.Select(r => ConfirmCorrectType(r, nameof(records))));

    public bool Remove(FormKey key) => InternalCache.Remove(key);

    public void Remove(IEnumerable<FormKey> keys) => InternalCache.Remove(keys);

    public void Clear() => InternalCache.Clear();

    public bool ContainsKey(FormKey key) => InternalCache.ContainsKey(key);

    public ILoquiRegistration ContainedRecordRegistration => _registration;

    public abstract IEnumerable<IFormLinkGetter> EnumerateFormLinks(bool iterateNestedRecords = true);

    public abstract IEnumerable<IAssetLink> EnumerateListedAssetLinks();

    public abstract void RemapListedAssetLinks(IReadOnlyDictionary<IAssetLinkGetter, string> mapping);

    public abstract void RemapAssetLinks(IReadOnlyDictionary<IAssetLinkGetter, string> mapping, AssetLinkQuery query, IAssetLinkCache? linkCache);

    public abstract IEnumerable<IAssetLinkGetter> EnumerateAssetLinks(
        AssetLinkQuery queryCategories = AssetLinkQuery.Listed,
        IAssetLinkCache? linkCache = null,
        Type? assetType = null);
}

internal static class GroupRecordTypeGetter<T>
{
    public static readonly RecordType GRUP_RECORD_TYPE;

    static GroupRecordTypeGetter()
    {
        var regis = LoquiRegistration.GetRegister(typeof(T));
        if (regis == null) throw new ArgumentException();
        GRUP_RECORD_TYPE = (RecordType)regis.ClassType.GetField(Constants.GrupRecordTypeMember)!.GetValue(null)!;
    }
}

internal sealed class GroupMajorRecordCacheWrapper<T> : IReadOnlyCache<T, FormKey>
    where T : IMajorRecordGetter
{
    private readonly IReadOnlyDictionary<FormKey, int> _locs;
    private readonly ReadOnlyMemorySlice<byte> _data;
    private readonly BinaryOverlayFactoryPackage _package;

    public GroupMajorRecordCacheWrapper(
        IReadOnlyDictionary<FormKey, int> locs,
        ReadOnlyMemorySlice<byte> data,
        BinaryOverlayFactoryPackage package)
    {
        _locs = locs;
        _data = data;
        _package = package;
    }

    public T? TryGetValue(FormKey key)
    {
        if (_locs.TryGetValue(key, out var loc))
        {
            return ConstructWrapper(loc);
        }
        return default;
    }

    public T this[FormKey key]
    {
        get
        {
            try
            {
                return ConstructWrapper(_locs[key]);
            }
            catch (Exception ex)
            {
                RecordException.EnrichAndThrow<T>(ex, key, edid: null);
                throw;
            }
        }
    }

    public bool TryGetValue(FormKey key, [MaybeNullWhen(false)] out T value)
    {
        if (_locs.TryGetValue(key, out var loc))
        {
            value = ConstructWrapper(loc);
            return true;
        }
        value = default;
        return false;
    }

    public int Count => _locs.Count;

    public IEnumerable<FormKey> Keys => _locs.Keys;

    public IEnumerable<T> Items => this.Select(kv => kv.Value);

    public bool ContainsKey(FormKey key) => _locs.ContainsKey(key);

    public IEnumerator<IKeyValue<FormKey, T>> GetEnumerator()
    {
        foreach (var kv in _locs)
        {
            KeyValue<FormKey, T> item;
            try
            {
                item = new KeyValue<FormKey, T>(kv.Key, ConstructWrapper(kv.Value));
            }
            catch (Exception ex)
            {
                RecordException.EnrichAndThrow<T>(ex, kv.Key, edid: null);
                throw;
            }
            yield return item;
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private T ConstructWrapper(int pos)
    {
        var data = _data.Slice(pos);
        if (SubgroupsBinaryTranslation<T>.TryReadOrphanedSubgroupWrappers(data, _package, out var rec))
        {
            return rec;
        }
        var stream = new OverlayStream(data, _package);
        return LoquiBinaryOverlayTranslation<T>.Create(
            stream: stream,
            package: _package,
            recordTypeConverter: null);
    }

    public static GroupMajorRecordCacheWrapper<T> Factory<TStream>(
        TStream stream,
        ReadOnlyMemorySlice<byte> data,
        BinaryOverlayFactoryPackage package,
        int offset)
        where TStream : IMutagenReadStream
    {
        Dictionary<FormKey, int> locationDict = new Dictionary<FormKey, int>();

        stream.Position -= package.MetaData.Constants.GroupConstants.HeaderLength;
        var groupMeta = stream.GetGroupHeader(package.MetaData);
        var finalPos = stream.Position + groupMeta.TotalLength;
        stream.Position += package.MetaData.Constants.GroupConstants.HeaderLength;

        FormKey? lastParsed = default;
        while (stream.Position < finalPos)
        {
            VariableHeader varMeta = package.MetaData.Constants.VariableHeader(stream.RemainingMemory);
            if (varMeta.TryGetAsGroup(out var groupHeader))
            {
                var formId = FormID.Factory(groupHeader.ContainedRecordTypeData.UInt32());
                var formKey = FormKey.Factory(package.MetaData.MasterReferences, formId, reference: false);
                if (formKey != lastParsed)
                {

                    try
                    {
                        locationDict.Add(formKey, checked((int)(stream.Position - offset)));
                    }
                    catch (ArgumentException)
                    {
                        throw new RecordCollisionException(
                            stream.MetaData.ModKey,
                            formKey,
                            typeof(T));
                    }
                }
                stream.Position += checked((int)varMeta.TotalLength);
                lastParsed = null;
            }
            else
            {
                MajorRecordHeader majorMeta = package.MetaData.Constants.MajorRecordHeader(stream.RemainingMemory);
                var formKey = FormKey.Factory(package.MetaData.MasterReferences, majorMeta.FormID, reference: false);
                if (majorMeta.RecordType != GroupRecordTypeGetter<T>.GRUP_RECORD_TYPE)
                {
                    throw new RecordException(formKey: formKey, recordType: null, modKey: package.MetaData.ModKey, edid: null, message: "Unexpected type encountered when parsing MajorRecord locations: " + majorMeta.RecordType);
                }

                locationDict[formKey] = checked((int)(stream.Position - offset));
                stream.Position += checked((int)majorMeta.TotalLength);
                lastParsed = formKey;
            }
        }

        return new GroupMajorRecordCacheWrapper<T>(
            locationDict,
            data,
            package);
    }
}

internal abstract class AGroupBinaryOverlay<TMajor> : PluginBinaryOverlay, IGroupGetter<TMajor>
    where TMajor : class, IMajorRecordGetter
{
    protected IReadOnlyCache<TMajor, FormKey> _recordCache = null!;
    private static readonly ILoquiRegistration _registration = LoquiRegistration.GetRegister(typeof(TMajor));

    public TMajor this[FormKey key] => _recordCache[key];
    IMajorRecordGetter IGroupGetter.this[FormKey key] => _recordCache[key];
    public IReadOnlyCache<TMajor, FormKey> RecordCache => _recordCache;
    public IMod SourceMod => throw new NotImplementedException();
    public IEnumerable<TMajor> Records => RecordCache.Items;
    public int Count => RecordCache.Count;
    public IEnumerable<FormKey> FormKeys => _recordCache.Keys;
    public IEnumerable<TMajor> Items => _recordCache.Items;
    IReadOnlyCache<IMajorRecordGetter, FormKey> IGroupGetter.RecordCache => _recordCache;
    IEnumerable<IMajorRecordGetter> IGroupGetter.Records => Records;
    IEnumerable<ILoquiObject> IGroupCommonGetter.Records => Records;
    public ILoquiRegistration ContainedRecordRegistration => _registration;
    public Type ContainedRecordType => typeof(TMajor);

    public abstract IEnumerable<IFormLinkGetter> EnumerateFormLinks(bool iterateNestedRecords = true);

    public abstract IEnumerable<IAssetLinkGetter> EnumerateAssetLinks(
        AssetLinkQuery queryCategories = AssetLinkQuery.Listed,
        IAssetLinkCache? linkCache = null,
        Type? assetType = null);

    public bool ContainsKey(FormKey key)
    {
        return _recordCache.ContainsKey(key);
    }

    public IEnumerator<TMajor> GetEnumerator()
    {
        return _recordCache.Items.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return _recordCache.GetEnumerator();
    }

    protected AGroupBinaryOverlay(
        PluginBinaryOverlay.MemoryPair memoryPair,
        BinaryOverlayFactoryPackage package)
        : base(memoryPair, package)
    {
    }
}