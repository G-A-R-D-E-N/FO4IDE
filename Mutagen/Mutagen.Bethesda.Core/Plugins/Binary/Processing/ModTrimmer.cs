using System.Text;
using Mutagen.Bethesda.Plugins.Analysis;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Utility;

namespace Mutagen.Bethesda.Plugins.Binary.Processing;

public static class ModTrimmer
{

    public static void TrimGroups(
        Func<IMutagenReadStream> streamCreator,
        Stream outputStream,
        RecordInterest interest)
    {
        using var inputStream = streamCreator();
        if (inputStream.Complete) return;

        using var writer = new BinaryWriter(outputStream, Encoding.Default, leaveOpen: true);

        var modHeader = inputStream.ReadModHeaderFrame();
        writer.Write(modHeader.HeaderAndContentData);

        while (!inputStream.Complete)
        {
            var groupMeta = inputStream.GetGroupHeader(readSafe: true);
            if (interest.IsInterested(groupMeta.ContainedRecordType))
            {
                inputStream.WriteTo(outputStream, checked((int)groupMeta.TotalLength));
            }
            else
            {
                inputStream.Position += groupMeta.TotalLength;
            }
        }
    }
}