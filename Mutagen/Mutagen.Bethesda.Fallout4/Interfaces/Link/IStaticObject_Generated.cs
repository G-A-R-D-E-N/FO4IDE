
using Mutagen.Bethesda;

namespace Mutagen.Bethesda.Fallout4
{

    public partial interface IStaticObject :
        IFallout4MajorRecordInternal,
        IStaticObjectGetter
    {
    }

    public partial interface IStaticObjectGetter : IFallout4MajorRecordGetter
    {
    }
}
