
using Mutagen.Bethesda;

namespace Mutagen.Bethesda.Fallout4
{

    public partial interface ILockList :
        IFallout4MajorRecordInternal,
        ILockListGetter
    {
    }

    public partial interface ILockListGetter : IFallout4MajorRecordGetter
    {
    }
}
