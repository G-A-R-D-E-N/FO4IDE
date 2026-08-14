using Mutagen.Bethesda.Plugins.Internals;
using Mutagen.Bethesda.Plugins.Meta;
using Noggog;
using System.Buffers.Binary;
using System.Collections;
using System.Diagnostics.CodeAnalysis;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Masters;

namespace Mutagen.Bethesda.Plugins.Binary.Headers;

public readonly struct MajorRecordHeader
{

    public GameConstants Meta { get; }

    public ReadOnlyMemorySlice<byte> HeaderData { get; }

    public MajorRecordHeader(GameConstants meta, ReadOnlyMemorySlice<byte> span)
    {
        Meta = meta;
        HeaderData = span.Slice(0, meta.MajorConstants.HeaderLength);
    }

    public GameRelease Release => Meta.Release;

    public byte HeaderLength => Meta.MajorConstants.HeaderLength;

    public RecordType RecordType => new(BinaryPrimitives.ReadInt32LittleEndian(HeaderData.Slice(0, 4)));

    public uint ContentLength => BinaryPrimitives.ReadUInt32LittleEndian(HeaderData.Slice(4, Meta.MajorConstants.LengthLength));

    public int MajorRecordFlags => BinaryPrimitives.ReadInt32LittleEndian(HeaderData.Slice(8, 4));

    public FormID FormID => FormID.Factory(BinaryPrimitives.ReadUInt32LittleEndian(HeaderData.Slice(12, 4)));

    public int VersionControl => BinaryPrimitives.ReadInt32LittleEndian(HeaderData.Slice(16, 4));

    public long TotalLength => HeaderLength + ContentLength;

    public bool IsCompressed => (MajorRecordFlags & Constants.CompressedFlag) > 0;

    public bool IsDeleted => (MajorRecordFlags & Constants.DeletedFlag) > 0;

    public short? FormVersion
    {
        get
        {
            if (!Meta.MajorConstants.FormVersionLocationOffset.HasValue) return null;
            return BinaryPrimitives.ReadInt16LittleEndian(
                HeaderData.Slice(Meta.MajorConstants.FormVersionLocationOffset.Value));
        }
    }

    public short? VersionControl2
    {
        get
        {
            if (!Meta.MajorConstants.FormVersionLocationOffset.HasValue) return null;
            return BinaryPrimitives.ReadInt16LittleEndian(
                HeaderData.Slice(Meta.MajorConstants.FormVersionLocationOffset.Value + 2));
        }
    }

    public override string ToString() => $"{RecordType} ({FormID}) [0x{ContentLength:X}]";

    public MajorRecordPinHeader Pin(int loc) => new(this, loc);
}

public readonly struct MajorRecordPinHeader
{

    public GameConstants Meta { get; }

    public ReadOnlyMemorySlice<byte> HeaderData { get; }

    public int Location { get; }

    public MajorRecordPinHeader(GameConstants meta, ReadOnlyMemorySlice<byte> span, int pinLocation)
    {
        Meta = meta;
        HeaderData = span.Slice(0, meta.MajorConstants.HeaderLength);
        Location = pinLocation;
    }

    public MajorRecordPinHeader(MajorRecordHeader header, int pinLocation)
    {
        Meta = header.Meta;
        HeaderData = header.HeaderData;
        Location = pinLocation;
    }

    public GameRelease Release => Meta.Release;

    public byte HeaderLength => Meta.MajorConstants.HeaderLength;

    public RecordType RecordType => new(BinaryPrimitives.ReadInt32LittleEndian(HeaderData.Slice(0, 4)));

    public uint ContentLength => BinaryPrimitives.ReadUInt32LittleEndian(HeaderData.Slice(4, Meta.MajorConstants.LengthLength));

    public int MajorRecordFlags => BinaryPrimitives.ReadInt32LittleEndian(HeaderData.Slice(8, 4));

    public FormID FormID => FormID.Factory(BinaryPrimitives.ReadUInt32LittleEndian(HeaderData.Slice(12, 4)));

    public int VersionControl => BinaryPrimitives.ReadInt32LittleEndian(HeaderData.Slice(16, 4));

    public long TotalLength => HeaderLength + ContentLength;

    public bool IsCompressed => (MajorRecordFlags & Constants.CompressedFlag) > 0;

    public short? FormVersion
    {
        get
        {
            if (!Meta.MajorConstants.FormVersionLocationOffset.HasValue) return null;
            return BinaryPrimitives.ReadInt16LittleEndian(
                HeaderData.Slice(Meta.MajorConstants.FormVersionLocationOffset.Value));
        }
    }

    public short? VersionControl2
    {
        get
        {
            if (!Meta.MajorConstants.FormVersionLocationOffset.HasValue) return null;
            return BinaryPrimitives.ReadInt16LittleEndian(
                HeaderData.Slice(Meta.MajorConstants.FormVersionLocationOffset.Value + 2));
        }
    }

    public override string ToString() => $"{RecordType} ({FormID}) [0x{ContentLength:X}] @ 0x{Location:X}";
}

public ref struct MajorRecordHeaderWritable
{

    public GameConstants Meta { get; }

    public Span<byte> HeaderData { get; }

    public MajorRecordHeaderWritable(GameConstants meta, Span<byte> span)
    {
        Meta = meta;
        HeaderData = span.Slice(0, meta.MajorConstants.HeaderLength);
    }

    public GameRelease Release => Meta.Release;

    public byte HeaderLength => Meta.MajorConstants.HeaderLength;

    public RecordType RecordType
    {
        get => new RecordType(BinaryPrimitives.ReadInt32LittleEndian(HeaderData.Slice(0, 4)));
        set => BinaryPrimitives.WriteInt32LittleEndian(HeaderData.Slice(0, 4), value.TypeInt);
    }

    public uint ContentLength
    {
        get => BinaryPrimitives.ReadUInt32LittleEndian(HeaderData.Slice(4, 4));
        set => BinaryPrimitives.WriteUInt32LittleEndian(HeaderData.Slice(4, 4), value);
    }

    public int MajorRecordFlags
    {
        get => BinaryPrimitives.ReadInt32LittleEndian(HeaderData.Slice(8, 4));
        set => BinaryPrimitives.WriteInt32LittleEndian(HeaderData.Slice(8, 4), value);
    }

    public FormID FormID
    {
        get => FormID.Factory(BinaryPrimitives.ReadUInt32LittleEndian(HeaderData.Slice(12, 4)));
        set => BinaryPrimitives.WriteUInt32LittleEndian(HeaderData.Slice(12, 4), value.Raw);
    }

    public int VersionControl
    {
        get => BinaryPrimitives.ReadInt32LittleEndian(HeaderData.Slice(16, 4));
        set => BinaryPrimitives.WriteInt32LittleEndian(HeaderData.Slice(16, 4), value);
    }

    public long TotalLength => HeaderLength + ContentLength;

    [DisallowNull]
    public ushort? FormVersion
    {
        get
        {
            if (!Meta.MajorConstants.FormVersionLocationOffset.HasValue) return null;
            return BinaryPrimitives.ReadUInt16LittleEndian(
                HeaderData.Slice(Meta.MajorConstants.FormVersionLocationOffset.Value));
        }
        set
        {
            if (!Meta.MajorConstants.FormVersionLocationOffset.HasValue)
            {
                throw new ArgumentException("Attempted to set Form Version on a non-applicable game.");
            }
            BinaryPrimitives.WriteUInt16LittleEndian(
                HeaderData.Slice(Meta.MajorConstants.FormVersionLocationOffset.Value, 2),
                value.Value);
        }
    }

    [DisallowNull]
    public short? VersionControl2
    {
        get
        {
            if (!Meta.MajorConstants.FormVersionLocationOffset.HasValue) return null;
            return BinaryPrimitives.ReadInt16LittleEndian(
                HeaderData.Slice(Meta.MajorConstants.FormVersionLocationOffset.Value + 2));
        }
        set
        {
            if (!Meta.MajorConstants.FormVersionLocationOffset.HasValue)
            {
                throw new ArgumentException("Attempted to set Form Version on a non-applicable game.");
            }
            BinaryPrimitives.WriteInt16LittleEndian(
                HeaderData.Slice(Meta.MajorConstants.FormVersionLocationOffset.Value + 2, 2),
                value.Value);
        }
    }

    public bool IsCompressed
    {
        get => (MajorRecordFlags & Constants.CompressedFlag) > 0;
        set
        {
            if (value)
            {
                MajorRecordFlags |= Constants.CompressedFlag;
            }
            else
            {
                MajorRecordFlags &= ~Constants.CompressedFlag;
            }
        }
    }

    public override string ToString() => $"{RecordType} ({FormID}) [0x{ContentLength:X}]";
}

public readonly struct MajorRecordFrame : IEnumerable<SubrecordPinFrame>
{
    public readonly MajorRecordHeader Header;

    public ReadOnlyMemorySlice<byte> HeaderAndContentData { get; }

    public ReadOnlyMemorySlice<byte> Content => HeaderAndContentData.Slice(Header.HeaderLength, checked((int)Header.ContentLength));

    public long TotalLength => HeaderAndContentData.Length;

    public MajorRecordFrame(GameConstants meta, ReadOnlyMemorySlice<byte> span)
    {
        Header = meta.MajorRecordHeader(span);
        HeaderAndContentData = span.Slice(0, checked((int)Header.TotalLength));
    }

    public MajorRecordFrame(MajorRecordHeader header, ReadOnlyMemorySlice<byte> span)
    {
        Header = header;
        HeaderAndContentData = span.Slice(0, checked((int)Header.TotalLength));
    }

    public override string ToString() => Header.ToString();

    public IEnumerator<SubrecordPinFrame> GetEnumerator() => HeaderExt.EnumerateSubrecords(this).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public MajorRecordFrame Decompress(out ReadOnlyMemorySlice<byte> rawDecompressedBytes)
    {
        var resultLen = BinaryPrimitives.ReadUInt32LittleEndian(Content);
        rawDecompressedBytes = Decompression.Decompress(Content.Slice(4), resultLen);
        var resultBytes = new byte[HeaderData.Length + rawDecompressedBytes.Length];
        HeaderData.Span.CopyTo(resultBytes);
        BinaryPrimitives.WriteUInt32LittleEndian(
            resultBytes.AsSpan(4),
            (uint)rawDecompressedBytes.Length);
        rawDecompressedBytes.Span.CopyTo(resultBytes.AsSpan(HeaderData.Length));
        return new MajorRecordFrame(
            Header.Meta,
            resultBytes);
    }

    #region Header Forwarding

    public ReadOnlyMemorySlice<byte> HeaderData => Header.HeaderData;

    public GameConstants Meta => Header.Meta;

    public GameRelease Release => Header.Release;

    public byte HeaderLength => Header.HeaderLength;

    public RecordType RecordType => Header.RecordType;

    public uint ContentLength => (uint)Content.Length;

    public int MajorRecordFlags => Header.MajorRecordFlags;

    public FormID FormID => Header.FormID;

    public int VersionControl => Header.VersionControl;

    public bool IsCompressed => Header.IsCompressed;

    public bool IsDeleted => Header.IsDeleted;

    public short? FormVersion => Header.FormVersion;

    public short? VersionControl2 => Header.VersionControl2;
    #endregion
}

public readonly struct MajorRecordPinFrame
{

    public MajorRecordFrame Frame { get; }

    public int Location { get; }

    public MajorRecordPinFrame(GameConstants meta, ReadOnlyMemorySlice<byte> span, int pinLocation)
    {
        Frame = new MajorRecordFrame(meta, span);
        Location = pinLocation;
    }

    public MajorRecordPinFrame(MajorRecordHeader header, ReadOnlyMemorySlice<byte> span, int pinLocation)
    {
        Frame = new MajorRecordFrame(header, span);
        Location = pinLocation;
    }

    public override string ToString() => $"{Frame} @ 0x{Location:X}";

    #region Header Forwarding

    public MajorRecordHeader Header => Frame.Header;

    public long TotalLength => Header.TotalLength;

    public ReadOnlyMemorySlice<byte> HeaderData => Header.HeaderData;

    public GameConstants Meta => Header.Meta;

    public GameRelease Release => Header.Release;

    public byte HeaderLength => Header.HeaderLength;

    public RecordType RecordType => Header.RecordType;

    public uint ContentLength => Frame.ContentLength;

    public int MajorRecordFlags => Header.MajorRecordFlags;

    public FormID FormID => Header.FormID;

    public int VersionControl => Header.VersionControl;

    public bool IsCompressed => Header.IsCompressed;

    public short? FormVersion => Header.FormVersion;

    public short? VersionControl2 => Header.VersionControl2;
    #endregion

    public static implicit operator MajorRecordHeader(MajorRecordPinFrame pin)
    {
        return pin.Header;
    }

    public static implicit operator MajorRecordFrame(MajorRecordPinFrame pin)
    {
        return pin.Frame;
    }
}