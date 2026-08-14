using System.Text;

namespace FO4RecordEditor.Services.Archives;

/// <summary>
/// Decoding for stored archive names. UTF-8 first, falling back to Latin-1 per name.
///
/// Not every vanilla name is UTF-8: three entries in Fallout4 - Voices.ba2 are Windows-1252
/// (Mar\xEDa, S\xE1nchez). A plain UTF-8 decode turns those bytes into U+FFFD, which both displays
/// wrong and, if re-encoded, changes the file. Latin-1 agrees with Windows-1252 for the accented
/// range these use and never fails, so it is the right fallback; the raw bytes are kept regardless,
/// so this only ever affects what a human sees.
/// </summary>
public static class Ba2Text
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static string Decode(byte[] bytes)
    {
        try { return StrictUtf8.GetString(bytes); }
        catch (DecoderFallbackException) { return Encoding.Latin1.GetString(bytes); }
    }
}
