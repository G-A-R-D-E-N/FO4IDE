using Mutagen.Bethesda.Plugins;

namespace Mutagen.Bethesda.Fallout4;




public interface IHarvestable : IFallout4MajorRecordInternal, IHarvestableGetter
{
    new IFormLinkNullable<IHarvestTargetGetter> Ingredient { get; }
    new IFormLinkNullable<ISoundDescriptorGetter> HarvestSound { get; }
}




public interface IHarvestableGetter : IFallout4MajorRecordGetter
{
    IFormLinkNullableGetter<IHarvestTargetGetter> Ingredient { get; }
    IFormLinkNullableGetter<ISoundDescriptorGetter> HarvestSound { get; }
}