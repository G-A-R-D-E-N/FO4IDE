using Mutagen.Bethesda.Plugins.Meta;
using Noggog;
using System.Buffers.Binary;

namespace Mutagen.Bethesda.Plugins.Binary.Headers;

public readonly struct VariableHeader
{

    public ReadOnlyMemorySlice<byte> HeaderAndContentData { get; }

    public ReadOnlyMemorySlice<byte> Content => HeaderAndContentData.Slice(HeaderConstants.HeaderLength, checked((int)ContentLength));

    public GameConstants Constants { get; }

    public RecordHeaderConstants HeaderConstants { get; }

    public VariableHeader(GameConstants constants, ObjectType objectType, ReadOnlyMemorySlice<byte> span)
    {
        Constants = constants;
        HeaderAndContentData = span;
        HeaderConstants = constants.Constants(objectType);
    }

    public VariableHeader(GameConstants constants, RecordHeaderConstants headerConstants, ReadOnlyMemorySlice<byte> span)
    {
        Constants = constants;
        HeaderAndContentData = span;
        HeaderConstants = headerConstants;
    }

    public byte HeaderLength => HeaderConstants.HeaderLength;

    public int RecordTypeInt => BinaryPrimitives.ReadInt32LittleEndian(HeaderAndContentData.Slice(0, 4));

    public RecordType RecordType => new(RecordTypeInt);

    public uint RecordLength
    {
        get
        {
            switch (HeaderConstants.LengthLength)
            {
                case 1:
                    return HeaderAndContentData[4];
                case 2:
                    return BinaryPrimitives.ReadUInt16LittleEndian(HeaderAndContentData.Slice(4, 2));
                case 4:
                    return BinaryPrimitives.ReadUInt32LittleEndian(HeaderAndContentData.Slice(4, 4));
                default:
                    throw new NotImplementedException();
            }
        }
    }

    public int TypeAndLengthLength => HeaderConstants.TypeAndLengthLength;

    public long TotalLength => HeaderConstants.HeaderIncludedInLength ? RecordLength : (HeaderLength + RecordLength);

    public bool IsGroup => HeaderConstants.ObjectType == ObjectType.Group;

    public long ContentLength => HeaderConstants.HeaderIncludedInLength ? RecordLength - HeaderLength : RecordLength;

    public override string ToString() => $"{RecordType} [0x{ContentLength:X}]";
}

public readonly struct VariablePinHeader
{

    public VariableHeader Header { get; }

    public int Location { get; }

    public VariablePinHeader(GameConstants constants, ObjectType objectType, ReadOnlyMemorySlice<byte> span, int pinLocation)
    {
        Header = new VariableHeader(constants, objectType, span);
        Location = pinLocation;
    }

    public VariablePinHeader(GameConstants constants, RecordHeaderConstants recordHeaderConstants, ReadOnlyMemorySlice<byte> span, int pinLocation)
    {
        Header = new VariableHeader(constants, recordHeaderConstants, span);
        Location = pinLocation;
    }

    public VariablePinHeader(VariableHeader header, int pinLocation)
    {
        Header = header;
        Location = pinLocation;
    }

    public byte HeaderLength => HeaderConstants.HeaderLength;

    public int RecordTypeInt => Header.RecordTypeInt;

    public RecordType RecordType => Header.RecordType;

    public uint RecordLength => Header.RecordLength;

    public ReadOnlyMemorySlice<byte> HeaderAndContentData => Header.HeaderAndContentData;

    public ReadOnlyMemorySlice<byte> Content => Header.Content;

    public GameConstants Constants => Header.Constants;

    public RecordHeaderConstants HeaderConstants => Header.HeaderConstants;

    public int TypeAndLengthLength => HeaderConstants.TypeAndLengthLength;

    public long TotalLength => HeaderConstants.HeaderIncludedInLength ? RecordLength : (HeaderLength + RecordLength);

    public bool IsGroup => HeaderConstants.ObjectType == ObjectType.Group;

    public long ContentLength => HeaderConstants.HeaderIncludedInLength ? RecordLength - HeaderLength : RecordLength;

    public override string ToString() => $"{RecordType} [0x{ContentLength:X}] @ 0x{Location:X}";
}