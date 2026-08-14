using Noggog;
using Noggog.Streams.Binary;

namespace Mutagen.Bethesda.Plugins.Binary.Streams;

public sealed class MutagenMemoryReadStream : LittleEndianBinaryMemoryReadStream, IMutagenReadStream
{

    public long OffsetReference { get; }

    public ParsingMeta MetaData { get; }

    public MutagenMemoryReadStream(
        ReadOnlyMemorySlice<byte> data,
        ParsingMeta metaData,
        long offsetReference = 0)
        : base(data)
    {
        MetaData = metaData;
        OffsetReference = offsetReference;
    }

    public IMutagenReadStream ReadAndReframe(int length)
    {
        return new MutagenMemoryReadStream(
            Data,
            MetaData,
            offsetReference: OffsetReference + Position);
    }
}