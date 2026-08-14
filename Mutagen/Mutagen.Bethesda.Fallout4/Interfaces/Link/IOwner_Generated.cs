
using Mutagen.Bethesda;

namespace Mutagen.Bethesda.Fallout4
{

    public partial interface IOwner :
        IFallout4MajorRecordInternal,
        IOwnerGetter
    {
    }

    public partial interface IOwnerGetter : IFallout4MajorRecordGetter
    {
    }
}
