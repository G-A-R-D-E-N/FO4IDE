using System.IO.Abstractions;
using IniParser;
using IniParser.Model.Configuration;
using IniParser.Parser;
using Mutagen.Bethesda.Assets;
using Mutagen.Bethesda.Inis.DI;
using Noggog;
namespace Mutagen.Bethesda.Fonts.DI;

public interface IGetFontConfigListing
{




	DataRelativePath? Get();






	DataRelativePath? Get(FilePath path);






	DataRelativePath? Get(Stream iniStream);
}

public sealed class GetFontConfigListing : IGetFontConfigListing
{
	private static readonly IniParserConfiguration Config = new()
	{
		AllowDuplicateKeys = true,
		AllowDuplicateSections = true,
		AllowKeysWithoutSection = true,
		AllowCreateSectionsOnFly = true,
		CaseInsensitive = true,
		SkipInvalidLines = true,
	};

	private readonly IFileSystem _fileSystem;
	private readonly IIniPathProvider _iniPathProvider;

	public GetFontConfigListing(
		IFileSystem fileSystem,
		IIniPathProvider iniPathProvider)
	{
		_fileSystem = fileSystem;
		_iniPathProvider = iniPathProvider;
	}


	public DataRelativePath? Get()
	{
		return Get(_iniPathProvider.Path);
	}


	public DataRelativePath? Get(FilePath path)
	{
		if (!_fileSystem.File.Exists(path)) return null;

		var fileStream = _fileSystem.File.OpenRead(path.Path);
		return Get(fileStream);
	}


	public DataRelativePath? Get(Stream iniStream)
	{

		var iniDataParser = new IniDataParser(Config);
		var parser = new FileIniDataParser(iniDataParser);
		var data = parser.ReadData(new StreamReader(iniStream));
		var basePath = data["Fonts"];
		var configFile = basePath["sFontConfigFile"];
		if (configFile is null) return null;

		return configFile;
	}
}
