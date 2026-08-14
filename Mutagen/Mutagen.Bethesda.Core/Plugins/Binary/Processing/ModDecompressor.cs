using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Utility;
using System.Text;
using Mutagen.Bethesda.Plugins.Analysis;
using Mutagen.Bethesda.Plugins.Binary.Headers;

namespace Mutagen.Bethesda.Plugins.Binary.Processing;




public static class ModDecompressor
{






    public static void Decompress(
        Func<IMutagenReadStream> streamCreator,
        Stream outputStream,
        RecordInterest? interest = null)
    {
        using var inputStream = streamCreator();
        using var inputStreamJumpback = streamCreator();
        using var writer = new BinaryWriter(outputStream, Encoding.Default, leaveOpen: true);

        long runningDiff = 0;
        var fileLocs = RecordLocator.GetLocations(
            inputStream,
            interest: interest,
            additionalCriteria: (_, majorRecord) =>
            {
                return majorRecord.IsCompressed;
            });


        var grupMeta = new Dictionary<long, (uint Length, long Offset)>();
        inputStream.Position = 0;
        while (!inputStream.Complete)
        {

            long noRecordLength;
            if (fileLocs.ListedRecords.TryGetInDirection(
                    inputStream.Position,
                    higher: true,
                    result: out var nextRec))
            {
                var recordLocation = fileLocs.ListedRecords.Keys[nextRec.Key];
                noRecordLength = recordLocation - inputStream.Position;
            }
            else
            {
                noRecordLength = inputStream.Length - inputStream.Position;
            }
            inputStream.WriteTo(outputStream, (int)noRecordLength);


            if (inputStream.Complete) break;
            var majorMeta = inputStream.ReadMajorRecordHeader(readSafe: true);
            var len = majorMeta.ContentLength;
            using var frame = MutagenFrame.ByLength(
                reader: inputStream,
                length: len);


            var decompressed = frame.Decompress();
            var decompressedLen = decompressed.TotalLength;
            var lengthDiff = decompressedLen - len;
            var majorMetaSpan = majorMeta.HeaderData.ToArray();


            var writableMajorMeta = inputStream.MetaData.Constants.MajorRecordHeaderWritable(majorMetaSpan.AsSpan());
            writableMajorMeta.ContentLength = (uint)(len + lengthDiff);
            writer.Write(majorMetaSpan);
            writer.Write(decompressed.ReadRemainingSpan(readSafe: false));


            if (lengthDiff == 0) continue;


            foreach (var grupLoc in fileLocs.GetContainingGroupLocations(nextRec.Value.FormKey))
            {
                if (!grupMeta.TryGetValue(grupLoc, out var loc))
                {
                    loc.Offset = runningDiff;
                    inputStreamJumpback.Position = grupLoc + 4;
                    loc.Length = inputStreamJumpback.ReadUInt32();
                }
                grupMeta[grupLoc] = ((uint)(loc.Length + lengthDiff), loc.Offset);
            }
            runningDiff += lengthDiff;
        }

        foreach (var item in grupMeta)
        {
            var grupLoc = item.Key;
            outputStream.Position = grupLoc + 4 + item.Value.Offset;
            writer.Write(item.Value.Length);
        }
    }
}