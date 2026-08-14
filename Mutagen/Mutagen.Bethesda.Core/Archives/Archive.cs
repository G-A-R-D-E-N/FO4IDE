using System.IO.Abstractions;
using Mutagen.Bethesda.Plugins;
using Noggog;
using Mutagen.Bethesda.Archives.DI;
using Mutagen.Bethesda.Environments.DI;
using Mutagen.Bethesda.Installs.DI;
using Mutagen.Bethesda.Plugins.Utility;
using StrongInject;
using Stream = System.IO.Stream;

namespace Mutagen.Bethesda.Archives;

[RegisterModule(typeof(MutagenStrongInjectModule))]
partial class ArchiveContainer : IContainer<IGetApplicableArchivePaths>
{
    [Instance] private readonly IFileSystem _fileSystem;
    [Instance] private readonly IGameReleaseContext _gameReleaseContext;
    [Instance] private readonly IDataDirectoryProvider _dataDirectoryProvider;
    [Instance] private readonly IGameDirectoryLookup _gameDirectoryLookup = GameLocatorLookupCache.Instance;

    public ArchiveContainer(
        IFileSystem fileSystem,
        IGameReleaseContext gameReleaseContext,
        IDataDirectoryProvider dataDirectoryProvider)
    {
        _fileSystem = fileSystem;
        _gameReleaseContext = gameReleaseContext;
        _dataDirectoryProvider = dataDirectoryProvider;
    }
}

[RegisterModule(typeof(MutagenStrongInjectModule))]
partial class GetArchiveIniListingsContainer : IContainer<IGetArchiveIniListings>
{
    [Instance] private readonly IFileSystem _fileSystem;
    [Instance] private readonly IGameReleaseContext _gameReleaseContext;
    [Instance] private readonly IGameDirectoryLookup _gameDirectoryLookup = GameLocatorLookupCache.Instance;

    public GetArchiveIniListingsContainer(
        IFileSystem? fileSystem,
        GameRelease release)
    {
        _fileSystem = fileSystem.GetOrDefault();
        _gameReleaseContext = new GameReleaseInjection(release);
    }
}

public static class Archive
{
    private static IGetApplicableArchivePaths GetApplicableArchivePathsDi(
        GameRelease release,
        DirectoryPath dataFolderPath,
        IFileSystem? fileSystem)
    {
        var cont = new ArchiveContainer(
            fileSystem: fileSystem.GetOrDefault(),
            new GameReleaseInjection(release),
            new DataDirectoryInjection(dataFolderPath));
        return cont.Resolve().Value;
    }

    public static string GetExtension(GameRelease release)
    {
        switch (release.ToCategory())
        {
            case GameCategory.Oblivion:
            case GameCategory.Skyrim:
                return ".bsa";
            case GameCategory.Fallout4:
            case GameCategory.Starfield:
                return ".ba2";
            default:
                throw new NotImplementedException();
        }
    }

    public static IArchiveReader CreateReader(GameRelease release, FilePath path, IFileSystem? fileSystem = null)
    {
        return new ArchiveReaderProvider(
                fileSystem.GetOrDefault(),
                new GameReleaseInjection(release))
            .Create(path);
    }

    public static IEnumerable<FilePath> GetApplicableArchivePaths(
        GameRelease release, DirectoryPath dataFolderPath, IFileSystem? fileSystem = null,
        bool returnEmptyIfMissing = true)
    {
        return GetApplicableArchivePathsDi(release, dataFolderPath, fileSystem: fileSystem)
            .Get();
    }

    public static IEnumerable<FilePath> GetApplicableArchivePaths(GameRelease release, DirectoryPath dataFolderPath,
        ModKey modKey, IFileSystem? fileSystem = null, bool returnEmptyIfMissing = true)
    {
        return GetApplicableArchivePathsDi(release, dataFolderPath, fileSystem: fileSystem)
            .Get(modKey);
    }

    public static bool IsApplicable(GameRelease release, ModKey modKey, FileName archiveFileName)
    {
        return new CheckArchiveApplicability(
                new ArchiveExtensionProvider(
                    new GameReleaseInjection(release)))
            .IsApplicable(modKey, archiveFileName);
    }

    public static IEnumerable<FileName> GetIniListings(GameRelease release, IFileSystem? fileSystem = null)
    {
        return new GetArchiveIniListingsContainer(fileSystem, release)
            .Resolve().Value
            .Get();
    }

    public static IEnumerable<FileName> GetIniListings(GameRelease release, FilePath path, IFileSystem? fileSystem = null)
    {
        return new GetArchiveIniListingsContainer(fileSystem, release)
            .Resolve().Value
            .Get(path);
    }

    public static IEnumerable<FileName> GetIniListings(GameRelease release, Stream iniStream, IFileSystem? fileSystem = null)
    {
        return new GetArchiveIniListingsContainer(fileSystem, release)
            .Resolve().Value
            .Get(iniStream);
    }
}