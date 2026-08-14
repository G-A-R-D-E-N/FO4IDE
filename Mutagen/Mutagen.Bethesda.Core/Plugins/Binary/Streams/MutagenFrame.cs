using Noggog;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace Mutagen.Bethesda.Plugins.Binary.Streams;






public readonly struct MutagenFrame : IMutagenReadStream
{



    public readonly IMutagenReadStream Reader;




    public readonly long InitialPosition;




    public readonly long FinalLocation;





    public bool Complete => Position >= FinalLocation;


    public long Position
    {
        get => Reader.Position;
        set => Reader.Position = value;
    }


    public long PositionWithOffset => Position + Reader.OffsetReference;


    public long FinalWithOffset => FinalLocation + Reader.OffsetReference;


    public long TotalLength => FinalLocation - InitialPosition;


    public long Remaining => FinalLocation - Position;


    public long Length => Reader.Length;


    public long OffsetReference => Reader.OffsetReference;


    public ReadOnlySpan<byte> RemainingSpan => Reader.RemainingSpan;


    public ReadOnlyMemorySlice<byte> RemainingMemory => Reader.RemainingMemory;


    public ParsingMeta MetaData => Reader.MetaData;

    public bool IsLittleEndian => Reader.IsLittleEndian;

    public bool IsPersistantBacking => Reader.IsPersistantBacking;

    public Stream BaseStream => Reader.BaseStream;




    [DebuggerStepThrough]
    public MutagenFrame(IMutagenReadStream reader)
    {
        Reader = reader;
        InitialPosition = reader.Position;
        FinalLocation = reader.Length;
    }

    [DebuggerStepThrough]
    private MutagenFrame(
        IMutagenReadStream reader,
        long finalPosition)
    {
        Reader = reader;
        InitialPosition = reader.Position;
        FinalLocation = finalPosition;
    }


    public bool TryCheckUpcomingRead(long length)
    {
        return Position + length <= FinalLocation;
    }


    public void CheckUpcomingRead(long length)
    {
        if (!TryCheckUpcomingRead(length, out var ex))
        {
            throw ex;
        }
    }


    public bool TryCheckUpcomingRead(long length, [MaybeNullWhen(true)]out Exception ex)
    {
        if (!TryCheckUpcomingRead(length))
        {
            if (Complete)
            {
                ex = new ArgumentException($"Frame was complete, so did not have any remaining bytes to parse. At {PositionWithOffset}. Desired {length} more bytes. {Remaining} past the final position {FinalWithOffset}.");
                return false;
            }
            else
            {
                ex = new ArgumentException($"Frame did not have enough remaining bytes to parse. At {PositionWithOffset}. Desired {length} more bytes.  Only {Remaining} left before final position {FinalWithOffset}.");
                return false;
            }
        }
        ex = default!;
        return true;
    }






    public bool ContainsPosition(long loc)
    {
        return Position <= loc && FinalLocation >= loc;
    }


    public void SetPosition(long pos)
    {
        Position = pos;
    }


    public void Dispose()
    {
    }


    public void SetToFinalPosition()
    {
        Reader.Position = FinalLocation;
    }


    public byte[] ReadRemainingBytes()
    {
        return Reader.ReadBytes(checked((int)Remaining));
    }

    public void SkipRemainingBytes()
    {
        Position += Remaining;
    }


    public ReadOnlySpan<byte> ReadRemainingSpan(bool readSafe)
    {
        return Reader.ReadSpan(checked((int)Remaining), readSafe: readSafe);
    }


    public ReadOnlyMemorySlice<byte> ReadRemainingMemory(bool readSafe)
    {
        return Reader.ReadMemory(checked((int)Remaining), readSafe: readSafe);
    }


    public override string ToString()
    {
        return $"0x{PositionWithOffset.ToString("X")} - 0x{(FinalWithOffset - 1).ToString("X")} (0x{Remaining.ToString("X")})";
    }







    [DebuggerStepThrough]
    public static MutagenFrame ByFinalPosition<T>(
        T reader,
        long finalPosition)
        where T : IMutagenReadStream
    {
        return new MutagenFrame(
            reader: reader,
            finalPosition: finalPosition);
    }







    [DebuggerStepThrough]
    public static MutagenFrame ByLength<T>(
        T reader,
        long length)
        where T : IMutagenReadStream
    {
        return new MutagenFrame(
            reader: reader,
            finalPosition: reader.Position + length);
    }






    [DebuggerStepThrough]
    public MutagenFrame SpawnWithFinalPosition(long finalPosition)
    {
        return new MutagenFrame(
            Reader,
            finalPosition);
    }








    public MutagenFrame SpawnWithLength(long length, bool checkFraming = true)
    {
        if (checkFraming
            && Remaining < length)
        {
            throw new ArgumentException($"Frame did not have enough remaining to allocate for desired length at {PositionWithOffset}. Desired {length} more bytes, but only had {Remaining}.");
        }
        return new MutagenFrame(
            Reader,
            Reader.Position + length);
    }

    public MutagenFrame SpawnAll()
    {
        return new MutagenFrame(Reader, Reader.Length);
    }






    public MutagenFrame Decompress()
    {
        var resultLen = Reader.ReadUInt32();
        var bytes = Reader.ReadBytes((int)Remaining);
        var res = Decompression.Decompress(bytes, resultLen);
        return new MutagenFrame(
            new MutagenMemoryReadStream(res, MetaData));
    }






    public MutagenFrame ReadAndReframe(int length)
    {
        var offset = PositionWithOffset;
        return new MutagenFrame(
            new MutagenMemoryReadStream(
                ReadMemory(length, readSafe: true),
                MetaData,
                offsetReference: offset));
    }


    IMutagenReadStream IMutagenReadStream.ReadAndReframe(int length) => ReadAndReframe(length);


    public int Read(byte[] buffer, int offset, int amount)
    {
        return Reader.Read(buffer, offset, amount);
    }


    public int Read(byte[] buffer)
    {
        return Reader.Read(buffer);
    }


    public byte[] ReadBytes(int amount)
    {
        return Reader.ReadBytes(amount);
    }


    public bool ReadBoolean()
    {
        return Reader.ReadBoolean();
    }


    public byte ReadUInt8()
    {
        return Reader.ReadUInt8();
    }


    public ushort ReadUInt16()
    {
        return Reader.ReadUInt16();
    }


    public uint ReadUInt32()
    {
        return Reader.ReadUInt32();
    }


    public ulong ReadUInt64()
    {
        return Reader.ReadUInt64();
    }


    public sbyte ReadInt8()
    {
        return Reader.ReadInt8();
    }


    public short ReadInt16()
    {
        return Reader.ReadInt16();
    }


    public int ReadInt32()
    {
        return Reader.ReadInt32();
    }


    public long ReadInt64()
    {
        return Reader.ReadInt64();
    }


    public float ReadFloat()
    {
        return Reader.ReadFloat();
    }


    public double ReadDouble()
    {
        return Reader.ReadDouble();
    }

    public char ReadChar()
    {
        return (char)Reader.ReadUInt8();
    }


    public string ReadStringUTF8(int amount)
    {
        return Reader.ReadStringUTF8(amount);
    }


    public void WriteTo(Stream stream, int amount)
    {
        Reader.WriteTo(stream, amount);
    }


    public int Get(byte[] buffer, int offset, int amount)
    {
        return Reader.Get(buffer, offset, amount);
    }


    public byte[] GetBytes(int amount)
    {
        return Reader.GetBytes(amount);
    }


    public int Get(byte[] buffer, int offset)
    {
        return Reader.Get(buffer, offset);
    }


    public bool GetBoolean(int offset)
    {
        return Reader.GetBoolean(offset);
    }


    public byte GetUInt8(int offset)
    {
        return Reader.GetUInt8(offset);
    }


    public ushort GetUInt16(int offset)
    {
        return Reader.GetUInt16(offset);
    }


    public uint GetUInt32(int offset)
    {
        return Reader.GetUInt32(offset);
    }


    public ulong GetUInt64(int offset)
    {
        return Reader.GetUInt64(offset);
    }


    public sbyte GetInt8(int offset)
    {
        return Reader.GetInt8(offset);
    }


    public short GetInt16(int offset)
    {
        return Reader.GetInt16(offset);
    }


    public int GetInt32(int offset)
    {
        return Reader.GetInt32(offset);
    }


    public long GetInt64(int offset)
    {
        return Reader.GetInt64(offset);
    }


    public float GetFloat(int offset)
    {
        return Reader.GetFloat(offset);
    }


    public double GetDouble(int offset)
    {
        return Reader.GetDouble(offset);
    }


    public string GetStringUTF8(int amount, int offset)
    {
        return Reader.GetStringUTF8(amount, offset);
    }


    public bool GetBoolean()
    {
        return Reader.GetBoolean();
    }


    public byte GetUInt8()
    {
        return Reader.GetUInt8();
    }


    public ushort GetUInt16()
    {
        return Reader.GetUInt16();
    }


    public uint GetUInt32()
    {
        return Reader.GetUInt32();
    }


    public ulong GetUInt64()
    {
        return Reader.GetUInt64();
    }


    public sbyte GetInt8()
    {
        return Reader.GetInt8();
    }


    public short GetInt16()
    {
        return Reader.GetInt16();
    }


    public int GetInt32()
    {
        return Reader.GetInt32();
    }


    public long GetInt64()
    {
        return Reader.GetInt64();
    }


    public float GetFloat()
    {
        return Reader.GetFloat();
    }


    public double GetDouble()
    {
        return Reader.GetDouble();
    }

    public char GetChar()
    {
        return (char)Reader.GetUInt8();
    }


    public string GetStringUTF8(int amount)
    {
        return Reader.GetStringUTF8(amount);
    }


    public ReadOnlySpan<byte> ReadSpan(int amount, bool readSafe = true)
    {
        return Reader.ReadSpan(amount, readSafe);
    }


    public ReadOnlySpan<byte> ReadSpan(int amount, int offset, bool readSafe = true)
    {
        return Reader.ReadSpan(amount, offset, readSafe);
    }


    public ReadOnlySpan<byte> GetSpan(int amount, bool readSafe = true)
    {
        return Reader.GetSpan(amount, readSafe);
    }


    public ReadOnlySpan<byte> GetSpan(int amount, int offset, bool readSafe = true)
    {
        return Reader.GetSpan(amount, offset, readSafe);
    }


    public ReadOnlyMemorySlice<byte> ReadMemory(int amount, bool readSafe = true)
    {
        return Reader.ReadMemory(amount, readSafe);
    }


    public ReadOnlyMemorySlice<byte> ReadMemory(int amount, int offset, bool readSafe = true)
    {
        return Reader.ReadMemory(amount, offset, readSafe);
    }


    public ReadOnlyMemorySlice<byte> GetMemory(int amount, bool readSafe = true)
    {
        return Reader.GetMemory(amount, readSafe);
    }


    public ReadOnlyMemorySlice<byte> GetMemory(int amount, int offset, bool readSafe = true)
    {
        return Reader.GetMemory(amount, offset, readSafe);
    }
}