using System.IO.Abstractions;
using Mutagen.Bethesda.Environments.DI;
using Path = System.IO.Path;

namespace Mutagen.Bethesda.Plugins.Order.DI;

public interface ICreationClubListingsProvider : IListingsProvider
{

    public IEnumerable<IModListingGetter> Get(bool throwIfMissing);
}

public sealed class CreationClubListingsProvider : ICreationClubListingsProvider
{
    private readonly IFileSystem _fileSystem;
    public IDataDirectoryProvider DirectoryProvider { get; }
    public ICreationClubListingsPathProvider ListingsPathProvider { get; }
    public ICreationClubRawListingsReader Reader { get; }

    public CreationClubListingsProvider(
        IFileSystem fileSystem,
        IDataDirectoryProvider dataDirectoryProvider,
        ICreationClubListingsPathProvider pluginListingsPathProvider,
        ICreationClubRawListingsReader reader)
    {
        _fileSystem = fileSystem;
        DirectoryProvider = dataDirectoryProvider;
        ListingsPathProvider = pluginListingsPathProvider;
        Reader = reader;
    }

    public IEnumerable<IModListingGetter> Get()
    {
        return Get(throwIfMissing: true);
    }

    IEnumerable<ILoadOrderListingGetter> IListingsProvider.Get() => Get();

    public IEnumerable<IModListingGetter> Get(bool throwIfMissing)
    {
        var path = ListingsPathProvider.Path;
        if (path == null) return [];
        if (!_fileSystem.File.Exists(path.Value))
        {
            if (throwIfMissing)
            {
                throw new FileNotFoundException("Could not locate ccc plugin file", path.Value.Path);
            }
            else
            {
                return [];
            }
        }

        return Reader.Read(_fileSystem.File.OpenRead(path.Value))
            .Where(x => _fileSystem.File.Exists(Path.Combine(DirectoryProvider.Path, x.ModKey.FileName)))
            .Select(x => x.ToModListing(modExists: true))
            .ToList();
    }
}