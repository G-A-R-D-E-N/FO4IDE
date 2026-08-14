using System.IO.Abstractions;
using Mutagen.Bethesda.Plugins.Exceptions;
using Noggog;
using Path = System.IO.Path;

namespace Mutagen.Bethesda.Plugins.Order.DI;

public interface ITimestampAligner
{

    bool NeedsTimestampAlignment(GameCategory game);

    IEnumerable<ILoadOrderListingGetter> AlignToTimestamps(
        IEnumerable<ILoadOrderListingGetter> incomingLoadOrder,
        DirectoryPath dataPath,
        bool throwOnMissingMods = true);

    IEnumerable<ModKey> AlignToTimestamps(IEnumerable<(ModKey ModKey, DateTime Write)> incomingLoadOrder);

    void AlignTimestamps(
        IEnumerable<ModKey> loadOrder,
        DirectoryPath dataPath,
        bool throwOnMissingMods = true,
        DateTime? startDate = null,
        TimeSpan? interval = null);
}

public sealed class TimestampAligner : ITimestampAligner
{
    private readonly IFileSystem _FileSystem;

    public TimestampAligner(IFileSystem fileSystem)
    {
        _FileSystem = fileSystem;
    }

    public bool NeedsTimestampAlignment(GameCategory game)
    {
        switch (game)
        {
            case GameCategory.Oblivion:
                return true;
            case GameCategory.Skyrim:
                return false;
            case GameCategory.Fallout4:
                return false;
            default:
                throw new NotImplementedException();
        }
    }

    public IEnumerable<ILoadOrderListingGetter> AlignToTimestamps(
        IEnumerable<ILoadOrderListingGetter> incomingLoadOrder,
        DirectoryPath dataPath,
        bool throwOnMissingMods = true)
    {
        var list = new List<(bool Enabled, ModKey ModKey, DateTime Write)>();
        foreach (var key in incomingLoadOrder)
        {
            ModPath modPath = new ModPath(key.ModKey, Path.Combine(dataPath.Path, key.ModKey.FileName));
            if (!_FileSystem.File.Exists(modPath.Path))
            {
                if (throwOnMissingMods) throw new MissingModException(modPath);
                continue;
            }
            list.Add((key.Enabled, key.ModKey, _FileSystem.File.GetLastWriteTime(modPath.Path.Path)));
        }
        var comp = new LoadOrderTimestampComparer(incomingLoadOrder.Select(i => i.ModKey).ToList());
        return list
            .OrderBy(i => (i.ModKey, i.Write), comp)
            .Select(i => new LoadOrderListing(i.ModKey, i.Enabled));
    }

    public IEnumerable<ModKey> AlignToTimestamps(IEnumerable<(ModKey ModKey, DateTime Write)> incomingLoadOrder)
    {
        return incomingLoadOrder
            .OrderBy(i => i, new LoadOrderTimestampComparer(incomingLoadOrder.Select(i => i.ModKey).ToList()))
            .Select(i => i.ModKey);
    }

    public void AlignTimestamps(
        IEnumerable<ModKey> loadOrder,
        DirectoryPath dataPath,
        bool throwOnMissingMods = true,
        DateTime? startDate = null,
        TimeSpan? interval = null)
    {
        startDate ??= DateTime.Today.AddDays(-1);
        interval ??= TimeSpan.FromMinutes(1);
        foreach (var mod in loadOrder)
        {
            ModPath modPath = new ModPath(mod, Path.Combine(dataPath.Path, mod.FileName));
            if (!modPath.Path.Exists)
            {
                if (throwOnMissingMods) throw new MissingModException(modPath);
                continue;
            }
            _FileSystem.File.SetLastWriteTime(modPath.Path.Path, startDate.Value);
            startDate = startDate.Value.Add(interval.Value);
        }
    }
}