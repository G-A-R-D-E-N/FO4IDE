using System.IO;
using FO4RecordEditor.Services.Archives;

namespace FO4RecordEditor.Services.Textures;

/// <summary>
/// DDS bytes to RGBA pixels, or straight to a PNG, without touching the disk or launching anything.
/// This is the read half of issue #76: the Cell Viewer used to write a temp .dds, launch
/// Texconv.exe and read a temp .png back for every texture it showed.
///
/// Block formats go through <see cref="BcnDecoder"/>; the uncompressed layouts are unpacked here.
/// A format neither handles throws, so a caller can fall back to Texconv rather than render
/// something wrong.
/// </summary>
public static class DdsDecoder
{
    /// <summary>True when this DXGI format can be decoded in process.</summary>
    public static bool CanDecode(byte dxgi) => BcnDecoder.CanDecode(dxgi) || IsUncompressed(dxgi);

    private static bool IsUncompressed(byte dxgi) => dxgi is 28 or 29 or 87 or 88 or 91 or 93 or 49 or 61 or 65 or 85 or 86 or 115;

    /// <summary>
    /// Decode mip 0 of the first surface to tightly packed RGBA rows, top to bottom.
    ///
    /// <paramref name="reconstructZ"/> rebuilds a tangent-space normal's Z as sqrt(1 - x^2 - y^2).
    /// BC5 stores only X and Y, so its blue channel decodes to 0 and three.js lights the mesh
    /// wrong; this matches what the Texconv path did with -reconstructz.
    /// </summary>
    public static byte[] Decode(ReadOnlySpan<byte> ddsBytes, out int width, out int height, bool reconstructZ = false)
    {
        var info = DdsCodec.Parse(ddsBytes);
        width = info.Width;
        height = info.Height;

        var surfaceLength = (int)DdsCodec.MipSize(info.Format, info.Width, info.Height);
        if (ddsBytes.Length < info.DataOffset + surfaceLength)
            throw new InvalidDataException($"Texture is {ddsBytes.Length} bytes; its top mip alone needs {info.DataOffset + surfaceLength}.");
        var surface = ddsBytes.Slice(info.DataOffset, surfaceLength);

        var pixels = (long)width * height;
        if (pixels * 4 > int.MaxValue) throw new InvalidDataException($"{width}x{height} is too large to decode into one buffer.");
        var rgba = new byte[pixels * 4];

        if (BcnDecoder.CanDecode(info.DxgiFormat))
            BcnDecoder.DecodeBlockFormat(surface, info.DxgiFormat, width, height, rgba);
        else if (IsUncompressed(info.DxgiFormat))
            Unpack(surface, rgba, info.DxgiFormat, (int)pixels, info.Format.PixelBytes);
        else
            throw new InvalidDataException($"{info.Format.Name} cannot be decoded in process.");

        if (reconstructZ) ReconstructZ(rgba);
        return rgba;
    }

    /// <summary>Decode and encode in one step. <paramref name="reconstructZ"/> as in <see cref="Decode"/>.</summary>
    public static byte[] ToPng(ReadOnlySpan<byte> ddsBytes, bool reconstructZ = false)
    {
        var rgba = Decode(ddsBytes, out var width, out var height, reconstructZ);
        return PngWriter.Write(rgba, width, height);
    }

    /// <summary>True when this file is a BC5 normal map, i.e. the one that needs Z rebuilt.</summary>
    public static bool IsBc5(ReadOnlySpan<byte> ddsBytes)
    {
        try { return DdsCodec.Parse(ddsBytes).DxgiFormat is 82 or 83 or 84; }
        catch { return false; }
    }

    private static void ReconstructZ(byte[] rgba)
    {
        for (int i = 0; i < rgba.Length; i += 4)
        {
            var x = rgba[i] / 255.0 * 2.0 - 1.0;
            var y = rgba[i + 1] / 255.0 * 2.0 - 1.0;
            var z = Math.Sqrt(Math.Max(0.0, 1.0 - x * x - y * y));
            rgba[i + 2] = (byte)Math.Clamp(Math.Round((z + 1.0) * 0.5 * 255.0), 0, 255);
        }
    }

    private static void Unpack(ReadOnlySpan<byte> surface, byte[] rgba, byte dxgi, int pixels, int stride)
    {
        for (int i = 0; i < pixels; i++)
        {
            var s = i * stride;
            var d = i * 4;
            switch (dxgi)
            {
                case 28 or 29:                      // R8G8B8A8
                    rgba[d] = surface[s]; rgba[d + 1] = surface[s + 1]; rgba[d + 2] = surface[s + 2]; rgba[d + 3] = surface[s + 3];
                    break;
                case 87 or 91:                      // B8G8R8A8
                    rgba[d] = surface[s + 2]; rgba[d + 1] = surface[s + 1]; rgba[d + 2] = surface[s]; rgba[d + 3] = surface[s + 3];
                    break;
                case 88 or 93:                      // B8G8R8X8: the fourth byte is undefined, not alpha
                    rgba[d] = surface[s + 2]; rgba[d + 1] = surface[s + 1]; rgba[d + 2] = surface[s]; rgba[d + 3] = 255;
                    break;
                case 49:                            // R8G8
                    rgba[d] = surface[s]; rgba[d + 1] = surface[s + 1]; rgba[d + 2] = 0; rgba[d + 3] = 255;
                    break;
                case 61:                            // R8 expands to grey, as DirectXTex does
                    rgba[d] = rgba[d + 1] = rgba[d + 2] = surface[s]; rgba[d + 3] = 255;
                    break;
                case 65:                            // A8
                    rgba[d] = rgba[d + 1] = rgba[d + 2] = 0; rgba[d + 3] = surface[s];
                    break;
                case 85:                            // B5G6R5
                {
                    var v = surface[s] | (surface[s + 1] << 8);
                    var r5 = (v >> 11) & 0x1F; var g6 = (v >> 5) & 0x3F; var b5 = v & 0x1F;
                    rgba[d] = (byte)((r5 * 527 + 23) >> 6);
                    rgba[d + 1] = (byte)((g6 * 259 + 33) >> 6);
                    rgba[d + 2] = (byte)((b5 * 527 + 23) >> 6);
                    rgba[d + 3] = 255;
                    break;
                }
                case 86:                            // B5G5R5A1
                {
                    var v = surface[s] | (surface[s + 1] << 8);
                    var r5 = (v >> 10) & 0x1F; var g5 = (v >> 5) & 0x1F; var b5 = v & 0x1F;
                    rgba[d] = (byte)((r5 * 527 + 23) >> 6);
                    rgba[d + 1] = (byte)((g5 * 527 + 23) >> 6);
                    rgba[d + 2] = (byte)((b5 * 527 + 23) >> 6);
                    rgba[d + 3] = (byte)((v & 0x8000) != 0 ? 255 : 0);
                    break;
                }
                case 115:                           // B4G4R4A4
                {
                    var v = surface[s] | (surface[s + 1] << 8);
                    rgba[d] = (byte)(((v >> 8) & 0xF) * 17);
                    rgba[d + 1] = (byte)(((v >> 4) & 0xF) * 17);
                    rgba[d + 2] = (byte)((v & 0xF) * 17);
                    rgba[d + 3] = (byte)(((v >> 12) & 0xF) * 17);
                    break;
                }
                default:
                    throw new InvalidDataException($"DXGI format {dxgi} is not an uncompressed layout this tool unpacks.");
            }
        }
    }
}
