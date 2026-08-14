using System.Text;

namespace FO4RecordEditor.Services.Archives;

/// <summary>
/// The FO4 BA2 name hash. Verified against real vanilla entries (Fallout4 - Meshes.ba2), not taken
/// on faith from any source: the stem/parent CRCs and the extension word reproduce exactly.
///
/// The CRC is NOT standard CRC-32. It uses the same reflected 0xEDB88320 table, but the accumulator
/// starts at 0 and there is no final complement, so feeding it through a stock Crc32 gives the wrong
/// value for every entry.
/// </summary>
public static class Ba2Hash
{
    private static readonly uint[] Table = BuildTable();

    private static uint[] BuildTable()
    {
        var t = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            var c = i;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            t[i] = c;
        }
        return t;
    }

    private static uint Crc(ReadOnlySpan<byte> bytes)
    {
        uint crc = 0;
        foreach (var b in bytes) crc = (crc >> 8) ^ Table[(crc ^ b) & 0xFF];
        return crc;
    }

    /// <summary>Lowercase, forward slashes to backslashes, no leading or trailing separator. This is
    /// the form the hashes are computed over; the string table still stores the original case.</summary>
    public static string Normalize(string path)
    {
        var p = (path ?? "").Replace('/', '\\').ToLowerInvariant().Trim('\\');
        return p.Length == 0 || p.Length >= 260 ? "." : p;
    }

    /// <summary>(nameHash, extensionWord, directoryHash) for a data-relative path.</summary>
    public static (uint Name, uint Extension, uint Directory) Compute(string path)
    {
        var p = Normalize(path);
        var sep = p.LastIndexOf('\\');
        var parent = sep >= 0 ? p[..sep] : "";
        var dot = p.LastIndexOf('.');
        var ext = dot >= 0 ? p[(dot + 1)..] : "";
        var stem = dot >= 0 ? p[(sep + 1)..dot] : p[(sep + 1)..];

        uint extWord = 0;
        var extBytes = Encoding.UTF8.GetBytes(ext);
        for (int i = 0; i < Math.Min(4, extBytes.Length); i++) extWord |= (uint)extBytes[i] << (i * 8);

        return (Crc(Encoding.UTF8.GetBytes(stem)), extWord, Crc(Encoding.UTF8.GetBytes(parent)));
    }
}
