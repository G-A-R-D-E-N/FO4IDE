namespace FO4RecordEditor.Services.Archives;

public enum Ba2Format { General, DirectX }

/// <summary>
/// One chunk's payload. <paramref name="Data"/> is exactly the bytes stored in the archive: the
/// zlib stream when compressed, the raw file when not.
/// </summary>
/// <param name="DecompressedSize">The inflated length. Equals Data.Length when not compressed.</param>
/// <param name="MipFirst">DX10 only.</param>
/// <param name="MipLast">DX10 only.</param>
public sealed record Ba2Chunk(byte[] Data, uint DecompressedSize, bool Compressed, ushort MipFirst, ushort MipLast)
{
    public static Ba2Chunk Stored(byte[] data) => new(data, (uint)data.Length, false, 0, 0);
}

/// <summary>The per-file DX10 texture header. Absent for General archives.</summary>
public sealed record Ba2TextureInfo(ushort Height, ushort Width, byte MipCount, byte DxgiFormat, byte Flags, byte TileMode);

/// <summary>
/// One archived file. <paramref name="Path"/> keeps its ORIGINAL case: the hashes are computed from
/// the lowercased form but the string table stores the name as authored, verified against every
/// vanilla archive.
/// </summary>
public sealed class Ba2Entry
{
    /// <summary>
    /// The name exactly as stored. Kept as bytes, not a string, because three vanilla entries in
    /// Fallout4 - Voices.ba2 are Windows-1252, not UTF-8 (Mar\xEDa_F.fuz, Mar\xEDa_M.fuz,
    /// S\xE1nchez_F.fuz) -- decoding those to a string and re-encoding replaces the byte and grows
    /// the file. Real archives also store forward slashes and original case verbatim.
    /// </summary>
    public byte[] NameBytes { get; set; } = Array.Empty<byte>();

    /// <summary>Display/lookup form of <see cref="NameBytes"/>. Setting it re-encodes as UTF-8.</summary>
    public string Path
    {
        get => Ba2Text.Decode(NameBytes);
        set => NameBytes = System.Text.Encoding.UTF8.GetBytes(value ?? "");
    }
    public required List<Ba2Chunk> Chunks { get; init; }
    public Ba2TextureInfo? Texture { get; init; }

    // Real archives carry these derived from Path; kept settable so the structural reader can
    // round-trip an entry whose stored hash disagrees with its stored name without silently
    // "correcting" it, which would break a byte-exact rewrite.
    public uint NameHash { get; set; }
    public uint ExtensionHash { get; set; }
    public uint DirectoryHash { get; set; }
}

public sealed class Ba2Archive
{
    public uint Version { get; set; } = 1;
    public Ba2Format Format { get; set; } = Ba2Format.General;
    public bool HasStringTable { get; set; } = true;
    public List<Ba2Entry> Entries { get; set; } = new();
}
