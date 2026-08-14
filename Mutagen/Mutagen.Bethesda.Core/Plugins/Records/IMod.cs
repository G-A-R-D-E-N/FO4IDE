using Loqui;
using Mutagen.Bethesda.Plugins.Allocators;
using Noggog;
using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Plugins.Binary.Translations;

namespace Mutagen.Bethesda.Plugins.Records;

public interface IModGetter :
    IModFlagsGetter,
    IMajorRecordGetterEnumerable,
    IMajorRecordSimpleContextEnumerable,
    IFormLinkContainerGetter,
    IEqualsMask
{

    GameRelease GameRelease { get; }

    IReadOnlyList<IMasterReferenceGetter> MasterReferences { get; }

    IReadOnlyList<IFormLinkGetter<IMajorRecordGetter>>? OverriddenForms { get; }

    uint NextFormID { get; }

    IGroupGetter<TMajor>? TryGetTopLevelGroup<TMajor>() where TMajor : IMajorRecordGetter;

    IGroupGetter? TryGetTopLevelGroup(Type type);

    void WriteToBinary(FilePath path, BinaryWriteParameters? param = null);

    void WriteToBinary(Stream stream, BinaryWriteParameters? param = null);

    uint GetDefaultInitialNextFormID(bool? forceUseLowerFormIDRanges = false);

    IBinaryModdedWriteBuilderTargetChoice BeginWrite { get; }

    uint GetRecordCount();

    IMod DeepCopy();
}

public interface IMod : IModGetter, IMajorRecordEnumerable, IFormKeyAllocator, IFormLinkContainer
{

    new IList<MasterReference> MasterReferences { get; }

    new IGroup<TMajor>? TryGetTopLevelGroup<TMajor>() where TMajor : IMajorRecord;

    new IGroup? TryGetTopLevelGroup(Type type);

    new uint NextFormID { get; set; }

    new bool UsingLocalization { get; set; }

    new bool IsSmallMaster { get; set; }

    new bool IsMediumMaster { get; set; }

    new bool IsMaster { get; set; }

    TAlloc SetAllocator<TAlloc>(TAlloc allocator)
        where TAlloc : IFormKeyAllocator;
}

public interface IModDisposeGetter : IModGetter, IDisposable
{
}