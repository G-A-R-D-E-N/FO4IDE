using Mutagen.Bethesda.Plugins.Order;
using Noggog;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Cache.Internals.Implementations;
using Mutagen.Bethesda.Plugins.Records;

namespace Mutagen.Bethesda;

public static class LinkCacheConstructionMixIn
{








    public static ImmutableModLinkCache ToUntypedImmutableLinkCache(
        this IModGetter mod,
        LinkCachePreferences? prefs = null)
    {
        return new ImmutableModLinkCache(mod, prefs ?? LinkCachePreferences.Default);
    }










    public static ImmutableLoadOrderLinkCache ToUntypedImmutableLinkCache<TMod>(
        this ILoadOrderGetter<TMod> loadOrder,
        LinkCachePreferences? prefs = null)
        where TMod : class, IModGetter
    {
        return new ImmutableLoadOrderLinkCache(
            loadOrder.ListedOrder,
            GameCategoryHelper.TryFromModType<TMod>(), prefs ?? LinkCachePreferences.Default);
    }











    public static ImmutableLoadOrderLinkCache ToUntypedImmutableLinkCache(
        this ILoadOrderGetter<IModGetter> loadOrder,
        GameCategory category,
        LinkCachePreferences? prefs = null)
    {
        return new ImmutableLoadOrderLinkCache(
            loadOrder.ListedOrder.Select(x =>
            {
                if (x.GameRelease.ToCategory() != category)
                {
                    throw new ArgumentException($"Supplied a mod that was not of specified category. {x.GameRelease.ToCategory()} != {category}", nameof(loadOrder));
                }

                return x;
            }),
            category,
            prefs ?? LinkCachePreferences.Default);
    }










    public static ImmutableLoadOrderLinkCache ToUntypedImmutableLinkCache<TMod>(
        this ILoadOrderGetter<IModListingGetter<TMod>> loadOrder,
        LinkCachePreferences? prefs = null)
        where TMod : class, IModGetter
    {
        return new ImmutableLoadOrderLinkCache(
            loadOrder.ListedOrder
                .Select(listing => listing.Mod)
                .WhereNotNull(),
            GameCategoryHelper.TryFromModType<TMod>(),
            prefs ?? LinkCachePreferences.Default);
    }











    public static ImmutableLoadOrderLinkCache ToUntypedImmutableLinkCache(
        this ILoadOrderGetter<IModListingGetter<IModGetter>> loadOrder,
        GameCategory category,
        LinkCachePreferences? prefs = null)
    {
        return new ImmutableLoadOrderLinkCache(
            loadOrder.ListedOrder
                .Select(listing => listing.Mod)
                .WhereNotNull()
                .Select(x =>
                {
                    if (x.GameRelease.ToCategory() != category)
                    {
                        throw new ArgumentException($"Supplied a mod that was not of specified category. {x.GameRelease.ToCategory()} != {category}", nameof(loadOrder));
                    }

                    return x;
                }),
            category,
            prefs ?? LinkCachePreferences.Default);
    }










    public static ImmutableLoadOrderLinkCache ToUntypedImmutableLinkCache<TMod>(
        this IEnumerable<IModListingGetter<TMod>> loadOrder,
        LinkCachePreferences? prefs = null)
        where TMod : class, IModGetter
    {
        return new ImmutableLoadOrderLinkCache(
            loadOrder
                .Select(listing => listing.Mod)
                .WhereNotNull(),
            GameCategoryHelper.TryFromModType<TMod>(),
            prefs ?? LinkCachePreferences.Default);
    }











    public static ImmutableLoadOrderLinkCache ToUntypedImmutableLinkCache(
        this IEnumerable<IModListingGetter<IModGetter>> loadOrder,
        GameCategory category,
        LinkCachePreferences? prefs = null)
    {
        return new ImmutableLoadOrderLinkCache(
            loadOrder
                .Select(listing => listing.Mod)
                .WhereNotNull()
                .Select(x =>
                {
                    if (x.GameRelease.ToCategory() != category)
                    {
                        throw new ArgumentException($"Supplied a mod that was not of specified category. {x.GameRelease.ToCategory()} != {category}", nameof(loadOrder));
                    }

                    return x;
                }),
            category,
            prefs ?? LinkCachePreferences.Default);
    }










    public static ImmutableLoadOrderLinkCache ToUntypedImmutableLinkCache<TMod>(
        this IEnumerable<TMod> loadOrder,
        LinkCachePreferences? prefs = null)
        where TMod : class, IModGetter
    {
        return new ImmutableLoadOrderLinkCache(
            loadOrder,
            GameCategoryHelper.TryFromModType<TMod>(),
            prefs ?? LinkCachePreferences.Default);
    }











    public static ImmutableLoadOrderLinkCache ToUntypedImmutableLinkCache<TMod>(
        this IEnumerable<TMod> loadOrder,
        GameCategory category,
        LinkCachePreferences? prefs = null)
        where TMod : class, IModGetter
    {
        return new ImmutableLoadOrderLinkCache(
            loadOrder
                .Select(x =>
                {
                    if (x.GameRelease.ToCategory() != category)
                    {
                        throw new ArgumentException($"Supplied a mod that was not of specified category. {x.GameRelease.ToCategory()} != {category}", nameof(loadOrder));
                    }

                    return x;
                }),
            category,
            prefs ?? LinkCachePreferences.Default);
    }









    public static ImmutableModLinkCache<TMod, TModGetter> ToImmutableLinkCache<TMod, TModGetter>(
        this TModGetter mod,
        LinkCachePreferences? prefs = null)
        where TMod : class, IContextMod<TMod, TModGetter>, TModGetter
        where TModGetter : class, IContextGetterMod<TMod, TModGetter>
    {
        return new ImmutableModLinkCache<TMod, TModGetter>(mod, prefs ?? LinkCachePreferences.Default);
    }








    public static MutableModLinkCache<TMod, TModGetter> ToMutableLinkCache<TMod, TModGetter>(
        this TModGetter mod)
        where TMod : class, IContextMod<TMod, TModGetter>, TModGetter
        where TModGetter : class, IContextGetterMod<TMod, TModGetter>
    {
        return new MutableModLinkCache<TMod, TModGetter>(mod);
    }








    public static MutableModLinkCache ToUntypedMutableLinkCache(
        this IModGetter mod)
    {
        return new MutableModLinkCache(mod);
    }










    public static ImmutableLoadOrderLinkCache<TMod, TModGetter> ToImmutableLinkCache<TMod, TModGetter>(
        this ILoadOrderGetter<TModGetter> loadOrder,
        LinkCachePreferences? prefs = null)
        where TMod : class, IContextMod<TMod, TModGetter>, TModGetter
        where TModGetter : class, IContextGetterMod<TMod, TModGetter>
    {
        return new ImmutableLoadOrderLinkCache<TMod, TModGetter>(loadOrder.ListedOrder, prefs ?? LinkCachePreferences.Default);
    }










    public static ImmutableLoadOrderLinkCache<TMod, TModGetter> ToImmutableLinkCache<TMod, TModGetter>(
        this ILoadOrderGetter<IModListingGetter<TModGetter>> loadOrder,
        LinkCachePreferences? prefs = null)
        where TMod : class, IContextMod<TMod, TModGetter>, TModGetter
        where TModGetter : class, IContextGetterMod<TMod, TModGetter>
    {
        return new ImmutableLoadOrderLinkCache<TMod, TModGetter>(
            loadOrder
                .Select(listing => listing.Value.Mod)
                .WhereNotNull(),
            prefs ?? LinkCachePreferences.Default);
    }










    public static ImmutableLoadOrderLinkCache<TMod, TModGetter> ToImmutableLinkCache<TMod, TModGetter>(
        this IEnumerable<IModListingGetter<TModGetter>> loadOrder,
        LinkCachePreferences? prefs = null)
        where TMod : class, IContextMod<TMod, TModGetter>, TModGetter
        where TModGetter : class, IContextGetterMod<TMod, TModGetter>
    {
        return new ImmutableLoadOrderLinkCache<TMod, TModGetter>(
            loadOrder
                .Select(listing => listing.Mod)
                .WhereNotNull(),
            prefs ?? LinkCachePreferences.Default);
    }










    public static ImmutableLoadOrderLinkCache<TMod, TModGetter> ToImmutableLinkCache<TMod, TModGetter>(
        this IEnumerable<TModGetter> loadOrder,
        LinkCachePreferences? prefs = null)
        where TMod : class, IContextMod<TMod, TModGetter>, TModGetter
        where TModGetter : class, IContextGetterMod<TMod, TModGetter>
    {
        return new ImmutableLoadOrderLinkCache<TMod, TModGetter>(loadOrder, prefs ?? LinkCachePreferences.Default);
    }








    public static ILinkCache<TMod, TModGetter> ToMutableLinkCache<TMod, TModGetter>(
        this ILoadOrderGetter<TModGetter> immutableBaseCache,
        params TMod[] mutableMods)
        where TMod : class, IContextMod<TMod, TModGetter>, TModGetter
        where TModGetter : class, IContextGetterMod<TMod, TModGetter>
    {
        if (mutableMods.Length == 0) return immutableBaseCache.ToImmutableLinkCache<TMod, TModGetter>();
        return new MutableLoadOrderLinkCache<TMod, TModGetter>(
            immutableBaseCache.ToImmutableLinkCache<TMod, TModGetter>(),
            mutableMods);
    }









    public static MutableLoadOrderLinkCache ToUntypedMutableLinkCache(
        this ILoadOrderGetter<IModGetter> immutableBaseCache,
        IMod mutableMod,
        params IMod[] additionalMutableMods)
    {
        return new MutableLoadOrderLinkCache(
            immutableBaseCache.ToUntypedImmutableLinkCache(mutableMod.GameRelease.ToCategory()),
            mutableMod.AsEnumerable().Concat(additionalMutableMods).ToArray());
    }








    public static ILinkCache<TMod, TModGetter> ToMutableLinkCache<TMod, TModGetter>(
        this ILoadOrderGetter<IModListingGetter<TModGetter>> immutableBaseCache,
        params TMod[] mutableMods)
        where TMod : class, IContextMod<TMod, TModGetter>, TModGetter
        where TModGetter : class, IContextGetterMod<TMod, TModGetter>
    {
        if (mutableMods.Length == 0) return immutableBaseCache.ToImmutableLinkCache<TMod, TModGetter>();
        return new MutableLoadOrderLinkCache<TMod, TModGetter>(
            immutableBaseCache.ToImmutableLinkCache<TMod, TModGetter>(),
            mutableMods);
    }








    public static ILinkCache ToUntypedMutableLinkCache<TMod>(
        this ILoadOrderGetter<IModListingGetter<TMod>> immutableBaseCache,
        params TMod[] mutableMods)
        where TMod : class, IModGetter
    {
        if (mutableMods.Length == 0) return immutableBaseCache.ToUntypedImmutableLinkCache();
        return new MutableLoadOrderLinkCache(
            immutableBaseCache.ToUntypedImmutableLinkCache<TMod>(),
            mutableMods.Select(x => (IMod)x).ToArray());
    }









    public static ILinkCache ToUntypedMutableLinkCache(
        this ILoadOrderGetter<IModListingGetter<IModGetter>> immutableBaseCache,
        GameCategory category,
        params IMod[] mutableMods)
    {
        if (mutableMods.Length == 0) return immutableBaseCache.ToUntypedImmutableLinkCache(category);
        var mismatchedMod = mutableMods.FirstOrDefault(x => x.GameRelease.ToCategory() != category);
        if (mismatchedMod != null)
        {
            throw new ArgumentException($"Supplied a mod that was not of specified category. {mismatchedMod.GameRelease.ToCategory()} != {category}", nameof(mutableMods));
        }
        return new MutableLoadOrderLinkCache(
            immutableBaseCache.ToUntypedImmutableLinkCache(category),
            mutableMods);
    }








    public static ILinkCache<TMod, TModGetter> ToMutableLinkCache<TMod, TModGetter>(
        this IEnumerable<TModGetter> immutableBaseCache,
        params TMod[] mutableMods)
        where TMod : class, IContextMod<TMod, TModGetter>, TModGetter
        where TModGetter : class, IContextGetterMod<TMod, TModGetter>
    {
        if (mutableMods.Length == 0) return immutableBaseCache.ToImmutableLinkCache<TMod, TModGetter>();
        return new MutableLoadOrderLinkCache<TMod, TModGetter>(
            immutableBaseCache.ToImmutableLinkCache<TMod, TModGetter>(),
            mutableMods);
    }









    public static ILinkCache ToUntypedMutableLinkCache(
        this IEnumerable<IModGetter> immutableBaseCache,
        GameCategory category,
        params IMod[] mutableMods)
    {
        if (mutableMods.Length == 0) return immutableBaseCache.ToUntypedImmutableLinkCache();
        var mismatchedMod = mutableMods.FirstOrDefault(x => x.GameRelease.ToCategory() != category);
        if (mismatchedMod != null)
        {
            throw new ArgumentException($"Supplied a mod that was not of specified category. {mismatchedMod.GameRelease.ToCategory()} != {category}", nameof(mutableMods));
        }
        return new MutableLoadOrderLinkCache(
            immutableBaseCache.ToUntypedImmutableLinkCache(category),
            mutableMods);
    }
}