




using Mutagen.Bethesda;

namespace Mutagen.Bethesda.Fallout4
{



    public partial interface IStoryManagerItem :
        IFallout4MajorRecordInternal,
        IStoryManagerItemGetter
    {
    }




    public partial interface IStoryManagerItemGetter : IFallout4MajorRecordGetter
    {
    }
}
