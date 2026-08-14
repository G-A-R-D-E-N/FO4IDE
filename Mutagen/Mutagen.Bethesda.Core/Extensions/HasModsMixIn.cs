using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Exceptions;
using Mutagen.Bethesda.Plugins.Order;
using Noggog;

namespace Mutagen.Bethesda;

public static class HasModsMixIn
{
    private static bool CheckListing(IModListingGetter listing, bool? enabled, bool? present)
    {
        return (present == null || listing.ModExists == present)
               && (enabled == null || listing.Enabled == enabled);
    }

    private static bool CheckListingWithMod<TMod>(IModListingGetter<TMod> listing, bool? enabled, bool? present)
        where TMod : class, IModKeyed
    {
        return CheckListing(listing, enabled: enabled, present: present)
               && (present == null || listing.Mod != null);
    }

    public static bool ModExists(this IEnumerable<IModListingGetter> listings, ModKey modKey, bool? enabled = null)
    {
        foreach (var listing in listings)
        {
            if (listing.ModKey == modKey
                && CheckListing(listing, enabled, present: true))
            {
                return true;
            }
        }
        return false;
    }

    public static void AssertModExists(this IEnumerable<IModListingGetter> listings, ModKey modKey, bool? enabled = null, string? message = null)
    {
        if (!ModExists(listings, modKey, enabled))
        {
            throw new MissingModException(modKey, message: message);
        }
    }

    public static bool ModsExist(this IEnumerable<IModListingGetter> listings, IEnumerable<ModKey> modKeys)
    {
        return ModsExist(listings, modKeys: modKeys.ToArray());
    }

    public static void AssertModsExist(this IEnumerable<IModListingGetter> listings, IEnumerable<ModKey> modKeys, string? message = null)
    {
        AssertModsExist(listings, enabled: null, message: message, modKeys.ToArray());
    }

    public static bool ModsExist(this IEnumerable<IModListingGetter> listings, bool? enabled, params ModKey[] modKeys)
    {
        if (modKeys.Length == 0) return true;
        if (modKeys.Length == 1) return ModExists(listings, modKeys[0], enabled);
        var set = modKeys.ToHashSet();
        foreach (var listing in listings)
        {
            if (CheckListing(listing, enabled, present: true)
                && set.Remove(listing.ModKey)
                && set.Count == 0)
            {
                return true;
            }
        }
        return false;
    }

    public static void AssertModsExist(this IEnumerable<IModListingGetter> listings, bool? enabled, params ModKey[] modKeys)
    {
        AssertModsExist(listings, message: null, enabled: enabled, modKeys: modKeys);
    }

    public static void AssertModsExist(this IEnumerable<IModListingGetter> listings, bool? enabled, string? message, params ModKey[] modKeys)
    {
        if (modKeys.Length == 0) return;
        if (modKeys.Length == 1)
        {
            AssertModExists(listings, modKeys[0]);
            return;
        }
        var set = modKeys.ToHashSet();
        foreach (var listing in listings)
        {
            if (CheckListing(listing, enabled, present: true)
                && set.Remove(listing.ModKey)
                && set.Count == 0)
            {
                return;
            }
        }
        if (set.Count > 0)
        {
            throw new MissingModException(set, message: message);
        }
    }

    public static bool ModsExist(this IEnumerable<IModListingGetter> listings, bool? enabled, IEnumerable<ModKey> modKeys)
    {
        return ModsExist(listings, enabled, modKeys.ToArray());
    }

    public static void AssertModsExist(this IEnumerable<IModListingGetter> listings, bool? enabled, IEnumerable<ModKey> modKeys, string? message = null)
    {
        AssertModsExist(listings, enabled: enabled, message: message, modKeys: modKeys.ToArray());
    }

    public static bool ModsExist(this IEnumerable<IModListingGetter> keys, params ModKey[] modKeys)
    {
        if (modKeys.Length == 0) return true;
        if (modKeys.Length == 1) return ModExists(keys, modKeys[0]);
        var set = modKeys.ToHashSet();
        foreach (var listing in keys)
        {
            if (!CheckListing(listing, enabled: null, present: true)) return false;
            if (set.Remove(listing.ModKey)
                && set.Count == 0)
            {
                return true;
            }
        }
        return false;
    }

    public static void AssertModsExist(this IEnumerable<IModListingGetter> keys, params ModKey[] modKeys)
    {
        AssertModsExist(keys, enabled: null, message: null, modKeys);
    }

    public static bool ModsExist<TMod>(this IEnumerable<IModListingGetter<TMod>> listings, bool? enabled, bool? present, params ModKey[] modKeys)
        where TMod : class, IModKeyed
    {
        if (modKeys.Length == 0) return true;
        if (modKeys.Length == 1) return ModExists(listings, modKeys[0], enabled);
        var set = modKeys.ToHashSet();
        foreach (var listing in listings)
        {
            if (!CheckListingWithMod(listing, enabled: enabled, present: present)) continue;
            if (set.Remove(listing.ModKey)
                && set.Count == 0)
            {
                return true;
            }
        }
        return false;
    }

    public static void AssertModsExist<TMod>(this IEnumerable<IModListingGetter<TMod>> listings, bool? enabled, bool? present, params ModKey[] modKeys)
        where TMod : class, IModKeyed
    {
        AssertModsExist(listings, enabled: enabled, present: present, message: null, modKeys: modKeys);
    }

    public static void AssertModsExist<TMod>(this IEnumerable<IModListingGetter<TMod>> listings, bool? enabled, bool? present, string? message, params ModKey[] modKeys)
        where TMod : class, IModKeyed
    {
        if (modKeys.Length == 0) return;
        if (modKeys.Length == 1)
        {
            AssertModExists(listings, modKeys[0], enabled);
            return;
        }
        var set = modKeys.ToHashSet();
        foreach (var listing in listings)
        {
            if (!CheckListingWithMod(listing, enabled: enabled, present: present)) continue;
            if (set.Remove(listing.ModKey)
                && set.Count == 0)
            {
                return;
            }
        }

        if (set.Count > 0)
        {
            throw new MissingModException(set, message: message);
        }
    }

    public static bool ModExists(this ILoadOrderGetter<IModListingGetter> loadOrder, ModKey modKey, bool? enabled = null)
    {
        if (loadOrder.TryGetValue(modKey, out var listing))
        {
            return CheckListing(listing, enabled: enabled, present: true);
        }

        return false;
    }

    public static void AssertModExists(this ILoadOrderGetter<IModListingGetter> loadOrder, ModKey modKey, bool? enabled = null, string? message = null)
    {
        if (!ModExists(loadOrder, modKey, enabled))
        {
            throw new MissingModException(modKey, message: message);
        }
    }

    public static bool ModsExist(this ILoadOrderGetter<IModListingGetter> loadOrder, params ModKey[] modKeys)
    {
        foreach (var key in modKeys)
        {
            if (!loadOrder.ContainsKey(key)) return false;
        }

        return true;
    }

    public static void AssertModsExist(this ILoadOrderGetter<IModListingGetter> loadOrder, params ModKey[] modKeys)
    {
        AssertModsExist(loadOrder, message: null, modKeys);
    }

    public static void AssertModsExist(this ILoadOrderGetter<IModListingGetter> loadOrder, string? message, params ModKey[] modKeys)
    {
        AssertModsExist(loadOrder.ListedOrder, enabled: null, message: message, modKeys);
    }

    public static bool ModsExist(this ILoadOrderGetter<IModListingGetter> loadOrder, IEnumerable<ModKey> modKeys)
    {
        return ModsExist(loadOrder, modKeys.ToArray());
    }

    public static void AssertModsExist(this ILoadOrderGetter<IModListingGetter> loadOrder, IEnumerable<ModKey> modKeys, string? message = null)
    {
        AssertModsExist(loadOrder, message, modKeys.ToArray());
    }

    public static bool ModsExist(this ILoadOrderGetter<IModListingGetter> loadOrder, bool? enabled, params ModKey[] modKeys)
    {
        if (modKeys.Length == 0) return true;
        if (modKeys.Length == 1) return ModExists(loadOrder, modKeys[0], enabled);
        var set = modKeys.ToHashSet();
        foreach (var listing in loadOrder.ListedOrder)
        {
            if (CheckListing(listing, enabled, present: true)
                && set.Remove(listing.ModKey)
                && set.Count == 0)
            {
                return true;
            }
        }
        return false;
    }

    public static void AssertModsExist(this ILoadOrderGetter<IModListingGetter> loadOrder, bool? enabled, params ModKey[] modKeys)
    {
        AssertModsExist(loadOrder, enabled, message: null, modKeys: modKeys);
    }

    public static void AssertModsExist(this ILoadOrderGetter<IModListingGetter> loadOrder, bool? enabled, string? message, params ModKey[] modKeys)
    {
        if (modKeys.Length == 0) return;
        if (modKeys.Length == 1) AssertModExists(loadOrder, modKeys[0], enabled);
        var set = modKeys.ToHashSet();
        foreach (var listing in loadOrder.ListedOrder)
        {
            if (CheckListing(listing, enabled, present: true)
                && set.Remove(listing.ModKey)
                && set.Count == 0)
            {
                return;
            }
        }
        if (set.Count > 0)
        {
            throw new MissingModException(set, message: message);
        }
    }

    public static bool ModsExist(this ILoadOrderGetter<IModListingGetter> loadOrder, bool? enabled, IEnumerable<ModKey> modKeys)
    {
        return ModsExist(loadOrder, enabled, modKeys.ToArray());
    }

    public static void AssertModsExist(this ILoadOrderGetter<IModListingGetter> loadOrder, bool? enabled, IEnumerable<ModKey> modKeys, string? message = null)
    {
        AssertModsExist(loadOrder, enabled, message: message, modKeys: modKeys.ToArray());
    }
}