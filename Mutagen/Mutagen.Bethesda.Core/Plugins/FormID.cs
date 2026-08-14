using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Mutagen.Bethesda.Plugins.Masters;

namespace Mutagen.Bethesda.Plugins;

public readonly struct FormID : IEquatable<FormID>
{

    public static readonly FormID Null = new(0);

    public const uint SmallMasterMarker = 0xFE;
    public const uint MediumMasterMarker = 0xFD;
    internal const uint SmallMasterMarkerShifted = 0xFE000000;
    internal const uint MediumMasterMarkerShifted = 0xFD000000;

    public const uint FullIdMask =   0x00FFFFFF;
    public const uint MediumIdMask = 0x0000FFFF;
    public const uint SmallIdMask =  0x00000FFF;

    public readonly uint Raw;

    public uint FullId => Raw & FullIdMask;

    public uint MediumId => Raw & MediumIdMask;

    public uint LightId => Raw & SmallIdMask;

    public const uint FullMasterIndexMask =   0xFF000000;
    public const uint MediumMasterIndexMask = 0x00FF0000;
    public const uint LightMasterIndexMask =  0x00FFF000;
    public const byte FullMasterIndexShift = 24;
    public const byte MediumMasterIndexShift = 16;
    public const byte LightMasterIndexShift = 12;

    public uint FullMasterIndex => (Raw & FullMasterIndexMask) >> FullMasterIndexShift;

    public uint MediumMasterIndex => (Raw & MediumMasterIndexMask) >> MediumMasterIndexShift;

    public uint LightMasterIndex => (Raw & LightMasterIndexMask) >> LightMasterIndexShift;

    public FormID(uint idWithModIndex)
    {
        Raw = idWithModIndex;
    }

    public static FormID Factory(ReadOnlySpan<char> hexStr)
    {
        if (!TryFactory(hexStr, out var result))
        {
            throw new ArgumentException($"Invalid FormID hex: {hexStr.ToString()}");
        }
        return result;
    }

    public static bool TryFactory(ReadOnlySpan<char> hexStr, [MaybeNullWhen(false)] out FormID id, bool strictLength = true)
    {
        if (hexStr.StartsWith("0x"))
        {
            hexStr = hexStr.Slice(2);
        }

        if (strictLength && hexStr.Length != 8)
        {
            id = default;
            return false;
        }

        if (!uint.TryParse(hexStr, NumberStyles.HexNumber, null, out var intID))
        {
            id = default;
            return false;
        }
        id = new FormID(intID);
        return true;
    }

    public static FormID? TryFactory(ReadOnlySpan<char> hexStr, bool strictLength = true)
    {
        if (TryFactory(hexStr, out var id, strictLength: strictLength))
        {
            return id;
        }
        return default;
    }

    public static FormID Factory(ReadOnlySpan<byte> bytes)
    {
        return Factory(BinaryPrimitives.ReadUInt32LittleEndian(bytes));
    }

    public static FormID Factory(uint idWithModIndex)
    {
        return new FormID(idWithModIndex);
    }

    public static FormID Factory(MasterStyle style, uint masterIndex, uint id)
    {
        byte shift;
        uint mask;
        uint upperValue;

        switch (style)
        {
            case MasterStyle.Full:
                shift = FullMasterIndexShift;
                mask = FullIdMask;
                upperValue = 0;
                break;
            case MasterStyle.Medium:
                shift = MediumMasterIndexShift;
                mask = MediumIdMask;
                upperValue = MediumMasterMarkerShifted;
                break;
            case MasterStyle.Small:
                shift = LightMasterIndexShift;
                mask = SmallIdMask;
                upperValue = SmallMasterMarkerShifted;
                break;
            default:
                throw new NotImplementedException();
        }

        var raw = masterIndex << shift;
        id &= mask;
        raw += id;
        raw += upperValue;
        return new FormID(raw);
    }

    public byte[] ToBytes()
    {
        return BitConverter.GetBytes(Raw);
    }

    public override string ToString()
    {
        return Raw.ToString("X");
    }

    public string IdString(MasterStyle style)
    {
        switch (style)
        {
            case MasterStyle.Full:
                return FullId.ToString("X6");
            case MasterStyle.Small:
                return LightId.ToString("X3");
            case MasterStyle.Medium:
                return MediumId.ToString("X4");
            default:
                throw new ArgumentOutOfRangeException(nameof(style), style, null);
        }
    }

    public uint Id(MasterStyle style)
    {
        switch (style)
        {
            case MasterStyle.Full:
                return FullId;
            case MasterStyle.Small:
                return LightId;
            case MasterStyle.Medium:
                return MediumId;
            default:
                throw new ArgumentOutOfRangeException(nameof(style), style, null);
        }
    }

    public static uint IdMask(MasterStyle style) => style switch
    {
        MasterStyle.Small => SmallIdMask,
        MasterStyle.Medium => MediumIdMask,
        MasterStyle.Full => FullIdMask,
        _ => throw new NotImplementedException()
    };

    public static uint MasterIndexShift(MasterStyle style) => style switch
    {
        MasterStyle.Small => LightMasterIndexShift,
        MasterStyle.Medium => MediumMasterIndexShift,
        MasterStyle.Full => FullMasterIndexShift,
        _ => throw new NotImplementedException()
    };

    public uint MasterIndex(MasterStyle style) => style switch
    {
        MasterStyle.Small => LightMasterIndex,
        MasterStyle.Medium => MediumMasterIndex,
        MasterStyle.Full => FullMasterIndex,
        _ => throw new NotImplementedException()
    };

    public override bool Equals(object? obj)
    {
        if (obj is not FormID formID) return false;
        return Equals(formID);
    }

    public bool Equals(FormID other)
    {
        return Raw == other.Raw;
    }

    public override int GetHashCode()
    {
        return Raw.GetHashCode();
    }

    public static bool operator ==(FormID a, FormID b)
    {
        return a.Raw == b.Raw;
    }

    public static bool operator !=(FormID a, FormID b)
    {
        return !(a == b);
    }
}
