
using Mutagen.Bethesda;

namespace Mutagen.Bethesda.Fallout4
{

    public partial interface IObjectId :
        IFallout4MajorRecordInternal,
        IObjectIdGetter
    {
    }

    public partial interface IObjectIdGetter : IFallout4MajorRecordGetter
    {
    }
}
