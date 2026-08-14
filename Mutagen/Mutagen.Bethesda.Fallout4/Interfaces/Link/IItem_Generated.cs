




using Mutagen.Bethesda;

namespace Mutagen.Bethesda.Fallout4
{



    public partial interface IItem :
        IFallout4MajorRecordInternal,
        IItemGetter
    {
    }




    public partial interface IItemGetter : IFallout4MajorRecordGetter
    {
    }
}
