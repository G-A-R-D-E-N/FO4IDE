using System.Collections;
using Loqui;
using Mutagen.Bethesda.Plugins.Assets;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Binary.Translations;
using Noggog;
using Noggog.StructuredStrings;

namespace Mutagen.Bethesda.Plugins.Records;

public interface IMergeableBlock : ILoquiObject, IBinaryItem, IFormLinkContainerGetter, IAssetLinkContainerGetter, IMajorRecordGetterEnumerable
{

    int BlockNumber { get; }
}

public class MergedListGroup<TBlock, TListGroup> : ILoquiObject, IListGroupGetter<TBlock>, IBinaryItem, IFormLinkContainerGetter, IAssetLinkContainerGetter, IMajorRecordGetterEnumerable
    where TBlock : class, ILoquiObject, IBinaryItem, IFormLinkContainerGetter, IAssetLinkContainerGetter, IMajorRecordGetterEnumerable
    where TListGroup : IEnumerable<TBlock>
{
    private readonly IEnumerable<TListGroup> _sourceGroups;
    private readonly Func<TBlock, int> _getBlockNumberFunc;
    private readonly Func<int, List<TBlock>, TBlock> _mergeBlocksFunc;
    private List<TBlock>? _cache;
    private readonly object _cacheLock = new object();

    public MergedListGroup(
        IEnumerable<TListGroup> sourceGroups,
        Func<TBlock, int> getBlockNumberFunc,
        Func<int, List<TBlock>, TBlock> mergeBlocksFunc)
    {
        _sourceGroups = sourceGroups;
        _getBlockNumberFunc = getBlockNumberFunc;
        _mergeBlocksFunc = mergeBlocksFunc;
    }

    private List<TBlock> Cache
    {
        get
        {
            if (_cache != null) return _cache;

            lock (_cacheLock)
            {
                if (_cache != null) return _cache;

                var blocksByNumber = new Dictionary<int, List<TBlock>>();

                foreach (var group in _sourceGroups)
                {
                    foreach (var block in group)
                    {
                        var blockNumber = _getBlockNumberFunc(block);
                        if (!blocksByNumber.ContainsKey(blockNumber))
                        {
                            blocksByNumber[blockNumber] = new List<TBlock>();
                        }
                        blocksByNumber[blockNumber].Add(block);
                    }
                }

                var result = new List<TBlock>();
                foreach (var blockNumber in blocksByNumber.Keys.OrderBy(k => k))
                {
                    var blocksForNumber = blocksByNumber[blockNumber];
                    if (blocksForNumber.Count == 1)
                    {
                        result.Add(blocksForNumber[0]);
                    }
                    else
                    {

                        result.Add(_mergeBlocksFunc(blockNumber, blocksForNumber));
                    }
                }

                _cache = result;
                return _cache;
            }
        }
    }

    public IEnumerator<TBlock> GetEnumerator() => Cache.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public int Count => Cache.Count;

    public TBlock this[int index] => Cache[index];

    public IReadOnlyList<TBlock> Records => Cache;
    IReadOnlyList<ILoquiObject> IListGroupGetter.Records => Cache;
    IEnumerable<TBlock> IGroupCommonGetter<TBlock>.Records => Cache;
    IEnumerable<ILoquiObject> IGroupCommonGetter.Records => Cache;

    IEnumerable<TBlock> IListGroupGetter<TBlock>.GetEnumerator() => Cache;

    ILoquiRegistration ILoquiObject.Registration => null!;

    public void Print(StructuredStringBuilder sb, string? name = null)
    {
        sb.AppendLine($"Merged List Group ({Count} blocks):");
        foreach (var block in Cache.Take(5))
        {
            sb.AppendLine($"  - Block {_getBlockNumberFunc(block)}");
        }
        if (Count > 5)
        {
            sb.AppendLine($"  ... and {Count - 5} more");
        }
    }

    public IEnumerable<IAssetLinkGetter> EnumerateAssetLinks(AssetLinkQuery queryCategories = AssetLinkQuery.Listed, IAssetLinkCache? linkCache = null, Type? assetType = null)
    {
        foreach (var block in Cache)
        {
            foreach (var link in block.EnumerateAssetLinks(queryCategories, linkCache, assetType))
            {
                yield return link;
            }
        }
    }

    void IBinaryItem.WriteToBinary(MutagenWriter writer, TypedWriteParams translationParams)
    {
        foreach (var block in Cache)
        {
            block.WriteToBinary(writer, translationParams);
        }
    }

    object IBinaryItem.BinaryWriteTranslator => this;

    public IEnumerable<IFormLinkGetter> EnumerateFormLinks(bool iterateNestedRecords = true)
    {
        foreach (var block in Cache)
        {
            foreach (var link in block.EnumerateFormLinks(iterateNestedRecords))
            {
                yield return link;
            }
        }
    }

    public ILoquiRegistration ContainedRecordRegistration
    {
        get
        {
            var firstGroup = _sourceGroups.FirstOrDefault();
            if (firstGroup is IGroupCommonGetter groupCommon)
            {
                return groupCommon.ContainedRecordRegistration;
            }
            throw new NotImplementedException("Source groups do not implement IGroupCommonGetter");
        }
    }
    public Type ContainedRecordType => typeof(TBlock);

    public IEnumerable<IMajorRecordGetter> EnumerateMajorRecords()
    {
        foreach (var block in Cache)
        {
            if (block is IMajorRecordGetterEnumerable enumerable)
            {
                foreach (var record in enumerable.EnumerateMajorRecords())
                {
                    yield return record;
                }
            }
        }
    }

    public IEnumerable<T> EnumerateMajorRecords<T>(bool throwIfUnknown = true)
        where T : class, IMajorRecordQueryableGetter
    {
        foreach (var block in Cache)
        {
            if (block is IMajorRecordGetterEnumerable enumerable)
            {
                foreach (var record in enumerable.EnumerateMajorRecords<T>(throwIfUnknown))
                {
                    yield return record;
                }
            }
        }
    }

    public IEnumerable<IMajorRecordGetter> EnumerateMajorRecords(Type type, bool throwIfUnknown = true)
    {
        foreach (var block in Cache)
        {
            if (block is IMajorRecordGetterEnumerable enumerable)
            {
                foreach (var record in enumerable.EnumerateMajorRecords(type, throwIfUnknown))
                {
                    yield return record;
                }
            }
        }
    }
}
