
using Mutagen.Bethesda;

namespace Mutagen.Bethesda.Fallout4
{

    public partial interface IPlacedThing :
        IFallout4MajorRecordInternal,
        IPlacedThingGetter
    {
    }

    public partial interface IPlacedThingGetter : IFallout4MajorRecordGetter
    {
    }
}
