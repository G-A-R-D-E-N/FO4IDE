




using Mutagen.Bethesda;

namespace Mutagen.Bethesda.Fallout4
{



    public partial interface IPlaced :
        IFallout4MajorRecordInternal,
        IPlacedGetter
    {
    }




    public partial interface IPlacedGetter : IFallout4MajorRecordGetter
    {
    }
}
