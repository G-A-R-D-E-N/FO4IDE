using Mutagen.Bethesda.Plugins.Binary.Translations;
using Noggog;
using System.Drawing;

namespace Mutagen.Bethesda.Binary;

public static class IBinaryStreamExt
{

    public static Color ReadColor<TStream>(this TStream stream, ColorBinaryType binaryType)
        where TStream : IBinaryReadStream
    {
        switch (binaryType)
        {
            case ColorBinaryType.NoAlpha:
                return ReadColor(stream.ReadSpan(3), binaryType);
            case ColorBinaryType.Alpha:
                return ReadColor(stream.ReadSpan(4), binaryType);
            case ColorBinaryType.NoAlphaFloat:
                return ReadColor(stream.ReadSpan(12), binaryType);
            case ColorBinaryType.AlphaFloat:
                return ReadColor(stream.ReadSpan(16), binaryType);
            default:
                throw new NotImplementedException();
        }
    }

    public static Color ReadColor(this ReadOnlySpan<byte> span, ColorBinaryType binaryType)
    {
        switch (binaryType)
        {
            case ColorBinaryType.NoAlpha:
                return Color.FromArgb(
                    red: span[0],
                    green: span[1],
                    blue: span[2],
                    alpha: 0);
            case ColorBinaryType.Alpha:
                return Color.FromArgb(
                    red: span[0],
                    green: span[1],
                    blue: span[2],
                    alpha: span[3]);
            case ColorBinaryType.NoAlphaFloat:
                return Color.FromArgb(
                    red: GetColorByte(span.Slice(0, 4).Float()),
                    green: GetColorByte(span.Slice(4, 4).Float()),
                    blue: GetColorByte(span.Slice(8, 4).Float()),
                    alpha: 0);
            case ColorBinaryType.AlphaFloat:
                return Color.FromArgb(
                    red: GetColorByte(span.Slice(0, 4).Float()),
                    green: GetColorByte(span.Slice(4, 4).Float()),
                    blue: GetColorByte(span.Slice(8, 4).Float()),
                    alpha: GetColorByte(span.Slice(12, 4).Float()));
            default:
                throw new NotImplementedException();
        }
    }

    public static byte GetColorByte(float f)
    {
        if (f <= 0)
        {
            return 0;
        }
        else if (f >= byte.MaxValue)
        {
            return byte.MaxValue;
        }
        else
        {
            return (byte)Math.Round(byte.MaxValue * f);
        }
    }

    public static Color ReadColor(this ReadOnlyMemorySlice<byte> span, ColorBinaryType binaryType)
    {
        return span.Span.ReadColor(binaryType);
    }
}