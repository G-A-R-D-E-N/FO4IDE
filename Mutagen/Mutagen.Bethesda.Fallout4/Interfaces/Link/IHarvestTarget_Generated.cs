




using Mutagen.Bethesda;

namespace Mutagen.Bethesda.Fallout4
{



    public partial interface IHarvestTarget :
        IFallout4MajorRecordInternal,
        IHarvestTargetGetter
    {
    }




    public partial interface IHarvestTargetGetter : IFallout4MajorRecordGetter
    {
    }
}
