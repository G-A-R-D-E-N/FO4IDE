using System.Text;

namespace FO4RecordEditor.Services.Archives;

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

    public static string Normalize(string path)
    {
        var p = (path ?? "").Replace('/', '\\').ToLowerInvariant().Trim('\\');
        return p.Length == 0 || p.Length >= 260 ? "." : p;
    }

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
