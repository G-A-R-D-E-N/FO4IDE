using System.IO.Abstractions;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Strings;
using Mutagen.Bethesda.Plugins.Records;
using Noggog;

namespace Mutagen.Bethesda.Plugins.Binary.Parameters;




public delegate IReadOnlyCollection<ModKey> MastersContentCustomOverride(IReadOnlyCollection<ModKey> inputMasters);




public sealed record BinaryWriteParameters
{
    public static BinaryWriteParameters Default => new();




    public ModKeyOption ModKey { get; init; } = ModKeyOption.ThrowIfMisaligned;





    public MastersListContentOption MastersListContent { get; init; } = MastersListContentOption.Iterate;






    public MastersContentCustomOverride? MastersContentCustomOverride { get; init; }




    public RecordCountOption RecordCount { get; init; } = RecordCountOption.Iterate;





    public AMastersListOrderingOption? MastersListOrdering { get; init; }




    public NextFormIDOption NextFormID { get; init; } = NextFormIDOption.Iterate;




    public AMinimumFormIdOption MinimumFormID { get; init; } = new AutomaticLowerFormIdRangeOption();





    public FormIDUniquenessOption FormIDUniqueness { get; init; } = FormIDUniquenessOption.Iterate;





    public FormIDCompactionOption FormIDCompaction { get; init; } = FormIDCompactionOption.Iterate;




    public StringsWriter? StringsWriter { get; init; }





    public Language? TargetLanguageOverride { get; init; }




    public bool CleanNulls { get; init; } = true;




    public EncodingBundle? Encodings { get; init; }






    public ALowerRangeDisallowedHandlerOption LowerRangeDisallowedHandler { get; init; } = new AddPlaceholderMasterIfLowerRangeDisallowed();




    public IReadOnlyCache<IModMasterStyledGetter, ModKey>? MasterFlagsLookup { get; init; }




    public ParallelWriteParameters Parallel { get; init; } = new();




    public IFileSystem? FileSystem { get; init; }




    public OverriddenFormsOption OverriddenFormsOption { get; init; } = OverriddenFormsOption.NoCheck;











    public ModKey RunMasterMatch(IModGetter mod, FilePath path)
    {
        if (ModKey == ModKeyOption.NoCheck) return mod.ModKey;
        if (!Plugins.ModKey.TryFromNameAndExtension(path.Name, out var pathModKey))
        {
            throw new ArgumentException($"Could not convert path to a ModKey to compare against: {Path.GetFileName(path)}");
        }
        switch (ModKey)
        {
            case ModKeyOption.ThrowIfMisaligned:
                if (mod.ModKey != pathModKey)
                {
                    throw new ArgumentException($"ModKeys were misaligned: {mod.ModKey} != {pathModKey}.  " +
                                                $"Export to a file that matches the mod object's ModKey, or " +
                                                $"modify your {nameof(BinaryWriteParameters)}.{nameof(ModKey)} parameters " +
                                                $"to override this behavior.");
                }
                return mod.ModKey;
            case ModKeyOption.CorrectToPath:
                return pathModKey;
            default:
                throw new NotImplementedException();
        }
    }
}