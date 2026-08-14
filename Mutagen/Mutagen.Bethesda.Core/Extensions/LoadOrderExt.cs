using System.Diagnostics.CodeAnalysis;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Exceptions;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Plugins.Records;
using Noggog;

namespace Mutagen.Bethesda;

public static class LoadOrderExt
{

    public static IEnumerable<TListing> OnlyEnabled<TListing>(this IEnumerable<TListing> loadOrder)
        where TListing : ILoadOrderListingGetter
    {
        return loadOrder.Where(x => x.Enabled);
    }

    public static IEnumerable<TListing> OnlyExisting<TListing, TMod>(this IEnumerable<TListing> loadOrder)
        where TListing : IModListingGetter
        where TMod : class, IModGetter
    {
        return loadOrder
            .Where(x => x.ModExists);
    }

    public static IEnumerable<TListing> OnlyEnabledAndExisting<TListing>(this IEnumerable<TListing> loadOrder)
        where TListing : IModListingGetter
    {
        return loadOrder
            .Where(x => x.Enabled && x.ModExists);
    }

    [Obsolete("Use ResolveAllModsExist instead")]
    public static IEnumerable<TMod> Resolve<TMod>(this IEnumerable<IModListingGetter<TMod>> loadOrder)
        where TMod : class, IModGetter
    {
        return ResolveAllModsExist(loadOrder);
    }

    public static IEnumerable<TModItem> ResolveAllModsExist<TModItem>(this IEnumerable<IModListingGetter<TModItem>> loadOrder)
        where TModItem : class, IModKeyed
    {
        loadOrder = loadOrder.ToArray();
        var missingMods = loadOrder.Where(x => x.Mod == null)
            .ToArray();

        if (missingMods.Length > 0)
        {
            throw new MissingModException(missingMods.Select(x => x.ModKey));
        }

        return loadOrder.Select(x => x.Mod!);
    }

    public static TModItem ResolveMod<TModItem>(
        this ILoadOrderGetter<IModListingGetter<TModItem>> loadOrder,
        ModKey modKey)
        where TModItem : class, IModKeyed
    {
        if (!loadOrder.TryGetValue(modKey, out var listing)
            || listing.Mod == null)
        {
            throw new MissingModException(modKey);
        }

        return listing.Mod;
    }

    public static IEnumerable<TModItem> ResolveExistingMods<TModItem>(this IEnumerable<IModListingGetter<TModItem>> loadOrder)
        where TModItem : class, IModKeyed
    {
        return loadOrder
            .Select(x => x.Mod)
            .WhereNotNull();
    }

    public static LoadOrder<TModItem> ResolveAllModsExist<TModItem>(
        this ILoadOrderGetter<IModListingGetter<TModItem>> loadOrder,
        bool? disposeItems = null)
        where TModItem : class, IModKeyed
    {
        return new LoadOrder<TModItem>(ResolveAllModsExist<TModItem>(loadOrder.ListedOrder), disposeItems: disposeItems ?? loadOrder.DisposingItems);
    }

    public static LoadOrder<TModItem> ResolveExistingMods<TModItem>(
        this ILoadOrderGetter<IModListingGetter<TModItem>> loadOrder,
        bool? disposeItems = null)
        where TModItem : class, IModKeyed
    {
        return new LoadOrder<TModItem>(ResolveExistingMods<TModItem>(loadOrder.ListedOrder), disposeItems: disposeItems ?? loadOrder.DisposingItems);
    }

    public static IEnumerable<ILoadOrderListingGetter> ToLoadOrderListings(this IEnumerable<ModKey> loadOrder, bool markEnabled = true)
    {
        return loadOrder.Select(x => new LoadOrderListing(x, markEnabled));
    }

    public static IEnumerable<IModListingGetter> ToModListings(this IEnumerable<ModKey> loadOrder, bool modExists, bool markEnabled = true)
    {
        return loadOrder.Select(x => new ModListing(x, enabled: markEnabled, modExists: modExists));
    }

    public static bool TryGetIndex<TListing>(this ILoadOrderGetter<TListing> loadOrder, int index, [MaybeNullWhen(false)] out TListing listing)
        where TListing : IModKeyed
    {
        var result = loadOrder.TryGetAtIndex(index);
        if (result == null)
        {
            listing = default;
            return false;
        }

        listing = result;
        return true;
    }

    public static LoadOrder<TListing> TrimAt<TListing>(this ILoadOrderGetter<TListing> loadOrder, ModKey modKey)
        where TListing : IModKeyed
    {
        return new LoadOrder<TListing>(loadOrder.ListedOrder.TrimAt(modKey));
    }

    public static IEnumerable<TListing> TrimAt<TListing>(this IEnumerable<TListing> loadOrder, ModKey modKey)
        where TListing : IModKeyed
    {
        return loadOrder.TakeWhile(x => x.ModKey != modKey);
    }

    public static LoadOrder<TRetListing> Transform<TListing, TRetListing>(this ILoadOrderGetter<TListing> loadOrder, Func<TListing, TRetListing> transformer)
        where TListing : IModKeyed
        where TRetListing : IModKeyed
    {
        return new LoadOrder<TRetListing>(
            loadOrder.ListedOrder
                .Select(transformer));
    }

    public static LoadOrder<TListing> Where<TListing>(this ILoadOrderGetter<TListing> loadOrder, Func<TListing, bool> filter)
        where TListing : IModKeyed
    {
        return new LoadOrder<TListing>(
            loadOrder.ListedOrder
                .Where(filter));
    }

    public static LoadOrder<TListing> WhereEnabled<TListing>(this ILoadOrderGetter<TListing> loadOrder)
        where TListing : ILoadOrderListingGetter
    {
        return new LoadOrder<TListing>(
            loadOrder.ListedOrder
                .Where(x => x.Enabled));
    }

    public static ILoadOrderGetter<IModListingGetter<TModItem>> WhereEnabledAndExisting<TModItem>(this ILoadOrderGetter<IModListingGetter<TModItem>> loadOrder)
        where TModItem : class, IModKeyed
    {
        return loadOrder
            .Where(x => x.Enabled && x.ModExists);
    }

    public static LoadOrder<TListing> FilterToMods<TListing>(this ILoadOrderGetter<TListing> loadOrder, IReadOnlyCollection<ModKey> modKeys)
        where TListing : IModKeyed
    {
        return new LoadOrder<TListing>(
            loadOrder.ListedOrder
                .Where(x => modKeys.Contains(x.ModKey)));
    }

    public static LoadOrder<TListing> FilterToMods<TListing>(this ILoadOrderGetter<TListing> loadOrder, params ModKey[] modKeys)
        where TListing : IModKeyed
    {
        return FilterToMods(loadOrder, modKeys.ToHashSet());
    }
}