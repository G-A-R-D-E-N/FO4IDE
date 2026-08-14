using Noggog;

namespace Mutagen.Bethesda.Plugins.Binary.Streams;

public sealed class MutagenInterfaceReadStream : IMutagenReadStream
{
    private readonly IBinaryReadStream _readStream;

    public long OffsetReference { get; }

    public ParsingMeta MetaData { get; }

    public bool IsLittleEndian => _readStream.IsLittleEndian;

    public MutagenInterfaceReadStream(
        IBinaryReadStream stream,
        ParsingMeta metaData,
        long offsetReference = 0)
    {
        _readStream = stream;
        MetaData = metaData;
        OffsetReference = offsetReference;
    }

    public long Position
    {
        get => _readStream.Position;
        set => _readStream.Position = value;
    }

    public long Length => _readStream.Length;

    public long Remaining => _readStream.Remaining;

    public bool Complete => _readStream.Complete;

    public ReadOnlySpan<byte> RemainingSpan => _readStream.RemainingSpan;

    public ReadOnlyMemorySlice<byte> RemainingMemory => _readStream.RemainingMemory;

    public bool IsPersistantBacking => _readStream.IsPersistantBacking;

    public Stream BaseStream => _readStream.BaseStream;

    public void Dispose() => _readStream.Dispose();

    public int Get(byte[] buffer, int targetOffset, int amount) => _readStream.Get(buffer, targetOffset, amount);

    public int Get(byte[] buffer, int targetOffset) => _readStream.Get(buffer, targetOffset);

    public bool GetBoolean() => _readStream.GetBoolean();

    public bool GetBoolean(int offset) => _readStream.GetBoolean(offset);

    public byte[] GetBytes(int amount) => _readStream.GetBytes(amount);

    public double GetDouble() => _readStream.GetDouble();

    public double GetDouble(int offset) => _readStream.GetDouble(offset);

    public float GetFloat() => _readStream.GetFloat();

    public float GetFloat(int offset) => _readStream.GetFloat(offset);

    public short GetInt16() => _readStream.GetInt16();

    public short GetInt16(int offset) => _readStream.GetInt16(offset);

    public int GetInt32() => _readStream.GetInt32();

    public int GetInt32(int offset) => _readStream.GetInt32(offset);

    public long GetInt64() => _readStream.GetInt64();

    public long GetInt64(int offset) => _readStream.GetInt64(offset);

    public sbyte GetInt8() => _readStream.GetInt8();

    public sbyte GetInt8(int offset) => _readStream.GetInt8(offset);

    public ReadOnlyMemorySlice<byte> GetMemory(int amount, bool readSafe = true) => _readStream.GetMemory(amount, readSafe);

    public ReadOnlyMemorySlice<byte> GetMemory(int amount, int offset, bool readSafe = true) => _readStream.GetMemory(amount, offset, readSafe);

    public ReadOnlySpan<byte> GetSpan(int amount, bool readSafe = true) => _readStream.GetSpan(amount, readSafe);

    public ReadOnlySpan<byte> GetSpan(int amount, int offset, bool readSafe = true) => _readStream.GetSpan(amount, offset, readSafe);

    public string GetStringUTF8(int amount) => _readStream.GetStringUTF8(amount);

    public string GetStringUTF8(int amount, int offset) => _readStream.GetStringUTF8(amount, offset);

    public ushort GetUInt16() => _readStream.GetUInt16();

    public ushort GetUInt16(int offset) => _readStream.GetUInt16(offset);

    public uint GetUInt32() => _readStream.GetUInt32();

    public uint GetUInt32(int offset) => _readStream.GetUInt32(offset);

    public ulong GetUInt64() => _readStream.GetUInt64();

    public ulong GetUInt64(int offset) => _readStream.GetUInt64(offset);

    public byte GetUInt8() => _readStream.GetUInt8();

    public byte GetUInt8(int offset) => _readStream.GetUInt8(offset);

    public int Read(byte[] buffer, int offset, int amount) => _readStream.Read(buffer, offset, amount);

    public int Read(byte[] buffer) => _readStream.Read(buffer);

    public IMutagenReadStream ReadAndReframe(int length)
    {
        var offset = OffsetReference + Position;
        return new MutagenMemoryReadStream(
            ReadMemory(length, readSafe: true),
            MetaData,
            offsetReference: offset);
    }

    public bool ReadBoolean() => _readStream.ReadBoolean();

    public byte[] ReadBytes(int amount) => _readStream.ReadBytes(amount);

    public double ReadDouble() => _readStream.ReadDouble();

    public float ReadFloat() => _readStream.ReadFloat();

    public short ReadInt16() => _readStream.ReadInt16();

    public int ReadInt32() => _readStream.ReadInt32();

    public long ReadInt64() => _readStream.ReadInt64();

    public sbyte ReadInt8() => _readStream.ReadInt8();

    public ReadOnlyMemorySlice<byte> ReadMemory(int amount, bool readSafe = true) => _readStream.ReadMemory(amount, readSafe);

    public ReadOnlyMemorySlice<byte> ReadMemory(int amount, int offset, bool readSafe = true) => _readStream.ReadMemory(amount, offset, readSafe);

    public ReadOnlySpan<byte> ReadSpan(int amount, bool readSafe = true) => _readStream.ReadSpan(amount, readSafe);

    public ReadOnlySpan<byte> ReadSpan(int amount, int offset, bool readSafe = true) => _readStream.ReadSpan(amount, offset, readSafe);

    public string ReadStringUTF8(int amount) => _readStream.ReadStringUTF8(amount);

    public ushort ReadUInt16() => _readStream.ReadUInt16();

    public uint ReadUInt32() => _readStream.ReadUInt32();

    public ulong ReadUInt64() => _readStream.ReadUInt64();

    public byte ReadUInt8() => _readStream.ReadUInt8();

    public void WriteTo(Stream stream, int amount) => _readStream.WriteTo(stream, amount);
}