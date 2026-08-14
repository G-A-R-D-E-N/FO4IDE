using System.IO;
using System.IO.Compression;
using System.Text;

namespace FO4RecordEditor.Services.Archives;

public static class Ba2Codec
{
    private const uint Magic = 0x58445442;
    private const uint GnrlTag = 0x4C524E47;
    private const uint Dx10Tag = 0x30315844;
    private const uint ChunkSentinel = 0xBAADF00D;

    private const int HeaderSize = 24;
    private const ushort ChunkHeaderSizeGeneral = 0x10;
    private const ushort ChunkHeaderSizeDirectX = 0x18;
    private const int ChunkRecordSizeGeneral = 20;
    private const int ChunkRecordSizeDirectX = 24;

    public static Ba2Archive Read(string path)
    {
        using var fs = File.OpenRead(path);
        return Read(fs);
    }

    public static Ba2Archive Read(Stream stream)
    {
        using var r = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

        if (r.ReadUInt32() != Magic) throw new InvalidDataException("Not a BA2: missing BTDX magic.");
        var version = r.ReadUInt32();
        var tag = r.ReadUInt32();
        var format = tag switch
        {
            GnrlTag => Ba2Format.General,
            Dx10Tag => Ba2Format.DirectX,
            _ => throw new InvalidDataException($"Unsupported BA2 content format 0x{tag:X8} (only GNRL and DX10 are FO4).")
        };
        var fileCount = r.ReadUInt32();
        var stringTableOffset = r.ReadUInt64();

        var archive = new Ba2Archive
        {
            Version = version,
            Format = format,
            HasStringTable = stringTableOffset != 0,
        };

        var entries = new List<Ba2Entry>((int)fileCount);
        var chunkOffsets = new List<(Ba2Entry Entry, int Index, ulong Offset, uint Compressed, uint Decompressed)>();

        for (uint i = 0; i < fileCount; i++)
        {
            var nameHash = r.ReadUInt32();
            var extHash = r.ReadUInt32();
            var dirHash = r.ReadUInt32();
            r.ReadByte();
            var chunkCount = r.ReadByte();
            var chunkHeaderSize = r.ReadUInt16();

            var expected = format == Ba2Format.General ? ChunkHeaderSizeGeneral : ChunkHeaderSizeDirectX;
            if (chunkHeaderSize != expected)
                throw new InvalidDataException($"Entry {i}: chunk header size 0x{chunkHeaderSize:X4}, expected 0x{expected:X4}.");

            Ba2TextureInfo? texture = null;
            if (format == Ba2Format.DirectX)
                texture = new Ba2TextureInfo(r.ReadUInt16(), r.ReadUInt16(), r.ReadByte(), r.ReadByte(), r.ReadByte(), r.ReadByte());

            var entry = new Ba2Entry
            {
                Chunks = new List<Ba2Chunk>(chunkCount),
                Texture = texture,
                NameHash = nameHash,
                ExtensionHash = extHash,
                DirectoryHash = dirHash,
            };

            for (int c = 0; c < chunkCount; c++)
            {
                var offset = r.ReadUInt64();
                var compressed = r.ReadUInt32();
                var decompressed = r.ReadUInt32();
                ushort mipFirst = 0, mipLast = 0;
                if (format == Ba2Format.DirectX) { mipFirst = r.ReadUInt16(); mipLast = r.ReadUInt16(); }
                var sentinel = r.ReadUInt32();
                if (sentinel != ChunkSentinel)
                    throw new InvalidDataException($"Entry {i} chunk {c}: sentinel 0x{sentinel:X8}, expected 0x{ChunkSentinel:X8}.");

                entry.Chunks.Add(new Ba2Chunk(Array.Empty<byte>(), decompressed, compressed != 0, mipFirst, mipLast));
                chunkOffsets.Add((entry, c, offset, compressed, decompressed));
            }

            entries.Add(entry);
        }

        foreach (var (entry, index, offset, compressed, decompressed) in chunkOffsets)
        {
            stream.Position = (long)offset;
            var length = compressed != 0 ? compressed : decompressed;
            var data = new byte[length];
            ReadExactly(stream, data);
            var existing = entry.Chunks[index];
            entry.Chunks[index] = existing with { Data = data };
        }

        if (stringTableOffset != 0)
        {
            stream.Position = (long)stringTableOffset;
            foreach (var entry in entries)
            {
                var len = r.ReadUInt16();
                var bytes = new byte[len];
                ReadExactly(stream, bytes);
                entry.NameBytes = bytes;
            }
        }

        archive.Entries = entries;
        return archive;
    }

    private static void ReadExactly(Stream s, byte[] buffer)
    {
        int read = 0;
        while (read < buffer.Length)
        {
            var n = s.Read(buffer, read, buffer.Length - read);
            if (n <= 0) throw new EndOfStreamException("Archive truncated.");
            read += n;
        }
    }

    public static void Write(Ba2Archive archive, string path)
    {
        using var fs = File.Create(path);
        Write(archive, fs);
    }

    public static void Write(Ba2Archive archive, Stream stream)
    {
        using var w = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

        var directX = archive.Format == Ba2Format.DirectX;
        var chunkHeaderSize = directX ? ChunkHeaderSizeDirectX : ChunkHeaderSizeGeneral;
        var chunkRecordSize = directX ? ChunkRecordSizeDirectX : ChunkRecordSizeGeneral;
        var entryFixedSize = directX ? 24 : 16;

        long tableSize = 0;
        int totalChunks = 0;
        foreach (var e in archive.Entries)
        {
            if (directX != (e.Texture != null))
                throw new InvalidDataException($"'{e.Path}': a DirectX archive needs texture info on every entry, a General archive on none.");
            if (e.Chunks.Count > byte.MaxValue)
                throw new InvalidDataException($"'{e.Path}': {e.Chunks.Count} chunks, the format stores the count in one byte.");
            tableSize += entryFixedSize + (long)e.Chunks.Count * chunkRecordSize;
            totalChunks += e.Chunks.Count;
        }

        var dataStart = HeaderSize + tableSize;
        var offsets = new ulong[totalChunks];
        {
            var cursor = (ulong)dataStart;
            int i = 0;
            foreach (var e in archive.Entries)
                foreach (var c in e.Chunks)
                {
                    offsets[i++] = cursor;
                    cursor += (ulong)c.Data.Length;
                }
        }
        var stringTableOffset = archive.HasStringTable
            ? (ulong)dataStart + (ulong)archive.Entries.Sum(e => e.Chunks.Sum(c => (long)c.Data.Length))
            : 0ul;

        w.Write(Magic);
        w.Write(archive.Version);
        w.Write(directX ? Dx10Tag : GnrlTag);
        w.Write((uint)archive.Entries.Count);
        w.Write(stringTableOffset);

        int chunkIndex = 0;
        foreach (var e in archive.Entries)
        {
            w.Write(e.NameHash);
            w.Write(e.ExtensionHash);
            w.Write(e.DirectoryHash);
            w.Write((byte)0);
            w.Write((byte)e.Chunks.Count);
            w.Write(chunkHeaderSize);

            if (directX)
            {
                var t = e.Texture!;
                w.Write(t.Height); w.Write(t.Width);
                w.Write(t.MipCount); w.Write(t.DxgiFormat); w.Write(t.Flags); w.Write(t.TileMode);
            }

            foreach (var c in e.Chunks)
            {
                w.Write(offsets[chunkIndex++]);
                w.Write(c.Compressed ? (uint)c.Data.Length : 0u);
                w.Write(c.DecompressedSize);
                if (directX) { w.Write(c.MipFirst); w.Write(c.MipLast); }
                w.Write(ChunkSentinel);
            }
        }

        foreach (var e in archive.Entries)
            foreach (var c in e.Chunks)
                w.Write(c.Data);

        if (archive.HasStringTable)
            foreach (var e in archive.Entries)
            {

                var bytes = e.NameBytes;
                if (bytes.Length > ushort.MaxValue)
                    throw new InvalidDataException($"'{e.Path}': name too long for the u16-prefixed string table.");
                w.Write((ushort)bytes.Length);
                w.Write(bytes);
            }
    }

    public static byte[] Decompress(Ba2Chunk chunk)
    {
        if (!chunk.Compressed) return chunk.Data;
        using var input = new MemoryStream(chunk.Data);
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        var outBuf = new byte[chunk.DecompressedSize];
        var read = 0;
        while (read < outBuf.Length)
        {
            var n = zlib.Read(outBuf, read, outBuf.Length - read);
            if (n <= 0) break;
            read += n;
        }
        if (read != outBuf.Length)
            throw new InvalidDataException($"Chunk inflated to {read} bytes, header says {chunk.DecompressedSize}.");
        return outBuf;
    }

    public static Ba2Chunk Compress(byte[] data)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
            zlib.Write(data, 0, data.Length);
        var packed = output.ToArray();

        return packed.Length >= data.Length
            ? Ba2Chunk.Stored(data)
            : new Ba2Chunk(packed, (uint)data.Length, true, 0, 0);
    }
}
