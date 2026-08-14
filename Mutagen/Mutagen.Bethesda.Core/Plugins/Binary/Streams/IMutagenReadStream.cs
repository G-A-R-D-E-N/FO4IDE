using Noggog;

namespace Mutagen.Bethesda.Plugins.Binary.Streams;

public interface IMutagenReadStream : IBinaryReadStream
{

    ParsingMeta MetaData { get; }

    long OffsetReference { get; }

    IMutagenReadStream ReadAndReframe(int length);
}