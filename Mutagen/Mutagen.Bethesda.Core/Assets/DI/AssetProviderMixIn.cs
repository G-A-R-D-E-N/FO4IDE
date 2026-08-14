using System.Diagnostics.CodeAnalysis;
using Mutagen.Bethesda.Plugins.Assets;

namespace Mutagen.Bethesda.Assets.DI;

public static class AssetProviderMixIn
{
    public static bool Exists(this IAssetProvider assetProvider, IAssetLinkGetter assetLink)
    {
        return assetProvider.Exists(assetLink.DataRelativePath);
    }

    public static bool TryGetStream(
        this IAssetProvider assetProvider,
        IAssetLinkGetter assetPath,
        [MaybeNullWhen(false)] out Stream stream)
    {
        return assetProvider.TryGetStream(assetPath.DataRelativePath, out stream);
    }

    public static Stream GetStream(this IAssetProvider assetProvider, DataRelativePath assetPath)
    {
        return assetProvider.TryGetStream(assetPath, out var stream)
            ? stream
            : throw new FileNotFoundException($"Asset not found: {assetPath}");
    }

    public static Stream GetStream(this IAssetProvider assetProvider, IAssetLinkGetter assetLink)
    {
        return assetProvider.GetStream(assetLink.DataRelativePath);
    }

    public static bool TryGetSize(this IAssetProvider assetProvider, IAssetLinkGetter assetPath, out uint size)
    {
        return assetProvider.TryGetSize(assetPath.DataRelativePath, out size);
    }

    public static uint GetSize(this IAssetProvider assetProvider, DataRelativePath assetPath)
    {
        return assetProvider.TryGetSize(assetPath, out var size)
            ? size
            : throw new FileNotFoundException($"Asset not found: {assetPath}");
    }

    public static uint GetSize(this IAssetProvider assetProvider, IAssetLinkGetter assetLink)
    {
        return assetProvider.GetSize(assetLink.DataRelativePath);
    }
}
