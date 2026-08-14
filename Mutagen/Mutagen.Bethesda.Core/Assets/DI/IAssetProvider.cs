using System.Diagnostics.CodeAnalysis;

namespace Mutagen.Bethesda.Assets.DI;

public interface IAssetProvider
{

    bool Exists(DataRelativePath assetPath);

    bool TryGetStream(DataRelativePath assetPath, [MaybeNullWhen(false)] out Stream stream);

    bool TryGetSize(DataRelativePath assetPath, out uint size);
}
