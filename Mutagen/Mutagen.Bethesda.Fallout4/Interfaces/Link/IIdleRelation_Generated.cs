
using Mutagen.Bethesda;

namespace Mutagen.Bethesda.Fallout4
{

    public partial interface IIdleRelation :
        IFallout4MajorRecordInternal,
        IIdleRelationGetter
    {
    }

    public partial interface IIdleRelationGetter : IFallout4MajorRecordGetter
    {
    }
}
