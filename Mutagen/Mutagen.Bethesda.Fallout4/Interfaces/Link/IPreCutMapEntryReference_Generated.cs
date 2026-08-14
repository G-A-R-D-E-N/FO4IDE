
using Mutagen.Bethesda;

namespace Mutagen.Bethesda.Fallout4
{

    public partial interface IPreCutMapEntryReference :
        IFallout4MajorRecordInternal,
        IPreCutMapEntryReferenceGetter
    {
    }

    public partial interface IPreCutMapEntryReferenceGetter : IFallout4MajorRecordGetter
    {
    }
}
