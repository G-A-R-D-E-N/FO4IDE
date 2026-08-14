




using Mutagen.Bethesda;

namespace Mutagen.Bethesda.Fallout4
{



    public partial interface IBindableEquipment :
        IBindableEquipmentGetter,
        IFallout4MajorRecordInternal
    {
    }




    public partial interface IBindableEquipmentGetter : IFallout4MajorRecordGetter
    {
    }
}
