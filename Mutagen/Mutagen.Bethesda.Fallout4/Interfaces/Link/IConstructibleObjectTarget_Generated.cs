
using Mutagen.Bethesda;

namespace Mutagen.Bethesda.Fallout4
{

    public partial interface IConstructibleObjectTarget :
        IConstructibleObjectTargetGetter,
        IFallout4MajorRecordInternal
    {
    }

    public partial interface IConstructibleObjectTargetGetter : IFallout4MajorRecordGetter
    {
    }
}
