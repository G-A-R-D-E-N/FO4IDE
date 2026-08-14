using System.IO.Abstractions;
using IniParser;
using IniParser.Model.Configuration;
using IniParser.Parser;
using Mutagen.Bethesda.Inis.DI;
using Noggog;

namespace Mutagen.Bethesda.Archives.DI;

public interface IGetArchiveIniListings
{




    IEnumerable<FileName>? TryGet();





    IEnumerable<FileName> Get();






    IEnumerable<FileName> Get(FilePath path);






    IEnumerable<FileName> Get(Stream iniStream);
}

public sealed class GetArchiveIniListings : IGetArchiveIniListings
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

    public GetArchiveIniListings(
        IFileSystem fileSystem,
        IIniPathProvider iniPathProvider)
    {
        _fileSystem = fileSystem;
        _iniPathProvider = iniPathProvider;
    }


    public IEnumerable<FileName>? TryGet()
    {
        var path = _iniPathProvider.TryGetPath();
        if (path == null)
        {
            return null;
        }

        return Get();
    }


    public IEnumerable<FileName> Get()
    {
        return Get(_iniPathProvider.Path);
    }


    public IEnumerable<FileName> Get(FilePath path)
    {
        if (!_fileSystem.File.Exists(path))
        {
            return [];
        }
        return Get(_fileSystem.File.OpenRead(path.Path));
    }


    public IEnumerable<FileName> Get(Stream iniStream)
    {

        var parser = new FileIniDataParser(new IniDataParser(Config));
        var data = parser.ReadData(new StreamReader(iniStream));
        var basePath = data["Archive"];
        var str1 = basePath["sResourceArchiveList"]?.Split(',');
        var str2 = basePath["sResourceArchiveList2"]?.Split(',');
        var ret = str1.EmptyIfNull().And(str2.EmptyIfNull())
            .Select(x => x.Trim())
            .Select(x => new FileName(x))
            .ToList();
        return ret;
    }
}