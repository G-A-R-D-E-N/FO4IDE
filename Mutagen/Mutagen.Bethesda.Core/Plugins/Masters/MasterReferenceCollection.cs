using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Records;
using Noggog;
using System.IO.Abstractions;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Binary.Overlay;
using Mutagen.Bethesda.Plugins.Exceptions;
using Mutagen.Bethesda.Plugins.Internals;

namespace Mutagen.Bethesda.Plugins.Masters;





public interface IReadOnlyMasterReferenceCollection
{



    IReadOnlyList<IMasterReferenceGetter> Masters { get; }




    ModKey CurrentMod { get; }




    bool TryGetIndex(ModKey modKey, out uint index);
}

public interface IMasterReferenceCollection : IReadOnlyMasterReferenceCollection
{




    void SetTo(IEnumerable<IMasterReferenceGetter> masters);
}





public sealed class MasterReferenceCollection : IMasterReferenceCollection
{
    private readonly Dictionary<ModKey, uint> _masterIndices = new();




    public static IReadOnlyMasterReferenceCollection Empty { get; } = new MasterReferenceCollection(ModKey.Null);


    public IReadOnlyList<IMasterReferenceGetter> Masters { get; private set; } = [];


    public ModKey CurrentMod { get; }





    public MasterReferenceCollection(ModKey modKey)
    {
        CurrentMod = modKey;
        SetTo([]);
    }






    public MasterReferenceCollection(ModKey modKey, IEnumerable<IMasterReferenceGetter> masters)
    {
        CurrentMod = modKey;
        SetTo(masters);
    }


    public void SetTo(IEnumerable<IMasterReferenceGetter> masters)
    {



        Masters = masters.ToList();
        _masterIndices.Clear();

        uint index = 0;
        foreach (var master in Masters)
        {
            var modKey = master.Master;
            if (index >= Constants.PluginMasterLimit)
            {
                throw new TooManyMastersException(CurrentMod, Masters.Select(x => x.Master).ToArray());
            }
            if (modKey == CurrentMod)
            {
                throw new SelfReferenceException(CurrentMod);
            }

            if (!_masterIndices.ContainsKey(modKey))
            {
                _masterIndices[modKey] = index;
            }
            index++;
        }


        _masterIndices[CurrentMod] = index;
    }


    public bool TryGetIndex(ModKey modKey, out uint index)
    {
        return _masterIndices.TryGetValue(modKey, out index);
    }





    internal static MasterReferenceCollection CreateUnsafe(
        ModKey modKey,
        IEnumerable<IMasterReferenceGetter> masters)
    {
        var result = new MasterReferenceCollection(modKey);
        result.Masters = masters.ToList();
        result._masterIndices.Clear();

        uint index = 0;
        foreach (var master in result.Masters)
        {
            if (!result._masterIndices.ContainsKey(master.Master))
            {
                result._masterIndices[master.Master] = index;
            }
            index++;
        }


        result._masterIndices[modKey] = index;
        return result;
    }

    public static MasterReferenceCollection FromPath(ModPath path, GameRelease release, IFileSystem? fileSystem = null)
    {
        var header = ModHeaderFrame.FromPath(path: path, release: release,
            fileSystem: fileSystem);
        return FromModHeader(path.ModKey, header);
    }

    public static MasterReferenceCollection FromStream(Stream stream, ModKey modKey, GameRelease release, bool disposeStream = true)
    {
        using var interf = new MutagenInterfaceReadStream(
            new BinaryReadStream(stream, dispose: disposeStream),
            new ParsingMeta(
                release,
                modKey,
                masterReferences: null!));
        return FromStream(interf);
    }

    public static MasterReferenceCollection FromStream<TStream>(TStream stream)
        where TStream : IMutagenReadStream
    {
        var header = stream.ReadModHeaderFrame(readSafe: true);
        return FromModHeader(stream.MetaData.ModKey, header);
    }

    public static MasterReferenceCollection FromModHeader(
        ModKey modKey,
        ModHeaderFrame header)
    {
        return new MasterReferenceCollection(
            modKey,
            header.Masters(modKey));
    }
}