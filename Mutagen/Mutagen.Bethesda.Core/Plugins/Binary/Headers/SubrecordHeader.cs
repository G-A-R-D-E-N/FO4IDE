using Mutagen.Bethesda.Plugins.Meta;
using Noggog;
using System.Buffers.Binary;
using Mutagen.Bethesda.Plugins.Records.Internals;

namespace Mutagen.Bethesda.Plugins.Binary.Headers;

public readonly struct SubrecordHeader
{

    public GameConstants Meta { get; }

    public ReadOnlyMemorySlice<byte> HeaderData { get; }

    public SubrecordHeader(GameConstants meta, ReadOnlyMemorySlice<byte> span)
    {
        Meta = meta;
        HeaderData = span.Slice(0, meta.SubConstants.HeaderLength);
    }

    public GameRelease Release => Meta.Release;

    public byte HeaderLength => Meta.SubConstants.HeaderLength;

    public RecordType RecordType => new RecordType(RecordTypeInt);

    public int RecordTypeInt => BinaryPrimitives.ReadInt32LittleEndian(HeaderData.Slice(0, 4));

    public ushort ContentLength => BinaryPrimitives.ReadUInt16LittleEndian(HeaderData.Slice(4, 2));

    public int TotalLength => HeaderLength + ContentLength;

    public override string ToString() => $"{RecordType.ToString()} [0x{ContentLength:X}]";

    public SubrecordPinHeader Pin(int location) => new SubrecordPinHeader(this, location);
}

public readonly struct SubrecordPinHeader
{

    public GameConstants Meta { get; }

    public ReadOnlyMemorySlice<byte> HeaderData { get; }

    public int Location { get; }

    public int EndLocation => Location + HeaderLength;

    public SubrecordPinHeader(GameConstants meta, ReadOnlyMemorySlice<byte> span, int pinLocation)
    {
        Meta = meta;
        HeaderData = span.Slice(0, meta.SubConstants.HeaderLength);
        Location = pinLocation;
    }

    public SubrecordPinHeader(SubrecordHeader header, int pinLocation)
    {
        Meta = header.Meta;
        HeaderData = header.HeaderData;
        Location = pinLocation;
    }

    public GameRelease Release => Meta.Release;

    public byte HeaderLength => Meta.SubConstants.HeaderLength;

    public RecordType RecordType => new RecordType(RecordTypeInt);

    public int RecordTypeInt => BinaryPrimitives.ReadInt32LittleEndian(HeaderData.Slice(0, 4));

    public ushort ContentLength => BinaryPrimitives.ReadUInt16LittleEndian(HeaderData.Slice(4, 2));

    public int TotalLength => HeaderLength + ContentLength;

    public override string ToString() => $"{RecordType} [0x{ContentLength:X}] @ 0x{Location:X}";

    public static implicit operator SubrecordHeader(SubrecordPinHeader frame)
    {
        return new SubrecordHeader(frame.Meta, span: frame.HeaderData);
    }
}

public readonly struct SubrecordFrame
{

    public SubrecordHeader Header { get; }

    public ReadOnlyMemorySlice<byte> HeaderAndContentData { get; }

    public int TotalLength => HeaderAndContentData.Length;

    public ReadOnlyMemorySlice<byte> Content => HeaderAndContentData.Slice(Header.HeaderLength);

    public SubrecordFrame(GameConstants meta, ReadOnlyMemorySlice<byte> span)
    {
        Header = meta.SubrecordHeader(span);
        HeaderAndContentData = span.Slice(0, Header.TotalLength);
    }

    private SubrecordFrame(SubrecordHeader header, ReadOnlyMemorySlice<byte> span)
    {
        Header = header;
        HeaderAndContentData = span;
    }

    public static SubrecordFrame Factory(SubrecordHeader header, ReadOnlyMemorySlice<byte> span)
    {
        return new SubrecordFrame(header, span.Slice(0, header.TotalLength));
    }

    public static SubrecordFrame FactoryNoTrim(SubrecordHeader header, ReadOnlyMemorySlice<byte> span)
    {
        return new SubrecordFrame(header, span);
    }

    public override string ToString() => $"{RecordType} [0x{ContentLength:X}]";

    #region Header Forwarding

    public GameConstants Meta => Header.Meta;

    public ReadOnlyMemorySlice<byte> HeaderData => Header.HeaderData;

    public GameRelease Release => Header.Release;

    public byte HeaderLength => Header.HeaderLength;

    public RecordType RecordType => Header.RecordType;

    public int RecordTypeInt => Header.RecordTypeInt;

    public int ContentLength => Content.Length;
    #endregion

    public static implicit operator SubrecordHeader(SubrecordFrame frame)
    {
        return frame.Header;
    }

    public SubrecordPinFrame Pin(int loc)
    {
        return new SubrecordPinFrame(this, loc);
    }
}

public readonly struct SubrecordPinFrame
{

    public SubrecordFrame Frame { get; }

    public int Location { get; }

    public int ContentLocation => Location + Meta.SubConstants.HeaderLength;

    public int EndLocation => Location + TotalLength;

    public int? LengthOverrideRecordLocation { get; }

    public SubrecordPinFrame(GameConstants meta, ReadOnlyMemorySlice<byte> span, int pinLocation)
    {
        Frame = new SubrecordFrame(meta, span);
        Location = pinLocation;
    }

    public SubrecordPinFrame(SubrecordFrame frame, int pinLocation)
    {
        Frame = frame;
        Location = pinLocation;
    }

    private SubrecordPinFrame(SubrecordFrame frame, int pinLocation, int? lengthOverrideRecordLocation)
    {
        Frame = frame;
        Location = pinLocation;
        LengthOverrideRecordLocation = lengthOverrideRecordLocation;
    }

    public static SubrecordPinFrame Factory(SubrecordHeader header, ReadOnlyMemorySlice<byte> span, int pinLocation)
    {
        return new SubrecordPinFrame(
            SubrecordFrame.Factory(header, span),
            pinLocation);
    }

    public static SubrecordPinFrame FactoryNoTrim(SubrecordHeader header, ReadOnlyMemorySlice<byte> span, int pinLocation)
    {
        return new SubrecordPinFrame(
            SubrecordFrame.FactoryNoTrim(header, span),
            pinLocation);
    }

    public static SubrecordPinFrame FactoryWithOverrideLength(SubrecordHeader header, ReadOnlyMemorySlice<byte> span,
        int pinLocation, int overrideSubrecLocation)
    {
        return new SubrecordPinFrame(
            SubrecordFrame.FactoryNoTrim(header, span),
            pinLocation,
            overrideSubrecLocation);
    }

    public override string ToString() => $"{Frame.ToString()} => 0x{ContentLength:X} @ 0x{Location:X}";

    #region Forwarding

    public SubrecordHeader Header => Frame.Header;

    public ReadOnlyMemorySlice<byte> HeaderAndContentData => Frame.HeaderAndContentData;

    public int TotalLength => Frame.TotalLength;

    public ReadOnlyMemorySlice<byte> Content => Frame.Content;

    public GameConstants Meta => Frame.Meta;

    public ReadOnlyMemorySlice<byte> HeaderData => Frame.HeaderData;

    public GameRelease Release => Frame.Release;

    public byte HeaderLength => Frame.HeaderLength;

    public RecordType RecordType => Frame.RecordType;

    public int RecordTypeInt => Frame.RecordTypeInt;

    public int ContentLength => Frame.ContentLength;
    #endregion

    public static implicit operator SubrecordHeader(SubrecordPinFrame pin)
    {
        return pin.Header;
    }

    public static implicit operator SubrecordFrame(SubrecordPinFrame pin)
    {
        return pin.Frame;
    }

    public static implicit operator SubrecordPinHeader(SubrecordPinFrame pin)
    {
        return new SubrecordPinHeader(pin.Header, pin.Location);
    }

    public SubrecordPinFrame Shift(int offset)
    {
        return new SubrecordPinFrame(
            this.Frame,
            Location + offset,
            LengthOverrideRecordLocation + offset);
    }

    public SubrecordPinFrame WithoutOverflow()
    {
        return new SubrecordPinFrame(
            this.Frame,
            Location,
            lengthOverrideRecordLocation: null);
    }
}