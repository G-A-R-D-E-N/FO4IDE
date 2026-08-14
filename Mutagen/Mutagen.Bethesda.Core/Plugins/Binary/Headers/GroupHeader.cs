using Mutagen.Bethesda.Plugins.Internals;
using Mutagen.Bethesda.Plugins.Meta;
using Noggog;
using System.Buffers.Binary;
using System.Collections;

namespace Mutagen.Bethesda.Plugins.Binary.Headers;

public readonly struct GroupHeader
{

    public GameConstants Meta { get; }

    public ReadOnlyMemorySlice<byte> HeaderData { get; }

    public GroupHeader(GameConstants meta, ReadOnlyMemorySlice<byte> span)
    {
        Meta = meta;
        HeaderData = span.Slice(0, meta.GroupConstants.HeaderLength);
    }

    public GameRelease Release => Meta.Release;

    public byte HeaderLength => Meta.GroupConstants.HeaderLength;

    public RecordType RecordType => new RecordType(BinaryPrimitives.ReadInt32LittleEndian(HeaderData.Slice(0, 4)));

    private uint RecordLength => BinaryPrimitives.ReadUInt32LittleEndian(HeaderData.Slice(4, 4));

    public ReadOnlyMemorySlice<byte> ContainedRecordTypeData => HeaderData.Slice(8, 4);

    public RecordType ContainedRecordType => new RecordType(BinaryPrimitives.ReadInt32LittleEndian(ContainedRecordTypeData));

    public int GroupType => BinaryPrimitives.ReadInt32LittleEndian(HeaderData.Slice(12, 4));

    public ReadOnlyMemorySlice<byte> LastModifiedData => HeaderData.Slice(16, 4);

    public long TotalLength => RecordLength;

    public bool IsGroup => RecordType == Constants.Group;

    public uint ContentLength => checked((uint)(TotalLength - HeaderLength));

    public int TypeAndLengthLength => Meta.GroupConstants.TypeAndLengthLength;

    public bool IsTopLevel => GroupType == 0;

    public bool CanHaveSubGroups => Meta.GroupConstants.CanHaveSubGroups(GroupType);

    public override string ToString() => $"{RecordType} ({ContainedRecordType}) [0x{ContentLength:X}]";
}

public readonly struct GroupPinHeader
{

    public GameConstants Meta { get; }

    public ReadOnlyMemorySlice<byte> HeaderData { get; }

    public long Location { get; }

    public long EndLocation => Location + HeaderLength;

    public GroupPinHeader(GameConstants meta, ReadOnlyMemorySlice<byte> span, long pinLocation)
    {
        Meta = meta;
        HeaderData = span.Slice(0, meta.GroupConstants.HeaderLength);
        Location = pinLocation;
    }

    public GroupPinHeader(GroupHeader header, long pinLocation)
    {
        Meta = header.Meta;
        HeaderData = header.HeaderData;
        Location = pinLocation;
    }

    public GameRelease Release => Meta.Release;

    public byte HeaderLength => Meta.GroupConstants.HeaderLength;

    public RecordType RecordType => new RecordType(BinaryPrimitives.ReadInt32LittleEndian(HeaderData.Slice(0, 4)));

    private uint RecordLength => BinaryPrimitives.ReadUInt32LittleEndian(HeaderData.Slice(4, 4));

    public ReadOnlyMemorySlice<byte> ContainedRecordTypeData => HeaderData.Slice(8, 4);

    public RecordType ContainedRecordType => new RecordType(BinaryPrimitives.ReadInt32LittleEndian(ContainedRecordTypeData));

    public int GroupType => BinaryPrimitives.ReadInt32LittleEndian(HeaderData.Slice(12, 4));

    public ReadOnlyMemorySlice<byte> LastModifiedData => HeaderData.Slice(16, 4);

    public long TotalLength => RecordLength;

    public bool IsGroup => RecordType == Constants.Group;

    public uint ContentLength => checked((uint)(TotalLength - HeaderLength));

    public int TypeAndLengthLength => Meta.GroupConstants.TypeAndLengthLength;

    public bool IsTopLevel => GroupType == 0;

    public bool CanHaveSubGroups => Meta.GroupConstants.CanHaveSubGroups(GroupType);

    public override string ToString() => $"{RecordType} ({ContainedRecordType}) [0x{ContentLength:X}] @ 0x{Location:X}";

    public static implicit operator GroupHeader(GroupPinHeader frame)
    {
        return new GroupHeader(frame.Meta, span: frame.HeaderData);
    }
}

public readonly struct GroupFrame : IEnumerable<VariablePinHeader>
{

    public GroupHeader Header { get; }

    public ReadOnlyMemorySlice<byte> HeaderAndContentData { get; }

    public ReadOnlyMemorySlice<byte> Content => HeaderAndContentData.Slice(Header.HeaderLength, checked((int)Header.ContentLength));

    public long TotalLength => HeaderLength + Content.Length;

    public GroupFrame(GameConstants meta, ReadOnlyMemorySlice<byte> span)
    {
        Header = meta.GroupHeader(span);
        HeaderAndContentData = span.Slice(0, checked((int)Header.TotalLength));
    }

    public GroupFrame(GroupHeader header, ReadOnlyMemorySlice<byte> span)
    {
        Header = header;
        HeaderAndContentData = span.Slice(0, checked((int)Header.TotalLength));
    }

    public override string ToString() => Header.ToString();

    public IEnumerator<VariablePinHeader> GetEnumerator() => HeaderExt.EnumerateRecords(this).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    #region Header Forwarding
    public GameConstants Meta => Header.Meta;

    public ReadOnlyMemorySlice<byte> HeaderData => Header.HeaderData;

    public GameRelease Release => Header.Release;

    public byte HeaderLength => Header.HeaderLength;

    public RecordType RecordType => Header.RecordType;

    public ReadOnlyMemorySlice<byte> ContainedRecordTypeData => Header.ContainedRecordTypeData;

    public RecordType ContainedRecordType => Header.ContainedRecordType;

    public int GroupType => Header.GroupType;

    public ReadOnlyMemorySlice<byte> LastModifiedData => Header.LastModifiedData;

    public bool IsGroup => Header.IsGroup;

    public uint ContentLength => (uint)Content.Length;

    public int TypeAndLengthLength => Header.TypeAndLengthLength;

    public bool IsTopLevel => Header.IsTopLevel;

    public bool CanHaveSubGroups => Meta.GroupConstants.CanHaveSubGroups(GroupType);
    #endregion
}

public readonly struct GroupPinFrame : IEnumerable<VariablePinHeader>
{

    public GroupFrame Frame { get; }

    public GroupHeader Header => Frame.Header;

    public long Location { get; }

    public long EndLocation => Location + TotalLength;

    public GroupPinFrame(GameConstants meta, ReadOnlyMemorySlice<byte> span, long pinLocation)
    {
        Frame = new GroupFrame(meta, span);
        Location = pinLocation;
    }

    public GroupPinFrame(GroupFrame frame, long pinLocation)
    {
        Frame = frame;
        Location = pinLocation;
    }

    public override string ToString() => $"{Frame.ToString()} => 0x{ContentLength:X} @ 0x{Location:X}";

    public IEnumerator<VariablePinHeader> GetEnumerator() => HeaderExt.EnumerateRecords(Frame).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    #region Header Forwarding

    public ReadOnlyMemorySlice<byte> HeaderAndContentData => Frame.HeaderAndContentData;

    public ReadOnlyMemorySlice<byte> Content => Frame.Content;

    public long TotalLength => Frame.TotalLength;

    public GameConstants Meta => Frame.Header.Meta;

    public ReadOnlyMemorySlice<byte> HeaderData => Frame.Header.HeaderData;

    public GameRelease Release => Frame.Header.Release;

    public byte HeaderLength => Frame.Header.HeaderLength;

    public RecordType RecordType => Frame.Header.RecordType;

    public ReadOnlyMemorySlice<byte> ContainedRecordTypeData => Frame.Header.ContainedRecordTypeData;

    public RecordType ContainedRecordType => Frame.Header.ContainedRecordType;

    public int GroupType => Frame.Header.GroupType;

    public ReadOnlyMemorySlice<byte> LastModifiedData => Frame.Header.LastModifiedData;

    public bool IsGroup => Frame.Header.IsGroup;

    public uint ContentLength => (uint)Content.Length;

    public int TypeAndLengthLength => Frame.Header.TypeAndLengthLength;

    public bool IsTopLevel => Frame.Header.IsTopLevel;

    public bool CanHaveSubGroups => Meta.GroupConstants.CanHaveSubGroups(GroupType);
    #endregion

    public static implicit operator GroupHeader(GroupPinFrame pin)
    {
        return pin.Header;
    }

    public static implicit operator GroupFrame(GroupPinFrame pin)
    {
        return pin.Frame;
    }

    public static implicit operator GroupPinHeader(GroupPinFrame pin)
    {
        return new GroupPinHeader(pin.Header, pin.Location);
    }
}
