using System.IO.Abstractions;
using Loqui.Internal;
using Mutagen.Bethesda.Installs.DI;
using Mutagen.Bethesda.Plugins.Analysis;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Cache.Internals.Implementations;
using Mutagen.Bethesda.Plugins.Masters;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Strings;
using Mutagen.Bethesda.Strings.DI;
using Noggog;

namespace Mutagen.Bethesda.Plugins.Binary.Translations;

internal record BinaryReadBuilderParams<TMod, TModGetter, TGroupMask>
    where TMod : IMod
    where TModGetter : class, IModDisposeGetter
{
    internal GameRelease GameRelease { get; init; }
    internal ModKey ModKey { get; init; }
    internal FilePath? _path { get; init; }
    internal Stream? _stream { get; init; }
    internal Func<Stream>? _streamFactory { get; init; }
    internal bool _needsRecordTypeInfoCacheReader { get; init; }
    internal ErrorMaskBuilder? ErrorMaskBuilder { get; init; }
    internal TGroupMask? GroupMask { get; init; }
    internal BinaryReadParameters Params { get; init; } = BinaryReadParameters.Default;
    internal IBinaryReadBuilderInstantiator<TMod, TModGetter, TGroupMask> _instantiator { get; init; } = null!;
    internal Func<BinaryReadBuilderParams<TMod, TModGetter, TGroupMask>, IReadOnlyCollection<ModKey>, IEnumerable<IModMasterStyledGetter>>? _loadOrderSetter { get; init; }
    internal Func<BinaryReadBuilderParams<TMod, TModGetter, TGroupMask>, DirectoryPath>? _dataFolderGetter { get; init; }
    internal IModMasterStyledGetter[] KnownMasters { get; init; } = [];
    internal bool _autoSplit { get; init; }
    internal bool _hasLoadOrderCall { get; init; }
    internal bool _hasLinkCacheCall { get; init; }
}

internal interface IBinaryReadBuilderInstantiator<TMod, TModGetter, TGroupMask>
    where TMod : IMod
    where TModGetter : class, IModDisposeGetter
{
    TMod Mutable(BinaryReadMutableBuilder<TMod, TModGetter, TGroupMask> builder);
    TModGetter Readonly(BinaryReadBuilder<TMod, TModGetter, TGroupMask> builder);
}

public class BinaryReadBuilderSourceChoice<TMod, TModGetter, TGroupMask>
    where TMod : IMod
    where TModGetter : class, IModDisposeGetter
{
    private readonly GameRelease _release;
    private readonly IBinaryReadBuilderInstantiator<TMod, TModGetter, TGroupMask> _instantiator;
    private readonly bool _needsRecordTypeInfoCacheReader;

    internal BinaryReadBuilderSourceChoice(
        GameRelease release,
        IBinaryReadBuilderInstantiator<TMod, TModGetter, TGroupMask> instantiator,
        bool needsRecordTypeInfoCacheReader)
    {
        _release = release;
        _instantiator = instantiator;
        _needsRecordTypeInfoCacheReader = needsRecordTypeInfoCacheReader;
    }

    public BinaryReadBuilder<TMod, TModGetter, TGroupMask> FromPath(
        ModPath path)
    {
        return new BinaryReadBuilder<TMod, TModGetter, TGroupMask>(
            new BinaryReadBuilderParams<TMod, TModGetter, TGroupMask>()
            {
                _instantiator = _instantiator,
                GameRelease = _release,
                ModKey = path.ModKey,
                _path = path.Path,
                _needsRecordTypeInfoCacheReader = _needsRecordTypeInfoCacheReader
            });
    }

    public BinaryReadBuilder<TMod, TModGetter, TGroupMask> FromStream(
        Stream stream,
        ModKey modKey)
    {
        return new BinaryReadBuilder<TMod, TModGetter, TGroupMask>(
            new BinaryReadBuilderParams<TMod, TModGetter, TGroupMask>()
            {
                _instantiator = _instantiator,
                GameRelease = _release,
                ModKey = modKey,
                _stream = stream,
                _needsRecordTypeInfoCacheReader = _needsRecordTypeInfoCacheReader
            });
    }
}

public class BinaryReadBuilderSourceStreamFactoryChoice<TMod, TModGetter, TGroupMask>
    where TMod : IMod
    where TModGetter : class, IModDisposeGetter
{
    private readonly GameRelease _release;
    private readonly IBinaryReadBuilderInstantiator<TMod, TModGetter, TGroupMask> _instantiator;
    private readonly bool _needsRecordTypeInfoCacheReader;

    internal BinaryReadBuilderSourceStreamFactoryChoice(
        GameRelease release,
        IBinaryReadBuilderInstantiator<TMod, TModGetter, TGroupMask> instantiator,
        bool needsRecordTypeInfoCacheReader)
    {
        _release = release;
        _instantiator = instantiator;
        _needsRecordTypeInfoCacheReader = needsRecordTypeInfoCacheReader;
    }

    public BinaryReadBuilder<TMod, TModGetter, TGroupMask> FromPath(ModPath path)
    {
        return new BinaryReadBuilder<TMod, TModGetter, TGroupMask>(
            new BinaryReadBuilderParams<TMod, TModGetter, TGroupMask>()
            {
                _instantiator = _instantiator,
                GameRelease = _release,
                ModKey = path.ModKey,
                _path = path.Path,
                _needsRecordTypeInfoCacheReader = _needsRecordTypeInfoCacheReader
            });
    }

    public BinaryReadBuilder<TMod, TModGetter, TGroupMask> FromStreamFactory(
        Func<Stream> streamFactory,
        ModKey modKey)
    {
        return new BinaryReadBuilder<TMod, TModGetter, TGroupMask>(
            new BinaryReadBuilderParams<TMod, TModGetter, TGroupMask>()
            {
                _instantiator = _instantiator,
                GameRelease = _release,
                ModKey = modKey,
                _streamFactory = streamFactory,
                _needsRecordTypeInfoCacheReader = _needsRecordTypeInfoCacheReader
            });
    }
}

public class BinaryReadBuilderSeparatedSourceChoice<TMod, TModGetter, TGroupMask>
    where TMod : IMod
    where TModGetter : class, IModDisposeGetter
{
    private readonly GameRelease _release;
    private readonly IBinaryReadBuilderInstantiator<TMod, TModGetter, TGroupMask> _instantiator;
    private readonly bool _needsRecordTypeInfoCacheReader;

    internal BinaryReadBuilderSeparatedSourceChoice(
        GameRelease release,
        IBinaryReadBuilderInstantiator<TMod, TModGetter, TGroupMask> instantiator,
        bool needsRecordTypeInfoCacheReader)
    {
        _release = release;
        _instantiator = instantiator;
        _needsRecordTypeInfoCacheReader = needsRecordTypeInfoCacheReader;
    }

    public BinaryReadBuilderSeparatedChoice<TMod, TModGetter, TGroupMask> FromPath(
        ModPath path)
    {
        return new BinaryReadBuilderSeparatedChoice<TMod, TModGetter, TGroupMask>(
            new BinaryReadBuilderParams<TMod, TModGetter, TGroupMask>()
            {
                _instantiator = _instantiator,
                GameRelease = _release,
                ModKey = path.ModKey,
                _path = path.Path,
                _needsRecordTypeInfoCacheReader = _needsRecordTypeInfoCacheReader
            });
    }

    public BinaryReadBuilderSeparatedChoice<TMod, TModGetter, TGroupMask> FromStream(
        Stream stream,
        ModKey modKey)
    {
        return new BinaryReadBuilderSeparatedChoice<TMod, TModGetter, TGroupMask>(
            new BinaryReadBuilderParams<TMod, TModGetter, TGroupMask>()
            {
                _instantiator = _instantiator,
                GameRelease = _release,
                ModKey = modKey,
                _stream = stream,
                _needsRecordTypeInfoCacheReader = _needsRecordTypeInfoCacheReader
            });
    }
}

public class BinaryReadBuilderSeparatedChoice<TMod, TModGetter, TGroupMask>
    where TMod : IMod
    where TModGetter : class, IModDisposeGetter
{
    private readonly BinaryReadBuilderParams<TMod, TModGetter, TGroupMask> _param;

    internal BinaryReadBuilderSeparatedChoice(
        BinaryReadBuilderParams<TMod, TModGetter, TGroupMask> param)
    {
        _param = param;
    }

    public BinaryReadBuilder<TMod, TModGetter, TGroupMask> WithDefaultLoadOrder()
    {
        return new BinaryReadBuilder<TMod, TModGetter, TGroupMask>(_param with
        {
            _dataFolderGetter = static (param) => GameLocatorLookupCache.Instance.GetDataDirectory(param.GameRelease),
            _loadOrderSetter = static (param, alreadyKnownMasters) =>
            {
                var dataFolder = param._dataFolderGetter?.Invoke(param) ?? throw new ArgumentNullException("Data folder source was not set");
                var lo = LoadOrder.Import<IModMasterStyledGetter>(
                    dataFolder,
                    param.GameRelease,
                    factory: (modPath) => KeyedMasterStyle.FromPath(modPath, param.GameRelease, param.Params.FileSystem),
                    param.Params.FileSystem);
                return lo.ListedOrder
                    .Where(x => !alreadyKnownMasters.Contains(x.ModKey))
                    .ResolveExistingMods();
            }
        });
    }

    public BinaryReadBuilderDataFolderChoice<TMod, TModGetter, TGroupMask> WithLoadOrder(IEnumerable<ModKey>? loadOrder)
    {
        return WithLoadOrder(loadOrder?.ToArray() ?? []);
    }

    public BinaryReadBuilderDataFolderChoice<TMod, TModGetter, TGroupMask> WithLoadOrder(params ModKey[] loadOrder)
    {
        return new BinaryReadBuilderDataFolderChoice<TMod, TModGetter, TGroupMask>(_param with
        {
            _loadOrderSetter = (param, alreadyKnownMasters) =>
            {
                if (loadOrder.Length == 0)
                {
                    return [];
                }

                var dataFolder = param._dataFolderGetter?.Invoke(param);
                if (dataFolder == null)
                {
                    return [];
                }

                var lo = LoadOrder.Import<IModMasterStyledGetter>(
                    dataFolder.Value,
                    loadOrder,
                    param.GameRelease,
                    factory: (modPath) => KeyedMasterStyle.FromPath(modPath, param.GameRelease, param.Params.FileSystem),
                    param.Params.FileSystem);
                return lo.ListedOrder
                    .Where(x => !alreadyKnownMasters.Contains(x.ModKey))
                    .ResolveExistingMods();
            }
        });
    }

    public BinaryReadBuilder<TMod, TModGetter, TGroupMask> WithLoadOrder(IEnumerable<IModMasterStyledGetter>? loadOrder)
    {
        return WithLoadOrder(loadOrder?.ToArray() ?? []);
    }

    public BinaryReadBuilder<TMod, TModGetter, TGroupMask> WithLoadOrder(params IModMasterStyledGetter[] loadOrder)
    {
        return new BinaryReadBuilder<TMod, TModGetter, TGroupMask>(_param with
        {
            _loadOrderSetter = (param, alreadyKnownMasters) =>
            {
                var lo = new LoadOrder<IModMasterStyledGetter>(loadOrder, disposeItems: false);
                return lo.ListedOrder
                    .Where(x => !alreadyKnownMasters.Contains(x.ModKey));
            }
        });
    }

    public BinaryReadBuilder<TMod, TModGetter, TGroupMask> WithLoadOrder(ILoadOrderGetter<IModMasterStyledGetter>? loadOrder)
    {
        if (loadOrder == null)
        {
            return new BinaryReadBuilder<TMod, TModGetter, TGroupMask>(_param);
        }

        return new BinaryReadBuilder<TMod, TModGetter, TGroupMask>(_param with
        {
            _loadOrderSetter = (param, alreadyKnownMasters) =>
            {
                return loadOrder.ListedOrder
                    .Where(x => !alreadyKnownMasters.Contains(x.ModKey));
            }
        });
    }

    public BinaryReadBuilder<TMod, TModGetter, TGroupMask> WithNoLoadOrder()
    {
        return new BinaryReadBuilder<TMod, TModGetter, TGroupMask>(_param);
    }

    public BinaryReadBuilderDataFolderChoice<TMod, TModGetter, TGroupMask> WithLoadOrderFromHeaderMasters()
    {
        return new BinaryReadBuilderDataFolderChoice<TMod, TModGetter, TGroupMask>(_param with
        {
            _loadOrderSetter = static (param, alreadyKnownMasters) =>
            {
                var dataFolder = param._dataFolderGetter?.Invoke(param);
                if (dataFolder == null)
                {
                    return [];
                }

                ModHeaderFrame modHeader;
                if (param._path != null)
                {
                    modHeader = ModHeaderFrame.FromPath(
                        new ModPath(param.ModKey, param._path.Value), param.GameRelease,
                        param.Params.FileSystem);
                }
                else if (param._stream != null)
                {
                    var pos = param._stream.Position;
                    modHeader = ModHeaderFrame.FromStream(param._stream, param.ModKey, param.GameRelease);
                    param._stream.Position = pos;
                }
                else if (param._streamFactory != null)
                {
                    using var stream = param._streamFactory();
                    modHeader = ModHeaderFrame.FromStream(stream, param.ModKey, param.GameRelease);
                }
                else
                {
                    throw new ArgumentException("Parameters didn't define any filepath or streams");
                }

                var masters = MasterReferenceCollection.FromModHeader(
                    param.ModKey,
                    modHeader);
                var lo = LoadOrder.Import<IModMasterStyledGetter>(
                    dataFolder.Value,
                    masters.Masters.Select(x => x.Master),
                    param.GameRelease,
                    factory: (modPath) => KeyedMasterStyle.FromPath(modPath, param.GameRelease, param.Params.FileSystem),
                    param.Params.FileSystem);
                return lo.ListedOrder
                    .Where(x => !alreadyKnownMasters.Contains(x.ModKey))
                    .ResolveExistingMods();
            }
        });
    }

    public BinaryReadBuilder<TMod, TModGetter, TGroupMask> WithKnownMasters(params IModMasterStyledGetter[] knownMasters)
    {
        var match = _param.KnownMasters.FirstOrDefault(existingKnownMaster =>
            knownMasters.Any(x => x.ModKey == existingKnownMaster.ModKey));
        if (match != null)
        {
            throw new ArgumentException($"ModKey was already added as a known master: {match.ModKey}");
        }

        return new BinaryReadBuilder<TMod, TModGetter, TGroupMask>(_param with
        {
            KnownMasters = _param.KnownMasters.And(knownMasters).ToArray()
        });
    }

    public BinaryReadBuilder<TMod, TModGetter, TGroupMask> WithKnownMasters(params KeyedMasterStyle[] knownMasters)
    {
        return WithKnownMasters(knownMasters.Cast<IModMasterStyledGetter>().ToArray());
    }

    public BinaryReadBuilder<TMod, TModGetter, TGroupMask> WithLinkCache(ILinkCache? linkCache)
    {
        return new BinaryReadBuilder<TMod, TModGetter, TGroupMask>(_param with
        {
            Params = _param.Params with
            {
                LinkCache = linkCache
            },
            _hasLinkCacheCall = true
        });
    }
}

public class BinaryReadBuilderDataFolderChoice<TMod, TModGetter, TGroupMask>
    where TMod : IMod
    where TModGetter : class, IModDisposeGetter
{
    private readonly BinaryReadBuilderParams<TMod, TModGetter, TGroupMask> _param;

    internal BinaryReadBuilderDataFolderChoice(
        BinaryReadBuilderParams<TMod, TModGetter, TGroupMask> param)
    {
        _param = param;
    }

    public BinaryReadBuilder<TMod, TModGetter, TGroupMask> WithDefaultDataFolder()
    {
        return new BinaryReadBuilder<TMod, TModGetter, TGroupMask>(_param with
        {
            _dataFolderGetter = static (param) => GameLocatorLookupCache.Instance.GetDataDirectory(param.GameRelease)
        });
    }

    public BinaryReadBuilder<TMod, TModGetter, TGroupMask> WithDataFolder(DirectoryPath? dataFolder)
    {
        if (dataFolder == null)
        {
            return new BinaryReadBuilder<TMod, TModGetter, TGroupMask>(_param);
        }
        return new BinaryReadBuilder<TMod, TModGetter, TGroupMask>(_param with
        {
            _dataFolderGetter = (param) => dataFolder.Value
        });
    }

    public BinaryReadBuilder<TMod, TModGetter, TGroupMask> WithNoDataFolder()
    {
        return new BinaryReadBuilder<TMod, TModGetter, TGroupMask>(_param);
    }
}

public record BinaryReadBuilder<TMod, TModGetter, TGroupMask>
    where TMod : IMod
    where TModGetter : class, IModDisposeGetter
{
    internal BinaryReadBuilderParams<TMod, TModGetter, TGroupMask> _param { get; set; }

    internal BinaryReadBuilder(
        BinaryReadBuilderParams<TMod, TModGetter, TGroupMask> param)
    {
        _param = param;
    }

    public TModGetter Construct()
    {
        _param = BinaryReadBuilderHelper.RunFinalizationSetters(_param);

        if (_param._autoSplit)
        {
            if (_param._path == null)
            {
                throw new NotSupportedException("WithAutoSplitSupport() only works with file path reads (FromPath), not stream reads.");
            }

            var fileSystem = _param.Params.FileSystem.GetOrDefault();

            if (MultiModFileAnalysis.IsMultiModFile(_param._path.Value, fileSystem))
            {
                var splitFiles = MultiModFileAnalysis.GetSplitModFiles(_param._path.Value, fileSystem);
                var loadOrder = _param.Params.MasterFlagsLookup?.Items.Select(x => x.ModKey) ?? Enumerable.Empty<ModKey>();

                return ModFactory<TModGetter>.ImportMultiFileGetter(
                    _param.ModKey,
                    splitFiles.Select(f => (ModPath)f.Path),
                    loadOrder,
                    _param.GameRelease,
                    _param.Params);
            }
        }

        return _param._instantiator.Readonly(this);
    }

    public BinaryReadMutableBuilder<TMod, TModGetter, TGroupMask> Mutable()
    {
        return new BinaryReadMutableBuilder<TMod, TModGetter, TGroupMask>(_param);
    }

    #region Common

    public BinaryReadBuilder<TMod, TModGetter, TGroupMask> WithStringsFolder(DirectoryPath dir)
    {
        return this with
        {
            _param = _param with
            {
                Params = _param.Params with
                {
                    StringsParam = (_param.Params.StringsParam ?? new()) with
                    {
                        StringsFolderOverride = dir
                    }
                }
            }
        };
    }

    public BinaryReadBuilder<TMod, TModGetter, TGroupMask> WithStringsParameters(StringsReadParameters param)
    {
        return this with
        {
            _param = _param with
            {
                Params = _param.Params with
                {
                    StringsParam = param
                }
            }
        };
    }

    public BinaryReadBuilder<TMod, TModGetter, TGroupMask> WithBsaFolder(DirectoryPath dir)
    {
        return this with
        {
            _param = _param with
            {
                Params = _param.Params with
                {
                    StringsParam = (_param.Params.StringsParam ?? new()) with
                    {
                        BsaFolderOverride = dir
                    }
                }
            }
        };
    }

    public BinaryReadBuilder<TMod, TModGetter, TGroupMask> WithEncoding(IMutagenEncodingProvider encodingProvider)
    {
        return this with
        {
            _param = _param with
            {
                Params = _param.Params with
                {
                    StringsParam = (_param.Params.StringsParam ?? new()) with
                    {
                        EncodingProvider = encodingProvider
                    }
                }
            }
        };
    }

    public BinaryReadBuilder<TMod, TModGetter, TGroupMask> WithTargetLanguage(Language targetLanguage)
    {
        return this with
        {
            _param = _param with
            {
                Params = _param.Params with
                {
                    StringsParam = (_param.Params.StringsParam ?? new()) with
                    {
                        TargetLanguage = targetLanguage
                    }
                }
            }
        };
    }

    public BinaryReadBuilder<TMod, TModGetter, TGroupMask> WithNonTranslatedEncoding(IMutagenEncoding nonTranslatedEncoding)
    {
        return this with
        {
            _param = _param with
            {
                Params = _param.Params with
                {
                    StringsParam = (_param.Params.StringsParam ?? new()) with
                    {
                        NonTranslatedEncodingOverride = nonTranslatedEncoding
                    }
                }
            }
        };
    }

    public BinaryReadBuilder<TMod, TModGetter, TGroupMask> WithNonLocalizedEncoding(IMutagenEncoding nonLocalizedEncoding)
    {
        return this with
        {
            _param = _param with
            {
                Params = _param.Params with
                {
                    StringsParam = (_param.Params.StringsParam ?? new()) with
                    {
                        NonLocalizedEncodingOverride = nonLocalizedEncoding
                    }
                }
            }
        };
    }

    public BinaryReadBuilder<TMod, TModGetter, TGroupMask> WithUtf8Encoding(bool on = true)
    {
        return this with
        {
            _param = _param with
            {
                Params = _param.Params with
                {
                    StringsParam = (_param.Params.StringsParam ?? new()) with
                    {
                        NonLocalizedEncodingOverride = on ? MutagenEncoding._utf8 : null
                    }
                }
            }
        };
    }

    public BinaryReadBuilder<TMod, TModGetter, TGroupMask> SingleThread()
    {
        return this with
        {
            _param = _param with
            {
                Params = _param.Params with
                {
                    Parallel = false
                }
            }
        };
    }

    public BinaryReadBuilder<TMod, TModGetter, TGroupMask> Parallel(bool parallel = true)
    {
        return this with
        {
            _param = _param with
            {
                Params = _param.Params with
                {
                    Parallel = parallel
                }
            }
        };
    }

    public BinaryReadBuilder<TMod, TModGetter, TGroupMask> ThrowIfUnknownSubrecord(bool shouldThrow = true)
    {
        return this with
        {
            _param = _param with
            {
                Params = _param.Params with
                {
                    ThrowOnUnknownSubrecord = shouldThrow
                }
            }
        };
    }

    public BinaryReadBuilder<TMod, TModGetter, TGroupMask> WithKnownMasters(params IModMasterStyledGetter[] knownMasters)
    {
        var match = _param.KnownMasters.FirstOrDefault(existingKnownMaster =>
            knownMasters.Any(x => x.ModKey == existingKnownMaster.ModKey));
        if (match != null)
        {
            throw new ArgumentException($"ModKey was already added as a known master: {match.ModKey}");
        }
        return this with
        {
            _param = _param with
            {
                KnownMasters = _param.KnownMasters.And(knownMasters).ToArray()
            }
        };
    }

    public BinaryReadBuilder<TMod, TModGetter, TGroupMask> WithKnownMasters(params KeyedMasterStyle[] knownMasters)
    {
        return WithKnownMasters(knownMasters.Cast<IModMasterStyledGetter>().ToArray());
    }

    public BinaryReadBuilder<TMod, TModGetter, TGroupMask> WithFileSystem(IFileSystem? fileSystem)
    {
        return this with
        {
            _param = _param with
            {
                Params = _param.Params with
                {
                    FileSystem = fileSystem
                }
            }
        };
    }

    public BinaryReadBuilder<TMod, TModGetter, TGroupMask> WithDefaultDataFolder()
    {
        return new BinaryReadBuilder<TMod, TModGetter, TGroupMask>(_param with
        {
            _dataFolderGetter = static (param) => GameLocatorLookupCache.Instance.GetDataDirectory(param.GameRelease)
        });
    }

    public BinaryReadBuilder<TMod, TModGetter, TGroupMask> WithDataFolder(DirectoryPath? dataFolder)
    {
        if (dataFolder == null)
        {
            return new BinaryReadBuilder<TMod, TModGetter, TGroupMask>(_param);
        }
        return new BinaryReadBuilder<TMod, TModGetter, TGroupMask>(_param with
        {
            _dataFolderGetter = (param) => dataFolder.Value
        });
    }

    public BinaryReadBuilder<TMod, TModGetter, TGroupMask> WithAutoSplitSupport()
    {
        return this with
        {
            _param = _param with
            {
                _autoSplit = true
            }
        };
    }

    private void AssertLoadOrderLinkCacheMutualExclusion(bool isLoadOrderCall)
    {
        if (isLoadOrderCall && _param._hasLinkCacheCall)
        {
            throw new InvalidOperationException("Cannot call WithLoadOrder after WithLinkCache has been called. These methods are mutually exclusive.");
        }
        if (!isLoadOrderCall && _param._hasLoadOrderCall)
        {
            throw new InvalidOperationException("Cannot call WithLinkCache after WithLoadOrder has been called. These methods are mutually exclusive.");
        }
    }

    public BinaryReadBuilder<TMod, TModGetter, TGroupMask> WithLinkCache(ILinkCache? linkCache)
    {
        AssertLoadOrderLinkCacheMutualExclusion(isLoadOrderCall: false);

        return this with
        {
            _param = _param with
            {
                Params = _param.Params with
                {
                    LinkCache = linkCache
                },
                _hasLinkCacheCall = true
            }
        };
    }

    public BinaryReadBuilderDataFolderChoice<TMod, TModGetter, TGroupMask> WithLoadOrder(IEnumerable<ModKey>? loadOrder)
    {
        AssertLoadOrderLinkCacheMutualExclusion(isLoadOrderCall: true);
        return WithLoadOrder(loadOrder?.ToArray() ?? []);
    }

    public BinaryReadBuilderDataFolderChoice<TMod, TModGetter, TGroupMask> WithLoadOrder(params ModKey[] loadOrder)
    {
        AssertLoadOrderLinkCacheMutualExclusion(isLoadOrderCall: true);

        return new BinaryReadBuilderDataFolderChoice<TMod, TModGetter, TGroupMask>(_param with
        {
            _hasLoadOrderCall = true,
            _loadOrderSetter = (param, alreadyKnownMasters) =>
            {
                if (loadOrder.Length == 0)
                {
                    return [];
                }

                var dataFolder = param._dataFolderGetter?.Invoke(param);
                if (dataFolder == null)
                {
                    return [];
                }

                var lo = LoadOrder.Import<IModMasterStyledGetter>(
                    dataFolder.Value,
                    loadOrder,
                    param.GameRelease,
                    factory: (modPath) => KeyedMasterStyle.FromPath(modPath, param.GameRelease, param.Params.FileSystem),
                    param.Params.FileSystem);
                return lo.ListedOrder
                    .Where(x => !alreadyKnownMasters.Contains(x.ModKey))
                    .ResolveExistingMods();
            }
        });
    }

    public BinaryReadBuilderDataFolderChoice<TMod, TModGetter, TGroupMask> WithLoadOrder(IEnumerable<IModMasterStyledGetter>? loadOrder)
    {
        AssertLoadOrderLinkCacheMutualExclusion(isLoadOrderCall: true);
        return WithLoadOrder(loadOrder?.ToArray() ?? []);
    }

    public BinaryReadBuilderDataFolderChoice<TMod, TModGetter, TGroupMask> WithLoadOrder(params IModMasterStyledGetter[] loadOrder)
    {
        AssertLoadOrderLinkCacheMutualExclusion(isLoadOrderCall: true);

        return new BinaryReadBuilderDataFolderChoice<TMod, TModGetter, TGroupMask>(_param with
        {
            _hasLoadOrderCall = true,
            _loadOrderSetter = (param, alreadyKnownMasters) =>
            {
                var dataFolder = param._dataFolderGetter?.Invoke(param);
                if (dataFolder == null)
                {
                    return [];
                }

                var lo = new LoadOrder<IModMasterStyledGetter>(loadOrder, disposeItems: false);
                return lo.ListedOrder
                    .Where(x => !alreadyKnownMasters.Contains(x.ModKey));
            }
        });
    }

    public BinaryReadBuilderDataFolderChoice<TMod, TModGetter, TGroupMask> WithLoadOrder(ILoadOrderGetter<IModMasterStyledGetter>? loadOrder)
    {
        AssertLoadOrderLinkCacheMutualExclusion(isLoadOrderCall: true);

        if (loadOrder == null)
        {
            return new BinaryReadBuilderDataFolderChoice<TMod, TModGetter, TGroupMask>(_param with
            {
                _hasLoadOrderCall = true
            });
        }

        return new BinaryReadBuilderDataFolderChoice<TMod, TModGetter, TGroupMask>(_param with
        {
            _hasLoadOrderCall = true,
            _loadOrderSetter = (param, alreadyKnownMasters) =>
            {
                return loadOrder.ListedOrder
                    .Where(x => !alreadyKnownMasters.Contains(x.ModKey));
            }
        });
    }

    #endregion
}

public record BinaryReadMutableBuilder<TMod, TModGetter, TGroupMask> : BinaryReadBuilder<TMod, TModGetter, TGroupMask>
    where TMod : IMod
    where TModGetter : class, IModDisposeGetter
{
    internal BinaryReadMutableBuilder(
        BinaryReadBuilderParams<TMod, TModGetter, TGroupMask> param)
        : base(param)
    {
    }

    #region Common

    public BinaryReadMutableBuilder<TMod, TModGetter, TGroupMask> WithStringsFolder(DirectoryPath dir)
    {
        return this with
        {
            _param = _param with
            {
                Params = _param.Params with
                {
                    StringsParam = (_param.Params.StringsParam ?? new()) with
                    {
                        StringsFolderOverride = dir
                    }
                }
            }
        };
    }

    public BinaryReadMutableBuilder<TMod, TModGetter, TGroupMask> WithStringsParameters(StringsReadParameters param)
    {
        return this with
        {
            _param = _param with
            {
                Params = _param.Params with
                {
                    StringsParam = param
                }
            }
        };
    }

    public BinaryReadMutableBuilder<TMod, TModGetter, TGroupMask> WithBsaFolder(DirectoryPath dir)
    {
        return this with
        {
            _param = _param with
            {
                Params = _param.Params with
                {
                    StringsParam = (_param.Params.StringsParam ?? new()) with
                    {
                        BsaFolderOverride = dir
                    }
                }
            }
        };
    }

    public BinaryReadMutableBuilder<TMod, TModGetter, TGroupMask> WithEncoding(IMutagenEncodingProvider encodingProvider)
    {
        return this with
        {
            _param = _param with
            {
                Params = _param.Params with
                {
                    StringsParam = (_param.Params.StringsParam ?? new()) with
                    {
                        EncodingProvider = encodingProvider
                    }
                }
            }
        };
    }

    public BinaryReadMutableBuilder<TMod, TModGetter, TGroupMask> WithTargetLanguage(Language targetLanguage)
    {
        return this with
        {
            _param = _param with
            {
                Params = _param.Params with
                {
                    StringsParam = (_param.Params.StringsParam ?? new()) with
                    {
                        TargetLanguage = targetLanguage
                    }
                }
            }
        };
    }

    public BinaryReadMutableBuilder<TMod, TModGetter, TGroupMask> WithNonTranslatedEncoding(IMutagenEncoding nonTranslatedEncoding)
    {
        return this with
        {
            _param = _param with
            {
                Params = _param.Params with
                {
                    StringsParam = (_param.Params.StringsParam ?? new()) with
                    {
                        NonTranslatedEncodingOverride = nonTranslatedEncoding
                    }
                }
            }
        };
    }

    public BinaryReadMutableBuilder<TMod, TModGetter, TGroupMask> WithNonLocalizedEncoding(IMutagenEncoding nonLocalizedEncoding)
    {
        return this with
        {
            _param = _param with
            {
                Params = _param.Params with
                {
                    StringsParam = (_param.Params.StringsParam ?? new()) with
                    {
                        NonLocalizedEncodingOverride = nonLocalizedEncoding
                    }
                }
            }
        };
    }

    public BinaryReadMutableBuilder<TMod, TModGetter, TGroupMask> WithUtf8Encoding(bool on = true)
    {
        return this with
        {
            _param = _param with
            {
                Params = _param.Params with
                {
                    StringsParam = (_param.Params.StringsParam ?? new()) with
                    {
                        NonLocalizedEncodingOverride = on ? MutagenEncoding._utf8 : null
                    }
                }
            }
        };
    }

    public BinaryReadMutableBuilder<TMod, TModGetter, TGroupMask> SingleThread()
    {
        return this with
        {
            _param = _param with
            {
                Params = _param.Params with
                {
                    Parallel = false
                }
            }
        };
    }

    public BinaryReadMutableBuilder<TMod, TModGetter, TGroupMask> Parallel(bool parallel = true)
    {
        return this with
        {
            _param = _param with
            {
                Params = _param.Params with
                {
                    Parallel = parallel
                }
            }
        };
    }

    public BinaryReadMutableBuilder<TMod, TModGetter, TGroupMask> ThrowIfUnknownSubrecord(bool shouldThrow = true)
    {
        return this with
        {
            _param = _param with
            {
                Params = _param.Params with
                {
                    ThrowOnUnknownSubrecord = shouldThrow
                }
            }
        };
    }

    public BinaryReadBuilder<TMod, TModGetter, TGroupMask> WithKnownMasters(params IModMasterStyledGetter[] knownMasters)
    {
        var match = _param.KnownMasters.FirstOrDefault(existingKnownMaster =>
            knownMasters.Any(x => x.ModKey == existingKnownMaster.ModKey));
        if (match != null)
        {
            throw new ArgumentException($"ModKey was already added as a known master: {match.ModKey}");
        }
        return this with
        {
            _param = _param with
            {
                KnownMasters = _param.KnownMasters.And(knownMasters).ToArray()
            }
        };
    }

    public BinaryReadBuilder<TMod, TModGetter, TGroupMask> WithKnownMasters(params KeyedMasterStyle[] knownMasters)
    {
        return WithKnownMasters(knownMasters.Cast<IModMasterStyledGetter>().ToArray());
    }

    public BinaryReadMutableBuilder<TMod, TModGetter, TGroupMask> WithFileSystem(IFileSystem fileSystem)
    {
        return this with
        {
            _param = _param with
            {
                Params = _param.Params with
                {
                    FileSystem = fileSystem
                }
            }
        };
    }

    public BinaryReadMutableBuilder<TMod, TModGetter, TGroupMask> WithDefaultDataFolder()
    {
        return new BinaryReadMutableBuilder<TMod, TModGetter, TGroupMask>(_param with
        {
            _dataFolderGetter = static (param) => GameLocatorLookupCache.Instance.GetDataDirectory(param.GameRelease)
        });
    }

    public BinaryReadMutableBuilder<TMod, TModGetter, TGroupMask> WithDataFolder(DirectoryPath? dataFolder)
    {
        if (dataFolder == null)
        {
            return new BinaryReadMutableBuilder<TMod, TModGetter, TGroupMask>(_param);
        }
        return new BinaryReadMutableBuilder<TMod, TModGetter, TGroupMask>(_param with
        {
            _dataFolderGetter = (param) => dataFolder.Value
        });
    }

    public new BinaryReadMutableBuilder<TMod, TModGetter, TGroupMask> WithAutoSplitSupport()
    {
        return this with
        {
            _param = _param with
            {
                _autoSplit = true
            }
        };
    }

    #endregion

    public BinaryReadMutableBuilder<TMod, TModGetter, TGroupMask> WithErrorMask(ErrorMaskBuilder? errorMask)
    {
        return this with
        {
            _param = _param with
            {
                ErrorMaskBuilder = errorMask
            }
        };
    }

    public BinaryReadMutableBuilder<TMod, TModGetter, TGroupMask> WithGroupMask(TGroupMask mask)
    {
        return this with
        {
            _param = _param with
            {
                GroupMask = mask
            }
        };
    }

    public new TMod Construct()
    {
        _param = BinaryReadBuilderHelper.RunFinalizationSetters(_param);

        if (_param._autoSplit)
        {
            if (_param._path == null)
            {
                throw new NotSupportedException("WithAutoSplitSupport() only works with file path reads (FromPath), not stream reads.");
            }

            var fileSystem = _param.Params.FileSystem.GetOrDefault();

            if (MultiModFileAnalysis.IsMultiModFile(_param._path.Value, fileSystem))
            {
                var splitFiles = MultiModFileAnalysis.GetSplitModFiles(_param._path.Value, fileSystem);
                var loadOrder = _param.Params.MasterFlagsLookup?.Items.Select(x => x.ModKey) ?? Enumerable.Empty<ModKey>();

                using var overlay = ModFactory<TModGetter>.ImportMultiFileGetter(
                    _param.ModKey,
                    splitFiles.Select(f => (ModPath)f.Path),
                    loadOrder,
                    _param.GameRelease,
                    _param.Params);

                return (TMod)overlay.DeepCopy();
            }
        }

        return _param._instantiator.Mutable(this);
    }
}

internal static class BinaryReadBuilderHelper
{
    public static BinaryReadBuilderParams<TMod, TModGetter, TGroupMask> RunFinalizationSetters<TMod, TModGetter, TGroupMask>(
        BinaryReadBuilderParams<TMod, TModGetter, TGroupMask> p)
        where TMod : IMod
        where TModGetter : class, IModDisposeGetter
    {

        if (p._hasLoadOrderCall && p._hasLinkCacheCall)
        {
            throw new InvalidOperationException("Cannot use both WithLoadOrder and WithLinkCache. These methods are mutually exclusive.");
        }

        var knownSet = new HashSet<ModKey>(p.KnownMasters.Select(x => x.ModKey));
        IReadOnlyCollection<IModMasterStyledGetter>? loadOrder = null;

        ILinkCache? linkCache = p.Params.LinkCache;

        if (p._loadOrderSetter != null)
        {
            loadOrder = p._loadOrderSetter(p, knownSet)
                .And(p.KnownMasters)
                .Distinct(x => x.ModKey)
                .ToArray();
        }
        else if (linkCache != null)
        {
            loadOrder = linkCache.ListedOrder
                .And(p.KnownMasters)
                .Distinct(x => x.ModKey)
                .ToArray();
        }
        else if (p.KnownMasters.Length > 0)
        {
            loadOrder = p.KnownMasters;
        }

        if (linkCache == null && loadOrder != null && loadOrder.Count > 0)
        {
            var dataFolder = p._dataFolderGetter?.Invoke(p);
            if (dataFolder != null)
            {
                var fileSystem = p.Params.FileSystem.GetOrDefault();
                var modOverlays = new List<IModGetter>();

                var masterFlagsLookup = loadOrder != null && loadOrder.Count > 0
                    ? new LoadOrder<IModMasterStyledGetter>(
                        p.KnownMasters.And(loadOrder).Distinct(x => x.ModKey))
                    : null;

                var loadParams = new BinaryReadParameters
                {
                    FileSystem = fileSystem,
                    MasterFlagsLookup = masterFlagsLookup
                };

                foreach (var master in loadOrder!)
                {
                    var modPath = Path.Combine(dataFolder.Value, master.ModKey.FileName);
                    if (fileSystem.File.Exists(modPath))
                    {
                        var overlay = ModFactory<IModGetter>.Importer(
                            new ModPath(master.ModKey, modPath),
                            p.GameRelease,
                            loadParams);
                        modOverlays.Add(overlay);
                    }
                }

                if (modOverlays.Count > 0)
                {
                    linkCache = new ImmutableLoadOrderLinkCache(
                        modOverlays,
                        gameCategory: p.GameRelease.ToCategory(),
                        prefs: null);
                }
            }
        }

        return p with
        {
            Params = p.Params with
            {
                MasterFlagsLookup = loadOrder != null && loadOrder.Count > 0
                    ? new LoadOrder<IModMasterStyledGetter>(
                        p.KnownMasters.And(loadOrder).Distinct(x => x.ModKey))
                    : null,
                LinkCache = linkCache
            }
        };
    }
}