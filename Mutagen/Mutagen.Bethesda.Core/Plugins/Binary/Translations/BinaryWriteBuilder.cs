using System.IO.Abstractions;
using Mutagen.Bethesda.Environments.DI;
using Mutagen.Bethesda.Installs.DI;
using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Masters;
using Mutagen.Bethesda.Plugins.Masters.DI;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Strings;
using Mutagen.Bethesda.Strings.DI;
using Noggog;



namespace Mutagen.Bethesda.Plugins.Binary.Translations;




internal record BinaryWriteBuilderParams<TModGetter>
    where TModGetter : class, IModGetter
{
    internal required GameRelease _gameRelease { get; init; }
    internal IBinaryWriteBuilderWriter<TModGetter> _writer { get; init; } = null!;
    internal BinaryWriteParameters _param { get; init; } = BinaryWriteParameters.Default;
    internal FilePath? _path { get; init; }
    internal Stream? _stream { get; init; }
    internal Func<TModGetter, BinaryWriteBuilderParams<TModGetter>, BinaryWriteParameters>? _masterSyncAction { get; init; }
    internal Func<TModGetter, BinaryWriteBuilderParams<TModGetter>, IReadOnlyCollection<ModKey>, BinaryWriteParameters>? _loadOrderSetter { get; init; }
    internal Func<TModGetter, BinaryWriteParameters, DirectoryPath>? _dataFolderGetter { get; init; }
    internal IModMasterStyledGetter[] KnownMasters { get; init; } = [];
    internal ILoadOrderGetter<IModListingGetter<IModGetter>>? _knownModLoadOrder { get; init; }
    internal bool _autoSplit { get; init; } = false;
}




internal interface IBinaryWriteBuilderWriter<TModGetter>
    where TModGetter : class, IModGetter
{
    Task WriteAsync(TModGetter mod, BinaryWriteBuilderParams<TModGetter> param);
    void Write(TModGetter mod, BinaryWriteBuilderParams<TModGetter> param);
}

public interface IBinaryModdedWriteBuilderTargetChoice
{






    public IBinaryModdedWriteBuilderLoadOrderChoice ToPath(FilePath path, IFileSystem? fileSystem = null);







    public IBinaryModdedWriteBuilderLoadOrderChoice IntoFolder(DirectoryPath folderPath, IFileSystem? fileSystem = null);
}

public record BinaryModdedWriteBuilderTargetChoice<TModGetter> : IBinaryModdedWriteBuilderTargetChoice
    where TModGetter : class, IModGetter
{
    internal BinaryWriteBuilderParams<TModGetter> _params;
    internal TModGetter _mod { get; init; } = null!;

    internal BinaryModdedWriteBuilderTargetChoice(
        TModGetter mod,
        IBinaryWriteBuilderWriter<TModGetter> writer)
    {
        _mod = mod;
        _params = new BinaryWriteBuilderParams<TModGetter>()
        {
            _gameRelease = mod.GameRelease,
            _writer = writer,
        };
    }







    public BinaryModdedWriteBuilderLoadOrderChoice<TModGetter> ToPath(FilePath path, IFileSystem? fileSystem = null)
    {
        return new BinaryModdedWriteBuilderLoadOrderChoice<TModGetter>(_mod, _params with
        {
            _path = path,
            _param = _params._param with
            {
                FileSystem = fileSystem
            }
        });
    }







    public BinaryModdedWriteBuilderLoadOrderChoice<TModGetter> IntoFolder(DirectoryPath folderPath, IFileSystem? fileSystem = null)
    {
        var path = Path.Combine(folderPath, _mod.ModKey.FileName);
        return ToPath(path, fileSystem);
    }

    IBinaryModdedWriteBuilderLoadOrderChoice IBinaryModdedWriteBuilderTargetChoice.ToPath(FilePath path, IFileSystem? fileSystem = null) =>
        ToPath(path, fileSystem);

    IBinaryModdedWriteBuilderLoadOrderChoice IBinaryModdedWriteBuilderTargetChoice.IntoFolder(DirectoryPath folderPath, IFileSystem? fileSystem = null) =>
        IntoFolder(folderPath, fileSystem);
}

public record BinaryWriteBuilderTargetChoice<TModGetter>
    where TModGetter : class, IModGetter
{
    internal BinaryWriteBuilderParams<TModGetter> _params;
    internal BinaryWriteBuilderTargetChoice(
        GameRelease release,
        IBinaryWriteBuilderWriter<TModGetter> writer)
    {
        _params = new BinaryWriteBuilderParams<TModGetter>()
        {
            _gameRelease = release,
            _writer = writer,
        };
    }







    public BinaryWriteBuilderLoadOrderChoice<TModGetter> ToPath(FilePath path, IFileSystem? fileSystem = null)
    {
        return new BinaryWriteBuilderLoadOrderChoice<TModGetter>(_params with
        {
            _path = path,
            _param = _params._param with
            {
                FileSystem = fileSystem
            }
        });
    }
}

public interface IBinaryModdedWriteBuilderLoadOrderChoice
{








    public IBinaryModdedWriteBuilder WithNoLoadOrder();






    public IBinaryModdedWriteBuilder WithLoadOrder(
        ILoadOrderGetter<IModListingGetter<IModGetter>> loadOrder);






    public IBinaryModdedWriteBuilder WithLoadOrder(
        ILoadOrderGetter<IModListingGetter<IModMasterStyledGetter>> loadOrder);






    public IBinaryModdedWriteBuilder WithLoadOrder(
        ILoadOrderGetter<IModMasterStyledGetter> loadOrder);






    public IBinaryModdedWriteBuilderDataFolderChoice WithLoadOrder(
        ILoadOrderGetter<ModKey> loadOrder);






    public IBinaryModdedWriteBuilder WithLoadOrder(
        IEnumerable<IModMasterStyledGetter> loadOrder);






    public IBinaryModdedWriteBuilder WithLoadOrder(
        params IModMasterStyledGetter[] loadOrder);





    public IBinaryModdedWriteBuilder WithDefaultLoadOrder();






    public IBinaryModdedWriteBuilderDataFolderChoice WithLoadOrder(
        IEnumerable<ModKey> loadOrder);






    public IBinaryModdedWriteBuilderDataFolderChoice WithLoadOrder(
        params ModKey[] loadOrder);





    public IBinaryModdedWriteBuilderDataFolderChoice WithLoadOrderFromHeaderMasters();
}

public record BinaryModdedWriteBuilderLoadOrderChoice<TModGetter> : IBinaryModdedWriteBuilderLoadOrderChoice
    where TModGetter : class, IModGetter
{
    internal TModGetter _mod { get; init; } = null!;
    internal BinaryWriteBuilderParams<TModGetter> _params;

    internal BinaryModdedWriteBuilderLoadOrderChoice(
        TModGetter mod,
        BinaryWriteBuilderParams<TModGetter> @params)
    {
        _mod = mod;
        _params = @params;
    }









    public BinaryModdedWriteBuilder<TModGetter> WithNoLoadOrder()
    {
        return new BinaryModdedWriteBuilder<TModGetter>(_mod, _params);
    }
    IBinaryModdedWriteBuilder IBinaryModdedWriteBuilderLoadOrderChoice.WithNoLoadOrder() => WithNoLoadOrder();

    public IBinaryModdedWriteBuilder WithLoadOrder(ILoadOrderGetter<IModListingGetter<IModGetter>> loadOrder)
    {
        return new BinaryModdedWriteBuilder<TModGetter>(_mod, _params with
        {
            _loadOrderSetter = (m, p, alreadyKnownMasters) =>
            {
                return p._param with
                {
                    MasterFlagsLookup = loadOrder
                        .Where(x => !alreadyKnownMasters.Contains(x.ModKey))
                        .ResolveExistingMods(),
                    MastersListOrdering = new MastersListOrderingByLoadOrder(loadOrder),
                    LowerRangeDisallowedHandler = ALowerRangeDisallowedHandlerOption.AddPlaceholderIfNotSkipping(loadOrder, p._param.LowerRangeDisallowedHandler)
                };
            },
            _knownModLoadOrder = loadOrder,
        });
    }






    public BinaryModdedWriteBuilder<TModGetter> WithLoadOrder(
        ILoadOrderGetter<IModListingGetter<IModMasterStyledGetter>> loadOrder)
    {
        return new BinaryModdedWriteBuilder<TModGetter>(_mod, _params with
        {
            _loadOrderSetter = (m, p, alreadyKnownMasters) =>
            {
                return p._param with
                {
                    MasterFlagsLookup = loadOrder
                        .Where(x => !alreadyKnownMasters.Contains(x.ModKey))
                        .ResolveExistingMods(),
                    MastersListOrdering = new MastersListOrderingByLoadOrder(loadOrder),
                    LowerRangeDisallowedHandler = ALowerRangeDisallowedHandlerOption.AddPlaceholderIfNotSkipping(loadOrder, p._param.LowerRangeDisallowedHandler)
                };
            }
        });
    }
    IBinaryModdedWriteBuilder IBinaryModdedWriteBuilderLoadOrderChoice.WithLoadOrder(ILoadOrderGetter<IModListingGetter<IModMasterStyledGetter>> loadOrder) => WithLoadOrder(loadOrder);






    public BinaryModdedWriteBuilder<TModGetter> WithLoadOrder(
        ILoadOrderGetter<IModMasterStyledGetter> loadOrder)
    {
        return new BinaryModdedWriteBuilder<TModGetter>(_mod, _params with
        {
            _loadOrderSetter = (m, p, alreadyKnownMasters) =>
            {
                return p._param with
                {
                    MasterFlagsLookup = loadOrder
                        .Where(x => !alreadyKnownMasters.Contains(x.ModKey)),
                    MastersListOrdering = new MastersListOrderingByLoadOrder(loadOrder),
                    LowerRangeDisallowedHandler = ALowerRangeDisallowedHandlerOption.AddPlaceholderIfNotSkipping(loadOrder, p._param.LowerRangeDisallowedHandler)
                };
            }
        });
    }
    IBinaryModdedWriteBuilder IBinaryModdedWriteBuilderLoadOrderChoice.WithLoadOrder(ILoadOrderGetter<IModMasterStyledGetter> loadOrder) => WithLoadOrder(loadOrder);






    public BinaryModdedWriteBuilderDataFolderChoice<TModGetter> WithLoadOrder(
        ILoadOrderGetter<ModKey> loadOrder)
    {
        return WithLoadOrder(loadOrder.ListedOrder);
    }
    IBinaryModdedWriteBuilderDataFolderChoice IBinaryModdedWriteBuilderLoadOrderChoice.WithLoadOrder(ILoadOrderGetter<ModKey> loadOrder) => WithLoadOrder(loadOrder);






    public BinaryModdedWriteBuilder<TModGetter> WithLoadOrder(
        params IModMasterStyledGetter[] loadOrder)
    {
        return WithLoadOrder(new LoadOrder<IModMasterStyledGetter>(loadOrder, disposeItems: false));
    }
    IBinaryModdedWriteBuilder IBinaryModdedWriteBuilderLoadOrderChoice.WithLoadOrder(params IModMasterStyledGetter[] loadOrder) => WithLoadOrder(loadOrder);






    public BinaryModdedWriteBuilder<TModGetter> WithLoadOrder(
        IEnumerable<IModMasterStyledGetter> loadOrder)
    {
        return WithLoadOrder(new LoadOrder<IModMasterStyledGetter>(loadOrder, disposeItems: false));
    }
    IBinaryModdedWriteBuilder IBinaryModdedWriteBuilderLoadOrderChoice.WithLoadOrder(IEnumerable<IModMasterStyledGetter> loadOrder) => WithLoadOrder(loadOrder);







    public BinaryModdedWriteBuilder<TModGetter> WithLoadOrder(
        ILoadOrderGetter<IModListingGetter<TModGetter>> loadOrder)
    {
        return new BinaryModdedWriteBuilder<TModGetter>(_mod, _params with
        {
            _knownModLoadOrder = loadOrder,
            _loadOrderSetter = (m, p, alreadyKnownMasters) =>
            {
                return p._param with
                {
                    MasterFlagsLookup = loadOrder
                        .Where(x => !alreadyKnownMasters.Contains(x.ModKey))
                        .ResolveExistingMods(),
                    MastersListOrdering = new MastersListOrderingByLoadOrder(loadOrder),
                    LowerRangeDisallowedHandler = ALowerRangeDisallowedHandlerOption.AddPlaceholderIfNotSkipping(loadOrder, p._param.LowerRangeDisallowedHandler)
                };
            }
        });
    }






    public BinaryModdedWriteBuilder<TModGetter> WithLoadOrder(
        ILoadOrderGetter<TModGetter> loadOrder)
    {
        return new BinaryModdedWriteBuilder<TModGetter>(_mod, _params with
        {
            _knownModLoadOrder = loadOrder.Transform(x => new ModListing<TModGetter>(x)),
            _loadOrderSetter = (m, p, alreadyKnownMasters) =>
            {
                return p._param with
                {
                    MasterFlagsLookup = loadOrder
                        .Where(x => !alreadyKnownMasters.Contains(x.ModKey)),
                    MastersListOrdering = new MastersListOrderingByLoadOrder(loadOrder),
                    LowerRangeDisallowedHandler = ALowerRangeDisallowedHandlerOption.AddPlaceholderIfNotSkipping(loadOrder, p._param.LowerRangeDisallowedHandler)
                };
            }
        });
    }






    public BinaryModdedWriteBuilder<TModGetter> WithLoadOrder(
        params TModGetter[] loadOrder)
    {
        return WithLoadOrder(new LoadOrder<TModGetter>(loadOrder, disposeItems: false));
    }






    public BinaryModdedWriteBuilder<TModGetter> WithLoadOrder(
        IEnumerable<TModGetter> loadOrder)
    {
        return WithLoadOrder(new LoadOrder<TModGetter>(loadOrder, disposeItems: false));
    }





    public BinaryModdedWriteBuilder<TModGetter> WithDefaultLoadOrder()
    {
        return new BinaryModdedWriteBuilder<TModGetter>(_mod, _params with
        {
            _dataFolderGetter = (m, p) => GameLocatorLookupCache.Instance.GetDataDirectory(m.GameRelease),
            _loadOrderSetter = (m, p, alreadyKnownMasters) =>
            {
                var dataFolder = p._dataFolderGetter?.Invoke(m, p._param) ?? throw new ArgumentNullException("Data folder source was not set");
                var lo = LoadOrder.Import<IModMasterStyledGetter>(
                    dataFolder,
                    m.GameRelease,
                    factory: (modPath) => KeyedMasterStyle.FromPath(modPath, p._gameRelease, p._param.FileSystem),
                    p._param.FileSystem);

                ILoadOrderGetter<IModMasterStyledGetter>? modFlagsLo = null;
                if (GameConstants.Get(m.GameRelease).SeparateMasterLoadOrders)
                {
                    modFlagsLo = lo
                        .ResolveExistingMods(disposeItems: false);
                }
                return p._param with
                {
                    MasterFlagsLookup = modFlagsLo?.Where(x => !alreadyKnownMasters.Contains(x.ModKey)),
                    MastersListOrdering = new MastersListOrderingByLoadOrder(lo),
                    LowerRangeDisallowedHandler = ALowerRangeDisallowedHandlerOption.AddPlaceholderIfNotSkipping(lo, p._param.LowerRangeDisallowedHandler)
                };
            }
        });
    }
    IBinaryModdedWriteBuilder IBinaryModdedWriteBuilderLoadOrderChoice.WithDefaultLoadOrder() => WithDefaultLoadOrder();






    public BinaryModdedWriteBuilderDataFolderChoice<TModGetter> WithLoadOrder(
        IEnumerable<ModKey> loadOrder)
    {
        return new BinaryModdedWriteBuilderDataFolderChoice<TModGetter>(_mod, _params with
        {
            _loadOrderSetter = (m, p, alreadyKnownMasters) =>
            {
                ModKey[] loArray = loadOrder.ToArray();
                var dataFolder = p._dataFolderGetter?.Invoke(m, p._param);
                if (dataFolder == null || !GameConstants.Get(m.GameRelease).SeparateMasterLoadOrders)
                {
                    return p._param with
                    {
                        MastersListOrdering = new MastersListOrderingByLoadOrder(loArray),
                        LowerRangeDisallowedHandler = ALowerRangeDisallowedHandlerOption.AddPlaceholderIfNotSkipping(loArray, p._param.LowerRangeDisallowedHandler)
                    };
                }
                else
                {
                    var lo = LoadOrder.Import<IModMasterStyledGetter>(
                        dataFolder.Value,
                        loArray,
                        p._gameRelease,
                        factory: (modPath) => KeyedMasterStyle.FromPath(modPath, p._gameRelease, p._param.FileSystem),
                        p._param.FileSystem);
                    return p._param with
                    {
                        MasterFlagsLookup = lo
                            .Where(x => !alreadyKnownMasters.Contains(x.ModKey))
                            .ResolveExistingMods(),
                        MastersListOrdering = new MastersListOrderingByLoadOrder(lo),
                        LowerRangeDisallowedHandler = ALowerRangeDisallowedHandlerOption.AddPlaceholderIfNotSkipping(lo, p._param.LowerRangeDisallowedHandler)
                    };
                }
            }
        });
    }
    IBinaryModdedWriteBuilderDataFolderChoice IBinaryModdedWriteBuilderLoadOrderChoice.WithLoadOrder(IEnumerable<ModKey> loadOrder) => WithLoadOrder(loadOrder);






    public BinaryModdedWriteBuilderDataFolderChoice<TModGetter> WithLoadOrder(
        params ModKey[] loadOrder)
    {
        return WithLoadOrder((IEnumerable<ModKey>)loadOrder);
    }
    IBinaryModdedWriteBuilderDataFolderChoice IBinaryModdedWriteBuilderLoadOrderChoice.WithLoadOrder(ModKey[] loadOrder) => WithLoadOrder(loadOrder);

    public BinaryModdedWriteBuilderDataFolderChoice<TModGetter> WithLoadOrderFromHeaderMasters()
    {
        return new BinaryModdedWriteBuilderDataFolderChoice<TModGetter>(_mod, _params with
        {
            _loadOrderSetter = (m, p, alreadyKnownMasters) =>
            {
                var dataFolder = p._dataFolderGetter?.Invoke(m, p._param);
                if (dataFolder == null || !GameConstants.Get(m.GameRelease).SeparateMasterLoadOrders)
                {
                    var lo = _mod.MasterReferences.Select(x => x.Master).ToArray();

                    return p._param with
                    {
                        MastersListOrdering = p._param.MastersListOrdering ?? new MastersListOrderingByLoadOrder(lo),
                        LowerRangeDisallowedHandler = ALowerRangeDisallowedHandlerOption.AddPlaceholderIfNotSkipping(lo, p._param.LowerRangeDisallowedHandler)
                    };
                }
                else
                {
                    var lo = LoadOrder.Import<IModMasterStyledGetter>(
                        dataFolder: dataFolder.Value,
                        loadOrder: _mod.MasterReferences.Select(x => x.Master),
                        m.GameRelease,
                        factory: (modPath) => KeyedMasterStyle.FromPath(modPath, m.GameRelease, p._param.FileSystem),
                        p._param.FileSystem);

                    return p._param with
                    {
                        MasterFlagsLookup = lo
                            .Where(x => !alreadyKnownMasters.Contains(x.ModKey))
                            .ResolveExistingMods(),
                        MastersListOrdering = p._param.MastersListOrdering ?? new MastersListOrderingByLoadOrder(lo),
                        LowerRangeDisallowedHandler = ALowerRangeDisallowedHandlerOption.AddPlaceholderIfNotSkipping(lo, p._param.LowerRangeDisallowedHandler)
                    };
                }
            }
        });
    }
    IBinaryModdedWriteBuilderDataFolderChoice IBinaryModdedWriteBuilderLoadOrderChoice.WithLoadOrderFromHeaderMasters() => WithLoadOrderFromHeaderMasters();
}

public record BinaryWriteBuilderLoadOrderChoice<TModGetter>
    where TModGetter : class, IModGetter
{
    internal BinaryWriteBuilderParams<TModGetter> _params;

    internal BinaryWriteBuilderLoadOrderChoice(BinaryWriteBuilderParams<TModGetter> @params)
    {
        _params = @params;
    }









    public BinaryWriteBuilder<TModGetter> WithNoLoadOrder()
    {
        return new BinaryWriteBuilder<TModGetter>(_params);
    }






    public BinaryWriteBuilder<TModGetter> WithLoadOrder(
        ILoadOrderGetter<IModListingGetter<IModMasterStyledGetter>> loadOrder)
    {
        return new BinaryWriteBuilder<TModGetter>(_params with
        {
            _loadOrderSetter = (m, p, alreadyKnownMasters) =>
            {
                return p._param with
                {
                    MasterFlagsLookup = loadOrder
                        .Where(x => !alreadyKnownMasters.Contains(x.ModKey))
                        .ResolveExistingMods(disposeItems: false),
                    MastersListOrdering = new MastersListOrderingByLoadOrder(loadOrder),
                    LowerRangeDisallowedHandler = ALowerRangeDisallowedHandlerOption.AddPlaceholderIfNotSkipping(loadOrder, p._param.LowerRangeDisallowedHandler)
                };
            }
        });
    }






    public BinaryWriteBuilder<TModGetter> WithLoadOrder(
        ILoadOrderGetter<IModMasterStyledGetter> loadOrder)
    {
        return new BinaryWriteBuilder<TModGetter>(_params with
        {
            _loadOrderSetter = (m, p, alreadyKnownMasters) =>
            {
                return p._param with
                {
                    MasterFlagsLookup = loadOrder
                        .Where(x => !alreadyKnownMasters.Contains(x.ModKey)),
                    MastersListOrdering = p._param.MastersListOrdering ?? new MastersListOrderingByLoadOrder(loadOrder),
                    LowerRangeDisallowedHandler = ALowerRangeDisallowedHandlerOption.AddPlaceholderIfNotSkipping(loadOrder, p._param.LowerRangeDisallowedHandler)
                };
            }
        });
    }






    public BinaryWriteBuilder<TModGetter> WithLoadOrder(
        params IModMasterStyledGetter[] loadOrder)
    {
        return WithLoadOrder(new LoadOrder<IModMasterStyledGetter>(loadOrder, disposeItems: false));
    }






    public BinaryWriteBuilder<TModGetter> WithLoadOrder(
        IEnumerable<IModMasterStyledGetter> loadOrder)
    {
        return WithLoadOrder(new LoadOrder<IModMasterStyledGetter>(loadOrder, disposeItems: false));
    }






    public BinaryWriteBuilder<TModGetter> WithLoadOrder(
        ILoadOrderGetter<IModListingGetter<TModGetter>> loadOrder)
    {
        return new BinaryWriteBuilder<TModGetter>(_params with
        {
            _knownModLoadOrder = loadOrder,
            _loadOrderSetter = (m, p, alreadyKnownMasters) =>
            {
                return p._param with
                {
                    MasterFlagsLookup = loadOrder
                        .Where(x => !alreadyKnownMasters.Contains(x.ModKey))
                        .ResolveExistingMods(disposeItems: false),
                    MastersListOrdering = p._param.MastersListOrdering ?? new MastersListOrderingByLoadOrder(loadOrder),
                    LowerRangeDisallowedHandler = ALowerRangeDisallowedHandlerOption.AddPlaceholderIfNotSkipping(loadOrder, p._param.LowerRangeDisallowedHandler)
                };
            }
        });
    }






    public BinaryWriteBuilder<TModGetter> WithLoadOrder(
        ILoadOrderGetter<TModGetter> loadOrder)
    {
        return new BinaryWriteBuilder<TModGetter>(_params with
        {
            _knownModLoadOrder = loadOrder.Transform(x => new ModListing<TModGetter>(x)),
            _loadOrderSetter = (m, p, alreadyKnownMasters) =>
            {
                return p._param with
                {
                    MasterFlagsLookup = loadOrder
                        .Where(x => !alreadyKnownMasters.Contains(x.ModKey)),
                    MastersListOrdering = p._param.MastersListOrdering ?? new MastersListOrderingByLoadOrder(loadOrder),
                    LowerRangeDisallowedHandler = ALowerRangeDisallowedHandlerOption.AddPlaceholderIfNotSkipping(loadOrder, p._param.LowerRangeDisallowedHandler)
                };
            }
        });
    }






    public BinaryWriteBuilder<TModGetter> WithLoadOrder(
        params TModGetter[] loadOrder)
    {
        return WithLoadOrder(new LoadOrder<TModGetter>(loadOrder, disposeItems: false));
    }






    public BinaryWriteBuilder<TModGetter> WithLoadOrder(
        IEnumerable<TModGetter> loadOrder)
    {
        return WithLoadOrder(new LoadOrder<TModGetter>(loadOrder, disposeItems: false));
    }





    public BinaryWriteBuilder<TModGetter> WithDefaultLoadOrder()
    {
        return new BinaryWriteBuilder<TModGetter>(_params with
        {
            _dataFolderGetter = static (m, p) => GameLocatorLookupCache.Instance.GetDataDirectory(m.GameRelease),
            _loadOrderSetter = static (m, p, alreadyKnownMasters) =>
            {
                var dataFolder = p._dataFolderGetter?.Invoke(m, p._param) ?? throw new ArgumentNullException("Data folder source was not set");
                var lo = LoadOrder.Import<IModMasterStyledGetter>(
                    dataFolder,
                    m.GameRelease,
                    factory: (modPath) => KeyedMasterStyle.FromPath(modPath, p._gameRelease, p._param.FileSystem),
                    p._param.FileSystem);

                return p._param with
                {
                    MasterFlagsLookup = lo
                        .Where(x => !alreadyKnownMasters.Contains(x.ModKey))
                        .ResolveExistingMods(disposeItems: false),
                    MastersListOrdering = p._param.MastersListOrdering ?? new MastersListOrderingByLoadOrder(lo)
                };
            }
        });
    }






    public BinaryWriteBuilderDataFolderChoice<TModGetter> WithLoadOrder(
        IEnumerable<ModKey> loadOrder)
    {
        return new BinaryWriteBuilderDataFolderChoice<TModGetter>(_params with
        {
            _loadOrderSetter = (m, p, alreadyKnownMasters) =>
            {
                var dataFolder = p._dataFolderGetter?.Invoke(m, p._param) ?? throw new ArgumentNullException("Data folder source was not set");
                var lo = LoadOrder.Import<IModMasterStyledGetter>(
                    dataFolder, loadOrder,
                    m.GameRelease,
                    factory: (modPath) => KeyedMasterStyle.FromPath(modPath, m.GameRelease, p._param.FileSystem),
                    p._param.FileSystem);
                return p._param with
                {
                    MasterFlagsLookup = lo
                        .Where(x => !alreadyKnownMasters.Contains(x.ModKey))
                        .ResolveExistingMods(disposeItems: false),
                    MastersListOrdering = p._param.MastersListOrdering ?? new MastersListOrderingByLoadOrder(lo),
                    LowerRangeDisallowedHandler = ALowerRangeDisallowedHandlerOption.AddPlaceholderIfNotSkipping(lo, p._param.LowerRangeDisallowedHandler)
                };
            }
        });
    }






    public BinaryWriteBuilderDataFolderChoice<TModGetter> WithLoadOrder(
        params ModKey[] loadOrder)
    {
        return WithLoadOrder((IEnumerable<ModKey>)loadOrder);
    }

    public BinaryWriteBuilderDataFolderChoice<TModGetter> WithLoadOrderFromHeaderMasters()
    {
        return new BinaryWriteBuilderDataFolderChoice<TModGetter>(_params with
        {
            _loadOrderSetter = static (m, p, alreadyKnownMasters) =>
            {
                var dataFolder = p._dataFolderGetter?.Invoke(m, p._param) ?? throw new ArgumentNullException("Data folder source was not set");
                var lo = LoadOrder.Import<IModMasterStyledGetter>(
                    dataFolder, m.MasterReferences.Select(x => x.Master),
                    m.GameRelease,
                    factory: (modPath) => KeyedMasterStyle.FromPath(modPath, m.GameRelease, p._param.FileSystem),
                    p._param.FileSystem);

                return p._param with
                {
                    MasterFlagsLookup = lo
                        .Where(x => !alreadyKnownMasters.Contains(x.ModKey))
                        .ResolveExistingMods(disposeItems: false),
                    MastersListOrdering = p._param.MastersListOrdering ?? new MastersListOrderingByLoadOrder(lo),
                    LowerRangeDisallowedHandler = ALowerRangeDisallowedHandlerOption.AddPlaceholderIfNotSkipping(lo, p._param.LowerRangeDisallowedHandler)
                };
            }
        });
    }
}

public record BinaryWriteBuilderDataFolderChoice<TModGetter>
    where TModGetter : class, IModGetter
{
    internal BinaryWriteBuilderParams<TModGetter> _param;

    internal BinaryWriteBuilderDataFolderChoice(BinaryWriteBuilderParams<TModGetter> @params)
    {
        _param = @params;
    }

    public BinaryWriteBuilder<TModGetter> WithDefaultDataFolder()
    {
        return new BinaryWriteBuilder<TModGetter>(_param with
        {
            _dataFolderGetter = (m, p) => GameLocatorLookupCache.Instance.GetDataDirectory(m.GameRelease)
        });
    }

    public BinaryWriteBuilder<TModGetter> WithDataFolder(DirectoryPath? dataFolder)
    {
        if (dataFolder == null)
        {
            return new BinaryWriteBuilder<TModGetter>(_param);
        }
        return new BinaryWriteBuilder<TModGetter>(_param with
        {
            _dataFolderGetter = (m, p) => dataFolder.Value
        });
    }








    public BinaryWriteBuilder<TModGetter> WithKnownMasters(params IModMasterStyledGetter[] knownMasters)
    {
        var match = _param.KnownMasters.FirstOrDefault(existingKnownMaster =>
            knownMasters.Any(x => x.ModKey == existingKnownMaster.ModKey));
        if (match != null)
        {
            throw new ArgumentException($"ModKey was already added as a known master: {match.ModKey}");
        }

        return new BinaryWriteBuilder<TModGetter>(_param with
        {
            KnownMasters = _param.KnownMasters.And(knownMasters).ToArray()
        });
    }








    public BinaryWriteBuilder<TModGetter> WithKnownMasters(params KeyedMasterStyle[] knownMasters)
    {
        return WithKnownMasters(knownMasters.Cast<IModMasterStyledGetter>().ToArray());
    }
}

public interface IBinaryModdedWriteBuilderDataFolderChoice
{
    IBinaryModdedWriteBuilder WithDefaultDataFolder();
    IBinaryModdedWriteBuilder WithDataFolder(DirectoryPath? dataFolder);
    IBinaryModdedWriteBuilder WithNoDataFolder();
    IBinaryModdedWriteBuilder WithKnownMasters(params IModMasterStyledGetter[] knownMasters);
    IBinaryModdedWriteBuilder WithKnownMasters(params KeyedMasterStyle[] knownMasters);
}

public record BinaryModdedWriteBuilderDataFolderChoice<TModGetter> : IBinaryModdedWriteBuilderDataFolderChoice
    where TModGetter : class, IModGetter
{
    private readonly TModGetter _mod;
    internal BinaryWriteBuilderParams<TModGetter> _param;

    internal BinaryModdedWriteBuilderDataFolderChoice(TModGetter mod, BinaryWriteBuilderParams<TModGetter> @params)
    {
        _mod = mod;
        _param = @params;
    }

    public BinaryModdedWriteBuilder<TModGetter> WithDefaultDataFolder()
    {
        return new BinaryModdedWriteBuilder<TModGetter>(_mod, _param with
        {
            _dataFolderGetter = (m, p) => GameLocatorLookupCache.Instance.GetDataDirectory(m.GameRelease)
        });
    }

    IBinaryModdedWriteBuilder IBinaryModdedWriteBuilderDataFolderChoice.WithDefaultDataFolder() =>
        WithDefaultDataFolder();

    public BinaryModdedWriteBuilder<TModGetter> WithDataFolder(DirectoryPath? dataFolder)
    {
        if (dataFolder == null)
        {
            return new BinaryModdedWriteBuilder<TModGetter>(_mod, _param);
        }
        return new BinaryModdedWriteBuilder<TModGetter>(_mod, _param with
        {
            _dataFolderGetter = (m, p) => dataFolder.Value
        });
    }

    IBinaryModdedWriteBuilder IBinaryModdedWriteBuilderDataFolderChoice.WithDataFolder(DirectoryPath? dataFolder) =>
        WithDataFolder(dataFolder);

    public BinaryModdedWriteBuilder<TModGetter> WithNoDataFolder()
    {
        return new BinaryModdedWriteBuilder<TModGetter>(_mod, _param);
    }

    IBinaryModdedWriteBuilder IBinaryModdedWriteBuilderDataFolderChoice.WithNoDataFolder() =>
        WithNoDataFolder();








    public BinaryModdedWriteBuilder<TModGetter> WithKnownMasters(params IModMasterStyledGetter[] knownMasters)
    {
        var match = _param.KnownMasters.FirstOrDefault(existingKnownMaster =>
            knownMasters.Any(x => x.ModKey == existingKnownMaster.ModKey));
        if (match != null)
        {
            throw new ArgumentException($"ModKey was already added as a known master: {match.ModKey}");
        }

        return new BinaryModdedWriteBuilder<TModGetter>(_mod, _param with
        {
            KnownMasters = _param.KnownMasters.And(knownMasters).ToArray()
        });
    }
    IBinaryModdedWriteBuilder IBinaryModdedWriteBuilderDataFolderChoice.WithKnownMasters(params IModMasterStyledGetter[] knownMasters) =>
        WithKnownMasters(knownMasters);








    public BinaryModdedWriteBuilder<TModGetter> WithKnownMasters(params KeyedMasterStyle[] knownMasters)
    {
        return WithKnownMasters(knownMasters.Cast<IModMasterStyledGetter>().ToArray());
    }
    IBinaryModdedWriteBuilder IBinaryModdedWriteBuilderDataFolderChoice.WithKnownMasters(params KeyedMasterStyle[] knownMasters) =>
        WithKnownMasters(knownMasters);
}

public interface IBinaryModdedWriteBuilder
{




    IBinaryModdedWriteBuilder WithModKeySync(ModKeyOption option);





    IBinaryModdedWriteBuilder NoModKeySync();






    IBinaryModdedWriteBuilder WithFileSystem(IFileSystem? fileSystem);






    IBinaryModdedWriteBuilder WithMastersListContent(MastersListContentOption option);






    IBinaryModdedWriteBuilder NoMastersListContentCheck();





    IBinaryModdedWriteBuilder WithRecordCount(RecordCountOption option);









    IBinaryModdedWriteBuilder WithMastersListOrdering(
        MastersListOrderingOption option);










    IBinaryModdedWriteBuilder WithMastersListOrdering(
        IEnumerable<ModKey> loadOrder);










    IBinaryModdedWriteBuilder WithMastersListOrdering(
        ILoadOrderGetter loadOrder);










    IBinaryModdedWriteBuilder WithMastersListOrdering(
        IReadOnlyMasterReferenceCollection otherMasters);





    IBinaryModdedWriteBuilder NoNextFormIDProcessing();






    IBinaryModdedWriteBuilder WithForcedLowerFormIdRangeUsage(bool? useLowerRange);








    IBinaryModdedWriteBuilder WithAutoSplit();





    IBinaryModdedWriteBuilder NoFormIDUniquenessCheck();





    IBinaryModdedWriteBuilder NoFormIDCompactnessCheck();






    IBinaryModdedWriteBuilder WithFormIDCompactnessCheck(FormIDCompactionOption option);






    IBinaryModdedWriteBuilder WithStringsWriter(StringsWriter? stringsWriter);






    IBinaryModdedWriteBuilder WithTargetLanguage(Language language);





    IBinaryModdedWriteBuilder NoNullFormIDStandardization();






    IBinaryModdedWriteBuilder WithEmbeddedEncodings(EncodingBundle? encodingBundle);







    IBinaryModdedWriteBuilder WithUtf8Encoding(bool on = true);








    IBinaryModdedWriteBuilder WithPlaceholderMasterIfLowerRangeDisallowed(ModKey placeholder);








    IBinaryModdedWriteBuilder WithPlaceholderMasterIfLowerRangeDisallowed(ILoadOrderGetter loadOrder);








    IBinaryModdedWriteBuilder WithPlaceholderMasterIfLowerRangeDisallowed(IEnumerable<ModKey> loadOrder);






    IBinaryModdedWriteBuilder ThrowIfLowerRangeDisallowed();





    IBinaryModdedWriteBuilder NoCheckIfLowerRangeDisallowed();






    IBinaryModdedWriteBuilder WithParallelWriteParameters(ParallelWriteParameters parameters);





    IBinaryModdedWriteBuilder SingleThread();






    IBinaryModdedWriteBuilder WithExtraIncludedMasters(IEnumerable<ModKey> modKeys);






    IBinaryModdedWriteBuilder WithExtraIncludedMasters(params ModKey[] modKeys);








    IBinaryModdedWriteBuilder WithExplicitOverridingMasterList(IEnumerable<ModKey> modKeys);








    IBinaryModdedWriteBuilder WithExplicitOverridingMasterList(params ModKey[] modKeys);







    IBinaryModdedWriteBuilder WithAllParentMasters();








    IBinaryModdedWriteBuilder WithKnownMasters(params IModMasterStyledGetter[] knownMasters);








    IBinaryModdedWriteBuilder WithKnownMasters(params KeyedMasterStyle[] knownMasters);

    internal IBinaryModdedWriteBuilder WithOverriddenFormsOption(OverriddenFormsOption option);

    public IBinaryModdedWriteBuilder WithDataFolder(DirectoryPath? dataFolder);




    void Write();





    Task WriteAsync();
}

public record BinaryModdedWriteBuilder<TModGetter> : IBinaryModdedWriteBuilder
    where TModGetter : class, IModGetter
{
    internal BinaryWriteBuilderParams<TModGetter> _params;
    internal TModGetter _mod { get; init; } = null!;

    internal BinaryModdedWriteBuilder(
        TModGetter mod,
        BinaryWriteBuilderParams<TModGetter> @params)
    {
        _mod = mod;
        _params = @params;
    }





    public BinaryModdedWriteBuilder<TModGetter> WithModKeySync(ModKeyOption option)
    {
        return this with
        {
            _params = _params with
            {
                _param = _params._param with
                {
                    ModKey = option
                }
            }
        };
    }

    IBinaryModdedWriteBuilder IBinaryModdedWriteBuilder.WithModKeySync(ModKeyOption option) => WithModKeySync(option);





    public BinaryModdedWriteBuilder<TModGetter> NoModKeySync()
    {
        return this with
        {
            _params = _params with
            {
                _param = _params._param with
                {
                    ModKey = ModKeyOption.NoCheck
                }
            }
        };
    }
    IBinaryModdedWriteBuilder IBinaryModdedWriteBuilder.NoModKeySync() => NoModKeySync();






    public BinaryModdedWriteBuilder<TModGetter> WithFileSystem(IFileSystem? fileSystem)
    {
        return this with
        {
            _params = _params with
            {
                _param = _params._param with
                {
                    FileSystem = fileSystem
                }
            }
        };
    }
    IBinaryModdedWriteBuilder IBinaryModdedWriteBuilder.WithFileSystem(IFileSystem? fileSystem) => WithFileSystem(fileSystem);






    public BinaryModdedWriteBuilder<TModGetter> WithMastersListContent(MastersListContentOption option)
    {
        return this with
        {
            _params = _params with
            {
                _param = _params._param with
                {
                    MastersListContent = option
                }
            }
        };
    }
    IBinaryModdedWriteBuilder IBinaryModdedWriteBuilder.WithMastersListContent(MastersListContentOption option) => WithMastersListContent(option);






    public BinaryModdedWriteBuilder<TModGetter> NoMastersListContentCheck()
    {
        return this with
        {
            _params = _params with
            {
                _param = _params._param with
                {
                    MastersListContent = MastersListContentOption.NoCheck
                }
            }
        };
    }
    IBinaryModdedWriteBuilder IBinaryModdedWriteBuilder.NoMastersListContentCheck() => NoMastersListContentCheck();





    public BinaryModdedWriteBuilder<TModGetter> WithRecordCount(RecordCountOption option)
    {
        return this with
        {
            _params = _params with
            {
                _param = _params._param with
                {
                    RecordCount = option
                }
            }
        };
    }
    IBinaryModdedWriteBuilder IBinaryModdedWriteBuilder.WithRecordCount(RecordCountOption option) => WithRecordCount(option);










    public BinaryModdedWriteBuilder<TModGetter> WithMastersListOrdering(
        MastersListOrderingOption option)
    {
        return this with
        {
            _params = _params with
            {
                _param = _params._param with
                {
                    MastersListOrdering = new MastersListOrderingEnumOption()
                    {
                        Option = option
                    }
                }
            }
        };
    }
    IBinaryModdedWriteBuilder IBinaryModdedWriteBuilder.WithMastersListOrdering(MastersListOrderingOption option) => WithMastersListOrdering(option);










    public BinaryModdedWriteBuilder<TModGetter> WithMastersListOrdering(
        IEnumerable<ModKey> loadOrder)
    {
        return this with
        {
            _params = _params with
            {
                _param = _params._param with
                {
                    MastersListOrdering = new MastersListOrderingByLoadOrder(loadOrder)
                }
            }
        };
    }
    IBinaryModdedWriteBuilder IBinaryModdedWriteBuilder.WithMastersListOrdering(IEnumerable<ModKey> loadOrder) => WithMastersListOrdering(loadOrder);










    public BinaryModdedWriteBuilder<TModGetter> WithMastersListOrdering(
        ILoadOrderGetter loadOrder)
    {
        return this with
        {
            _params = _params with
            {
                _param = _params._param with
                {
                    MastersListOrdering = new MastersListOrderingByLoadOrder(loadOrder)
                }
            }
        };
    }
    IBinaryModdedWriteBuilder IBinaryModdedWriteBuilder.WithMastersListOrdering(ILoadOrderGetter loadOrder) => WithMastersListOrdering(loadOrder);










    public BinaryModdedWriteBuilder<TModGetter> WithMastersListOrdering(
        IReadOnlyMasterReferenceCollection otherMasters)
    {
        return this with
        {
            _params = _params with
            {
                _param = _params._param with
                {
                    MastersListOrdering = new MastersListOrderingByLoadOrder(otherMasters
                        .Masters.Select(x => x.Master))
                }
            }
        };
    }
    IBinaryModdedWriteBuilder IBinaryModdedWriteBuilder.WithMastersListOrdering(IReadOnlyMasterReferenceCollection otherMasters) => WithMastersListOrdering(otherMasters);





    public BinaryModdedWriteBuilder<TModGetter> NoNextFormIDProcessing()
    {
        return this with
        {
            _params = _params with
            {
                _param = _params._param with
                {
                    NextFormID = NextFormIDOption.NoCheck
                }
            }
        };
    }
    IBinaryModdedWriteBuilder IBinaryModdedWriteBuilder.NoNextFormIDProcessing() => NoNextFormIDProcessing();






    public BinaryModdedWriteBuilder<TModGetter> WithForcedLowerFormIdRangeUsage(bool? useLowerRange)
    {
        return this with
        {
            _params = _params with
            {
                _param = _params._param with
                {
                    MinimumFormID = AMinimumFormIdOption.Force(useLowerRange)
                }
            }
        };
    }
    IBinaryModdedWriteBuilder IBinaryModdedWriteBuilder.WithForcedLowerFormIdRangeUsage(bool? useLowerRange) => WithForcedLowerFormIdRangeUsage(useLowerRange);










    public BinaryModdedWriteBuilder<TModGetter> WithAutoSplit()
    {
        return this with
        {
            _params = _params with
            {
                _autoSplit = true
            }
        };
    }
    IBinaryModdedWriteBuilder IBinaryModdedWriteBuilder.WithAutoSplit() => WithAutoSplit();





    public BinaryModdedWriteBuilder<TModGetter> NoFormIDUniquenessCheck()
    {
        return this with
        {
            _params = _params with
            {
                _param = _params._param with
                {
                    FormIDUniqueness = FormIDUniquenessOption.NoCheck
                }
            }
        };
    }
    IBinaryModdedWriteBuilder IBinaryModdedWriteBuilder.NoFormIDUniquenessCheck() => NoFormIDUniquenessCheck();





    public BinaryModdedWriteBuilder<TModGetter> NoFormIDCompactnessCheck()
    {
        return WithFormIDCompactnessCheck(FormIDCompactionOption.NoCheck);
    }
    IBinaryModdedWriteBuilder IBinaryModdedWriteBuilder.NoFormIDCompactnessCheck() => NoFormIDCompactnessCheck();






    public BinaryModdedWriteBuilder<TModGetter> WithFormIDCompactnessCheck(FormIDCompactionOption option)
    {
        return this with
        {
            _params = _params with
            {
                _param = _params._param with
                {
                    FormIDCompaction = option
                }
            }
        };
    }
    IBinaryModdedWriteBuilder IBinaryModdedWriteBuilder.WithFormIDCompactnessCheck(FormIDCompactionOption option) => WithFormIDCompactnessCheck(option);






    public BinaryModdedWriteBuilder<TModGetter> WithStringsWriter(StringsWriter? stringsWriter)
    {
        return this with
        {
            _params = _params with
            {
                _param = _params._param with
                {
                    StringsWriter = stringsWriter
                }
            }
        };
    }
    IBinaryModdedWriteBuilder IBinaryModdedWriteBuilder.WithStringsWriter(StringsWriter? stringsWriter) => WithStringsWriter(stringsWriter);






    public BinaryModdedWriteBuilder<TModGetter> WithTargetLanguage(Language language)
    {
        return this with
        {
            _params = _params with
            {
                _param = _params._param with
                {
                    TargetLanguageOverride = language
                }
            }
        };
    }
    IBinaryModdedWriteBuilder IBinaryModdedWriteBuilder.WithTargetLanguage(Language language) => WithTargetLanguage(language);





    public BinaryModdedWriteBuilder<TModGetter> NoNullFormIDStandardization()
    {
        return this with
        {
            _params = _params with
            {
                _param = _params._param with
                {
                    CleanNulls = false
                }
            }
        };
    }
    IBinaryModdedWriteBuilder IBinaryModdedWriteBuilder.NoNullFormIDStandardization() => NoNullFormIDStandardization();






    public BinaryModdedWriteBuilder<TModGetter> WithEmbeddedEncodings(EncodingBundle? encodingBundle)
    {
        return this with
        {
            _params = _params with
            {
                _param = _params._param with
                {
                    Encodings = encodingBundle
                }
            }
        };
    }
    IBinaryModdedWriteBuilder IBinaryModdedWriteBuilder.WithEmbeddedEncodings(EncodingBundle? encodingBundle) => WithEmbeddedEncodings(encodingBundle);







    public BinaryModdedWriteBuilder<TModGetter> WithUtf8Encoding(bool on = true)
    {
        return this with
        {
            _params = _params with
            {
                _param = _params._param with
                {
                    Encodings = on
                        ? new EncodingBundle(NonTranslated: MutagenEncoding._1252, NonLocalized: MutagenEncoding._utf8)
                        : null
                }
            }
        };
    }
    IBinaryModdedWriteBuilder IBinaryModdedWriteBuilder.WithUtf8Encoding(bool on) => WithUtf8Encoding(on);








    public BinaryModdedWriteBuilder<TModGetter> WithPlaceholderMasterIfLowerRangeDisallowed(ModKey placeholder)
    {
        return this with
        {
            _params = _params with
            {
                _param = _params._param with
                {
                    LowerRangeDisallowedHandler = ALowerRangeDisallowedHandlerOption.AddPlaceholderIfNotSkipping(placeholder, _params._param.LowerRangeDisallowedHandler)
                }
            }
        };
    }
    IBinaryModdedWriteBuilder IBinaryModdedWriteBuilder.WithPlaceholderMasterIfLowerRangeDisallowed(ModKey placeholder) => WithPlaceholderMasterIfLowerRangeDisallowed(placeholder);








    public BinaryModdedWriteBuilder<TModGetter> WithPlaceholderMasterIfLowerRangeDisallowed(ILoadOrderGetter loadOrder)
    {
        return this with
        {
            _params = _params with
            {
                _param = _params._param with
                {
                    LowerRangeDisallowedHandler = ALowerRangeDisallowedHandlerOption.AddPlaceholderIfNotSkipping(loadOrder, _params._param.LowerRangeDisallowedHandler)
                }
            }
        };
    }
    IBinaryModdedWriteBuilder IBinaryModdedWriteBuilder.WithPlaceholderMasterIfLowerRangeDisallowed(ILoadOrderGetter loadOrder) => WithPlaceholderMasterIfLowerRangeDisallowed(loadOrder);








    public BinaryModdedWriteBuilder<TModGetter> WithPlaceholderMasterIfLowerRangeDisallowed(IEnumerable<ModKey> loadOrder)
    {
        return this with
        {
            _params = _params with
            {
                _param = _params._param with
                {
                    LowerRangeDisallowedHandler = ALowerRangeDisallowedHandlerOption.AddPlaceholderIfNotSkipping(loadOrder, _params._param.LowerRangeDisallowedHandler)
                }
            }
        };
    }
    IBinaryModdedWriteBuilder IBinaryModdedWriteBuilder.WithPlaceholderMasterIfLowerRangeDisallowed(IEnumerable<ModKey> loadOrder) => WithPlaceholderMasterIfLowerRangeDisallowed(loadOrder);






    public BinaryModdedWriteBuilder<TModGetter> ThrowIfLowerRangeDisallowed()
    {
        return this with
        {
            _params = _params with
            {
                _param = _params._param with
                {
                    LowerRangeDisallowedHandler = new ThrowIfLowerRangeDisallowed()
                }
            }
        };
    }
    IBinaryModdedWriteBuilder IBinaryModdedWriteBuilder.ThrowIfLowerRangeDisallowed() => ThrowIfLowerRangeDisallowed();





    public BinaryModdedWriteBuilder<TModGetter> NoCheckIfLowerRangeDisallowed()
    {
        return this with
        {
            _params = _params with
            {
                _param = _params._param with
                {
                    LowerRangeDisallowedHandler = new NoCheckIfLowerRangeDisallowed()
                }
            }
        };
    }
    IBinaryModdedWriteBuilder IBinaryModdedWriteBuilder.NoCheckIfLowerRangeDisallowed() => NoCheckIfLowerRangeDisallowed();






    public BinaryModdedWriteBuilder<TModGetter> WithParallelWriteParameters(ParallelWriteParameters parameters)
    {
        return this with
        {
            _params = _params with
            {
                _param = _params._param with
                {
                    Parallel = parameters
                }
            }
        };
    }
    IBinaryModdedWriteBuilder IBinaryModdedWriteBuilder.WithParallelWriteParameters(ParallelWriteParameters parameters) => WithParallelWriteParameters(parameters);





    public BinaryModdedWriteBuilder<TModGetter> SingleThread()
    {
        return this with
        {
            _params = _params with
            {
                _param = _params._param with
                {
                    Parallel = new ParallelWriteParameters()
                    {
                        MaxDegreeOfParallelism = 1
                    }
                }
            }
        };
    }
    IBinaryModdedWriteBuilder IBinaryModdedWriteBuilder.SingleThread() => SingleThread();







    public BinaryModdedWriteBuilder<TModGetter> WithExtraIncludedMasters(IEnumerable<ModKey> modKeys)
    {
        return WithExtraIncludedMasters(modKeys.ToArray());
    }
    IBinaryModdedWriteBuilder IBinaryModdedWriteBuilder.WithExtraIncludedMasters(IEnumerable<ModKey> modKeys) => WithExtraIncludedMasters(modKeys);







    public BinaryModdedWriteBuilder<TModGetter> WithExtraIncludedMasters(params ModKey[] modKeys)
    {
        return this with
        {
            _params = _params with
            {
                _param = _params._param with
                {
                    MastersContentCustomOverride = (mods) =>
                    {
                        return (_params._param.MastersContentCustomOverride?.Invoke(mods) ?? mods)
                            .And(modKeys)
                            .Distinct()
                            .ToArray();
                    }
                }
            }
        };
    }
    IBinaryModdedWriteBuilder IBinaryModdedWriteBuilder.WithExtraIncludedMasters(params ModKey[] modKeys) => WithExtraIncludedMasters(modKeys);








    public BinaryModdedWriteBuilder<TModGetter> WithExplicitOverridingMasterList(IEnumerable<ModKey> modKeys)
    {
        return this with
        {
            _params = _params with
            {
                _masterSyncAction = null,
                _param = _params._param with
                {
                    MastersListContent = MastersListContentOption.NoCheck,
                    MastersContentCustomOverride = (mods) => modKeys.ToArray(),
                    MastersListOrdering = new MastersListOrderingByLoadOrder(modKeys.ToArray())
                }
            }
        };
    }
    IBinaryModdedWriteBuilder IBinaryModdedWriteBuilder.WithExplicitOverridingMasterList(IEnumerable<ModKey> modKeys) => WithExplicitOverridingMasterList(modKeys);








    public BinaryModdedWriteBuilder<TModGetter> WithExplicitOverridingMasterList(params ModKey[] modKeys)
    {
        return WithExplicitOverridingMasterList((IEnumerable<ModKey>)modKeys);
    }
    IBinaryModdedWriteBuilder IBinaryModdedWriteBuilder.WithExplicitOverridingMasterList(params ModKey[] modKeys) => WithExplicitOverridingMasterList(modKeys);







    public BinaryModdedWriteBuilder<TModGetter> WithAllParentMasters()
    {
        return this with
        {
            _params = _params with
            {
                _masterSyncAction = static (mod, p) =>
                {
                    var dataFolder = p._dataFolderGetter?.Invoke(mod, p._param) ?? throw new ArgumentNullException("Data folder source was not set");

                    return p._param with
                    {
                        MastersContentCustomOverride = (mods) =>
                        {
                            var locator = new TransitiveMasterLocator(
                                p._param.FileSystem.GetOrDefault(),
                                new DataDirectoryInjection(dataFolder),
                                new GameReleaseInjection(p._gameRelease));
                            return locator.GetAllMasters(
                                mod.ModKey,
                                mods,
                                p._knownModLoadOrder);
                        }
                    };
                },
            }
        };
    }
    IBinaryModdedWriteBuilder IBinaryModdedWriteBuilder.WithAllParentMasters() => WithAllParentMasters();








    public BinaryModdedWriteBuilder<TModGetter> WithKnownMasters(params IModMasterStyledGetter[] knownMasters)
    {
        var match = _params.KnownMasters.FirstOrDefault(existingKnownMaster =>
            knownMasters.Any(x => x.ModKey == existingKnownMaster.ModKey));
        if (match != null)
        {
            throw new ArgumentException($"ModKey was already added as a known master: {match.ModKey}");
        }

        return this with
        {
            _params = _params with
            {
                KnownMasters = _params.KnownMasters.And(knownMasters).ToArray()
            }
        };
    }
    IBinaryModdedWriteBuilder IBinaryModdedWriteBuilder.WithKnownMasters(params IModMasterStyledGetter[] knownMasters) => WithKnownMasters(knownMasters);








    public BinaryModdedWriteBuilder<TModGetter> WithKnownMasters(params KeyedMasterStyle[] knownMasters)
    {
        return WithKnownMasters(knownMasters.Cast<IModMasterStyledGetter>().ToArray());
    }
    IBinaryModdedWriteBuilder IBinaryModdedWriteBuilder.WithKnownMasters(params KeyedMasterStyle[] knownMasters) => WithKnownMasters(knownMasters);

    internal BinaryModdedWriteBuilder<TModGetter> WithOverriddenFormsOption(OverriddenFormsOption option)
    {
        return this with
        {
            _params = _params with
            {
                _param = _params._param with
                {
                    OverriddenFormsOption = option
                }
            }
        };
    }

    IBinaryModdedWriteBuilder IBinaryModdedWriteBuilder.WithOverriddenFormsOption(OverriddenFormsOption option) => WithOverriddenFormsOption(option);

    public BinaryModdedWriteBuilder<TModGetter> WithDataFolder(DirectoryPath? dataFolder)
    {
        if (dataFolder == null)
        {
            return new BinaryModdedWriteBuilder<TModGetter>(_mod, _params);
        }
        return new BinaryModdedWriteBuilder<TModGetter>(_mod, _params with
        {
            _dataFolderGetter = (m, p) => dataFolder.Value
        });
    }
    IBinaryModdedWriteBuilder IBinaryModdedWriteBuilder.WithDataFolder(DirectoryPath? dataFolder) => WithDataFolder(dataFolder);





    public async Task WriteAsync()
    {
        await _params._writer.WriteAsync(
            _mod,
            BinaryWriteBuilderHelper.RunPreWriteSetters<TModGetter>(_mod, _params));
    }





    public void Write()
    {
        _params._writer.Write(
            _mod,
            BinaryWriteBuilderHelper.RunPreWriteSetters<TModGetter>(_mod, _params));
    }
}

public record BinaryWriteBuilder<TModGetter>
    where TModGetter : class, IModGetter
{
    internal BinaryWriteBuilderParams<TModGetter> _params;

    internal BinaryWriteBuilder(
        BinaryWriteBuilderParams<TModGetter> @params)
    {
        _params = @params;
    }





    public BinaryWriteBuilder<TModGetter> WithModKeySync(ModKeyOption option)
    {
        return this with
        {
            _params = _params with
            {
                _param = _params._param with
                {
                    ModKey = option
                }
            }
        };
    }





    public BinaryWriteBuilder<TModGetter> NoModKeySync()
    {
        return this with
        {
            _params = _params with
            {
                _param = _params._param with
                {
                    ModKey = ModKeyOption.NoCheck
                }
            }
        };
    }






    public BinaryWriteBuilder<TModGetter> WithFileSystem(IFileSystem fileSystem)
    {
        return this with
        {
            _params = _params with
            {
                _param = _params._param with
                {
                    FileSystem = fileSystem
                }
            }
        };
    }






    public BinaryWriteBuilder<TModGetter> WithMastersListContent(MastersListContentOption option)
    {
        return this with
        {
            _params = _params with
            {
                _param = _params._param with
                {
                    MastersListContent = option
                }
            }
        };
    }






    public BinaryWriteBuilder<TModGetter> NoMastersListContentCheck()
    {
        return this with
        {
            _params = _params with
            {
                _param = _params._param with
                {
                    MastersListContent = MastersListContentOption.NoCheck
                }
            }
        };
    }





    public BinaryWriteBuilder<TModGetter> WithRecordCount(RecordCountOption option)
    {
        return this with
        {
            _params = _params with
            {
                _param = _params._param with
                {
                    RecordCount = option
                }
            }
        };
    }










    public BinaryWriteBuilder<TModGetter> WithMastersListOrdering(
        MastersListOrderingOption option)
    {
        return this with
        {
            _params = _params with
            {
                _param = _params._param with
                {
                    MastersListOrdering = new MastersListOrderingEnumOption()
                    {
                        Option = option
                    }
                }
            }
        };
    }










    public BinaryWriteBuilder<TModGetter> WithMastersListOrdering(
        IEnumerable<ModKey> loadOrder)
    {
        return this with
        {
            _params = _params with
            {
                _param = _params._param with
                {
                    MastersListOrdering = new MastersListOrderingByLoadOrder(loadOrder)
                }
            }
        };
    }










    public BinaryWriteBuilder<TModGetter> WithMastersListOrdering(
        ILoadOrderGetter loadOrder)
    {
        return this with
        {
            _params = _params with
            {
                _param = _params._param with
                {
                    MastersListOrdering = new MastersListOrderingByLoadOrder(loadOrder)
                }
            }
        };
    }










    public BinaryWriteBuilder<TModGetter> WithMastersListOrdering(
        IReadOnlyMasterReferenceCollection otherMasters)
    {
        return this with
        {
            _params = _params with
            {
                _param = _params._param with
                {
                    MastersListOrdering = new MastersListOrderingByLoadOrder(otherMasters
                        .Masters.Select(x => x.Master))
                }
            }
        };
    }





    public BinaryWriteBuilder<TModGetter> NoNextFormIDProcessing()
    {
        return this with
        {
            _params = _params with
            {
                _param = _params._param with
                {
                    NextFormID = NextFormIDOption.NoCheck
                }
            }
        };
    }






    public BinaryWriteBuilder<TModGetter> WithForcedLowerFormIdRangeUsage(bool? useLowerRange)
    {
        return this with
        {
            _params = _params with
            {
                _param = _params._param with
                {
                    MinimumFormID = AMinimumFormIdOption.Force(useLowerRange)
                }
            }
        };
    }










    public BinaryWriteBuilder<TModGetter> WithAutoSplit()
    {
        return this with
        {
            _params = _params with
            {
                _autoSplit = true
            }
        };
    }





    public BinaryWriteBuilder<TModGetter> NoFormIDUniquenessCheck()
    {
        return this with
        {
            _params = _params with
            {
                _param = _params._param with
                {
                    FormIDUniqueness = FormIDUniquenessOption.NoCheck
                }
            }
        };
    }





    public BinaryWriteBuilder<TModGetter> NoFormIDCompactnessCheck()
    {
        return WithFormIDCompactnessCheck(FormIDCompactionOption.NoCheck);
    }






    public BinaryWriteBuilder<TModGetter> WithFormIDCompactnessCheck(FormIDCompactionOption option)
    {
        return this with
        {
            _params = _params with
            {
                _param = _params._param with
                {
                    FormIDCompaction = option
                }
            }
        };
    }






    public BinaryWriteBuilder<TModGetter> WithStringsWriter(StringsWriter stringsWriter)
    {
        return this with
        {
            _params = _params with
            {
                _param = _params._param with
                {
                    StringsWriter = stringsWriter
                }
            }
        };
    }






    public BinaryWriteBuilder<TModGetter> WithTargetLanguage(Language language)
    {
        return this with
        {
            _params = _params with
            {
                _param = _params._param with
                {
                    TargetLanguageOverride = language
                }
            }
        };
    }





    public BinaryWriteBuilder<TModGetter> NoNullFormIDStandardization()
    {
        return this with
        {
            _params = _params with
            {
                _param = _params._param with
                {
                    CleanNulls = false
                }
            }
        };
    }






    public BinaryWriteBuilder<TModGetter> WithEmbeddedEncodings(EncodingBundle? encodingBundle)
    {
        return this with
        {
            _params = _params with
            {
                _param = _params._param with
                {
                    Encodings = encodingBundle
                }
            }
        };
    }







    public BinaryWriteBuilder<TModGetter> WithUtf8Encoding(bool on = true)
    {
        return this with
        {
            _params = _params with
            {
                _param = _params._param with
                {
                    Encodings = on
                        ? new EncodingBundle(NonTranslated: MutagenEncoding._1252, NonLocalized: MutagenEncoding._utf8)
                        : null
                }
            }
        };
    }








    public BinaryWriteBuilder<TModGetter> WithPlaceholderMasterIfLowerRangeDisallowed(ModKey placeholder)
    {
        return this with
        {
            _params = _params with
            {
                _param = _params._param with
                {
                    LowerRangeDisallowedHandler = ALowerRangeDisallowedHandlerOption.AddPlaceholderIfNotSkipping(placeholder, _params._param.LowerRangeDisallowedHandler)
                }
            }
        };
    }








    public BinaryWriteBuilder<TModGetter> WithPlaceholderMasterIfLowerRangeDisallowed(ILoadOrderGetter loadOrder)
    {
        return this with
        {
            _params = _params with
            {
                _param = _params._param with
                {
                    LowerRangeDisallowedHandler = ALowerRangeDisallowedHandlerOption.AddPlaceholderIfNotSkipping(loadOrder, _params._param.LowerRangeDisallowedHandler)
                }
            }
        };
    }








    public BinaryWriteBuilder<TModGetter> WithPlaceholderMasterIfLowerRangeDisallowed(IEnumerable<ModKey> loadOrder)
    {
        return this with
        {
            _params = _params with
            {
                _param = _params._param with
                {
                    LowerRangeDisallowedHandler = ALowerRangeDisallowedHandlerOption.AddPlaceholderIfNotSkipping(loadOrder, _params._param.LowerRangeDisallowedHandler)
                }
            }
        };
    }






    public BinaryWriteBuilder<TModGetter> ThrowIfLowerRangeDisallowed()
    {
        return this with
        {
            _params = _params with
            {
                _param = _params._param with
                {
                    LowerRangeDisallowedHandler = new ThrowIfLowerRangeDisallowed()
                }
            }
        };
    }





    public BinaryWriteBuilder<TModGetter> NoCheckIfLowerRangeDisallowed()
    {
        return this with
        {
            _params = _params with
            {
                _param = _params._param with
                {
                    LowerRangeDisallowedHandler = new NoCheckIfLowerRangeDisallowed()
                }
            }
        };
    }






    public BinaryWriteBuilder<TModGetter> WithParallelWriteParameters(ParallelWriteParameters parameters)
    {
        return this with
        {
            _params = _params with
            {
                _param = _params._param with
                {
                    Parallel = parameters
                }
            }
        };
    }





    public BinaryWriteBuilder<TModGetter> SingleThread()
    {
        return this with
        {
            _params = _params with
            {
                _param = _params._param with
                {
                    Parallel = new ParallelWriteParameters()
                    {
                        MaxDegreeOfParallelism = 1
                    }
                }
            }
        };
    }







    public BinaryWriteBuilder<TModGetter> WithExtraIncludedMasters(IEnumerable<ModKey> modKeys)
    {
        return WithExtraIncludedMasters(modKeys.ToArray());
    }







    public BinaryWriteBuilder<TModGetter> WithExtraIncludedMasters(params ModKey[] modKeys)
    {
        return this with
        {
            _params = _params with
            {
                _param = _params._param with
                {
                    MastersContentCustomOverride = (mods) =>
                    {
                        return (_params._param.MastersContentCustomOverride?.Invoke(mods) ?? mods)
                            .And(modKeys)
                            .Distinct()
                            .ToArray();
                    }
                }
            }
        };
    }








    public BinaryWriteBuilder<TModGetter> WithExplicitOverridingMasterList(IEnumerable<ModKey> modKeys)
    {
        return this with
        {
            _params = _params with
            {
                _masterSyncAction = null,
                _param = _params._param with
                {
                    MastersListContent = MastersListContentOption.NoCheck,
                    MastersContentCustomOverride = (mods) => modKeys.ToArray(),
                    MastersListOrdering = new MastersListOrderingByLoadOrder(modKeys.ToArray())
                }
            }
        };
    }








    public BinaryWriteBuilder<TModGetter> WithExplicitOverridingMasterList(params ModKey[] modKeys)
    {
        return WithExplicitOverridingMasterList((IEnumerable<ModKey>)modKeys);
    }







    public BinaryWriteBuilder<TModGetter> WithAllParentMasters()
    {
        return this with
        {
            _params = _params with
            {
                _masterSyncAction = static (mod, p) =>
                {
                    var dataFolder = p._dataFolderGetter?.Invoke(mod, p._param) ?? throw new ArgumentNullException("Data folder source was not set");

                    return p._param with
                    {
                        MastersContentCustomOverride = (mods) =>
                        {
                            var locator = new TransitiveMasterLocator(
                                p._param.FileSystem.GetOrDefault(),
                                new DataDirectoryInjection(dataFolder),
                                new GameReleaseInjection(p._gameRelease));
                            return locator.GetAllMasters(
                                mod.ModKey,
                                mods,
                                p._knownModLoadOrder);
                        }
                    };
                },
            }
        };
    }








    public BinaryWriteBuilder<TModGetter> WithKnownMasters(params IModMasterStyledGetter[] knownMasters)
    {
        var match = _params.KnownMasters.FirstOrDefault(existingKnownMaster =>
            knownMasters.Any(x => x.ModKey == existingKnownMaster.ModKey));
        if (match != null)
        {
            throw new ArgumentException($"ModKey was already added as a known master: {match.ModKey}");
        }

        return this with
        {
            _params = _params with
            {
                KnownMasters = _params.KnownMasters.And(knownMasters).ToArray()
            }
        };
    }








    public BinaryWriteBuilder<TModGetter> WithKnownMasters(params KeyedMasterStyle[] knownMasters)
    {
        return WithKnownMasters(knownMasters.Cast<IModMasterStyledGetter>().ToArray());
    }

    internal BinaryWriteBuilder<TModGetter> WithOverriddenFormsOption(OverriddenFormsOption option)
    {
        return this with
        {
            _params = _params with
            {
                _param = _params._param with
                {
                    OverriddenFormsOption = option
                }
            }
        };
    }

    public BinaryWriteBuilder<TModGetter> WithDataFolder(DirectoryPath? dataFolder)
    {
        if (dataFolder == null)
        {
            return new BinaryWriteBuilder<TModGetter>(_params);
        }
        return new BinaryWriteBuilder<TModGetter>(_params with
        {
            _dataFolderGetter = (m, p) => dataFolder.Value
        });
    }






    public async Task WriteAsync(TModGetter mod)
    {
        await _params._writer.WriteAsync(
            mod,
            BinaryWriteBuilderHelper.RunPreWriteSetters<TModGetter>(mod, _params));
    }





    public void Write(TModGetter mod)
    {
        _params._writer.Write(
            mod,
            BinaryWriteBuilderHelper.RunPreWriteSetters<TModGetter>(mod, _params));
    }
}

internal static class BinaryWriteBuilderHelper
{
    public static BinaryWriteBuilderParams<TModGetter> RunPreWriteSetters<TModGetter>(
        TModGetter mod,
        BinaryWriteBuilderParams<TModGetter> p)
        where TModGetter : class, IModGetter
    {
        var knownSet = new HashSet<ModKey>(p.KnownMasters.Select(x => x.ModKey));
        if (p._gameRelease != mod.GameRelease)
        {
            throw new ArgumentException($"GameRelease did not match provided mod: {p._gameRelease} != {mod.GameRelease}");
        }
        if (p._loadOrderSetter != null)
        {
            p = p with
            {
                _param = p._loadOrderSetter(mod, p, knownSet)
            };
        }


        if (p._param.MasterFlagsLookup != null)
        {
            Cache<IModMasterStyledGetter, ModKey>? masterFlagsLookup = new(x => x.ModKey);
            masterFlagsLookup.SetTo(p._param.MasterFlagsLookup.Items);
            p.KnownMasters.ForEach(x => masterFlagsLookup.Add(x));

            if (masterFlagsLookup.Count == 0)
            {
                masterFlagsLookup = null;
            }

            p = p with
            {
                _param = p._param with
                {
                    MasterFlagsLookup = masterFlagsLookup
                }
            };
        }
        else if (p.KnownMasters.Length > 0)
        {
            Cache<IModMasterStyledGetter, ModKey> masterFlagsLookup = new(x => x.ModKey);
            masterFlagsLookup.SetTo(p.KnownMasters);
            p = p with
            {
                _param = p._param with
                {
                    MasterFlagsLookup = masterFlagsLookup
                }
            };
        }

        if (p._masterSyncAction != null)
        {
            p = p with
            {
                _param = p._masterSyncAction(mod, p)
            };
        }

        return p;
    }
}