using Mutagen.Bethesda.Assets;
using Mutagen.Bethesda.Assets.DI;
using Mutagen.Bethesda.Strings;
namespace Mutagen.Bethesda.Fonts.DI;

public interface IGetFontConfig
{





	DataRelativePath GetAssetPath(Language language);






	Stream GetStream(Language language);
}

public class GetFontConfig : IGetFontConfig
{
	private readonly IAssetProvider _assetProvider;
	private readonly IGetFontConfigListing _iniListings;

	public GetFontConfig(
		IAssetProvider assetProvider,
		IGetFontConfigListing iniListings)
	{
		_assetProvider = assetProvider;
		_iniListings = iniListings;
	}

	public DataRelativePath GetAssetPath(Language language)
	{
		var iniFontConfig = _iniListings.Get();


		if (iniFontConfig is {} configAssetPath && _assetProvider.Exists(configAssetPath))
		{
			return configAssetPath;
		}


		var isoLanguageString = StringsUtility.GetIsoLanguageString(language);
		var languageAssetPath = new DataRelativePath($"Interface/FontConfig_{isoLanguageString}.txt");
		if (_assetProvider.Exists(languageAssetPath))
		{
			return languageAssetPath;
		}


		var defaultAssetPath = new DataRelativePath("Interface/FontConfig.txt");
		if (_assetProvider.Exists(defaultAssetPath))
		{
			return defaultAssetPath;
		}

		throw new FileNotFoundException();
	}

	public Stream GetStream(Language language)
	{
		return _assetProvider.GetStream(GetAssetPath(language));
	}
}
