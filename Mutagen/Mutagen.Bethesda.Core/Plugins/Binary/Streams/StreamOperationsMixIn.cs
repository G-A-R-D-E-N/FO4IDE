using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Internals;

namespace Mutagen.Bethesda;

public static class StreamOperationsMixIn
{

    public static bool TryScanToRecord<T>(this T stream, RecordType type, out SubrecordFrame foundRecord, IReadOnlyRecordCollection expectedTypes)
        where T : IMutagenReadStream
    {
        var pos = stream.Position;
        while (!stream.Complete)
        {
            var subRec = stream.ReadSubrecord();
            var recType = subRec.RecordType;
            if (!expectedTypes.Contains(recType))
            {
                stream.Position = pos;
                foundRecord = default;
                return false;
            }
            if (type == recType)
            {
                foundRecord = subRec;
                return true;
            }
        }

        stream.Position = pos;
        foundRecord = default;
        return false;
    }
}
