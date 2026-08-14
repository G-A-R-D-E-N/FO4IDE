
using Mutagen.Bethesda;

namespace Mutagen.Bethesda.Fallout4
{

    public partial interface IFurnitureAssociation :
        IFallout4MajorRecordInternal,
        IFurnitureAssociationGetter
    {
    }

    public partial interface IFurnitureAssociationGetter : IFallout4MajorRecordGetter
    {
    }
}
