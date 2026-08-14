using System.IO;

namespace FO4RecordEditor.Services.Materials;

/// <summary>
/// Little-endian byte reader matching native/materials/src/base.rs::Reader exactly (Bryant-21/
/// py-creation-lib, GPL-3.0, permission granted). read_string() is the GENERIC length-prefixed
/// reader used for the header -- it returns the raw bytes verbatim, including any embedded
/// trailing NUL a texture-path field was written with (see WriteBgsmString below). Do not add
/// trimming here; a byte-exact round-trip depends on returning exactly what was on disk.
/// </summary>
internal sealed class MatReader
{
    private readonly byte[] _data;
    private int _pos;
    public MatReader(byte[] data) { _data = data; _pos = 0; }

    private byte[] ReadExact(int len)
    {
        if (_pos + len > _data.Length) throw new InvalidDataException("unexpected EOF");
        var slice = _data[_pos..(_pos + len)];
        _pos += len;
        return slice;
    }

    public byte ReadU8() => ReadExact(1)[0];
    public bool ReadBool() => ReadU8() != 0;
    public uint ReadU32() => BitConverter.ToUInt32(ReadExact(4));
    public float ReadF32() => BitConverter.ToSingle(ReadExact(4));

    public string ReadString()
    {
        var len = (int)ReadU32();
        if (len == 0) return "";
        return System.Text.Encoding.UTF8.GetString(ReadExact(len));
    }

    public float[] ReadColor3() => new[] { ReadF32(), ReadF32(), ReadF32() };
}

/// <summary>Matching little-endian writer (base.rs::Writer).</summary>
internal sealed class MatWriter
{
    private readonly List<byte> _data = new();
    public void WriteU8(byte v) => _data.Add(v);
    public void WriteBool(bool v) => WriteU8((byte)(v ? 1 : 0));
    public void WriteU32(uint v) => _data.AddRange(BitConverter.GetBytes(v));
    public void WriteF32(float v) => _data.AddRange(BitConverter.GetBytes(v));

    /// <summary>Generic length-prefixed write (header strings, none exist in BGSM/BGEM today
    /// but kept for parity with base.rs::Writer::write_string). Empty string -> len=0, no bytes.</summary>
    public void WriteString(string value)
    {
        if (string.IsNullOrEmpty(value)) { WriteU32(0); return; }
        var bytes = System.Text.Encoding.UTF8.GetBytes(value);
        WriteU32((uint)bytes.Length);
        _data.AddRange(bytes);
    }

    /// <summary>
    /// BGSM texture/path strings are length-prefixed AND null-terminated -- an empty slot is
    /// len=1,byte=0x00, NEVER len=0. FO4's own BGSM parser misaligns the rest of the stream on a
    /// zero-length string in a texture slot (manifests as pink materials in-game), per
    /// bgsm.rs::write_bgsm_string's own comment. Ported verbatim, not simplified.
    /// </summary>
    public void WriteBgsmString(string? value)
    {
        value ??= "";
        if (value.Length == 0) { WriteString("\0"); return; }
        if (value.EndsWith('\0')) { WriteString(value); return; }
        WriteString(value + "\0");
    }

    public void WriteColor3(float[] c) { WriteF32(c[0]); WriteF32(c[1]); WriteF32(c[2]); }
    public byte[] ToArray() => _data.ToArray();
}

/// <summary>
/// FO4 .bgsm binary codec. Field order and version-conditional branches are a byte-for-byte port
/// of native/materials/src/{base.rs,bgsm.rs} (Bryant-21/py-creation-lib, GPL-3.0, permission
/// granted) -- verified against that source before writing this, not guessed, because a wrong
/// field order here silently corrupts a real mod's material on the next write.
/// </summary>
public static class BgsmCodec
{
    public const uint Signature = 0x4D534742; // 'BGSM' little-endian

    internal static MaterialHeader ParseHeader(MatReader r) => ParseHeader(r, Signature, "BGSM");

    /// <summary>The header is shared by .bgsm and .bgem (base.rs::read_header takes the expected
    /// signature the same way), so only the magic differs.</summary>
    internal static MaterialHeader ParseHeader(MatReader r, uint expectedSignature, string formatName)
    {
        var h = new MaterialHeader { Signature = r.ReadU32() };
        if (h.Signature != expectedSignature)
            throw new InvalidDataException($"Invalid {formatName} signature: 0x{h.Signature:X8}");
        h.Version = r.ReadU32();
        var tileFlags = r.ReadU32();
        h.TileU = (tileFlags & 2) != 0;
        h.TileV = (tileFlags & 1) != 0;
        h.UOffset = r.ReadF32();
        h.VOffset = r.ReadF32();
        h.UScale = r.ReadF32();
        h.VScale = r.ReadF32();
        h.Alpha = r.ReadF32();
        h.AlphaBlendMode0 = r.ReadU8();
        h.AlphaBlendMode1 = r.ReadU32();
        h.AlphaBlendMode2 = r.ReadU32();
        h.AlphaTestRef = r.ReadU8();
        h.AlphaTest = r.ReadBool();
        h.ZBufferWrite = r.ReadBool();
        h.ZBufferTest = r.ReadBool();
        h.Ssr = r.ReadBool();
        h.WetSsr = r.ReadBool();
        h.Decal = r.ReadBool();
        h.TwoSided = r.ReadBool();
        h.DecalNoFade = r.ReadBool();
        h.NonOccluder = r.ReadBool();
        h.Refraction = r.ReadBool();
        h.RefractionFalloff = r.ReadBool();
        h.RefractionPower = r.ReadF32();
        if (h.Version < 10) { h.EnvMapping = r.ReadBool(); h.EnvMappingMaskScale = r.ReadF32(); }
        else { h.DepthBias = r.ReadBool(); }
        h.GrayscaleToPaletteColor = r.ReadBool();
        if (h.Version >= 6) h.MaskWrites = r.ReadU8();
        return h;
    }

    internal static void WriteHeader(MatWriter w, MaterialHeader h)
    {
        w.WriteU32(h.Signature);
        w.WriteU32(h.Version);
        uint tileFlags = (uint)((h.TileU ? 2 : 0) | (h.TileV ? 1 : 0));
        w.WriteU32(tileFlags);
        w.WriteF32(h.UOffset); w.WriteF32(h.VOffset); w.WriteF32(h.UScale); w.WriteF32(h.VScale);
        w.WriteF32(h.Alpha);
        w.WriteU8(h.AlphaBlendMode0); w.WriteU32(h.AlphaBlendMode1); w.WriteU32(h.AlphaBlendMode2);
        w.WriteU8(h.AlphaTestRef); w.WriteBool(h.AlphaTest);
        w.WriteBool(h.ZBufferWrite); w.WriteBool(h.ZBufferTest);
        w.WriteBool(h.Ssr); w.WriteBool(h.WetSsr);
        w.WriteBool(h.Decal); w.WriteBool(h.TwoSided); w.WriteBool(h.DecalNoFade); w.WriteBool(h.NonOccluder);
        w.WriteBool(h.Refraction); w.WriteBool(h.RefractionFalloff); w.WriteF32(h.RefractionPower);
        if (h.Version < 10) { w.WriteBool(h.EnvMapping ?? false); w.WriteF32(h.EnvMappingMaskScale ?? 0f); }
        else { w.WriteBool(h.DepthBias ?? false); }
        w.WriteBool(h.GrayscaleToPaletteColor);
        if (h.Version >= 6) w.WriteU8(h.MaskWrites ?? 0);
    }

    public static BgsmData Parse(byte[] data)
    {
        var r = new MatReader(data);
        var d = new BgsmData { Header = ParseHeader(r) };
        var v = d.Header.Version;

        d.DiffuseTexture = r.ReadString();
        d.NormalTexture = r.ReadString();
        d.SmoothSpecTexture = r.ReadString();
        d.GreyscaleTexture = r.ReadString();
        if (v > 2)
        {
            d.GlowTexture = r.ReadString();
            d.WrinklesTexture = r.ReadString();
            d.SpecularTexture = r.ReadString();
            d.LightingTexture = r.ReadString();
            d.FlowTexture = r.ReadString();
            if (v >= 17) d.DistanceFieldAlphaTexture = r.ReadString();
        }
        else
        {
            d.EnvmapTexture = r.ReadString();
            d.GlowTexture = r.ReadString();
            d.InnerLayerTexture = r.ReadString();
            d.WrinklesTexture = r.ReadString();
            d.DisplacementTexture = r.ReadString();
        }

        d.EnableEditorAlphaRef = r.ReadBool();
        if (v >= 8)
        {
            d.Translucency = r.ReadBool();
            d.TranslucencyThickObject = r.ReadBool();
            d.TranslucencyMixAlbedoWithSubsurfaceColor = r.ReadBool();
            d.TranslucencySubsurfaceColor = r.ReadColor3();
            d.TranslucencyTransmissiveScale = r.ReadF32();
            d.TranslucencyTurbulence = r.ReadF32();
        }
        else
        {
            d.RimLighting = r.ReadBool();
            d.RimPower = r.ReadF32();
            d.BackLightPower = r.ReadF32();
            d.SubsurfaceLighting = r.ReadBool();
            d.SubsurfaceLightingRolloff = r.ReadF32();
        }

        d.SpecularEnabled = r.ReadBool();
        d.SpecularColor = r.ReadColor3();
        d.SpecularMult = r.ReadF32();
        d.Smoothness = r.ReadF32();
        d.FresnelPower = r.ReadF32();
        d.WetnessControlSpecScale = r.ReadF32();
        d.WetnessControlSpecPowerScale = r.ReadF32();
        d.WetnessControlSpecMinvar = r.ReadF32();
        if (v < 10) d.WetnessControlEnvMapScale = r.ReadF32();
        d.WetnessControlFresnelPower = r.ReadF32();
        d.WetnessControlMetalness = r.ReadF32();

        if (v > 2)
        {
            d.Pbr = r.ReadBool();
            if (v >= 9) { d.CustomPorosity = r.ReadBool(); d.PorosityValue = r.ReadF32(); }
        }

        d.RootMaterialPath = r.ReadString();
        d.AnisoLighting = r.ReadBool();
        d.EmitEnabled = r.ReadBool();
        if (d.EmitEnabled) d.EmittanceColor = r.ReadColor3();
        d.EmittanceMult = r.ReadF32();
        d.ModelSpaceNormals = r.ReadBool();
        d.ExternalEmittance = r.ReadBool();
        if (v >= 12) d.LumEmittance = r.ReadF32();

        if (v >= 13)
        {
            d.UseAdaptativeEmissive = r.ReadBool();
            d.AdaptativeEmissiveExposureOffset = r.ReadF32();
            d.AdaptativeEmissiveFinalExposureMin = r.ReadF32();
            d.AdaptativeEmissiveFinalExposureMax = r.ReadF32();
        }

        if (v < 8) d.BackLighting = r.ReadBool();
        d.ReceiveShadows = r.ReadBool();
        d.HideSecret = r.ReadBool();
        d.CastShadows = r.ReadBool();
        d.DissolveFade = r.ReadBool();
        d.AssumeShadowmask = r.ReadBool();
        d.Glowmap = r.ReadBool();
        if (v < 7) { d.EnvironmentMappingWindow = r.ReadBool(); d.EnvironmentMappingEye = r.ReadBool(); }

        d.Hair = r.ReadBool();
        d.HairTintColor = r.ReadColor3();
        d.Tree = r.ReadBool();
        d.Facegen = r.ReadBool();
        d.SkinTint = r.ReadBool();
        d.Tessellate = r.ReadBool();

        if (v < 3)
        {
            d.DisplacementTextureBias = r.ReadF32();
            d.DisplacementTextureScale = r.ReadF32();
            d.TessellationPnScale = r.ReadF32();
            d.TessellationBaseFactor = r.ReadF32();
            d.TessellationFadeDistance = r.ReadF32();
        }
        d.GrayscaleToPaletteScale = r.ReadF32();
        if (v >= 1) d.SkewSpecularAlpha = r.ReadBool();

        if (v >= 3)
        {
            var terrainEnabled = r.ReadBool();
            d.Terrain = terrainEnabled;
            if (terrainEnabled)
            {
                if (v == 3) d.UnkInt1 = r.ReadU32();
                d.TerrainThresholdFalloff = r.ReadF32();
                d.TerrainTilingDistance = r.ReadF32();
                d.TerrainRotationAngle = r.ReadF32();
            }
        }

        return d;
    }

    public static byte[] Write(BgsmData d)
    {
        var w = new MatWriter();
        var h = d.Header;
        var v = h.Version;
        WriteHeader(w, h);

        w.WriteBgsmString(d.DiffuseTexture);
        w.WriteBgsmString(d.NormalTexture);
        w.WriteBgsmString(d.SmoothSpecTexture);
        w.WriteBgsmString(d.GreyscaleTexture);
        if (v > 2)
        {
            w.WriteBgsmString(d.GlowTexture);
            w.WriteBgsmString(d.WrinklesTexture);
            w.WriteBgsmString(d.SpecularTexture);
            w.WriteBgsmString(d.LightingTexture);
            w.WriteBgsmString(d.FlowTexture);
            if (v >= 17) w.WriteBgsmString(d.DistanceFieldAlphaTexture);
        }
        else
        {
            w.WriteBgsmString(d.EnvmapTexture);
            w.WriteBgsmString(d.GlowTexture);
            w.WriteBgsmString(d.InnerLayerTexture);
            w.WriteBgsmString(d.WrinklesTexture);
            w.WriteBgsmString(d.DisplacementTexture);
        }

        w.WriteBool(d.EnableEditorAlphaRef);
        if (v >= 8)
        {
            w.WriteBool(d.Translucency ?? false);
            w.WriteBool(d.TranslucencyThickObject ?? false);
            w.WriteBool(d.TranslucencyMixAlbedoWithSubsurfaceColor ?? false);
            w.WriteColor3(d.TranslucencySubsurfaceColor ?? new[] { 1f, 1f, 1f });
            w.WriteF32(d.TranslucencyTransmissiveScale ?? 0f);
            w.WriteF32(d.TranslucencyTurbulence ?? 0f);
        }
        else
        {
            w.WriteBool(d.RimLighting ?? false);
            w.WriteF32(d.RimPower ?? 0f);
            w.WriteF32(d.BackLightPower ?? 0f);
            w.WriteBool(d.SubsurfaceLighting ?? false);
            w.WriteF32(d.SubsurfaceLightingRolloff ?? 0f);
        }

        w.WriteBool(d.SpecularEnabled);
        w.WriteColor3(d.SpecularColor);
        w.WriteF32(d.SpecularMult);
        w.WriteF32(d.Smoothness);
        w.WriteF32(d.FresnelPower);
        w.WriteF32(d.WetnessControlSpecScale);
        w.WriteF32(d.WetnessControlSpecPowerScale);
        w.WriteF32(d.WetnessControlSpecMinvar);
        if (v < 10) w.WriteF32(d.WetnessControlEnvMapScale ?? 0f);
        w.WriteF32(d.WetnessControlFresnelPower);
        w.WriteF32(d.WetnessControlMetalness);

        if (v > 2)
        {
            w.WriteBool(d.Pbr ?? false);
            if (v >= 9) { w.WriteBool(d.CustomPorosity ?? false); w.WriteF32(d.PorosityValue ?? 0f); }
        }

        w.WriteBgsmString(d.RootMaterialPath);
        w.WriteBool(d.AnisoLighting);
        w.WriteBool(d.EmitEnabled);
        if (d.EmitEnabled) w.WriteColor3(d.EmittanceColor ?? new[] { 1f, 1f, 1f });
        w.WriteF32(d.EmittanceMult);
        w.WriteBool(d.ModelSpaceNormals);
        w.WriteBool(d.ExternalEmittance);
        if (v >= 12) w.WriteF32(d.LumEmittance ?? 0f);

        if (v >= 13)
        {
            w.WriteBool(d.UseAdaptativeEmissive ?? false);
            w.WriteF32(d.AdaptativeEmissiveExposureOffset ?? 0f);
            w.WriteF32(d.AdaptativeEmissiveFinalExposureMin ?? 0f);
            w.WriteF32(d.AdaptativeEmissiveFinalExposureMax ?? 0f);
        }

        if (v < 8) w.WriteBool(d.BackLighting ?? false);
        w.WriteBool(d.ReceiveShadows);
        w.WriteBool(d.HideSecret);
        w.WriteBool(d.CastShadows);
        w.WriteBool(d.DissolveFade);
        w.WriteBool(d.AssumeShadowmask);
        w.WriteBool(d.Glowmap);
        if (v < 7) { w.WriteBool(d.EnvironmentMappingWindow ?? false); w.WriteBool(d.EnvironmentMappingEye ?? false); }

        w.WriteBool(d.Hair);
        w.WriteColor3(d.HairTintColor);
        w.WriteBool(d.Tree);
        w.WriteBool(d.Facegen);
        w.WriteBool(d.SkinTint);
        w.WriteBool(d.Tessellate);

        if (v < 3)
        {
            w.WriteF32(d.DisplacementTextureBias ?? 0f);
            w.WriteF32(d.DisplacementTextureScale ?? 0f);
            w.WriteF32(d.TessellationPnScale ?? 0f);
            w.WriteF32(d.TessellationBaseFactor ?? 0f);
            w.WriteF32(d.TessellationFadeDistance ?? 0f);
        }
        w.WriteF32(d.GrayscaleToPaletteScale);
        if (v >= 1) w.WriteBool(d.SkewSpecularAlpha ?? false);

        if (v >= 3)
        {
            var terrain = d.Terrain ?? false;
            w.WriteBool(terrain);
            if (terrain)
            {
                if (v == 3) w.WriteU32(d.UnkInt1 ?? 0u);
                w.WriteF32(d.TerrainThresholdFalloff ?? 0f);
                w.WriteF32(d.TerrainTilingDistance ?? 0f);
                w.WriteF32(d.TerrainRotationAngle ?? 0f);
            }
        }

        return w.ToArray();
    }
}
