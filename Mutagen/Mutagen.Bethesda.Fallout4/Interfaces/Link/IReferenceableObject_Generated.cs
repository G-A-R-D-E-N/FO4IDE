




using Mutagen.Bethesda;

namespace Mutagen.Bethesda.Fallout4
{



    public partial interface IReferenceableObject :
        IFallout4MajorRecordInternal,
        IReferenceableObjectGetter
    {
    }




    public partial interface IReferenceableObjectGetter : IFallout4MajorRecordGetter
    {
    }
}
