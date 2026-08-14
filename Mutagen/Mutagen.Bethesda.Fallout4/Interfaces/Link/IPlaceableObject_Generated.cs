




using Mutagen.Bethesda;

namespace Mutagen.Bethesda.Fallout4
{



    public partial interface IPlaceableObject :
        IFallout4MajorRecordInternal,
        IPlaceableObjectGetter
    {
    }




    public partial interface IPlaceableObjectGetter : IFallout4MajorRecordGetter
    {
    }
}
