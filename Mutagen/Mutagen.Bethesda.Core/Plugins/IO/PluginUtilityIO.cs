using System.IO.Abstractions;
using Mutagen.Bethesda.Plugins.IO.DI;
using Noggog;

namespace Mutagen.Bethesda.Plugins.IO;

public static class PluginUtilityIO
{














    public static void MoveModTo(
        ModPath pathToPlugin,
        DirectoryPath newDirectory,
        bool overwrite = false,
        AssociatedModFileCategory? categories = null,
        IFileSystem? fileSystem = null)
    {
        var loc = new AssociatedFilesLocator(fileSystem.GetOrDefault());
        var mover = new ModFilesMover(fileSystem.GetOrDefault(), loc);
        mover.MoveModTo(pathToPlugin, newDirectory, overwrite, categories);
    }











    public static IEnumerable<FilePath> GetAssociatedFiles(
        ModPath modPath,
        AssociatedModFileCategory? categories = null,
        IFileSystem? fileSystem = null)
    {
        var loc = new AssociatedFilesLocator(fileSystem.GetOrDefault());
        return loc.GetAssociatedFiles(modPath, categories);
    }
}