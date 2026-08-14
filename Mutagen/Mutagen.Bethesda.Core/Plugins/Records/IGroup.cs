using Noggog;
using Loqui;
using Mutagen.Bethesda.Plugins.Assets;

namespace Mutagen.Bethesda.Plugins.Records;

public interface IGroupCommonGetter :
    IFormLinkContainerGetter,
    IAssetLinkContainerGetter
{

    int Count { get; }

    IEnumerable<ILoquiObject> Records { get; }

    ILoquiRegistration ContainedRecordRegistration { get; }

    Type ContainedRecordType { get; }
}

public interface IGroupGetter : IGroupCommonGetter
{

    IMod SourceMod { get; }

    IEnumerable<FormKey> FormKeys { get; }

    new IEnumerable<IMajorRecordGetter> Records { get; }

    IReadOnlyCache<IMajorRecordGetter, FormKey> RecordCache { get; }

    IMajorRecordGetter this[FormKey key] { get; }

    bool ContainsKey(FormKey key);
}

public interface IListGroupGetter : IGroupCommonGetter
{

    new int Count { get; }

    new IReadOnlyList<ILoquiObject> Records { get; }
}

public interface IGroupCommonGetter<out TObject> : IGroupCommonGetter, IReadOnlyCollection<TObject>
    where TObject : ILoquiObject
{

    new IEnumerable<TObject> Records { get; }

    new int Count { get; }
}

public interface IGroupGetter<out TMajor> : IGroupCommonGetter<TMajor>, IGroupGetter
    where TMajor : IMajorRecordGetter
{

    new IEnumerable<TMajor> Records { get; }

    new IReadOnlyCache<TMajor, FormKey> RecordCache { get; }

    new TMajor this[FormKey key] { get; }
}

public interface IListGroupGetter<out TObject> : IGroupCommonGetter<TObject>, IListGroupGetter, IReadOnlyList<TObject>
    where TObject : ILoquiObject
{

    new int Count { get; }

    new IEnumerable<TObject> GetEnumerator();

    new IReadOnlyList<TObject> Records { get; }
}

public interface IGroupCommon : IGroupCommonGetter, IAssetLinkContainer
{

    void AddUntyped(IMajorRecord record);
}

public interface IGroup : IGroupCommon, IGroupGetter
{

    new IEnumerable<IMajorRecord> Records { get; }

    void SetUntyped(IMajorRecord record);

    void SetUntyped(IEnumerable<IMajorRecord> records);
}

public interface IListGroup : IGroupCommon
{
}

public interface IGroupCommon<TObject> : IGroupCommonGetter<TObject>, IGroupCommon, IClearable
    where TObject : ILoquiObject
{

    new IEnumerable<TObject> Records { get; }
}

public interface IGroup<TMajor> : IGroupGetter<TMajor>, IGroup, IGroupCommon<TMajor>
    where TMajor : IMajorRecord
{

    new IEnumerable<IMajorRecord> Records { get; }

    new ICache<TMajor, FormKey> RecordCache { get; }

    void Add(TMajor record);

    TMajor AddReturn(TMajor record);

    void Set(TMajor record);

    void Set(IEnumerable<TMajor> records);

    bool Remove(FormKey key);

    void Remove(IEnumerable<FormKey> keys);

    new TMajor this[FormKey key] { get; }
}

public interface IListGroup<TObject> : IListGroupGetter<TObject>, IListGroup, IGroupCommon<TObject>, IExtendedList<TObject>
    where TObject : ILoquiObject
{

    new IExtendedList<TObject> Records { get; }

    new TObject this[int index] { get; set; }

    new int Count { get; }

    new void Clear();
}