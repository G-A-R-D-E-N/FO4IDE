using Noggog;
using System.IO.Abstractions;
using Mutagen.Bethesda.Plugins.Masters;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Plugins.Records;

namespace Mutagen.Bethesda.Plugins.Binary.Streams;




public sealed class MutagenBinaryReadStream : BinaryReadStream, IMutagenReadStream
{
    private readonly FilePath _path;


    public long OffsetReference { get; }


    public ParsingMeta MetaData { get; }








    public MutagenBinaryReadStream(
        FilePath path,
        ParsingMeta metaData,
        int bufferSize = 4096,
        long offsetReference = 0)
        : base(metaData.FileSystem.File.OpenRead(path.Path), bufferSize)
    {
        _path = path;
        MetaData = metaData;
        OffsetReference = offsetReference;
    }










    public MutagenBinaryReadStream(
        ModPath path,
        GameRelease release,
        IReadOnlyCache<IModMasterStyledGetter, ModKey>? masterFlagLookup,
        int bufferSize = 4096,
        long offsetReference = 0,
        IFileSystem? fileSystem = null)
        : base(fileSystem.GetOrDefault().File.OpenRead(path), bufferSize)
    {
        MetaData = new ParsingMeta(
            release,
            path.ModKey,
            SeparatedMasterPackage.Factory(release, path, masterFlagLookup, fileSystem));
        OffsetReference = offsetReference;
    }









    public MutagenBinaryReadStream(
        Stream stream,
        ParsingMeta metaData,
        int bufferSize = 4096,
        bool dispose = true,
        long offsetReference = 0)
        : base(stream, bufferSize, dispose)
    {
        MetaData = metaData;
        OffsetReference = offsetReference;
    }









    public IMutagenReadStream ReadAndReframe(int length)
    {
        var offset = OffsetReference + Position;
        return new MutagenMemoryReadStream(
            ReadMemory(length, readSafe: true),
            MetaData,
            offsetReference: offset);
    }

    public override string ToString()
    {
        return $"{_path}{_stream.Position}-{_stream.Length} ({_stream.Remaining()})";
    }
}