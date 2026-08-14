using DynamicData;
using Mutagen.Bethesda.Plugins.Records;
using Noggog;
using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.IO.Abstractions;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using Mutagen.Bethesda.Environments.DI;
using Mutagen.Bethesda.Installs.DI;
using Mutagen.Bethesda.Plugins.Exceptions;
using Mutagen.Bethesda.Plugins.Implicit.DI;
using Mutagen.Bethesda.Plugins.Order.DI;
using Mutagen.Bethesda.Plugins.Records.DI;
using Mutagen.Bethesda.Plugins.Utility;
using StrongInject;

namespace Mutagen.Bethesda.Plugins.Order;




public static partial class LoadOrder
{
    private static TimestampAligner Aligner = new(IFileSystemExt.DefaultFilesystem);
    private static OrderListings Orderer = new();

    #region Timestamps






    public static bool NeedsTimestampAlignment(GameCategory game) => Aligner.NeedsTimestampAlignment(game);










    public static IEnumerable<ILoadOrderListingGetter> AlignToTimestamps(
        IEnumerable<ILoadOrderListingGetter> incomingLoadOrder,
        DirectoryPath dataPath,
        bool throwOnMissingMods = true)
    {
        return Aligner.AlignToTimestamps(incomingLoadOrder, dataPath, throwOnMissingMods: throwOnMissingMods);
    }







    public static IEnumerable<ModKey> AlignToTimestamps(IEnumerable<(ModKey ModKey, DateTime Write)> incomingLoadOrder)
    {
        return Aligner.AlignToTimestamps(incomingLoadOrder);
    }










    public static void AlignTimestamps(
        IEnumerable<ModKey> loadOrder,
        DirectoryPath dataPath,
        bool throwOnMissingMods = true,
        DateTime? startDate = null,
        TimeSpan? interval = null)
    {
        Aligner.AlignTimestamps(
            loadOrder,
            dataPath,
            throwOnMissingMods: throwOnMissingMods,
            startDate: startDate,
            interval: interval);
    }

    #endregion

    [RegisterModule(typeof(MutagenStrongInjectModule))]
    internal partial class GetLoadOrderListingsModule : IContainer<ILoadOrderListingsProvider>
    {
        [Instance] private readonly IGameReleaseContext _release;
        [Instance] private readonly IFileSystem _fileSystem;
        [Instance] private readonly IDataDirectoryProvider _dataDirectory;
        [Instance] private readonly ITimestampedPluginListingsPreferences _timestampedPrefs;

        public GetLoadOrderListingsModule(
            GameRelease release,
            DirectoryPath dataPath,
            bool throwOnMissingMods,
            IFileSystem? fileSystem)
        {
            _release = new GameReleaseInjection(release);
            _fileSystem = fileSystem.GetOrDefault();
            _dataDirectory = new DataDirectoryInjection(dataPath);
            _timestampedPrefs = new TimestampedPluginListingsPreferences()
            {
                ThrowOnMissingMods = throwOnMissingMods
            };
        }
    }












    public static IEnumerable<ILoadOrderListingGetter> GetLoadOrderListings(
        GameRelease game,
        DirectoryPath dataPath,
        bool throwOnMissingMods = true,
        IFileSystem? fileSystem = null)
    {
        var prov = new GetLoadOrderListingsModule(game, dataPath, throwOnMissingMods, fileSystem)
            .Resolve().Value;
        return prov.Get();
    }

    [RegisterModule(typeof(MutagenStrongInjectModule))]
    internal partial class GetLoadOrderListingsPluginsOverrideModule : IContainer<ILoadOrderListingsProvider>
    {
        [Instance] private readonly IGameReleaseContext _release;
        [Instance] private readonly IFileSystem _fileSystem;
        [Instance] private readonly IDataDirectoryProvider _dataDirectory;
        [Instance] private readonly ITimestampedPluginListingsPreferences _timestampedPrefs;
        [Instance] private readonly ICreationClubListingsPathProvider _creationClubListingsPathProvider;
        [Instance] private readonly IPluginListingsPathContext _pluginListingsPathContext;

        public GetLoadOrderListingsPluginsOverrideModule(
            GameRelease release,
            FilePath pluginsFilePath,
            FilePath? creationClubFilePath,
            DirectoryPath dataPath,
            bool throwOnMissingMods,
            IFileSystem? fileSystem)
        {
            _release = new GameReleaseInjection(release);
            _fileSystem = fileSystem.GetOrDefault();
            _pluginListingsPathContext = new PluginListingsPathInjection(pluginsFilePath);
            _dataDirectory = new DataDirectoryInjection(dataPath);
            _creationClubListingsPathProvider = new CreationClubListingsPathInjection(creationClubFilePath);
            _timestampedPrefs = new TimestampedPluginListingsPreferences()
            {
                ThrowOnMissingMods = throwOnMissingMods
            };
        }
    }

    public static IEnumerable<ILoadOrderListingGetter> GetLoadOrderListings(
        GameRelease game,
        FilePath pluginsFilePath,
        FilePath? creationClubFilePath,
        DirectoryPath dataPath,
        bool throwOnMissingMods = true,
        IFileSystem? fileSystem = null)
    {
        var prov = new GetLoadOrderListingsPluginsOverrideModule(
                game, pluginsFilePath, creationClubFilePath,
                dataPath, throwOnMissingMods, fileSystem)
            .Resolve().Value;
        return prov.Get();
    }

    public static IEnumerable<T> OrderListings<T>(IEnumerable<T> e, Func<T, ModKey> selector)
    {
        return Orderer.Order(e, selector);
    }

    public static IEnumerable<T> OrderListings<T>(
        IEnumerable<T> implicitListings,
        IEnumerable<T> pluginsListings,
        IEnumerable<T> creationClubListings,
        Func<T, ModKey> selector)
    {
        return Orderer.Order(implicitListings, pluginsListings, creationClubListings, selector);
    }

    [RegisterModule(typeof(MutagenStrongInjectModule))]
    internal partial class LiveLoadOrderProviderModule : IContainer<ILiveLoadOrderProvider>
    {
        [Instance] private readonly IGameReleaseContext _release;
        [Instance] private readonly IFileSystem _fileSystem;
        [Instance] private readonly IDataDirectoryProvider _dataDirectory;
        [Instance] private readonly ITimestampedPluginListingsPreferences _timestampedPrefs;

        public LiveLoadOrderProviderModule(
            GameRelease release,
            DirectoryPath dataFolderPath,
            bool throwOnMissingMods,
            IFileSystem? fileSystem)
        {
            _release = new GameReleaseInjection(release);
            _dataDirectory = new DataDirectoryInjection(dataFolderPath);
            _fileSystem = fileSystem.GetOrDefault();
            _timestampedPrefs = new TimestampedPluginListingsPreferences()
            {
                ThrowOnMissingMods = throwOnMissingMods
            };
        }
    }

    public static IObservable<IChangeSet<ILoadOrderListingGetter>> GetLiveLoadOrderListings(
        GameRelease game,
        DirectoryPath dataFolderPath,
        out IObservable<ErrorResponse> state,
        bool throwOnMissingMods = true,
        IScheduler? scheduler = null,
        IFileSystem? fileSystem = null)
    {
        var prov = new LiveLoadOrderProviderModule(game, dataFolderPath, throwOnMissingMods, fileSystem);
        return prov.Resolve().Value.Get(out state, scheduler);
    }

    public static IObservable<IChangeSet<ILoadOrderListingGetter>> GetLiveLoadOrderListings(
        IObservable<GameRelease> game,
        IObservable<DirectoryPath> dataFolderPath,
        out IObservable<ErrorResponse> state,
        bool throwOnMissingMods = true,
        IScheduler? scheduler = null)
    {
        var obs = Observable.CombineLatest(
                game,
                dataFolderPath,
                (gameVal, dataFolderVal) =>
                {
                    var lo = GetLiveLoadOrderListings(
                        game: gameVal,
                        dataFolderPath: dataFolderVal,
                        loadOrderFilePath: PluginListings.GetListingsPath(gameVal),
                        cccLoadOrderFilePath: CreationClubListings.GetListingsPath(gameVal.ToCategory(), dataFolderVal),
                        state: out var state,
                        throwOnMissingMods: throwOnMissingMods,
                        scheduler: scheduler);
                    return (LoadOrder: lo, State: state);
                })
            .Replay(1)
            .RefCount();
        state = obs.Select(x => x.State)
            .Switch();
        return obs.Select(x => x.LoadOrder)
            .Switch();
    }

    [RegisterModule(typeof(MutagenStrongInjectModule))]
    internal partial class GetLiveLoadOrderListingsPluginsListingsOverrideModule : IContainer<ILiveLoadOrderProvider>
    {
        [Instance] private readonly IGameReleaseContext _release;
        [Instance] private readonly IFileSystem _fileSystem;
        [Instance] private readonly IDataDirectoryProvider _dataDirectory;
        [Instance] private readonly ITimestampedPluginListingsPreferences _timestampedPrefs;
        [Instance] private readonly IPluginListingsPathContext _listingsPathContext;
        [Instance] private readonly ILiveLoadOrderTimings _liveLoadOrderTimings;
        private readonly FilePath? _cccLoadOrderFilePath;
        [Factory] private ICreationClubListingsPathProvider CreateCccPathProvider(
            IGameCategoryContext categoryContext,
            ICreationClubEnabledProvider isUsed,
            IGameDirectoryProvider gameDirectoryProvider) => _cccLoadOrderFilePath == null
            ? new CreationClubListingsPathProvider(categoryContext, isUsed, gameDirectoryProvider)
            : new CreationClubListingsPathInjection(_cccLoadOrderFilePath);

        public GetLiveLoadOrderListingsPluginsListingsOverrideModule(
            GameRelease release,
            FilePath loadOrderFilePath,
            DirectoryPath dataFolderPath,
            bool throwOnMissingMods,
            FilePath? cccLoadOrderFilePath,
            ILiveLoadOrderTimings? timings,
            IFileSystem? fileSystem)
        {
            _cccLoadOrderFilePath = cccLoadOrderFilePath;
            _liveLoadOrderTimings = timings ?? new LiveLoadOrderTimings();
            _listingsPathContext = new PluginListingsPathInjection(loadOrderFilePath);
            _release = new GameReleaseInjection(release);
            _dataDirectory = new DataDirectoryInjection(dataFolderPath);
            _fileSystem = fileSystem.GetOrDefault();
            _timestampedPrefs = new TimestampedPluginListingsPreferences()
            {
                ThrowOnMissingMods = throwOnMissingMods
            };
        }
    }

    public static IObservable<IChangeSet<ILoadOrderListingGetter>> GetLiveLoadOrderListings(
        GameRelease game,
        FilePath loadOrderFilePath,
        DirectoryPath dataFolderPath,
        out IObservable<ErrorResponse> state,
        FilePath? cccLoadOrderFilePath = null,
        bool throwOnMissingMods = true,
        IScheduler? scheduler = null,
        IFileSystem? fileSystem = null,
        ILiveLoadOrderTimings? timings = null)
    {
        var prov = new GetLiveLoadOrderListingsPluginsListingsOverrideModule(
            game, loadOrderFilePath, dataFolderPath, throwOnMissingMods, cccLoadOrderFilePath, timings, fileSystem);
        return prov.Resolve().Value.Get(out state, scheduler);
    }

    public static IObservable<IChangeSet<ILoadOrderListingGetter>> GetLiveLoadOrderListings(
        IObservable<GameRelease> game,
        IObservable<FilePath> loadOrderFilePath,
        IObservable<DirectoryPath> dataFolderPath,
        out IObservable<ErrorResponse> state,
        IObservable<FilePath?>? cccLoadOrderFilePath = null,
        bool throwOnMissingMods = true,
        IScheduler? scheduler = null)
    {
        var obs = Observable.CombineLatest(
                game,
                dataFolderPath,
                loadOrderFilePath,
                cccLoadOrderFilePath ?? Observable.Return(default(FilePath?)),
                (gameVal, dataFolderVal, loadOrderFilePathVal, cccVal) =>
                {
                    var lo = GetLiveLoadOrderListings(
                        game: gameVal,
                        dataFolderPath: dataFolderVal,
                        loadOrderFilePath: loadOrderFilePathVal,
                        cccLoadOrderFilePath: cccVal,
                        state: out var state,
                        throwOnMissingMods: throwOnMissingMods,
                        scheduler: scheduler);
                    return (LoadOrder: lo, State: state);
                })
            .Replay(1)
            .RefCount();
        state = obs.Select(x => x.State)
            .Switch();
        return obs.Select(x => x.LoadOrder)
            .Switch();
    }

    [RegisterModule(typeof(MutagenStrongInjectModule))]
    internal partial class ImportDataFolderModule<TMod> : IContainer<ILoadOrderImporter<TMod>>
        where TMod : class, IModGetter
    {
        [Instance] private readonly IGameReleaseContext _release;
        [Instance] private readonly IFileSystem _fileSystem;
        [Instance] private readonly IDataDirectoryProvider _dataDirectory;
        [Instance] private readonly ILoadOrderListingsProvider _loadOrder;

        public ImportDataFolderModule(
            GameRelease release,
            DirectoryPath dataPath,
            IEnumerable<ILoadOrderListingGetter> loadOrder,
            IFileSystem? fileSystem)
        {
            _release = new GameReleaseInjection(release);
            _dataDirectory = new DataDirectoryInjection(dataPath);
            _fileSystem = fileSystem.GetOrDefault();
            _loadOrder = new LoadOrderListingsInjection(loadOrder);
        }
    }










    public static ILoadOrder<IModListing<TMod>> Import<TMod>(
        DirectoryPath dataFolder,
        IEnumerable<ILoadOrderListingGetter> loadOrder,
        GameRelease gameRelease,
        IFileSystem? fileSystem = null)
        where TMod : class, IModGetter
    {
        return new ImportDataFolderModule<TMod>(gameRelease, dataFolder, loadOrder, fileSystem)
            .Resolve().Value.Import();
    }










    public static ILoadOrder<IModListing<TMod>> Import<TMod>(
        DirectoryPath dataFolder,
        IEnumerable<ModKey> loadOrder,
        GameRelease gameRelease,
        IFileSystem? fileSystem = null)
        where TMod : class, IModGetter
    {
        return Import<TMod>(
            dataFolder,
            loadOrder.Select(x => new LoadOrderListing(x, true)),
            gameRelease,
            fileSystem);
    }

    [RegisterModule(typeof(MutagenStrongInjectModule))]
    internal partial class ImportDataFolderModFactoryModule<TMod> : IContainer<ILoadOrderImporter<TMod>>
        where TMod : class, IModKeyed
    {
        [Instance] private readonly IGameReleaseContext _release;
        [Instance] private readonly IFileSystem _fileSystem;
        [Instance] private readonly IDataDirectoryProvider _dataDirectory;
        [Instance] private readonly IModImporter<TMod> _modImporter;
        [Instance] private readonly ILoadOrderListingsProvider _loadOrder;

        public ImportDataFolderModFactoryModule(
            GameRelease release,
            DirectoryPath dataPath,
            IEnumerable<ILoadOrderListingGetter> loadOrder,
            Func<ModPath, TMod> factory,
            IFileSystem? fileSystem)
        {
            _release = new GameReleaseInjection(release);
            _dataDirectory = new DataDirectoryInjection(dataPath);
            _fileSystem = fileSystem.GetOrDefault();
            _modImporter = new ModImporterWrapper<TMod>(factory);
            _loadOrder = new LoadOrderListingsInjection(loadOrder);
        }
    }









    public static ILoadOrder<IModListing<TMod>> Import<TMod>(
        DirectoryPath dataFolder,
        IEnumerable<ModKey> loadOrder,
        GameRelease gameRelease,
        Func<ModPath, TMod> factory,
        IFileSystem? fileSystem = null)
        where TMod : class, IModKeyed
    {
        return new ImportDataFolderModFactoryModule<TMod>(gameRelease, dataFolder, loadOrder.Select(x => new LoadOrderListing(x, true)), factory, fileSystem)
            .Resolve().Value.Import();
    }









    public static ILoadOrder<IModListing<TMod>> Import<TMod>(
        DirectoryPath dataFolder,
        IEnumerable<ILoadOrderListingGetter> loadOrder,
        GameRelease gameRelease,
        Func<ModPath, TMod> factory,
        IFileSystem? fileSystem = null)
        where TMod : class, IModKeyed
    {
        return new ImportDataFolderModFactoryModule<TMod>(gameRelease, dataFolder, loadOrder, factory, fileSystem)
            .Resolve().Value.Import();
    }

    [RegisterModule(typeof(MutagenStrongInjectModule))]
    internal partial class ImportModule<TMod> : IContainer<ILoadOrderImporter<TMod>>
        where TMod : class, IModGetter
    {
        [Instance] private readonly IGameReleaseContext _release;
        [Instance] private readonly IFileSystem _fileSystem;
        [Instance] private readonly ILoadOrderListingsProvider _loadOrder;

        public ImportModule(
            GameRelease release,
            IEnumerable<ILoadOrderListingGetter> loadOrder,
            IFileSystem? fileSystem)
        {
            _release = new GameReleaseInjection(release);
            _fileSystem = fileSystem.GetOrDefault();
            _loadOrder = new LoadOrderListingsInjection(loadOrder);
        }
    }









    public static ILoadOrder<IModListing<TMod>> Import<TMod>(
        IEnumerable<ILoadOrderListingGetter> loadOrder,
        GameRelease gameRelease,
        IFileSystem? fileSystem = null)
        where TMod : class, IModGetter
    {
        return new ImportModule<TMod>(gameRelease, loadOrder, fileSystem)
            .Resolve().Value.Import();
    }









    public static ILoadOrder<IModListing<TMod>> Import<TMod>(
        IEnumerable<ModKey> loadOrder,
        GameRelease gameRelease,
        IFileSystem? fileSystem = null)
        where TMod : class, IModGetter
    {
        return Import<TMod>(
            loadOrder.Select(x => new LoadOrderListing(x, true)),
            gameRelease,
            fileSystem);
    }

    [RegisterModule(typeof(MutagenStrongInjectModule))]
    internal partial class ImportModFactoryModule<TMod> : IContainer<ILoadOrderImporter<TMod>>
        where TMod : class, IModKeyed
    {
        [Instance] private readonly IGameReleaseContext _release;
        [Instance] private readonly IFileSystem _fileSystem;
        [Instance] private readonly IModImporter<TMod> _modImporter;
        [Instance] private readonly ILoadOrderListingsProvider _loadOrder;

        public ImportModFactoryModule(
            GameRelease release,
            IEnumerable<ILoadOrderListingGetter> loadOrder,
            Func<ModPath, TMod> factory,
            IFileSystem? fileSystem)
        {
            _release = new GameReleaseInjection(release);
            _fileSystem = fileSystem.GetOrDefault();
            _modImporter = new ModImporterWrapper<TMod>(factory);
            _loadOrder = new LoadOrderListingsInjection(loadOrder);
        }
    }










    public static ILoadOrder<IModListing<TMod>> Import<TMod>(
        IEnumerable<ModKey> loadOrder,
        GameRelease gameRelease,
        Func<ModPath, TMod> factory,
        IFileSystem? fileSystem = null)
        where TMod : class, IModKeyed
    {
        return new ImportModFactoryModule<TMod>(gameRelease,
                loadOrder.Select(x => new LoadOrderListing(x, true)),
                factory, fileSystem)
            .Resolve().Value.Import();
    }










    public static ILoadOrder<IModListing<TMod>> Import<TMod>(
        IEnumerable<ILoadOrderListingGetter> loadOrder,
        GameRelease gameRelease,
        Func<ModPath, TMod> factory,
        IFileSystem? fileSystem = null)
        where TMod : class, IModKeyed
    {
        return new ImportModFactoryModule<TMod>(
                gameRelease, loadOrder,
                factory, fileSystem)
            .Resolve().Value.Import();
    }

    [RegisterModule(typeof(MutagenStrongInjectModule))]
    internal partial class LoadOrderImporterModule<TMod> : IContainer<ILoadOrderImporter<TMod>>
        where TMod : class, IModGetter
    {
        [Instance] private readonly IGameReleaseContext _release;
        [Instance] private readonly IFileSystem _fileSystem;

        public LoadOrderImporterModule(
            GameRelease release,
            IFileSystem? fileSystem)
        {
            _release = new GameReleaseInjection(release);
            _fileSystem = fileSystem.GetOrDefault();
        }
    }








    public static ILoadOrder<IModListing<TMod>> Import<TMod>(
        GameRelease gameRelease,
        IFileSystem? fileSystem = null)
        where TMod : class, IModGetter
    {
        return new LoadOrderImporterModule<TMod>(gameRelease, fileSystem).Resolve().Value.Import();
    }

    [RegisterModule(typeof(MutagenStrongInjectModule))]
    internal partial class LoadOrderImporterFactoryModule<TMod> : IContainer<ILoadOrderImporter<TMod>>
        where TMod : class, IModKeyed
    {
        [Instance] private readonly IGameReleaseContext _release;
        [Instance] private readonly IFileSystem _fileSystem;
        [Instance] private readonly IModImporter<TMod> _modImporter;

        public LoadOrderImporterFactoryModule(
            GameRelease release,
            IFileSystem? fileSystem,
            Func<ModPath, TMod> factory)
        {
            _release = new GameReleaseInjection(release);
            _fileSystem = fileSystem.GetOrDefault();
            _modImporter = new ModImporterWrapper<TMod>(factory);
        }
    }









    public static ILoadOrder<IModListing<TMod>> Import<TMod>(
        GameRelease gameRelease,
        Func<ModPath, TMod> factory,
        IFileSystem? fileSystem = null)
        where TMod : class, IModKeyed
    {
        return new LoadOrderImporterFactoryModule<TMod>(gameRelease, fileSystem, factory).Resolve().Value.Import();
    }

    [RegisterModule(typeof(MutagenStrongInjectModule))]
    internal partial class LoadOrderImporterDataFolderModule<TMod> : IContainer<ILoadOrderImporter<TMod>>
        where TMod : class, IModGetter
    {
        [Instance] private readonly IGameReleaseContext _release;
        [Instance] private readonly IFileSystem _fileSystem;
        [Instance] private readonly IDataDirectoryProvider _dataDirectory;

        public LoadOrderImporterDataFolderModule(
            GameRelease release,
            IFileSystem? fileSystem,
            DirectoryPath dataFolder)
        {
            _release = new GameReleaseInjection(release);
            _fileSystem = fileSystem.GetOrDefault();
            _dataDirectory = new DataDirectoryInjection(dataFolder);
        }
    }









    public static ILoadOrder<IModListing<TMod>> Import<TMod>(
        DirectoryPath dataFolder,
        GameRelease gameRelease,
        IFileSystem? fileSystem = null)
        where TMod : class, IModGetter
    {
        return new LoadOrderImporterDataFolderModule<TMod>(gameRelease, fileSystem, dataFolder).Resolve().Value.Import();
    }

    [RegisterModule(typeof(MutagenStrongInjectModule))]
    internal partial class LoadOrderImporterDataFolderFactoryModule<TMod> : IContainer<ILoadOrderImporter<TMod>>
        where TMod : class, IModKeyed
    {
        [Instance] private readonly IGameReleaseContext _release;
        [Instance] private readonly IFileSystem _fileSystem;
        [Instance] private readonly IDataDirectoryProvider _dataDirectory;
        [Instance] private readonly IModImporter<TMod> _modImporter;

        public LoadOrderImporterDataFolderFactoryModule(
            GameRelease release,
            IFileSystem? fileSystem,
            Func<ModPath, TMod> factory,
            DirectoryPath dataFolder)
        {
            _release = new GameReleaseInjection(release);
            _fileSystem = fileSystem.GetOrDefault();
            _dataDirectory = new DataDirectoryInjection(dataFolder);
            _modImporter = new ModImporterWrapper<TMod>(factory);
        }
    }










    public static ILoadOrder<IModListing<TMod>> Import<TMod>(
        DirectoryPath dataFolder,
        GameRelease gameRelease,
        Func<ModPath, TMod> factory,
        IFileSystem? fileSystem = null)
        where TMod : class, IModKeyed
    {
        return new LoadOrderImporterDataFolderFactoryModule<TMod>(gameRelease, fileSystem, factory, dataFolder).Resolve().Value.Import();
    }

    public static void Write(
        FilePath path,
        GameRelease release,
        IEnumerable<ILoadOrderListingGetter> loadOrder,
        bool removeImplicitMods = true,
        IFileSystem? fileSystem = null)
    {
        fileSystem ??= IFileSystemExt.DefaultFilesystem;
        var rel = new GameReleaseInjection(release);
        new LoadOrderWriter(
                fileSystem,
                new HasEnabledMarkersProvider(rel),
                new ImplicitListingModKeyProvider(rel))
            .Write(path, loadOrder, removeImplicitMods);
    }

    public static void Write(
        FilePath path,
        GameRelease release,
        ILoadOrderGetter<IModListingGetter> loadOrder,
        bool removeImplicitMods = true,
        IFileSystem? fileSystem = null)
    {
        Write(
            path: path,
            release: release,
            loadOrder: loadOrder.ListedOrder,
            removeImplicitMods: removeImplicitMods,
            fileSystem: fileSystem);
    }

    public static ILoadOrderGetter CreateReadonly(IEnumerable<ModKey> modKeys)
    {
        return new LoadOrderGetter(modKeys);
    }
}

public interface ILoadOrderGetter : IDisposable
{



    int Count { get; }




    bool DisposingItems { get; }




    IEnumerable<ModKey> ListedOrder { get; }




    IEnumerable<ModKey> PriorityOrder { get; }




    bool ContainsKey(ModKey key);
}

public interface ILoadOrderGetter<out TListing> :
    ILoadOrderGetter,
    IReadOnlyList<Noggog.IKeyValue<ModKey, TListing>>,
    IReadOnlyCache<TListing, ModKey>
    where TListing : IModKeyed
{
    new TListing this[int index] { get; }

    TListing? TryGetAtIndex(int index);




    new IEnumerable<TListing> ListedOrder { get; }




    new IEnumerable<TListing> PriorityOrder { get; }




    new int Count { get; }




    new bool ContainsKey(ModKey key);






    int IndexOf(ModKey key);
}

public interface ILoadOrder<TListing> : ILoadOrderGetter<TListing>
    where TListing : IModKeyed
{





    void Add(TListing item);






    void Add(IEnumerable<TListing> items);







    void Add(TListing item, int index);




    void Clear();

    bool RemoveKey(ModKey modKey);

    void RemoveAt(int index);

    void Set(TListing listing);

    void Set(IEnumerable<TListing> items);
}





public sealed class LoadOrder<TListing> : ILoadOrder<TListing>
    where TListing : IModKeyed
{
    private readonly List<ItemContainer> _byLoadOrder = new();
    private readonly Dictionary<ModKey, ItemContainer> _byModKey = new();


    public int Count => _byLoadOrder.Count;

    public bool DisposingItems { get; }


    public TListing this[int index] => _byLoadOrder[index].Item;

    IEnumerable<TListing> IReadOnlyCache<TListing, ModKey>.Items => ListedOrder;


    public IEnumerable<ModKey> Keys => _byModKey.Keys;


    public IEnumerable<TListing> ListedOrder => _byLoadOrder.Select(i => i.Item);


    public IEnumerable<TListing> PriorityOrder =>
        ((IEnumerable<ItemContainer>)_byLoadOrder).Reverse().Select(i => i.Item);

    IEnumerable<ModKey> ILoadOrderGetter.ListedOrder => _byLoadOrder.Select(x => x.Item.ModKey);

    IEnumerable<ModKey> ILoadOrderGetter.PriorityOrder => _byLoadOrder.Select(x => x.Item.ModKey).Reverse();

    Noggog.IKeyValue<ModKey, TListing> IReadOnlyList<Noggog.IKeyValue<ModKey, TListing>>.this[int index]
    {
        get
        {
            var cont = _byLoadOrder[index];
            return new KeyValue<ModKey, TListing>(cont.Item.ModKey, cont.Item);
        }
    }


    public TListing this[ModKey key]
    {
        get
        {
            try
            {
                return _byModKey[key].Item;
            }
            catch (KeyNotFoundException e)
            {
                throw new MissingModException(key, "Tried to retrieve a mod from the load order that did not exist", e);
            }
        }
    }

    public LoadOrder(bool disposeItems = true)
    {
        DisposingItems = disposeItems;
    }

    public LoadOrder(IEnumerable<TListing> items, bool disposeItems = true)
    {
        DisposingItems = disposeItems;
        int index = 0;
        _byLoadOrder.AddRange(items.Select(i => new ItemContainer(i, index++)));
        foreach (var item in _byLoadOrder)
        {
            try
            {
                _byModKey.Add(item.Item.ModKey, item);
            }
            catch (ArgumentException)
            {
                throw new ArgumentException($"ModKey was already present: {item.Item.ModKey}");
            }
        }
    }







    public bool TryGetValue(ModKey key, [MaybeNullWhen(false)] out TListing value)
    {
        if (_byModKey.TryGetValue(key, out var container))
        {
            value = container.Item;
            return true;
        }

        value = default;
        return false;
    }






    public TListing? TryGetValue(ModKey key)
    {
        if (_byModKey.TryGetValue(key, out var container))
        {
            return container.Item;
        }

        return default;
    }






    public TListing? TryGetAtIndex(int index)
    {
        if (!_byLoadOrder.InRange(index))
        {
            return default;
        }

        return _byLoadOrder[index].Item;
    }


    public void Add(TListing item)
    {
        var index = _byLoadOrder.Count;
        var container = new ItemContainer(item, index);
        try
        {
            _byModKey.Add(item.ModKey, container);
        }
        catch (ArgumentException)
        {
            throw new ArgumentException($"ModKey was already present: {item.ModKey}");
        }

        _byLoadOrder.Add(container);
    }


    public void Add(IEnumerable<TListing> items)
    {
        foreach (var item in items)
        {
            Add(item);
        }
    }


    public void Add(TListing item, int index)
    {
        if (!_byLoadOrder.InRange(index))
        {
            throw new ArgumentException("Tried to insert at an out of range index.");
        }

        var container = new ItemContainer(item, index);
        try
        {
            _byModKey.Add(item.ModKey, container);
        }
        catch (ArgumentException)
        {
            throw new ArgumentException($"ModKey was already present: {item.ModKey}");
        }

        _byLoadOrder.Add(container);
        for (int i = index + 1; i < _byLoadOrder.Count; i++)
        {
            _byLoadOrder[i].Index += 1;
        }
    }


    public bool ContainsKey(ModKey key)
    {
        return IndexOf(key) != -1;
    }


    public int IndexOf(ModKey key)
    {
        if (!_byModKey.TryGetValue(key, out var container))
        {
            return -1;
        }

        return container.Index;
    }


    public void Clear()
    {
        Dispose();
        _byLoadOrder.Clear();
        _byModKey.Clear();
    }

    public bool RemoveKey(ModKey key)
    {
        if (!_byModKey.TryGetValue(key, out var registry)) return false;
        _byLoadOrder.RemoveAt(registry.Index);
        for (int i = registry.Index; i < _byLoadOrder.Count; i++)
        {
            _byLoadOrder[i].Index--;
        }

        _byModKey.Remove(key);
        return true;
    }

    public void RemoveAt(int index)
    {
        var item = _byLoadOrder[index];
        _byLoadOrder.RemoveAt(index);
        _byModKey.Remove(item.Item.ModKey);
        for (int i = index; i < _byLoadOrder.Count; i++)
        {
            _byLoadOrder[i].Index--;
        }

        if (DisposingItems && item.Item is IDisposable disp)
        {
            disp.Dispose();
        }
    }

    public void Set(TListing listing)
    {
        if (!_byModKey.TryGetValue(listing.ModKey, out var existing))
        {
            Add(listing);
            return;
        }

        var old = existing.Item;
        existing.Item = listing;

        if (DisposingItems && old is IDisposable disp)
        {
            disp.Dispose();
        }
    }

    public void Set(IEnumerable<TListing> items)
    {
        foreach (var item in items)
        {
            Set(item);
        }
    }

    IEnumerator<Noggog.IKeyValue<ModKey, TListing>> IEnumerable<Noggog.IKeyValue<ModKey, TListing>>.GetEnumerator()
    {
        return ListedOrder.Select(x => (Noggog.IKeyValue<ModKey, TListing>)new KeyValue<ModKey, TListing>(x.ModKey, x))
            .GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();




    public void Dispose()
    {
        if (!DisposingItems) return;
        foreach (var item in _byLoadOrder)
        {
            if (item.Item is IDisposable disp)
            {
                disp.Dispose();
            }
        }
    }

    public IEnumerator<TListing> GetEnumerator()
    {
        foreach (var item in _byLoadOrder)
        {
            yield return item.Item;
        }
    }

    private class ItemContainer
    {
        public TListing Item;
        public int Index;

        public ItemContainer(TListing item, int index)
        {
            Item = item;
            Index = index;
        }
    }
}

internal class LoadOrderGetter : ILoadOrderGetter
{
    private readonly IReadOnlyList<ModKey> _byLoadOrder;
    private readonly HashSet<ModKey> _byModKey;

    public int Count => _byLoadOrder.Count;
    public bool DisposingItems => false;

    public IEnumerable<ModKey> ListedOrder => _byLoadOrder;

    public IEnumerable<ModKey> PriorityOrder => _byLoadOrder.Reverse();

    public LoadOrderGetter(IEnumerable<ModKey> listedOrder)
    {
        _byLoadOrder = listedOrder.ToArray();
        _byModKey = listedOrder.ToHashSet();
    }

    public bool ContainsKey(ModKey key)
    {
        return _byModKey.Contains(key);
    }

    public void Dispose()
    {
    }
}