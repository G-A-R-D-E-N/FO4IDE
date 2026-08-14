using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Cache.Internals.Implementations;

namespace Mutagen.Bethesda.Fallout4
{
    public static class LinkCacheMixIns
    {







        public static ImmutableModLinkCache<IFallout4Mod, IFallout4ModGetter> ToImmutableLinkCache(this IFallout4ModGetter mod)
        {
            return mod.ToImmutableLinkCache<IFallout4Mod, IFallout4ModGetter>();
        }








        public static MutableModLinkCache<IFallout4Mod, IFallout4ModGetter> ToMutableLinkCache(this IFallout4ModGetter mod)
        {
            return mod.ToMutableLinkCache<IFallout4Mod, IFallout4ModGetter>();
        }









        public static ImmutableLoadOrderLinkCache<IFallout4Mod, IFallout4ModGetter> ToImmutableLinkCache(this ILoadOrderGetter<IFallout4ModGetter> loadOrder)
        {
            return loadOrder.ToImmutableLinkCache<IFallout4Mod, IFallout4ModGetter>();
        }









        public static ImmutableLoadOrderLinkCache<IFallout4Mod, IFallout4ModGetter> ToImmutableLinkCache(this ILoadOrderGetter<IModListingGetter<IFallout4ModGetter>> loadOrder)
        {
            return loadOrder.ToImmutableLinkCache<IFallout4Mod, IFallout4ModGetter>();
        }









        public static ImmutableLoadOrderLinkCache<IFallout4Mod, IFallout4ModGetter> ToImmutableLinkCache(this IEnumerable<IModListingGetter<IFallout4ModGetter>> loadOrder)
        {
            return loadOrder.ToImmutableLinkCache<IFallout4Mod, IFallout4ModGetter>();
        }









        public static ImmutableLoadOrderLinkCache<IFallout4Mod, IFallout4ModGetter> ToImmutableLinkCache(this IEnumerable<IFallout4ModGetter> loadOrder)
        {
            return loadOrder.ToImmutableLinkCache<IFallout4Mod, IFallout4ModGetter>();
        }








        public static ILinkCache<IFallout4Mod, IFallout4ModGetter> ToMutableLinkCache(
            this ILoadOrderGetter<IFallout4ModGetter> immutableBaseCache,
            params IFallout4Mod[] mutableMods)
        {
            return immutableBaseCache.ToMutableLinkCache<IFallout4Mod, IFallout4ModGetter>(mutableMods);
        }








        public static ILinkCache<IFallout4Mod, IFallout4ModGetter> ToMutableLinkCache(
            this ILoadOrderGetter<IModListingGetter<IFallout4ModGetter>> immutableBaseCache,
            params IFallout4Mod[] mutableMods)
        {
            return immutableBaseCache.ToMutableLinkCache<IFallout4Mod, IFallout4ModGetter>(mutableMods);
        }








        public static ILinkCache<IFallout4Mod, IFallout4ModGetter> ToMutableLinkCache(
            this IEnumerable<IFallout4ModGetter> immutableBaseCache,
            params IFallout4Mod[] mutableMods)
        {
            return immutableBaseCache.ToMutableLinkCache<IFallout4Mod, IFallout4ModGetter>(mutableMods);
        }

    }
}
