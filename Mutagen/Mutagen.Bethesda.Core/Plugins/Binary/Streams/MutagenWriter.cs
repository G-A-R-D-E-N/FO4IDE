using System.IO.Abstractions;
using Mutagen.Bethesda.Plugins.Meta;
using Noggog;

namespace Mutagen.Bethesda.Plugins.Binary.Streams;

public sealed class MutagenWriter : IBinaryWriteStream, IDisposable
{
    private readonly bool dispose = true;
    private const byte Zero = 0;

    public BinaryWriter Writer;

    public Stream BaseStream { get; }

    public WritingBundle MetaData { get; }

    public long Position
    {
        get => BaseStream.Position;
        set => BaseStream.Position = value;
    }

    public long Length
    {
        get => BaseStream.Length;
    }

    public bool IsLittleEndian => true;

    public MutagenWriter(
        FilePath path,
        GameConstants constants,
        IFileSystem? fileSystem = null)
    {
        BaseStream = fileSystem.GetOrDefault().FileStream.New(path.Path, FileMode.Create, FileAccess.Write);
        Writer = new BinaryWriter(BaseStream);
        MetaData = new WritingBundle(constants);
    }

    public MutagenWriter(
        FilePath path,
        WritingBundle meta,
        IFileSystem? fileSystem = null)
    {
        BaseStream = fileSystem.GetOrDefault().FileStream.New(path.Path, FileMode.Create, FileAccess.Write);
        Writer = new BinaryWriter(BaseStream);
        MetaData = meta;
    }

    public MutagenWriter(
        Stream stream,
        WritingBundle meta,
        bool dispose = true)
    {
        this.dispose = dispose;
        BaseStream = stream;
        Writer = new BinaryWriter(stream);
        MetaData = meta;
    }

    public MutagenWriter(
        Stream stream,
        GameConstants constants,
        bool dispose = true)
    {
        this.dispose = dispose;
        BaseStream = stream;
        Writer = new BinaryWriter(stream);
        MetaData = new WritingBundle(constants);
    }

    public MutagenWriter(
        BinaryWriter writer,
        GameConstants constants)
    {
        BaseStream = writer.BaseStream;
        Writer = writer;
        MetaData = new WritingBundle(constants);
    }

    public void Write(bool b)
    {
        Writer.Write(b);
    }

    public void Write(bool b, int length)
    {
        switch (length)
        {
            case 1:
                Writer.Write((byte)(b ? 1 : 0));
                break;
            case 2:
                Writer.Write((short)(b ? 1 : 0));
                break;
            case 4:
                Writer.Write((int)(b ? 1 : 0));
                break;
            default:
                throw new NotImplementedException();
        }
    }

    public void Write(bool? b)
    {
        if (!b.HasValue) return;
        Writer.Write(b.Value);
    }

    public void Write(byte b)
    {
        Writer.Write(b);
    }

    public void Write(byte? b)
    {
        if (!b.HasValue) return;
        Writer.Write(b.Value);
    }

    public void Write(byte[]? b)
    {
        if (b == null) return;
        Writer.Write(b);
    }

    public void Write(ReadOnlyMemorySlice<byte> b)
    {
        Writer.Write(b.Span);
    }

    public void Write(ReadOnlySpan<byte> b)
    {
        Writer.Write(b);
    }

    public void Write(ushort b)
    {
        Writer.Write(b);
    }

    public void Write(ushort? b)
    {
        if (!b.HasValue) return;
        Writer.Write(b.Value);
    }

    public void Write(uint b)
    {
        Writer.Write(b);
    }

    public void Write(uint? b)
    {
        if (!b.HasValue) return;
        Writer.Write(b.Value);
    }

    public void Write(ulong b)
    {
        Writer.Write(b);
    }

    public void Write(ulong? b)
    {
        if (!b.HasValue) return;
        Writer.Write(b.Value);
    }

    public void Write(sbyte s)
    {
        Writer.Write(s);
    }

    public void Write(sbyte? s)
    {
        if (!s.HasValue) return;
        Writer.Write(s.Value);
    }

    public void Write(short s)
    {
        Writer.Write(s);
    }

    public void Write(short? s)
    {
        if (!s.HasValue) return;
        Writer.Write(s.Value);
    }

    public void Write(int i)
    {
        Writer.Write(i);
    }

    public void Write(int i, byte length)
    {
        switch (length)
        {
            case 1:
                Writer.Write(checked((byte)i));
                break;
            case 2:
                Writer.Write(checked((short)i));
                break;
            case 4:
                Writer.Write(i);
                break;
            default:
                throw new NotImplementedException();
        }
    }

    public void Write(int? i)
    {
        if (!i.HasValue) return;
        Writer.Write(i.Value);
    }

    public void Write(long i)
    {
        Writer.Write(i);
    }

    public void Write(long? i)
    {
        if (!i.HasValue) return;
        Writer.Write(i.Value);
    }

    public void Write(float i)
    {
        Writer.Write(i);
    }

    public void Write(float? i)
    {
        if (!i.HasValue) return;
        Writer.Write(i.Value);
    }

    public void Write(double i)
    {
        Writer.Write(i);
    }

    public void Write(double? i)
    {
        if (!i.HasValue) return;
        Writer.Write(i.Value);
    }

    public void Write(char c)
    {
        Writer.Write(c);
    }

    public void Write(char? c)
    {
        if (!c.HasValue) return;
        Writer.Write(c.Value);
    }

    public void WriteZeros(uint num)
    {
        for (uint i = 0; i < num; i++)
        {
            Write(Zero);
        }
    }

    public void Dispose()
    {
        if (dispose)
        {
            Writer.Dispose();
        }
    }
}