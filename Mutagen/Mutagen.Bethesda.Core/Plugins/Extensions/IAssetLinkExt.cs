using Mutagen.Bethesda.Assets;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Assets;
using Mutagen.Bethesda.Plugins.Records;

namespace Mutagen.Bethesda;




public static class IAssetLinkExt
{



    public static IAssetLink<TAssetType> AsSetter<TAssetType>(this IAssetLinkGetter<TAssetType> link)
        where TAssetType : class, IAssetType
    {
        return new AssetLink<TAssetType>(link.GivenPath);
    }
}