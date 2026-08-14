using Mutagen.Bethesda.Environments.DI;
using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Plugins.Exceptions;
using Mutagen.Bethesda.Plugins.Records;
using Noggog;
using System.IO.Abstractions;

namespace Mutagen.Bethesda.Plugins.Analysis.DI;





public interface IMultiModFileReader
{











    TModGetter Read<TModGetter>(
        DirectoryPath folder,
        ModKey modKey,
        GameRelease gameRelease,
        IEnumerable<ModKey> loadOrder,
        BinaryReadParameters readParams)
        where TModGetter : class, IModDisposeGetter;
}





public class MultiModFileReader : IMultiModFileReader
{

    public TModGetter Read<TModGetter>(
        DirectoryPath folder,
        ModKey modKey,
        GameRelease gameRelease,
        IEnumerable<ModKey> loadOrder,
        BinaryReadParameters readParams)
        where TModGetter : class, IModDisposeGetter
    {
        var fileSystem = readParams.FileSystem.GetOrDefault();


        var modPath = new ModPath(modKey, Path.Combine(folder.Path, modKey.FileName));
        var splitFiles = MultiModFileAnalysis.GetSplitModFiles(modPath, fileSystem);

        if (splitFiles.Count == 0)
        {
            throw new SplitModException($"No split files found for {modKey} in {folder}");
        }


        var gameReleaseContext = new GameReleaseInjection(gameRelease);
        var modImporter = new Records.DI.ModImporter(fileSystem, gameReleaseContext);


        return modImporter.ImportMultiFile<TModGetter>(modKey, splitFiles.Select(f => (ModPath)f.Path), loadOrder, readParams);
    }
}
