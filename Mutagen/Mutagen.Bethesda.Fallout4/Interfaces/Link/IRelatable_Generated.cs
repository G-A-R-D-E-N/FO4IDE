
using Mutagen.Bethesda;

namespace Mutagen.Bethesda.Fallout4
{

    public partial interface IRelatable :
        IFallout4MajorRecordInternal,
        IRelatableGetter
    {
    }

    public partial interface IRelatableGetter : IFallout4MajorRecordGetter
    {
    }
}
