




using Mutagen.Bethesda;

namespace Mutagen.Bethesda.Fallout4
{



    public partial interface IEffectRecord :
        IEffectRecordGetter,
        IFallout4MajorRecordInternal
    {
    }




    public partial interface IEffectRecordGetter : IFallout4MajorRecordGetter
    {
    }
}
