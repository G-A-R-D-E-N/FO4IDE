using System.Collections.Generic;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Order;

namespace Mutagen.Bethesda.Fallout4
{
    public static class TypeOptionSolidifierMixIns
    {
        #region Normal

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IAcousticSpace, IAcousticSpaceGetter> AcousticSpace(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IAcousticSpace, IAcousticSpaceGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IAcousticSpaceGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IAcousticSpace, IAcousticSpaceGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IAcousticSpace, IAcousticSpaceGetter> AcousticSpace(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IAcousticSpace, IAcousticSpaceGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IAcousticSpaceGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IAcousticSpace, IAcousticSpaceGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IActionRecord, IActionRecordGetter> ActionRecord(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IActionRecord, IActionRecordGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IActionRecordGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IActionRecord, IActionRecordGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IActionRecord, IActionRecordGetter> ActionRecord(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IActionRecord, IActionRecordGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IActionRecordGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IActionRecord, IActionRecordGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IActivator, IActivatorGetter> Activator(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IActivator, IActivatorGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IActivatorGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IActivator, IActivatorGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IActivator, IActivatorGetter> Activator(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IActivator, IActivatorGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IActivatorGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IActivator, IActivatorGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IActorValueInformation, IActorValueInformationGetter> ActorValueInformation(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IActorValueInformation, IActorValueInformationGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IActorValueInformationGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IActorValueInformation, IActorValueInformationGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IActorValueInformation, IActorValueInformationGetter> ActorValueInformation(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IActorValueInformation, IActorValueInformationGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IActorValueInformationGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IActorValueInformation, IActorValueInformationGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IADamageType, IADamageTypeGetter> ADamageType(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IADamageType, IADamageTypeGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IADamageTypeGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IADamageType, IADamageTypeGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IADamageType, IADamageTypeGetter> ADamageType(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IADamageType, IADamageTypeGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IADamageTypeGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IADamageType, IADamageTypeGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IAddonNode, IAddonNodeGetter> AddonNode(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IAddonNode, IAddonNodeGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IAddonNodeGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IAddonNode, IAddonNodeGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IAddonNode, IAddonNodeGetter> AddonNode(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IAddonNode, IAddonNodeGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IAddonNodeGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IAddonNode, IAddonNodeGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IAimModel, IAimModelGetter> AimModel(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IAimModel, IAimModelGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IAimModelGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IAimModel, IAimModelGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IAimModel, IAimModelGetter> AimModel(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IAimModel, IAimModelGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IAimModelGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IAimModel, IAimModelGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IAmmunition, IAmmunitionGetter> Ammunition(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IAmmunition, IAmmunitionGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IAmmunitionGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IAmmunition, IAmmunitionGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IAmmunition, IAmmunitionGetter> Ammunition(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IAmmunition, IAmmunitionGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IAmmunitionGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IAmmunition, IAmmunitionGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IAnimatedObject, IAnimatedObjectGetter> AnimatedObject(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IAnimatedObject, IAnimatedObjectGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IAnimatedObjectGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IAnimatedObject, IAnimatedObjectGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IAnimatedObject, IAnimatedObjectGetter> AnimatedObject(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IAnimatedObject, IAnimatedObjectGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IAnimatedObjectGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IAnimatedObject, IAnimatedObjectGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IAnimationSoundTagSet, IAnimationSoundTagSetGetter> AnimationSoundTagSet(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IAnimationSoundTagSet, IAnimationSoundTagSetGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IAnimationSoundTagSetGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IAnimationSoundTagSet, IAnimationSoundTagSetGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IAnimationSoundTagSet, IAnimationSoundTagSetGetter> AnimationSoundTagSet(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IAnimationSoundTagSet, IAnimationSoundTagSetGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IAnimationSoundTagSetGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IAnimationSoundTagSet, IAnimationSoundTagSetGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IAObjectModification, IAObjectModificationGetter> AObjectModification(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IAObjectModification, IAObjectModificationGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IAObjectModificationGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IAObjectModification, IAObjectModificationGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IAObjectModification, IAObjectModificationGetter> AObjectModification(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IAObjectModification, IAObjectModificationGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IAObjectModificationGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IAObjectModification, IAObjectModificationGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IAPlacedTrap, IAPlacedTrapGetter> APlacedTrap(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IAPlacedTrap, IAPlacedTrapGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IAPlacedTrapGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IAPlacedTrap, IAPlacedTrapGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IAPlacedTrap, IAPlacedTrapGetter> APlacedTrap(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IAPlacedTrap, IAPlacedTrapGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IAPlacedTrapGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IAPlacedTrap, IAPlacedTrapGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IArmor, IArmorGetter> Armor(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IArmor, IArmorGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IArmorGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IArmor, IArmorGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IArmor, IArmorGetter> Armor(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IArmor, IArmorGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IArmorGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IArmor, IArmorGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IArmorAddon, IArmorAddonGetter> ArmorAddon(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IArmorAddon, IArmorAddonGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IArmorAddonGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IArmorAddon, IArmorAddonGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IArmorAddon, IArmorAddonGetter> ArmorAddon(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IArmorAddon, IArmorAddonGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IArmorAddonGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IArmorAddon, IArmorAddonGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IArtObject, IArtObjectGetter> ArtObject(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IArtObject, IArtObjectGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IArtObjectGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IArtObject, IArtObjectGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IArtObject, IArtObjectGetter> ArtObject(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IArtObject, IArtObjectGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IArtObjectGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IArtObject, IArtObjectGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IAssociationType, IAssociationTypeGetter> AssociationType(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IAssociationType, IAssociationTypeGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IAssociationTypeGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IAssociationType, IAssociationTypeGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IAssociationType, IAssociationTypeGetter> AssociationType(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IAssociationType, IAssociationTypeGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IAssociationTypeGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IAssociationType, IAssociationTypeGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IAStoryManagerNode, IAStoryManagerNodeGetter> AStoryManagerNode(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IAStoryManagerNode, IAStoryManagerNodeGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IAStoryManagerNodeGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IAStoryManagerNode, IAStoryManagerNodeGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IAStoryManagerNode, IAStoryManagerNodeGetter> AStoryManagerNode(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IAStoryManagerNode, IAStoryManagerNodeGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IAStoryManagerNodeGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IAStoryManagerNode, IAStoryManagerNodeGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IAttractionRule, IAttractionRuleGetter> AttractionRule(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IAttractionRule, IAttractionRuleGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IAttractionRuleGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IAttractionRule, IAttractionRuleGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IAttractionRule, IAttractionRuleGetter> AttractionRule(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IAttractionRule, IAttractionRuleGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IAttractionRuleGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IAttractionRule, IAttractionRuleGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IAudioCategorySnapshot, IAudioCategorySnapshotGetter> AudioCategorySnapshot(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IAudioCategorySnapshot, IAudioCategorySnapshotGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IAudioCategorySnapshotGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IAudioCategorySnapshot, IAudioCategorySnapshotGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IAudioCategorySnapshot, IAudioCategorySnapshotGetter> AudioCategorySnapshot(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IAudioCategorySnapshot, IAudioCategorySnapshotGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IAudioCategorySnapshotGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IAudioCategorySnapshot, IAudioCategorySnapshotGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IAudioEffectChain, IAudioEffectChainGetter> AudioEffectChain(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IAudioEffectChain, IAudioEffectChainGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IAudioEffectChainGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IAudioEffectChain, IAudioEffectChainGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IAudioEffectChain, IAudioEffectChainGetter> AudioEffectChain(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IAudioEffectChain, IAudioEffectChainGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IAudioEffectChainGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IAudioEffectChain, IAudioEffectChainGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IBendableSpline, IBendableSplineGetter> BendableSpline(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IBendableSpline, IBendableSplineGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IBendableSplineGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IBendableSpline, IBendableSplineGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IBendableSpline, IBendableSplineGetter> BendableSpline(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IBendableSpline, IBendableSplineGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IBendableSplineGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IBendableSpline, IBendableSplineGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IBodyPartData, IBodyPartDataGetter> BodyPartData(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IBodyPartData, IBodyPartDataGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IBodyPartDataGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IBodyPartData, IBodyPartDataGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IBodyPartData, IBodyPartDataGetter> BodyPartData(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IBodyPartData, IBodyPartDataGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IBodyPartDataGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IBodyPartData, IBodyPartDataGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IBook, IBookGetter> Book(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IBook, IBookGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IBookGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IBook, IBookGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IBook, IBookGetter> Book(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IBook, IBookGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IBookGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IBook, IBookGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ICameraPath, ICameraPathGetter> CameraPath(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ICameraPath, ICameraPathGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<ICameraPathGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, ICameraPath, ICameraPathGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ICameraPath, ICameraPathGetter> CameraPath(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ICameraPath, ICameraPathGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<ICameraPathGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, ICameraPath, ICameraPathGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ICameraShot, ICameraShotGetter> CameraShot(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ICameraShot, ICameraShotGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<ICameraShotGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, ICameraShot, ICameraShotGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ICameraShot, ICameraShotGetter> CameraShot(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ICameraShot, ICameraShotGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<ICameraShotGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, ICameraShot, ICameraShotGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ICell, ICellGetter> Cell(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ICell, ICellGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<ICellGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, ICell, ICellGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ICell, ICellGetter> Cell(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ICell, ICellGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<ICellGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, ICell, ICellGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IClass, IClassGetter> Class(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IClass, IClassGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IClassGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IClass, IClassGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IClass, IClassGetter> Class(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IClass, IClassGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IClassGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IClass, IClassGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IClimate, IClimateGetter> Climate(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IClimate, IClimateGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IClimateGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IClimate, IClimateGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IClimate, IClimateGetter> Climate(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IClimate, IClimateGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IClimateGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IClimate, IClimateGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ICollisionLayer, ICollisionLayerGetter> CollisionLayer(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ICollisionLayer, ICollisionLayerGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<ICollisionLayerGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, ICollisionLayer, ICollisionLayerGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ICollisionLayer, ICollisionLayerGetter> CollisionLayer(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ICollisionLayer, ICollisionLayerGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<ICollisionLayerGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, ICollisionLayer, ICollisionLayerGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IColorRecord, IColorRecordGetter> ColorRecord(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IColorRecord, IColorRecordGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IColorRecordGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IColorRecord, IColorRecordGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IColorRecord, IColorRecordGetter> ColorRecord(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IColorRecord, IColorRecordGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IColorRecordGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IColorRecord, IColorRecordGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ICombatStyle, ICombatStyleGetter> CombatStyle(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ICombatStyle, ICombatStyleGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<ICombatStyleGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, ICombatStyle, ICombatStyleGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ICombatStyle, ICombatStyleGetter> CombatStyle(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ICombatStyle, ICombatStyleGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<ICombatStyleGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, ICombatStyle, ICombatStyleGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IComponent, IComponentGetter> Component(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IComponent, IComponentGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IComponentGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IComponent, IComponentGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IComponent, IComponentGetter> Component(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IComponent, IComponentGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IComponentGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IComponent, IComponentGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IConstructibleObject, IConstructibleObjectGetter> ConstructibleObject(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IConstructibleObject, IConstructibleObjectGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IConstructibleObjectGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IConstructibleObject, IConstructibleObjectGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IConstructibleObject, IConstructibleObjectGetter> ConstructibleObject(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IConstructibleObject, IConstructibleObjectGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IConstructibleObjectGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IConstructibleObject, IConstructibleObjectGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IContainer, IContainerGetter> Container(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IContainer, IContainerGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IContainerGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IContainer, IContainerGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IContainer, IContainerGetter> Container(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IContainer, IContainerGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IContainerGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IContainer, IContainerGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IDebris, IDebrisGetter> Debris(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IDebris, IDebrisGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IDebrisGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IDebris, IDebrisGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IDebris, IDebrisGetter> Debris(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IDebris, IDebrisGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IDebrisGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IDebris, IDebrisGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IDefaultObject, IDefaultObjectGetter> DefaultObject(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IDefaultObject, IDefaultObjectGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IDefaultObjectGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IDefaultObject, IDefaultObjectGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IDefaultObject, IDefaultObjectGetter> DefaultObject(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IDefaultObject, IDefaultObjectGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IDefaultObjectGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IDefaultObject, IDefaultObjectGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IDefaultObjectManager, IDefaultObjectManagerGetter> DefaultObjectManager(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IDefaultObjectManager, IDefaultObjectManagerGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IDefaultObjectManagerGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IDefaultObjectManager, IDefaultObjectManagerGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IDefaultObjectManager, IDefaultObjectManagerGetter> DefaultObjectManager(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IDefaultObjectManager, IDefaultObjectManagerGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IDefaultObjectManagerGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IDefaultObjectManager, IDefaultObjectManagerGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IDialogBranch, IDialogBranchGetter> DialogBranch(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IDialogBranch, IDialogBranchGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IDialogBranchGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IDialogBranch, IDialogBranchGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IDialogBranch, IDialogBranchGetter> DialogBranch(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IDialogBranch, IDialogBranchGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IDialogBranchGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IDialogBranch, IDialogBranchGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IDialogResponses, IDialogResponsesGetter> DialogResponses(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IDialogResponses, IDialogResponsesGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IDialogResponsesGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IDialogResponses, IDialogResponsesGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IDialogResponses, IDialogResponsesGetter> DialogResponses(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IDialogResponses, IDialogResponsesGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IDialogResponsesGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IDialogResponses, IDialogResponsesGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IDialogTopic, IDialogTopicGetter> DialogTopic(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IDialogTopic, IDialogTopicGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IDialogTopicGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IDialogTopic, IDialogTopicGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IDialogTopic, IDialogTopicGetter> DialogTopic(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IDialogTopic, IDialogTopicGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IDialogTopicGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IDialogTopic, IDialogTopicGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IDialogView, IDialogViewGetter> DialogView(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IDialogView, IDialogViewGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IDialogViewGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IDialogView, IDialogViewGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IDialogView, IDialogViewGetter> DialogView(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IDialogView, IDialogViewGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IDialogViewGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IDialogView, IDialogViewGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IDoor, IDoorGetter> Door(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IDoor, IDoorGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IDoorGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IDoor, IDoorGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IDoor, IDoorGetter> Door(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IDoor, IDoorGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IDoorGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IDoor, IDoorGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IDualCastData, IDualCastDataGetter> DualCastData(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IDualCastData, IDualCastDataGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IDualCastDataGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IDualCastData, IDualCastDataGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IDualCastData, IDualCastDataGetter> DualCastData(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IDualCastData, IDualCastDataGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IDualCastDataGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IDualCastData, IDualCastDataGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IEffectShader, IEffectShaderGetter> EffectShader(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IEffectShader, IEffectShaderGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IEffectShaderGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IEffectShader, IEffectShaderGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IEffectShader, IEffectShaderGetter> EffectShader(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IEffectShader, IEffectShaderGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IEffectShaderGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IEffectShader, IEffectShaderGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IEncounterZone, IEncounterZoneGetter> EncounterZone(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IEncounterZone, IEncounterZoneGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IEncounterZoneGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IEncounterZone, IEncounterZoneGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IEncounterZone, IEncounterZoneGetter> EncounterZone(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IEncounterZone, IEncounterZoneGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IEncounterZoneGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IEncounterZone, IEncounterZoneGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IEquipType, IEquipTypeGetter> EquipType(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IEquipType, IEquipTypeGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IEquipTypeGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IEquipType, IEquipTypeGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IEquipType, IEquipTypeGetter> EquipType(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IEquipType, IEquipTypeGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IEquipTypeGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IEquipType, IEquipTypeGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IExplosion, IExplosionGetter> Explosion(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IExplosion, IExplosionGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IExplosionGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IExplosion, IExplosionGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IExplosion, IExplosionGetter> Explosion(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IExplosion, IExplosionGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IExplosionGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IExplosion, IExplosionGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IFaction, IFactionGetter> Faction(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IFaction, IFactionGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IFactionGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IFaction, IFactionGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IFaction, IFactionGetter> Faction(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IFaction, IFactionGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IFactionGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IFaction, IFactionGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IFallout4MajorRecord, IFallout4MajorRecordGetter> Fallout4MajorRecord(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IFallout4MajorRecord, IFallout4MajorRecordGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IFallout4MajorRecordGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IFallout4MajorRecord, IFallout4MajorRecordGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IFallout4MajorRecord, IFallout4MajorRecordGetter> Fallout4MajorRecord(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IFallout4MajorRecord, IFallout4MajorRecordGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IFallout4MajorRecordGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IFallout4MajorRecord, IFallout4MajorRecordGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IFlora, IFloraGetter> Flora(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IFlora, IFloraGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IFloraGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IFlora, IFloraGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IFlora, IFloraGetter> Flora(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IFlora, IFloraGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IFloraGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IFlora, IFloraGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IFootstep, IFootstepGetter> Footstep(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IFootstep, IFootstepGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IFootstepGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IFootstep, IFootstepGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IFootstep, IFootstepGetter> Footstep(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IFootstep, IFootstepGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IFootstepGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IFootstep, IFootstepGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IFootstepSet, IFootstepSetGetter> FootstepSet(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IFootstepSet, IFootstepSetGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IFootstepSetGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IFootstepSet, IFootstepSetGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IFootstepSet, IFootstepSetGetter> FootstepSet(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IFootstepSet, IFootstepSetGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IFootstepSetGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IFootstepSet, IFootstepSetGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IFormList, IFormListGetter> FormList(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IFormList, IFormListGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IFormListGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IFormList, IFormListGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IFormList, IFormListGetter> FormList(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IFormList, IFormListGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IFormListGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IFormList, IFormListGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IFurniture, IFurnitureGetter> Furniture(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IFurniture, IFurnitureGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IFurnitureGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IFurniture, IFurnitureGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IFurniture, IFurnitureGetter> Furniture(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IFurniture, IFurnitureGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IFurnitureGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IFurniture, IFurnitureGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IGameSetting, IGameSettingGetter> GameSetting(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IGameSetting, IGameSettingGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IGameSettingGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IGameSetting, IGameSettingGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IGameSetting, IGameSettingGetter> GameSetting(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IGameSetting, IGameSettingGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IGameSettingGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IGameSetting, IGameSettingGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IGlobal, IGlobalGetter> Global(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IGlobal, IGlobalGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IGlobalGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IGlobal, IGlobalGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IGlobal, IGlobalGetter> Global(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IGlobal, IGlobalGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IGlobalGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IGlobal, IGlobalGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IGodRays, IGodRaysGetter> GodRays(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IGodRays, IGodRaysGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IGodRaysGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IGodRays, IGodRaysGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IGodRays, IGodRaysGetter> GodRays(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IGodRays, IGodRaysGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IGodRaysGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IGodRays, IGodRaysGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IGrass, IGrassGetter> Grass(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IGrass, IGrassGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IGrassGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IGrass, IGrassGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IGrass, IGrassGetter> Grass(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IGrass, IGrassGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IGrassGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IGrass, IGrassGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IHazard, IHazardGetter> Hazard(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IHazard, IHazardGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IHazardGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IHazard, IHazardGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IHazard, IHazardGetter> Hazard(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IHazard, IHazardGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IHazardGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IHazard, IHazardGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IHeadPart, IHeadPartGetter> HeadPart(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IHeadPart, IHeadPartGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IHeadPartGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IHeadPart, IHeadPartGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IHeadPart, IHeadPartGetter> HeadPart(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IHeadPart, IHeadPartGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IHeadPartGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IHeadPart, IHeadPartGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IHolotape, IHolotapeGetter> Holotape(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IHolotape, IHolotapeGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IHolotapeGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IHolotape, IHolotapeGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IHolotape, IHolotapeGetter> Holotape(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IHolotape, IHolotapeGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IHolotapeGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IHolotape, IHolotapeGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IIdleAnimation, IIdleAnimationGetter> IdleAnimation(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IIdleAnimation, IIdleAnimationGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IIdleAnimationGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IIdleAnimation, IIdleAnimationGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IIdleAnimation, IIdleAnimationGetter> IdleAnimation(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IIdleAnimation, IIdleAnimationGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IIdleAnimationGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IIdleAnimation, IIdleAnimationGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IIdleMarker, IIdleMarkerGetter> IdleMarker(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IIdleMarker, IIdleMarkerGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IIdleMarkerGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IIdleMarker, IIdleMarkerGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IIdleMarker, IIdleMarkerGetter> IdleMarker(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IIdleMarker, IIdleMarkerGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IIdleMarkerGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IIdleMarker, IIdleMarkerGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IImageSpace, IImageSpaceGetter> ImageSpace(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IImageSpace, IImageSpaceGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IImageSpaceGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IImageSpace, IImageSpaceGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IImageSpace, IImageSpaceGetter> ImageSpace(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IImageSpace, IImageSpaceGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IImageSpaceGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IImageSpace, IImageSpaceGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IImageSpaceAdapter, IImageSpaceAdapterGetter> ImageSpaceAdapter(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IImageSpaceAdapter, IImageSpaceAdapterGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IImageSpaceAdapterGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IImageSpaceAdapter, IImageSpaceAdapterGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IImageSpaceAdapter, IImageSpaceAdapterGetter> ImageSpaceAdapter(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IImageSpaceAdapter, IImageSpaceAdapterGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IImageSpaceAdapterGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IImageSpaceAdapter, IImageSpaceAdapterGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IImpact, IImpactGetter> Impact(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IImpact, IImpactGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IImpactGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IImpact, IImpactGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IImpact, IImpactGetter> Impact(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IImpact, IImpactGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IImpactGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IImpact, IImpactGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IImpactDataSet, IImpactDataSetGetter> ImpactDataSet(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IImpactDataSet, IImpactDataSetGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IImpactDataSetGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IImpactDataSet, IImpactDataSetGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IImpactDataSet, IImpactDataSetGetter> ImpactDataSet(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IImpactDataSet, IImpactDataSetGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IImpactDataSetGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IImpactDataSet, IImpactDataSetGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IIngestible, IIngestibleGetter> Ingestible(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IIngestible, IIngestibleGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IIngestibleGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IIngestible, IIngestibleGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IIngestible, IIngestibleGetter> Ingestible(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IIngestible, IIngestibleGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IIngestibleGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IIngestible, IIngestibleGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IIngredient, IIngredientGetter> Ingredient(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IIngredient, IIngredientGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IIngredientGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IIngredient, IIngredientGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IIngredient, IIngredientGetter> Ingredient(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IIngredient, IIngredientGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IIngredientGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IIngredient, IIngredientGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IInstanceNamingRules, IInstanceNamingRulesGetter> InstanceNamingRules(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IInstanceNamingRules, IInstanceNamingRulesGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IInstanceNamingRulesGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IInstanceNamingRules, IInstanceNamingRulesGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IInstanceNamingRules, IInstanceNamingRulesGetter> InstanceNamingRules(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IInstanceNamingRules, IInstanceNamingRulesGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IInstanceNamingRulesGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IInstanceNamingRules, IInstanceNamingRulesGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IKey, IKeyGetter> Key(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IKey, IKeyGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IKeyGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IKey, IKeyGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IKey, IKeyGetter> Key(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IKey, IKeyGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IKeyGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IKey, IKeyGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IKeyword, IKeywordGetter> Keyword(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IKeyword, IKeywordGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IKeywordGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IKeyword, IKeywordGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IKeyword, IKeywordGetter> Keyword(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IKeyword, IKeywordGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IKeywordGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IKeyword, IKeywordGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ILandscape, ILandscapeGetter> Landscape(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ILandscape, ILandscapeGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<ILandscapeGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, ILandscape, ILandscapeGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ILandscape, ILandscapeGetter> Landscape(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ILandscape, ILandscapeGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<ILandscapeGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, ILandscape, ILandscapeGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ILandscapeTexture, ILandscapeTextureGetter> LandscapeTexture(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ILandscapeTexture, ILandscapeTextureGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<ILandscapeTextureGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, ILandscapeTexture, ILandscapeTextureGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ILandscapeTexture, ILandscapeTextureGetter> LandscapeTexture(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ILandscapeTexture, ILandscapeTextureGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<ILandscapeTextureGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, ILandscapeTexture, ILandscapeTextureGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ILayer, ILayerGetter> Layer(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ILayer, ILayerGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<ILayerGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, ILayer, ILayerGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ILayer, ILayerGetter> Layer(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ILayer, ILayerGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<ILayerGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, ILayer, ILayerGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ILensFlare, ILensFlareGetter> LensFlare(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ILensFlare, ILensFlareGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<ILensFlareGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, ILensFlare, ILensFlareGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ILensFlare, ILensFlareGetter> LensFlare(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ILensFlare, ILensFlareGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<ILensFlareGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, ILensFlare, ILensFlareGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ILeveledItem, ILeveledItemGetter> LeveledItem(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ILeveledItem, ILeveledItemGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<ILeveledItemGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, ILeveledItem, ILeveledItemGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ILeveledItem, ILeveledItemGetter> LeveledItem(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ILeveledItem, ILeveledItemGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<ILeveledItemGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, ILeveledItem, ILeveledItemGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ILeveledNpc, ILeveledNpcGetter> LeveledNpc(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ILeveledNpc, ILeveledNpcGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<ILeveledNpcGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, ILeveledNpc, ILeveledNpcGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ILeveledNpc, ILeveledNpcGetter> LeveledNpc(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ILeveledNpc, ILeveledNpcGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<ILeveledNpcGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, ILeveledNpc, ILeveledNpcGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ILight, ILightGetter> Light(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ILight, ILightGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<ILightGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, ILight, ILightGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ILight, ILightGetter> Light(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ILight, ILightGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<ILightGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, ILight, ILightGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ILightingTemplate, ILightingTemplateGetter> LightingTemplate(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ILightingTemplate, ILightingTemplateGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<ILightingTemplateGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, ILightingTemplate, ILightingTemplateGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ILightingTemplate, ILightingTemplateGetter> LightingTemplate(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ILightingTemplate, ILightingTemplateGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<ILightingTemplateGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, ILightingTemplate, ILightingTemplateGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ILoadScreen, ILoadScreenGetter> LoadScreen(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ILoadScreen, ILoadScreenGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<ILoadScreenGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, ILoadScreen, ILoadScreenGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ILoadScreen, ILoadScreenGetter> LoadScreen(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ILoadScreen, ILoadScreenGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<ILoadScreenGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, ILoadScreen, ILoadScreenGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ILocation, ILocationGetter> Location(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ILocation, ILocationGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<ILocationGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, ILocation, ILocationGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ILocation, ILocationGetter> Location(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ILocation, ILocationGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<ILocationGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, ILocation, ILocationGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ILocationReferenceType, ILocationReferenceTypeGetter> LocationReferenceType(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ILocationReferenceType, ILocationReferenceTypeGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<ILocationReferenceTypeGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, ILocationReferenceType, ILocationReferenceTypeGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ILocationReferenceType, ILocationReferenceTypeGetter> LocationReferenceType(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ILocationReferenceType, ILocationReferenceTypeGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<ILocationReferenceTypeGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, ILocationReferenceType, ILocationReferenceTypeGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IMagicEffect, IMagicEffectGetter> MagicEffect(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IMagicEffect, IMagicEffectGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IMagicEffectGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IMagicEffect, IMagicEffectGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IMagicEffect, IMagicEffectGetter> MagicEffect(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IMagicEffect, IMagicEffectGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IMagicEffectGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IMagicEffect, IMagicEffectGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IMaterialObject, IMaterialObjectGetter> MaterialObject(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IMaterialObject, IMaterialObjectGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IMaterialObjectGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IMaterialObject, IMaterialObjectGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IMaterialObject, IMaterialObjectGetter> MaterialObject(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IMaterialObject, IMaterialObjectGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IMaterialObjectGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IMaterialObject, IMaterialObjectGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IMaterialSwap, IMaterialSwapGetter> MaterialSwap(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IMaterialSwap, IMaterialSwapGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IMaterialSwapGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IMaterialSwap, IMaterialSwapGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IMaterialSwap, IMaterialSwapGetter> MaterialSwap(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IMaterialSwap, IMaterialSwapGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IMaterialSwapGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IMaterialSwap, IMaterialSwapGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IMaterialType, IMaterialTypeGetter> MaterialType(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IMaterialType, IMaterialTypeGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IMaterialTypeGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IMaterialType, IMaterialTypeGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IMaterialType, IMaterialTypeGetter> MaterialType(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IMaterialType, IMaterialTypeGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IMaterialTypeGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IMaterialType, IMaterialTypeGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IMessage, IMessageGetter> Message(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IMessage, IMessageGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IMessageGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IMessage, IMessageGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IMessage, IMessageGetter> Message(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IMessage, IMessageGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IMessageGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IMessage, IMessageGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IMiscItem, IMiscItemGetter> MiscItem(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IMiscItem, IMiscItemGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IMiscItemGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IMiscItem, IMiscItemGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IMiscItem, IMiscItemGetter> MiscItem(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IMiscItem, IMiscItemGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IMiscItemGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IMiscItem, IMiscItemGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IMovableStatic, IMovableStaticGetter> MovableStatic(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IMovableStatic, IMovableStaticGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IMovableStaticGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IMovableStatic, IMovableStaticGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IMovableStatic, IMovableStaticGetter> MovableStatic(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IMovableStatic, IMovableStaticGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IMovableStaticGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IMovableStatic, IMovableStaticGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IMovementType, IMovementTypeGetter> MovementType(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IMovementType, IMovementTypeGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IMovementTypeGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IMovementType, IMovementTypeGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IMovementType, IMovementTypeGetter> MovementType(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IMovementType, IMovementTypeGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IMovementTypeGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IMovementType, IMovementTypeGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IMusicTrack, IMusicTrackGetter> MusicTrack(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IMusicTrack, IMusicTrackGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IMusicTrackGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IMusicTrack, IMusicTrackGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IMusicTrack, IMusicTrackGetter> MusicTrack(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IMusicTrack, IMusicTrackGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IMusicTrackGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IMusicTrack, IMusicTrackGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IMusicType, IMusicTypeGetter> MusicType(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IMusicType, IMusicTypeGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IMusicTypeGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IMusicType, IMusicTypeGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IMusicType, IMusicTypeGetter> MusicType(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IMusicType, IMusicTypeGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IMusicTypeGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IMusicType, IMusicTypeGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, INavigationMesh, INavigationMeshGetter> NavigationMesh(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, INavigationMesh, INavigationMeshGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<INavigationMeshGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, INavigationMesh, INavigationMeshGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, INavigationMesh, INavigationMeshGetter> NavigationMesh(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, INavigationMesh, INavigationMeshGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<INavigationMeshGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, INavigationMesh, INavigationMeshGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, INavigationMeshInfoMap, INavigationMeshInfoMapGetter> NavigationMeshInfoMap(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, INavigationMeshInfoMap, INavigationMeshInfoMapGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<INavigationMeshInfoMapGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, INavigationMeshInfoMap, INavigationMeshInfoMapGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, INavigationMeshInfoMap, INavigationMeshInfoMapGetter> NavigationMeshInfoMap(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, INavigationMeshInfoMap, INavigationMeshInfoMapGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<INavigationMeshInfoMapGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, INavigationMeshInfoMap, INavigationMeshInfoMapGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, INavigationMeshObstacleManager, INavigationMeshObstacleManagerGetter> NavigationMeshObstacleManager(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, INavigationMeshObstacleManager, INavigationMeshObstacleManagerGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<INavigationMeshObstacleManagerGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, INavigationMeshObstacleManager, INavigationMeshObstacleManagerGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, INavigationMeshObstacleManager, INavigationMeshObstacleManagerGetter> NavigationMeshObstacleManager(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, INavigationMeshObstacleManager, INavigationMeshObstacleManagerGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<INavigationMeshObstacleManagerGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, INavigationMeshObstacleManager, INavigationMeshObstacleManagerGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, INpc, INpcGetter> Npc(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, INpc, INpcGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<INpcGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, INpc, INpcGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, INpc, INpcGetter> Npc(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, INpc, INpcGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<INpcGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, INpc, INpcGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IObjectEffect, IObjectEffectGetter> ObjectEffect(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IObjectEffect, IObjectEffectGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IObjectEffectGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IObjectEffect, IObjectEffectGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IObjectEffect, IObjectEffectGetter> ObjectEffect(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IObjectEffect, IObjectEffectGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IObjectEffectGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IObjectEffect, IObjectEffectGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IObjectVisibilityManager, IObjectVisibilityManagerGetter> ObjectVisibilityManager(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IObjectVisibilityManager, IObjectVisibilityManagerGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IObjectVisibilityManagerGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IObjectVisibilityManager, IObjectVisibilityManagerGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IObjectVisibilityManager, IObjectVisibilityManagerGetter> ObjectVisibilityManager(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IObjectVisibilityManager, IObjectVisibilityManagerGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IObjectVisibilityManagerGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IObjectVisibilityManager, IObjectVisibilityManagerGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IOutfit, IOutfitGetter> Outfit(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IOutfit, IOutfitGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IOutfitGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IOutfit, IOutfitGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IOutfit, IOutfitGetter> Outfit(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IOutfit, IOutfitGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IOutfitGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IOutfit, IOutfitGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IPackage, IPackageGetter> Package(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IPackage, IPackageGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IPackageGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IPackage, IPackageGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IPackage, IPackageGetter> Package(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IPackage, IPackageGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IPackageGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IPackage, IPackageGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IPackIn, IPackInGetter> PackIn(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IPackIn, IPackInGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IPackInGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IPackIn, IPackInGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IPackIn, IPackInGetter> PackIn(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IPackIn, IPackInGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IPackInGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IPackIn, IPackInGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IPerk, IPerkGetter> Perk(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IPerk, IPerkGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IPerkGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IPerk, IPerkGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IPerk, IPerkGetter> Perk(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IPerk, IPerkGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IPerkGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IPerk, IPerkGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IPlacedNpc, IPlacedNpcGetter> PlacedNpc(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IPlacedNpc, IPlacedNpcGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IPlacedNpcGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IPlacedNpc, IPlacedNpcGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IPlacedNpc, IPlacedNpcGetter> PlacedNpc(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IPlacedNpc, IPlacedNpcGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IPlacedNpcGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IPlacedNpc, IPlacedNpcGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IPlacedObject, IPlacedObjectGetter> PlacedObject(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IPlacedObject, IPlacedObjectGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IPlacedObjectGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IPlacedObject, IPlacedObjectGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IPlacedObject, IPlacedObjectGetter> PlacedObject(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IPlacedObject, IPlacedObjectGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IPlacedObjectGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IPlacedObject, IPlacedObjectGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IProjectile, IProjectileGetter> Projectile(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IProjectile, IProjectileGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IProjectileGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IProjectile, IProjectileGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IProjectile, IProjectileGetter> Projectile(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IProjectile, IProjectileGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IProjectileGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IProjectile, IProjectileGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IQuest, IQuestGetter> Quest(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IQuest, IQuestGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IQuestGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IQuest, IQuestGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IQuest, IQuestGetter> Quest(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IQuest, IQuestGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IQuestGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IQuest, IQuestGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IRace, IRaceGetter> Race(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IRace, IRaceGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IRaceGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IRace, IRaceGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IRace, IRaceGetter> Race(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IRace, IRaceGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IRaceGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IRace, IRaceGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IReferenceGroup, IReferenceGroupGetter> ReferenceGroup(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IReferenceGroup, IReferenceGroupGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IReferenceGroupGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IReferenceGroup, IReferenceGroupGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IReferenceGroup, IReferenceGroupGetter> ReferenceGroup(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IReferenceGroup, IReferenceGroupGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IReferenceGroupGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IReferenceGroup, IReferenceGroupGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IRegion, IRegionGetter> Region(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IRegion, IRegionGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IRegionGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IRegion, IRegionGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IRegion, IRegionGetter> Region(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IRegion, IRegionGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IRegionGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IRegion, IRegionGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IRelationship, IRelationshipGetter> Relationship(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IRelationship, IRelationshipGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IRelationshipGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IRelationship, IRelationshipGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IRelationship, IRelationshipGetter> Relationship(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IRelationship, IRelationshipGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IRelationshipGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IRelationship, IRelationshipGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IReverbParameters, IReverbParametersGetter> ReverbParameters(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IReverbParameters, IReverbParametersGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IReverbParametersGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IReverbParameters, IReverbParametersGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IReverbParameters, IReverbParametersGetter> ReverbParameters(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IReverbParameters, IReverbParametersGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IReverbParametersGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IReverbParameters, IReverbParametersGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IScene, ISceneGetter> Scene(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IScene, ISceneGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<ISceneGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IScene, ISceneGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IScene, ISceneGetter> Scene(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IScene, ISceneGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<ISceneGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IScene, ISceneGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ISceneCollection, ISceneCollectionGetter> SceneCollection(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ISceneCollection, ISceneCollectionGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<ISceneCollectionGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, ISceneCollection, ISceneCollectionGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ISceneCollection, ISceneCollectionGetter> SceneCollection(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ISceneCollection, ISceneCollectionGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<ISceneCollectionGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, ISceneCollection, ISceneCollectionGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IShaderParticleGeometry, IShaderParticleGeometryGetter> ShaderParticleGeometry(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IShaderParticleGeometry, IShaderParticleGeometryGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IShaderParticleGeometryGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IShaderParticleGeometry, IShaderParticleGeometryGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IShaderParticleGeometry, IShaderParticleGeometryGetter> ShaderParticleGeometry(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IShaderParticleGeometry, IShaderParticleGeometryGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IShaderParticleGeometryGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IShaderParticleGeometry, IShaderParticleGeometryGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ISoundCategory, ISoundCategoryGetter> SoundCategory(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ISoundCategory, ISoundCategoryGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<ISoundCategoryGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, ISoundCategory, ISoundCategoryGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ISoundCategory, ISoundCategoryGetter> SoundCategory(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ISoundCategory, ISoundCategoryGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<ISoundCategoryGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, ISoundCategory, ISoundCategoryGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ISoundDescriptor, ISoundDescriptorGetter> SoundDescriptor(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ISoundDescriptor, ISoundDescriptorGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<ISoundDescriptorGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, ISoundDescriptor, ISoundDescriptorGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ISoundDescriptor, ISoundDescriptorGetter> SoundDescriptor(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ISoundDescriptor, ISoundDescriptorGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<ISoundDescriptorGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, ISoundDescriptor, ISoundDescriptorGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ISoundKeywordMapping, ISoundKeywordMappingGetter> SoundKeywordMapping(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ISoundKeywordMapping, ISoundKeywordMappingGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<ISoundKeywordMappingGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, ISoundKeywordMapping, ISoundKeywordMappingGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ISoundKeywordMapping, ISoundKeywordMappingGetter> SoundKeywordMapping(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ISoundKeywordMapping, ISoundKeywordMappingGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<ISoundKeywordMappingGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, ISoundKeywordMapping, ISoundKeywordMappingGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ISoundMarker, ISoundMarkerGetter> SoundMarker(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ISoundMarker, ISoundMarkerGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<ISoundMarkerGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, ISoundMarker, ISoundMarkerGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ISoundMarker, ISoundMarkerGetter> SoundMarker(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ISoundMarker, ISoundMarkerGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<ISoundMarkerGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, ISoundMarker, ISoundMarkerGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ISoundOutputModel, ISoundOutputModelGetter> SoundOutputModel(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ISoundOutputModel, ISoundOutputModelGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<ISoundOutputModelGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, ISoundOutputModel, ISoundOutputModelGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ISoundOutputModel, ISoundOutputModelGetter> SoundOutputModel(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ISoundOutputModel, ISoundOutputModelGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<ISoundOutputModelGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, ISoundOutputModel, ISoundOutputModelGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ISpell, ISpellGetter> Spell(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ISpell, ISpellGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<ISpellGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, ISpell, ISpellGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ISpell, ISpellGetter> Spell(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ISpell, ISpellGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<ISpellGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, ISpell, ISpellGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IStatic, IStaticGetter> Static(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IStatic, IStaticGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IStaticGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IStatic, IStaticGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IStatic, IStaticGetter> Static(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IStatic, IStaticGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IStaticGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IStatic, IStaticGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IStaticCollection, IStaticCollectionGetter> StaticCollection(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IStaticCollection, IStaticCollectionGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IStaticCollectionGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IStaticCollection, IStaticCollectionGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IStaticCollection, IStaticCollectionGetter> StaticCollection(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IStaticCollection, IStaticCollectionGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IStaticCollectionGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IStaticCollection, IStaticCollectionGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ITalkingActivator, ITalkingActivatorGetter> TalkingActivator(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ITalkingActivator, ITalkingActivatorGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<ITalkingActivatorGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, ITalkingActivator, ITalkingActivatorGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ITalkingActivator, ITalkingActivatorGetter> TalkingActivator(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ITalkingActivator, ITalkingActivatorGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<ITalkingActivatorGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, ITalkingActivator, ITalkingActivatorGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ITerminal, ITerminalGetter> Terminal(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ITerminal, ITerminalGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<ITerminalGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, ITerminal, ITerminalGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ITerminal, ITerminalGetter> Terminal(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ITerminal, ITerminalGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<ITerminalGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, ITerminal, ITerminalGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ITextureSet, ITextureSetGetter> TextureSet(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ITextureSet, ITextureSetGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<ITextureSetGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, ITextureSet, ITextureSetGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ITextureSet, ITextureSetGetter> TextureSet(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ITextureSet, ITextureSetGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<ITextureSetGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, ITextureSet, ITextureSetGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ITransform, ITransformGetter> Transform(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ITransform, ITransformGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<ITransformGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, ITransform, ITransformGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ITransform, ITransformGetter> Transform(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ITransform, ITransformGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<ITransformGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, ITransform, ITransformGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ITree, ITreeGetter> Tree(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ITree, ITreeGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<ITreeGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, ITree, ITreeGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ITree, ITreeGetter> Tree(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ITree, ITreeGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<ITreeGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, ITree, ITreeGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IVisualEffect, IVisualEffectGetter> VisualEffect(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IVisualEffect, IVisualEffectGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IVisualEffectGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IVisualEffect, IVisualEffectGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IVisualEffect, IVisualEffectGetter> VisualEffect(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IVisualEffect, IVisualEffectGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IVisualEffectGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IVisualEffect, IVisualEffectGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IVoiceType, IVoiceTypeGetter> VoiceType(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IVoiceType, IVoiceTypeGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IVoiceTypeGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IVoiceType, IVoiceTypeGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IVoiceType, IVoiceTypeGetter> VoiceType(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IVoiceType, IVoiceTypeGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IVoiceTypeGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IVoiceType, IVoiceTypeGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IWater, IWaterGetter> Water(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IWater, IWaterGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IWaterGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IWater, IWaterGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IWater, IWaterGetter> Water(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IWater, IWaterGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IWaterGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IWater, IWaterGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IWeapon, IWeaponGetter> Weapon(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IWeapon, IWeaponGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IWeaponGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IWeapon, IWeaponGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IWeapon, IWeaponGetter> Weapon(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IWeapon, IWeaponGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IWeaponGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IWeapon, IWeaponGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IWeather, IWeatherGetter> Weather(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IWeather, IWeatherGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IWeatherGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IWeather, IWeatherGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IWeather, IWeatherGetter> Weather(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IWeather, IWeatherGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IWeatherGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IWeather, IWeatherGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IWorldspace, IWorldspaceGetter> Worldspace(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IWorldspace, IWorldspaceGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IWorldspaceGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IWorldspace, IWorldspaceGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IWorldspace, IWorldspaceGetter> Worldspace(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IWorldspace, IWorldspaceGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IWorldspaceGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IWorldspace, IWorldspaceGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IZoom, IZoomGetter> Zoom(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IZoom, IZoomGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IZoomGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IZoom, IZoomGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IZoom, IZoomGetter> Zoom(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TopLevelTypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IZoom, IZoomGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IZoomGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IZoom, IZoomGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        #endregion

        #region Link Interfaces

        public static TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IAliasVoiceType, IAliasVoiceTypeGetter> IAliasVoiceType(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IAliasVoiceType, IAliasVoiceTypeGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IAliasVoiceTypeGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IAliasVoiceType, IAliasVoiceTypeGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IAliasVoiceType, IAliasVoiceTypeGetter> IAliasVoiceType(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IAliasVoiceType, IAliasVoiceTypeGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IAliasVoiceTypeGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IAliasVoiceType, IAliasVoiceTypeGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IBindableEquipment, IBindableEquipmentGetter> IBindableEquipment(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IBindableEquipment, IBindableEquipmentGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IBindableEquipmentGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IBindableEquipment, IBindableEquipmentGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IBindableEquipment, IBindableEquipmentGetter> IBindableEquipment(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IBindableEquipment, IBindableEquipmentGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IBindableEquipmentGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IBindableEquipment, IBindableEquipmentGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IComplexLocation, IComplexLocationGetter> IComplexLocation(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IComplexLocation, IComplexLocationGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IComplexLocationGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IComplexLocation, IComplexLocationGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IComplexLocation, IComplexLocationGetter> IComplexLocation(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IComplexLocation, IComplexLocationGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IComplexLocationGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IComplexLocation, IComplexLocationGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IConstructible, IConstructibleGetter> IConstructible(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IConstructible, IConstructibleGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IConstructibleGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IConstructible, IConstructibleGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IConstructible, IConstructibleGetter> IConstructible(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IConstructible, IConstructibleGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IConstructibleGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IConstructible, IConstructibleGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IConstructibleObjectTarget, IConstructibleObjectTargetGetter> IConstructibleObjectTarget(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IConstructibleObjectTarget, IConstructibleObjectTargetGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IConstructibleObjectTargetGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IConstructibleObjectTarget, IConstructibleObjectTargetGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IConstructibleObjectTarget, IConstructibleObjectTargetGetter> IConstructibleObjectTarget(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IConstructibleObjectTarget, IConstructibleObjectTargetGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IConstructibleObjectTargetGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IConstructibleObjectTarget, IConstructibleObjectTargetGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IEffectRecord, IEffectRecordGetter> IEffectRecord(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IEffectRecord, IEffectRecordGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IEffectRecordGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IEffectRecord, IEffectRecordGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IEffectRecord, IEffectRecordGetter> IEffectRecord(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IEffectRecord, IEffectRecordGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IEffectRecordGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IEffectRecord, IEffectRecordGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IEmittance, IEmittanceGetter> IEmittance(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IEmittance, IEmittanceGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IEmittanceGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IEmittance, IEmittanceGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IEmittance, IEmittanceGetter> IEmittance(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IEmittance, IEmittanceGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IEmittanceGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IEmittance, IEmittanceGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IExplodeSpawn, IExplodeSpawnGetter> IExplodeSpawn(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IExplodeSpawn, IExplodeSpawnGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IExplodeSpawnGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IExplodeSpawn, IExplodeSpawnGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IExplodeSpawn, IExplodeSpawnGetter> IExplodeSpawn(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IExplodeSpawn, IExplodeSpawnGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IExplodeSpawnGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IExplodeSpawn, IExplodeSpawnGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IFurnitureAssociation, IFurnitureAssociationGetter> IFurnitureAssociation(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IFurnitureAssociation, IFurnitureAssociationGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IFurnitureAssociationGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IFurnitureAssociation, IFurnitureAssociationGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IFurnitureAssociation, IFurnitureAssociationGetter> IFurnitureAssociation(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IFurnitureAssociation, IFurnitureAssociationGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IFurnitureAssociationGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IFurnitureAssociation, IFurnitureAssociationGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IHarvestTarget, IHarvestTargetGetter> IHarvestTarget(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IHarvestTarget, IHarvestTargetGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IHarvestTargetGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IHarvestTarget, IHarvestTargetGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IHarvestTarget, IHarvestTargetGetter> IHarvestTarget(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IHarvestTarget, IHarvestTargetGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IHarvestTargetGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IHarvestTarget, IHarvestTargetGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IIdleRelation, IIdleRelationGetter> IIdleRelation(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IIdleRelation, IIdleRelationGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IIdleRelationGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IIdleRelation, IIdleRelationGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IIdleRelation, IIdleRelationGetter> IIdleRelation(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IIdleRelation, IIdleRelationGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IIdleRelationGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IIdleRelation, IIdleRelationGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IItem, IItemGetter> IItem(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IItem, IItemGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IItemGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IItem, IItemGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IItem, IItemGetter> IItem(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IItem, IItemGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IItemGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IItem, IItemGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IKeywordLinkedReference, IKeywordLinkedReferenceGetter> IKeywordLinkedReference(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IKeywordLinkedReference, IKeywordLinkedReferenceGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IKeywordLinkedReferenceGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IKeywordLinkedReference, IKeywordLinkedReferenceGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IKeywordLinkedReference, IKeywordLinkedReferenceGetter> IKeywordLinkedReference(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IKeywordLinkedReference, IKeywordLinkedReferenceGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IKeywordLinkedReferenceGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IKeywordLinkedReference, IKeywordLinkedReferenceGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ILockList, ILockListGetter> ILockList(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ILockList, ILockListGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<ILockListGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, ILockList, ILockListGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ILockList, ILockListGetter> ILockList(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ILockList, ILockListGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<ILockListGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, ILockList, ILockListGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, INpcSpawn, INpcSpawnGetter> INpcSpawn(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, INpcSpawn, INpcSpawnGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<INpcSpawnGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, INpcSpawn, INpcSpawnGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, INpcSpawn, INpcSpawnGetter> INpcSpawn(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, INpcSpawn, INpcSpawnGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<INpcSpawnGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, INpcSpawn, INpcSpawnGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IObjectId, IObjectIdGetter> IObjectId(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IObjectId, IObjectIdGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IObjectIdGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IObjectId, IObjectIdGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IObjectId, IObjectIdGetter> IObjectId(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IObjectId, IObjectIdGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IObjectIdGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IObjectId, IObjectIdGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IOutfitTarget, IOutfitTargetGetter> IOutfitTarget(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IOutfitTarget, IOutfitTargetGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IOutfitTargetGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IOutfitTarget, IOutfitTargetGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IOutfitTarget, IOutfitTargetGetter> IOutfitTarget(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IOutfitTarget, IOutfitTargetGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IOutfitTargetGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IOutfitTarget, IOutfitTargetGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IOwner, IOwnerGetter> IOwner(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IOwner, IOwnerGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IOwnerGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IOwner, IOwnerGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IOwner, IOwnerGetter> IOwner(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IOwner, IOwnerGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IOwnerGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IOwner, IOwnerGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IPlaceableObject, IPlaceableObjectGetter> IPlaceableObject(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IPlaceableObject, IPlaceableObjectGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IPlaceableObjectGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IPlaceableObject, IPlaceableObjectGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IPlaceableObject, IPlaceableObjectGetter> IPlaceableObject(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IPlaceableObject, IPlaceableObjectGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IPlaceableObjectGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IPlaceableObject, IPlaceableObjectGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IPlaced, IPlacedGetter> IPlaced(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IPlaced, IPlacedGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IPlacedGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IPlaced, IPlacedGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IPlaced, IPlacedGetter> IPlaced(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IPlaced, IPlacedGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IPlacedGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IPlaced, IPlacedGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IPlacedSimple, IPlacedSimpleGetter> IPlacedSimple(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IPlacedSimple, IPlacedSimpleGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IPlacedSimpleGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IPlacedSimple, IPlacedSimpleGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IPlacedSimple, IPlacedSimpleGetter> IPlacedSimple(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IPlacedSimple, IPlacedSimpleGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IPlacedSimpleGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IPlacedSimple, IPlacedSimpleGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IPlacedThing, IPlacedThingGetter> IPlacedThing(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IPlacedThing, IPlacedThingGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IPlacedThingGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IPlacedThing, IPlacedThingGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IPlacedThing, IPlacedThingGetter> IPlacedThing(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IPlacedThing, IPlacedThingGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IPlacedThingGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IPlacedThing, IPlacedThingGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IPlacedTrapTarget, IPlacedTrapTargetGetter> IPlacedTrapTarget(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IPlacedTrapTarget, IPlacedTrapTargetGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IPlacedTrapTargetGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IPlacedTrapTarget, IPlacedTrapTargetGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IPlacedTrapTarget, IPlacedTrapTargetGetter> IPlacedTrapTarget(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IPlacedTrapTarget, IPlacedTrapTargetGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IPlacedTrapTargetGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IPlacedTrapTarget, IPlacedTrapTargetGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IPreCutMapEntryReference, IPreCutMapEntryReferenceGetter> IPreCutMapEntryReference(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IPreCutMapEntryReference, IPreCutMapEntryReferenceGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IPreCutMapEntryReferenceGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IPreCutMapEntryReference, IPreCutMapEntryReferenceGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IPreCutMapEntryReference, IPreCutMapEntryReferenceGetter> IPreCutMapEntryReference(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IPreCutMapEntryReference, IPreCutMapEntryReferenceGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IPreCutMapEntryReferenceGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IPreCutMapEntryReference, IPreCutMapEntryReferenceGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IReferenceableObject, IReferenceableObjectGetter> IReferenceableObject(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IReferenceableObject, IReferenceableObjectGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IReferenceableObjectGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IReferenceableObject, IReferenceableObjectGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IReferenceableObject, IReferenceableObjectGetter> IReferenceableObject(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IReferenceableObject, IReferenceableObjectGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IReferenceableObjectGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IReferenceableObject, IReferenceableObjectGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IRegionTarget, IRegionTargetGetter> IRegionTarget(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IRegionTarget, IRegionTargetGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IRegionTargetGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IRegionTarget, IRegionTargetGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IRegionTarget, IRegionTargetGetter> IRegionTarget(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IRegionTarget, IRegionTargetGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IRegionTargetGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IRegionTarget, IRegionTargetGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IRelatable, IRelatableGetter> IRelatable(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IRelatable, IRelatableGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IRelatableGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IRelatable, IRelatableGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IRelatable, IRelatableGetter> IRelatable(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IRelatable, IRelatableGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IRelatableGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IRelatable, IRelatableGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ISound, ISoundGetter> ISound(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ISound, ISoundGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<ISoundGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, ISound, ISoundGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ISound, ISoundGetter> ISound(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ISound, ISoundGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<ISoundGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, ISound, ISoundGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ISpellRecord, ISpellRecordGetter> ISpellRecord(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ISpellRecord, ISpellRecordGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<ISpellRecordGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, ISpellRecord, ISpellRecordGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ISpellRecord, ISpellRecordGetter> ISpellRecord(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, ISpellRecord, ISpellRecordGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<ISpellRecordGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, ISpellRecord, ISpellRecordGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IStaticObject, IStaticObjectGetter> IStaticObject(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IStaticObject, IStaticObjectGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IStaticObjectGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IStaticObject, IStaticObjectGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IStaticObject, IStaticObjectGetter> IStaticObject(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IStaticObject, IStaticObjectGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IStaticObjectGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IStaticObject, IStaticObjectGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IStaticTarget, IStaticTargetGetter> IStaticTarget(this IEnumerable<IModListingGetter<IFallout4ModGetter>> listings)
        {
            return new TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IStaticTarget, IStaticTargetGetter>(
                (bool includeDeletedRecords) => listings.WinningOverrides<IStaticTargetGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => listings.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IStaticTarget, IStaticTargetGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        public static TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IStaticTarget, IStaticTargetGetter> IStaticTarget(this IEnumerable<IFallout4ModGetter> mods)
        {
            return new TypedLoadOrderAccess<IFallout4Mod, IFallout4ModGetter, IStaticTarget, IStaticTargetGetter>(
                (bool includeDeletedRecords) => mods.WinningOverrides<IStaticTargetGetter>(includeDeletedRecords: includeDeletedRecords),
                (ILinkCache linkCache, bool includeDeletedRecords) => mods.WinningContextOverrides<IFallout4Mod, IFallout4ModGetter, IStaticTarget, IStaticTargetGetter>(linkCache, includeDeletedRecords: includeDeletedRecords));
        }

        #endregion

    }
}
