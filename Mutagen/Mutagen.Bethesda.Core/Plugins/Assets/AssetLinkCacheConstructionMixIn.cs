using Mutagen.Bethesda.Plugins.Cache;

namespace Mutagen.Bethesda.Plugins.Assets;

public static class AssetLinkCacheConstructionMixIn
{






    public static IAssetLinkCache CreateImmutableAssetLinkCache(this ILinkCache linkCache)
    {
        return new AssetLinkCache(linkCache);
    }
}