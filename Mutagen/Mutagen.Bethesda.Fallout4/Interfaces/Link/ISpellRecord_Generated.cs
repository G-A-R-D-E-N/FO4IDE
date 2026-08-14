




using Mutagen.Bethesda;

namespace Mutagen.Bethesda.Fallout4
{



    public partial interface ISpellRecord :
        IFallout4MajorRecordInternal,
        ISpellRecordGetter
    {
    }




    public partial interface ISpellRecordGetter : IFallout4MajorRecordGetter
    {
    }
}
