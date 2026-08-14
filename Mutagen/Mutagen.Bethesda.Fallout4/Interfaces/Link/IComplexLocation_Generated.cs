




using Mutagen.Bethesda;

namespace Mutagen.Bethesda.Fallout4
{



    public partial interface IComplexLocation :
        IComplexLocationGetter,
        IFallout4MajorRecordInternal
    {
    }




    public partial interface IComplexLocationGetter : IFallout4MajorRecordGetter
    {
    }
}
