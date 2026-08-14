using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Internals;
using Mutagen.Bethesda.Plugins.Meta;
using static Mutagen.Bethesda.Translations.Binary.UtilityTranslation;

namespace Mutagen.Bethesda.Plugins.Binary.Translations;

public readonly struct HeaderExport : IDisposable
{

    public readonly MutagenWriter Writer;

    public readonly long SizePosition;

    public readonly RecordHeaderConstants RecordConstants;

    private HeaderExport(
        MutagenWriter writer,
        long sizePosition,
        RecordHeaderConstants recordConstants)
    {
        Writer = writer;
        RecordConstants = recordConstants;
        SizePosition = sizePosition;
    }

    public static HeaderExport Header(
        MutagenWriter writer,
        RecordType record,
        ObjectType type)
    {
        writer.Write(record.TypeInt);
        var sizePosition = writer.Position;
        writer.Write(Zeros.Slice(0, writer.MetaData.Constants.Constants(type).LengthLength));
        return new HeaderExport(writer, sizePosition, writer.MetaData.Constants.Constants(type));
    }

    public static HeaderExport Record(
        MutagenWriter writer,
        RecordType record)
    {
        return Header(writer, record, ObjectType.Record);
    }

    public static HeaderExport Group(
        MutagenWriter writer,
        RecordType record)
    {
        return Header(writer, record, ObjectType.Group);
    }

    public static HeaderExport Subrecord(
        MutagenWriter writer,
        RecordType record)
    {
        return Header(writer, record, ObjectType.Subrecord);
    }

    public static IDisposable Subrecord(
        MutagenWriter writer,
        RecordType record,
        RecordType? overflowRecord,
        out MutagenWriter writerToUse)
    {
        if (overflowRecord.HasValue)
        {
            var ret = new ExtraLengthHeaderExport(
                writer,
                record,
                overflowRecord.Value);
            writerToUse = ret.PrepWriter;
            return ret;
        }
        else
        {
            writerToUse = writer;
            return Subrecord(writer, record);
        }
    }

    public void Dispose()
    {
        var endPos = Writer.Position;
        Writer.Position = SizePosition;
        var diff = endPos - SizePosition;
        if (RecordConstants.HeaderIncludedInLength)
        {
            diff += Constants.HeaderLength;
        }
        else
        {
            diff -= RecordConstants.LengthAfterType;
        }

        if (diff < 0)
        {
            return;
        }

        try
        {
            switch (RecordConstants.ObjectType)
            {
                case ObjectType.Subrecord:
                    {
                        Writer.Write(checked((ushort)diff));
                    }
                    break;
                case ObjectType.Record:
                case ObjectType.Group:
                    {
                        Writer.Write(checked((uint)diff));
                    }
                    break;
                default:
                    throw new NotImplementedException();
            }
        }
        catch (OverflowException)
        {
            throw new OverflowException(
                $"{RecordConstants.ObjectType} header export resulted in an overflow. Diff: 0x{diff:X}");
        }
        Writer.Position = endPos;
    }
}
