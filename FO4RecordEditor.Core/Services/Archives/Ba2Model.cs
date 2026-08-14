namespace FO4RecordEditor.Services.Archives;

public enum Ba2Format { General, DirectX }

public sealed record Ba2Chunk(byte[] Data, uint DecompressedSize, bool Compressed, ushort MipFirst, ushort MipLast)
{
    public static Ba2Chunk Stored(byte[] data) => new(data, (uint)data.Length, false, 0, 0);
}

public sealed record Ba2TextureInfo(ushort Height, ushort Width, byte MipCount, byte DxgiFormat, byte Flags, byte TileMode);

public sealed class Ba2Entry
{

    public byte[] NameBytes { get; set; } = Array.Empty<byte>();

    public string Path
    {
        get => Ba2Text.Decode(NameBytes);
        set => NameBytes = System.Text.Encoding.UTF8.GetBytes(value ?? "");
    }
    public required List<Ba2Chunk> Chunks { get; init; }
    public Ba2TextureInfo? Texture { get; init; }

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
