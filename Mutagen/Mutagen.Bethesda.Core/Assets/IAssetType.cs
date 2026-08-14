namespace Mutagen.Bethesda.Assets;




public interface IAssetType
{
    static virtual IAssetType Instance => null!;




    string BaseFolder { get; }




    IEnumerable<string> FileExtensions { get; }
}