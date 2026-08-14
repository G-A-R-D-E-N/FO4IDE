using System.Buffers.Binary;
using System.Diagnostics;

namespace Mutagen.Bethesda.Plugins;

[DebuggerDisplay("{Type}")]
public readonly struct RecordType : IEquatable<RecordType>, IEquatable<string>
{

    public const byte Length = 4;

    public static readonly RecordType Null = new RecordType("\0\0\0\0");

    public readonly int TypeInt;

    public string Type => GetStringType(TypeInt);

    public string CheckedType => GetCheckedStringType(TypeInt);

    [DebuggerStepThrough]
    public RecordType (int type)
    {
        TypeInt = type;
    }

    [DebuggerStepThrough]
    public RecordType(ReadOnlySpan<char> typeStr)
    {
        if (typeStr.Length != Length)
        {
            throw new ArgumentException($"Type String not expected length: {typeStr.Length} != {Length}.");
        }
        TypeInt = GetTypeInt(typeStr);
    }

    public static bool TryFactory(ReadOnlySpan<char> str, out RecordType recType)
    {
        if (str.Length != Length)
        {
            recType = default;
            return false;
        }
        recType = new RecordType(GetTypeInt(str));
        return true;
    }

    public override bool Equals(object? other)
    {
        if (other is not RecordType rhs) return false;
        return Equals(rhs);
    }

    public bool Equals(RecordType other)
    {
        return TypeInt == other.TypeInt;
    }

    public bool Equals(string? other)
    {
        if (string.IsNullOrWhiteSpace(other)) return false;
        if (other.Length != 4) return false;
        return TypeInt == GetTypeInt(other);
    }

    public static bool operator ==(RecordType r1, RecordType r2)
    {
        return r1.Equals(r2);
    }

    public static bool operator !=(RecordType r1, RecordType r2)
    {
        return !r1.Equals(r2);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(TypeInt);
    }

    public override string ToString()
    {
        return Type;
    }

    [DebuggerStepThrough]
    public static string GetStringType(int typeInt)
    {
        return string.Create(4, typeInt, (chars, state) =>
        {
            chars[0] = (char)(state & 0x000000FF);
            chars[1] = (char)(state >> 8 & 0x000000FF);
            chars[2] = (char)(state >> 16 & 0x000000FF);
            chars[3] = (char)(state >> 24 & 0x000000FF);
        });
    }

    public static string GetCheckedStringType(int typeInt)
    {
        var ret = GetStringType(typeInt);
        for (int i = ret.Length - 1; i >= 0; i--)
        {
            var b = (byte)ret[i];
            if (b > 0x14) continue;
            ret = ret.Remove(i, 1);
            ret = ret.Insert(i, $"_{b:X}_");
        }
        return ret;
    }

    [DebuggerStepThrough]
    public static int GetTypeInt(ReadOnlySpan<char> typeStr)
    {
        if (typeStr.Length != Length)
        {
            throw new ArgumentException($"Type String not expected length: {Length}.");
        }
        Span<byte> b = stackalloc byte[4];
        for (int i = 0; i < Length; i++)
        {
            b[i] = (byte)typeStr[i];
        }
        return BinaryPrimitives.ReadInt32LittleEndian(b);
    }

    public static implicit operator RecordType(ReadOnlySpan<char> str)
    {
        return new RecordType(str);
    }

    public static implicit operator RecordType(string str)
    {
        return new RecordType(str);
    }
}