
using Mutagen.Bethesda;

namespace Mutagen.Bethesda.Fallout4
{

    public partial interface ISound :
        IFallout4MajorRecordInternal,
        ISoundGetter
    {
    }

    public partial interface ISoundGetter : IFallout4MajorRecordGetter
    {
    }
}
