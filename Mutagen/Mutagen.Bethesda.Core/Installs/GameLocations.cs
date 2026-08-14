using Noggog;
using System.Diagnostics.CodeAnalysis;
using Mutagen.Bethesda.Environments.DI;
using Mutagen.Bethesda.Installs.DI;

namespace Mutagen.Bethesda.Installs;

public static class GameLocations
{
    private static readonly GameLocatorLookupCache Locator = new();
    private static IGameDirectoryLookup GameDirLookup => Locator;
    private static IDataDirectoryLookup DataDirLookup => Locator;

    public static IEnumerable<DirectoryPath> GetGameFolders(GameRelease release)
    {
        foreach (var dir in GameDirLookup.GetAll(release))
        {
            yield return dir;
        }
    }

    public static bool TryGetGameFolder(GameRelease release, [MaybeNullWhen(false)] out DirectoryPath path)
    {
        return GameDirLookup.TryGet(release, out path);
    }

    public static DirectoryPath GetGameFolder(GameRelease release)
    {
        return GameDirLookup.Get(release);
    }

    public static bool TryGetDataFolder(GameRelease release, [MaybeNullWhen(false)] out DirectoryPath path)
    {
        return DataDirLookup.TryGet(release, out path);
    }

    public static DirectoryPath GetDataFolder(GameRelease release)
    {
        return DataDirLookup.Get(release);
    }
}