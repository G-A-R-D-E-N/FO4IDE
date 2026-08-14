using Mutagen.Bethesda.Plugins.Meta;
using Noggog;
using System.Buffers.Binary;
using System.Collections;
using System.IO.Abstractions;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Masters;

namespace Mutagen.Bethesda.Plugins.Binary.Headers;

public readonly struct ModHeader
{

    public GameConstants Meta { get; }

    public ReadOnlyMemorySlice<byte> Span { get; }

    public ModHeader(GameConstants meta, ReadOnlyMemorySlice<byte> span)
    {
        Meta = meta;
        Span = span.Slice(0, meta.ModHeaderLength);
    }

    public GameRelease Release => Meta.Release;

    public sbyte HeaderLength => Meta.ModHeaderLength;

    public RecordType RecordType => new RecordType(BinaryPrimitives.ReadInt32LittleEndian(Span.Slice(0, 4)));

    public uint RecordLength => BinaryPrimitives.ReadUInt32LittleEndian(Span.Slice(4, 4));

    public uint ContentLength => BinaryPrimitives.ReadUInt32LittleEndian(Span.Slice(4, 4));

    public long TotalLength => HeaderLength + ContentLength;

    public int Flags => BinaryPrimitives.ReadInt32LittleEndian(Span.Slice(8, 4));

    public MasterStyle MasterStyle => MasterStyleConstruction.ConstructFromFlags(Flags, Meta);
}

public readonly struct ModHeaderFrame : IEnumerable<SubrecordPinFrame>
{
    private readonly ModHeader _header;

    public ReadOnlyMemorySlice<byte> HeaderAndContentData { get; }

    public ReadOnlyMemorySlice<byte> Content => HeaderAndContentData.Slice(_header.HeaderLength, checked((int)_header.ContentLength));

    public long TotalLength => HeaderAndContentData.Length;

    public ModHeaderFrame(GameConstants meta, ReadOnlyMemorySlice<byte> span)
    {
        _header = meta.ModHeader(span);
        HeaderAndContentData = span.Slice(0, checked((int)_header.TotalLength));
    }

    public ModHeaderFrame(ModHeader header, ReadOnlyMemorySlice<byte> span)
    {
        _header = header;
        HeaderAndContentData = span.Slice(0, checked((int)_header.TotalLength));
    }

    #region Header Forwarding

    public override string? ToString() => _header.ToString();

    public IEnumerator<SubrecordPinFrame> GetEnumerator() => HeaderExt.EnumerateSubrecords(this).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public GameConstants Meta => _header.Meta;

    public GameRelease Release => _header.Release;

    public sbyte HeaderLength => _header.HeaderLength;

    public RecordType RecordType => _header.RecordType;

    public uint RecordLength => _header.RecordLength;

    public uint ContentLength => _header.ContentLength;

    public int Flags => _header.Flags;

    public MasterStyle MasterStyle => _header.MasterStyle;

    #endregion

    public static ModHeaderFrame FromPath(
        ModPath path,
        GameRelease release,
        IFileSystem? fileSystem = null)
    {
        var fs = fileSystem.GetOrDefault().FileStream.New(path, FileMode.Open, FileAccess.Read);
        using var stream = new MutagenBinaryReadStream(fs,
            new ParsingMeta(
                release,
                path.ModKey,
                masterReferences: null!));
        return stream.ReadModHeaderFrame(readSafe: true);
    }

    public static ModHeaderFrame FromStream(
        Stream stream,
        ModKey modKey,
        GameRelease release,
        bool readSafe = true)
    {
        using var mutStream = new MutagenBinaryReadStream(stream,
            new ParsingMeta(
                release,
                modKey,
                masterReferences: null!),
            dispose: false);
        return mutStream.ReadModHeaderFrame(readSafe: readSafe);
    }
}