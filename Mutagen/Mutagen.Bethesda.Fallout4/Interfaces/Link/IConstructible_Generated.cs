
using Mutagen.Bethesda;

namespace Mutagen.Bethesda.Fallout4
{

    public partial interface IConstructible :
        IConstructibleGetter,
        IFallout4MajorRecordInternal
    {
    }

    public partial interface IConstructibleGetter : IFallout4MajorRecordGetter
    {
    }
}
