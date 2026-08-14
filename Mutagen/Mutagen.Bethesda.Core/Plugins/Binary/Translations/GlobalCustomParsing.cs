using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Records;

namespace Mutagen.Bethesda.Plugins.Binary.Translations;




internal static class GlobalCustomParsing
{
    public static readonly RecordType GLOB = new("GLOB");
    public static readonly RecordType FNAM = new("FNAM");
    public static readonly RecordType FLTV = new("FLTV");




    public interface IGlobalCommon
    {
        float? RawFloat { get; set; }
    }







    public static char? GetGlobalChar(MajorRecordFrame frame)
    {
        if (!frame.TryFindSubrecord(FNAM, out var fnamMeta)) return null;
        if (fnamMeta.Content.Length != 1)
        {
            throw new ArgumentException($"FNAM had non 1 length: {fnamMeta.Content.Length}");
        }
        return (char)fnamMeta.Content[0];
    }








    public static T Create<T>(
        MutagenFrame frame,
        Func<MutagenFrame, char?, T> getter)
        where T : IMajorRecord, IGlobalCommon
    {
        var initialPos = frame.Position;
        var majorMeta = frame.GetMajorRecord();
        if (majorMeta.RecordType != GLOB)
        {
            throw new ArgumentException();
        }

        T g = getter(frame, GetGlobalChar(majorMeta));

        frame.Reader.Position = initialPos + frame.MetaData.Constants.MajorConstants.TypeAndLengthLength;


        var fltv = majorMeta.FindSubrecord(FLTV);
        g.RawFloat = fltv.AsFloat();


        frame.Reader.Position = initialPos + majorMeta.TotalLength;
        return g;
    }

}
