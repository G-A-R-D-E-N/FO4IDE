using Mutagen.Bethesda.Assets;

namespace Mutagen.Bethesda.Plugins.Assets;

public interface IAssetLinkGetter
{
    IAssetType AssetTypeInstance { get; }

    string GivenPath { get; }

    DataRelativePath DataRelativePath { get; }

    string Extension { get; }

    IAssetType Type { get; }

    public bool IsNull { get; }
}

public interface IAssetLinkGetter<out TAssetType> : IAssetLinkGetter
    where TAssetType : IAssetType
{
}

public interface IAssetLink<out TAssetType> : IAssetLink<IAssetLink<TAssetType>, TAssetType>
    where TAssetType : IAssetType
{
    new TAssetType AssetTypeInstance { get; }

    new string GivenPath { get; set; }
}

public interface IAssetLink<out TLinkType, out TAssetType> :
    IAssetLink,
    IAssetLinkGetter<TAssetType>
    where TAssetType : IAssetType
    where TLinkType : IAssetLink<TLinkType, TAssetType>
{

    new string GivenPath { get; set; }

    void SetToNull();
}

public interface IAssetLink : IAssetLinkGetter
{

    bool TrySetPath(DataRelativePath? path);

    bool TrySetPath(string? path);

    new string GivenPath { get; set; }
}