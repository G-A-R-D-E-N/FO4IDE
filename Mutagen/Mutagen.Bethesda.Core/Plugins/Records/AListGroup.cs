using System.Collections;
using System.Diagnostics;
using Loqui;
using Mutagen.Bethesda.Plugins.Assets;
using Noggog;

namespace Mutagen.Bethesda.Plugins.Records;

public abstract class AListGroup<TObject> : IListGroup<TObject>
    where TObject : class, ILoquiObject, IFormLinkContainer
{
    private static readonly ILoquiRegistration _registration = LoquiRegistration.GetRegister(typeof(TObject));

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    protected abstract IExtendedList<TObject> ProtectedList { get; }

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    internal IExtendedList<TObject> InternalCache => ProtectedList;

    IEnumerable<ILoquiObject> IGroupCommonGetter.Records => ProtectedList;
    IExtendedList<TObject> IListGroup<TObject>.Records => ProtectedList;
    IEnumerable<TObject> IGroupCommonGetter<TObject>.Records => ProtectedList;
    IEnumerable<TObject> IGroupCommon<TObject>.Records => ProtectedList;
    IReadOnlyList<ILoquiObject> IListGroupGetter.Records => ProtectedList;

    IReadOnlyList<TObject> IListGroupGetter<TObject>.Records => ProtectedList;

    public int Count => ProtectedList.Count;

    public bool Remove(TObject item) => ProtectedList.Remove(item);

    public bool IsReadOnly => false;

    IEnumerable<TObject> IListGroupGetter<TObject>.GetEnumerator() => ProtectedList;

    public TObject this[int index]
    {
        get => ProtectedList[index];
        set => ProtectedList[index] = value;
    }


    public ILoquiRegistration ContainedRecordRegistration => _registration;


    public Type ContainedRecordType => typeof(TObject);


    public void AddUntyped(IMajorRecord record) => Add(ConfirmCorrectType(record, nameof(record)));

    private TObject ConfirmCorrectType(IMajorRecord record, string paramName)
    {
        if (record == null) throw new ArgumentNullException(paramName);
        if (record is not TObject cast)
        {
            throw new ArgumentException(
                $"A record was added of the wrong type.  Expected {typeof(TObject)}, but was given {record.GetType()}",
                paramName);
        }

        return cast;
    }


    public int IndexOf(TObject item) => ProtectedList.IndexOf(item);


    public void Insert(int index, TObject item) => ProtectedList.Insert(index, item);


    public void RemoveAt(int index) => ProtectedList.RemoveAt(index);


    public void AddRange(IEnumerable<TObject> collection) => ProtectedList.AddRange(collection);


    public void InsertRange(IEnumerable<TObject> collection, int index) => ProtectedList.InsertRange(collection, index);


    public void RemoveRange(int index, int count) => ProtectedList.RemoveRange(index, count);


    public void Move(int original, int destination) => ProtectedList.Move(original, destination);


    public abstract void RemapListedAssetLinks(IReadOnlyDictionary<IAssetLinkGetter, string> mapping);


    public abstract void RemapAssetLinks(IReadOnlyDictionary<IAssetLinkGetter, string> mapping, AssetLinkQuery query, IAssetLinkCache? linkCache);


    public abstract IEnumerable<IAssetLink> EnumerateListedAssetLinks();


    public void Add(TObject item) => ProtectedList.Add(item);


    public bool Contains(TObject item) => ProtectedList.Contains(item);


    public void CopyTo(TObject[] array, int arrayIndex) => ProtectedList.CopyTo(array, arrayIndex);


    public void Clear() => InternalCache.Clear();

    IEnumerator<TObject> IEnumerable<TObject>.GetEnumerator() => ProtectedList.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => ProtectedList.GetEnumerator();


    public abstract IEnumerable<IFormLinkGetter> EnumerateFormLinks(bool iterateNestedRecords = true);


    public abstract IEnumerable<IAssetLinkGetter> EnumerateAssetLinks(AssetLinkQuery queryCategories,
        IAssetLinkCache? linkCache = null,
        Type? assetType = null);
}