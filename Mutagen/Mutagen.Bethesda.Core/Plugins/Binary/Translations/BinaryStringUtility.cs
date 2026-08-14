using Noggog;
using System.Buffers.Binary;
using Mutagen.Bethesda.Strings;

namespace Mutagen.Bethesda.Plugins.Binary.Translations;




public static class BinaryStringUtility
{






    public static string ToZString(ReadOnlySpan<byte> bytes, IMutagenEncoding encoding)
    {
        return encoding.GetString(bytes);
    }






    public static ReadOnlySpan<byte> ProcessNullTermination(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length == 0) return bytes;

        var nullTerm = bytes.IndexOf((byte)0);
        if (nullTerm == -1) return bytes;

        return bytes.Slice(0, nullTerm);
    }







    public static string ProcessWholeToZString(ReadOnlySpan<byte> bytes, IMutagenEncoding encoding)
    {
        bytes = ProcessNullTermination(bytes);
        return ToZString(bytes, encoding);
    }








    public static string ParseUnknownLengthString<TReader>(TReader stream, IMutagenEncoding encoding)
        where TReader : IBinaryReadStream
    {
        var mem = stream.RemainingMemory;
        var index = mem.Span.IndexOf(default(byte));
        if (index == -1)
        {
            throw new ArgumentException();
        }
        var ret = ToZString(mem[0..index], encoding);
        stream.Position += index + 1;
        return ret;
    }








    public static string ParseUnknownLengthString(ReadOnlySpan<byte> bytes, IMutagenEncoding encoding)
    {
        return ToZString(ExtractUnknownLengthString(bytes), encoding);
    }






    public static ReadOnlySpan<byte> ExtractUnknownLengthString(ReadOnlySpan<byte> bytes)
    {
        var index = bytes.IndexOf(default(byte));
        if (index == -1)
        {
            throw new ArgumentException();
        }
        return bytes[..index];
    }









    public static string ParsePrependedString(ReadOnlySpan<byte> span, byte lengthLength, IMutagenEncoding encoding)
    {
        return ProcessWholeToZString(ExtractPrependedString(span, lengthLength), encoding);
    }








    public static ReadOnlySpan<byte> ExtractPrependedString(ReadOnlySpan<byte> span, byte lengthLength)
    {
        switch (lengthLength)
        {
            case 1:
            {
                var length = span[0];
                return span.Slice(1, length);
            }
            case 2:
            {
                var length = BinaryPrimitives.ReadUInt16LittleEndian(span);
                return span.Slice(2, length);
            }
            case 4:
            {
                var length = BinaryPrimitives.ReadUInt32LittleEndian(span);
                return span.Slice(4, checked((int)length));
            }
            case 8:
            {
                var length = BinaryPrimitives.ReadUInt64LittleEndian(span);
                return span.Slice(8, checked((int)length));
            }
            default:
                throw new NotImplementedException();
        }
    }









    public static string ReadPrependedString<TStream>(this TStream stream, byte lengthLength, IMutagenEncoding encoding)
        where TStream : IBinaryReadStream
    {
        switch (lengthLength)
        {
            case 2:
                {
                    var length = stream.ReadUInt16();
                    return ToZString(stream.ReadSpan(length), encoding);
                }
            case 4:
                {
                    var length = checked((int)stream.ReadUInt32());
                    return ToZString(stream.ReadSpan(length), encoding);
                }
            default:
                throw new NotImplementedException();
        }
    }

    public static void Write<TStream>(this TStream stream, ReadOnlySpan<char> str, StringBinaryType binaryType, IMutagenEncoding encoding)
        where TStream : IBinaryWriteStream
    {
        switch (binaryType)
        {
            case StringBinaryType.Plain:
                Write(stream, str, encoding);
                break;
            case StringBinaryType.NullTerminate:
                Write(stream, str, encoding);
                stream.Write((byte)0);
                break;
            case StringBinaryType.NullTerminateIfNotEmpty:
                if (str.Length > 0)
                {
                    Write(stream, str, encoding);
                    stream.Write((byte)0);
                }
                break;
            case StringBinaryType.PrependLengthWithNullIfContent:
            {
                var len = encoding.GetByteCount(str);
                if (str.Length > 0)
                {
                    len += 1;
                }
                stream.Write(len);
                Write(stream, str, encoding, len);
                break;
            }
            case StringBinaryType.PrependLength:
            {
                var len = encoding.GetByteCount(str);
                stream.Write(len);
                Write(stream, str, encoding, len);
                break;
            }
            case StringBinaryType.PrependLengthUShort:
            {
                var len = encoding.GetByteCount(str);
                stream.Write(checked((ushort)len));
                Write(stream, str, encoding, len);
                break;
            }
            case StringBinaryType.PrependLengthUInt8:
            {
                var len = encoding.GetByteCount(str);
                stream.Write(checked((byte)len));
                Write(stream, str, encoding, len);
                break;
            }
            default:
                throw new NotImplementedException();
        }
    }

    public static void Write<TStream>(TStream stream, ReadOnlySpan<char> str, IMutagenEncoding encoding)
        where TStream : IBinaryWriteStream
    {
        Write(stream, str, encoding, encoding.GetByteCount(str));
    }

    public static void Write<TStream>(TStream stream, ReadOnlySpan<char> str, IMutagenEncoding encoding, int byteCount)
        where TStream : IBinaryWriteStream
    {
        Span<byte> bytes = stackalloc byte[byteCount];
        encoding.GetBytes(str, bytes);
        stream.Write(bytes);
    }
}
