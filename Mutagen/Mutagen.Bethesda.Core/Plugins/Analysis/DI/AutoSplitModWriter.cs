using System.IO.Abstractions;
using Mutagen.Bethesda.Plugins.Analysis;
using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Plugins.Exceptions;
using Mutagen.Bethesda.Plugins.Internals;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Plugins.Records;
using Noggog;

namespace Mutagen.Bethesda.Plugins.Analysis.DI;

public class AutoSplitModWriter : IAutoSplitModWriter
{
    private readonly IMultiModFileSplitter _splitter;

    public AutoSplitModWriter(IMultiModFileSplitter splitter)
    {
        _splitter = splitter;
    }

    public void Write<TMod, TModGetter>(
        TModGetter mod,
        FilePath path,
        BinaryWriteParameters param)
        where TMod : class, IMod, TModGetter, IMajorRecordContextEnumerable<TMod, TModGetter>
        where TModGetter : class, IModGetter
    {
        var fileSystem = param.FileSystem.GetOrDefault();

        try
        {

            mod.WriteToBinary(path, param);
        }
        catch (TooManyMastersException)
        {

            WriteWithSplit<TMod, TModGetter>(mod, path, param, fileSystem);
        }
    }

    private void WriteWithSplit<TMod, TModGetter>(
        TModGetter mod,
        FilePath path,
        BinaryWriteParameters param,
        IFileSystem fileSystem)
        where TMod : class, IMod, TModGetter, IMajorRecordContextEnumerable<TMod, TModGetter>
        where TModGetter : class, IModGetter
    {

        if (mod is not TMod mutableMod)
        {
            throw new ArgumentException(
                $"Mod must be of mutable type {typeof(TMod).Name} to support auto-splitting, but was {mod.GetType().Name}");
        }

        var splitMods = _splitter.Split<TMod, TModGetter>(mutableMod, Constants.PluginMasterLimit);

        CleanupOldSplitFiles(path, splitMods.Count, fileSystem);

        var splitParam = AugmentParamsWithSplitModKeys(param, splitMods);

        foreach (var splitMod in splitMods)
        {
            var splitPath = Path.Combine(Path.GetDirectoryName(path)!, splitMod.ModKey.FileName);

            splitMod.WriteToBinary(splitPath, splitParam);
        }
    }

    private BinaryWriteParameters AugmentParamsWithSplitModKeys<TMod>(
        BinaryWriteParameters param,
        IReadOnlyCollection<TMod> splitMods)
        where TMod : IModGetter
    {
        if (param.MastersListOrdering is not MastersListOrderingByLoadOrder loadOrderOrdering)
            return param;

        var splitModKeys = splitMods.Select(m => m.ModKey).ToList();

        ValidateSplitKeyOrder(loadOrderOrdering.LoadOrder, splitModKeys);

        var existingKeys = loadOrderOrdering.LoadOrder.ToHashSet();
        var missingKeys = splitModKeys.Where(k => !existingKeys.Contains(k)).ToList();
        if (missingKeys.Count == 0)
            return param;

        var augmentedOrder = loadOrderOrdering.LoadOrder.Concat(missingKeys);
        return param with
        {
            MastersListOrdering = new MastersListOrderingByLoadOrder(augmentedOrder)
        };
    }

    private static void ValidateSplitKeyOrder(
        IReadOnlyList<ModKey> loadOrderKeys,
        List<ModKey> splitModKeys)
    {
        var loadOrder = new LoadOrder<LoadOrderListing>(
            loadOrderKeys.Select(k => new LoadOrderListing(k, enabled: true)));

        var presentSplitKeys = new List<(int splitIndex, int loadOrderIndex, ModKey key)>();
        for (int i = 0; i < splitModKeys.Count; i++)
        {
            var loIndex = loadOrder.IndexOf(splitModKeys[i]);
            if (loIndex >= 0)
            {
                presentSplitKeys.Add((i, loIndex, splitModKeys[i]));
            }
        }

        if (presentSplitKeys.Count < 2)
            return;

        for (int i = 1; i < presentSplitKeys.Count; i++)
        {
            var prev = presentSplitKeys[i - 1];
            var curr = presentSplitKeys[i];
            if (curr.loadOrderIndex <= prev.loadOrderIndex)
            {
                throw new SplitModException(
                    $"Split mod files are out of order in the load order: " +
                    $"'{prev.key.FileName}' (index {prev.loadOrderIndex}) " +
                    $"appears before '{curr.key.FileName}' (index {curr.loadOrderIndex}), " +
                    $"but '{curr.key.FileName}' should come after '{prev.key.FileName}'. " +
                    $"Please ensure split files are ordered: base, _2, _3, etc.");
            }
        }
    }

    private FilePath GetSplitFilePath(FilePath originalPath, int index)
    {
        if (index == 0)
        {
            return originalPath;
        }

        var directory = Path.GetDirectoryName(originalPath.Path) ?? string.Empty;
        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(originalPath.Path);
        var extension = Path.GetExtension(originalPath.Path);

        var splitFileName = $"{fileNameWithoutExtension}_{index + 1}{extension}";
        return Path.Combine(directory, splitFileName);
    }

    private void CleanupOldSplitFiles(FilePath originalPath, int currentSplitCount, IFileSystem fileSystem)
    {
        var directory = Path.GetDirectoryName(originalPath.Path);
        if (string.IsNullOrEmpty(directory))
        {
            directory = Directory.GetCurrentDirectory();
        }

        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(originalPath.Path);
        var extension = Path.GetExtension(originalPath.Path);

        var searchPattern = $"{fileNameWithoutExtension}_*{extension}";

        foreach (var filePath in fileSystem.Directory.EnumerateFiles(directory, searchPattern))
        {
            var fileName = Path.GetFileNameWithoutExtension(filePath);

            if (MultiModFileAnalysis.IsSplitFileName(fileName, fileNameWithoutExtension, out var splitNumber)
                && splitNumber > currentSplitCount)
            {
                try
                {
                    fileSystem.File.Delete(filePath);
                }
                catch
                {

                }
            }
        }
    }
}
