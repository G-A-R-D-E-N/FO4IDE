
using Mutagen.Bethesda;

namespace Mutagen.Bethesda.Fallout4
{

    public partial interface ILinkedReference :
        IFallout4MajorRecordInternal,
        ILinkedReferenceGetter
    {
    }

    public partial interface ILinkedReferenceGetter : IFallout4MajorRecordGetter
    {
    }
}
