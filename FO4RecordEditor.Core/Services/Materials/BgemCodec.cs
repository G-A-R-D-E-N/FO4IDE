using System.IO;

namespace FO4RecordEditor.Services.Materials;

public static class BgemCodec
{
    public const uint Signature = 0x4D454742;

    public static BgemData Parse(byte[] data)
    {
        var r = new MatReader(data);
        var d = new BgemData { Header = BgsmCodec.ParseHeader(r, Signature, "BGEM") };
        var v = d.Header.Version;

        d.BaseTexture = r.ReadString();
        d.GrayscaleTexture = r.ReadString();
        d.EnvmapTexture = r.ReadString();
        d.NormalTexture = r.ReadString();
        d.EnvmapMaskTexture = r.ReadString();

        if (v >= 11)
        {
            d.SpecularTexture = r.ReadString();
            d.LightingTexture = r.ReadString();
            d.GlowTexture = r.ReadString();
        }

        if (v >= 21)
        {
            d.GlassRoughnessScratch = r.ReadString();
            d.GlassDirtOverlay = r.ReadString();
            var glassEnabled = r.ReadBool();
            d.GlassEnabled = glassEnabled;
            if (glassEnabled)
            {
                d.GlassFresnelColor = r.ReadColor3();
                d.GlassBlurScaleBase = r.ReadF32();
                if (v >= 22) d.GlassBlurScaleFactor = r.ReadF32();
                d.GlassRefractionScaleBase = r.ReadF32();
            }
        }

        if (v >= 10)
        {
            d.EnvironmentMapping = r.ReadBool();
            d.EnvironmentMappingMaskScale = r.ReadF32();
        }

        d.BloodEnabled = r.ReadBool();
        d.EffectLightingEnabled = r.ReadBool();
        d.FalloffEnabled = r.ReadBool();
        d.FalloffColorEnabled = r.ReadBool();
        d.GrayscaleToPaletteAlpha = r.ReadBool();
        d.SoftEnabled = r.ReadBool();

        d.BaseColor = r.ReadColor3();
        d.BaseColorScale = r.ReadF32();
        d.FalloffStartAngle = r.ReadF32();
        d.FalloffStopAngle = r.ReadF32();
        d.FalloffStartOpacity = r.ReadF32();
        d.FalloffStopOpacity = r.ReadF32();
        d.LightingInfluence = r.ReadF32();
        d.EnvmapMinLOD = r.ReadU8();
        d.SoftDepth = r.ReadF32();

        if (v >= 11) d.EmittanceColor = r.ReadColor3();

        if (v >= 15)
        {
            d.AdaptativeEmissiveExposureOffset = r.ReadF32();
            d.AdaptativeEmissiveFinalExposureMin = r.ReadF32();
            d.AdaptativeEmissiveFinalExposureMax = r.ReadF32();
        }

        if (v >= 16) d.Glowmap = r.ReadBool();
        if (v >= 20) d.EffectPbrSpecular = r.ReadBool();

        return d;
    }

    public static byte[] Write(BgemData d)
    {
        var w = new MatWriter();
        BgsmCodec.WriteHeader(w, d.Header);
        var v = d.Header.Version;

        w.WriteBgsmString(d.BaseTexture);
        w.WriteBgsmString(d.GrayscaleTexture);
        w.WriteBgsmString(d.EnvmapTexture);
        w.WriteBgsmString(d.NormalTexture);
        w.WriteBgsmString(d.EnvmapMaskTexture);

        if (v >= 11)
        {
            w.WriteBgsmString(d.SpecularTexture ?? "");
            w.WriteBgsmString(d.LightingTexture ?? "");
            w.WriteBgsmString(d.GlowTexture ?? "");
        }

        if (v >= 21)
        {
            w.WriteBgsmString(d.GlassRoughnessScratch ?? "");
            w.WriteBgsmString(d.GlassDirtOverlay ?? "");
            var glassEnabled = d.GlassEnabled ?? false;
            w.WriteBool(glassEnabled);
            if (glassEnabled)
            {
                w.WriteColor3(d.GlassFresnelColor ?? new[] { 1f, 1f, 1f });
                w.WriteF32(d.GlassBlurScaleBase ?? 0f);
                if (v >= 22) w.WriteF32(d.GlassBlurScaleFactor ?? 0f);
                w.WriteF32(d.GlassRefractionScaleBase ?? 0f);
            }
        }

        if (v >= 10)
        {
            w.WriteBool(d.EnvironmentMapping ?? false);
            w.WriteF32(d.EnvironmentMappingMaskScale ?? 0f);
        }

        w.WriteBool(d.BloodEnabled);
        w.WriteBool(d.EffectLightingEnabled);
        w.WriteBool(d.FalloffEnabled);
        w.WriteBool(d.FalloffColorEnabled);
        w.WriteBool(d.GrayscaleToPaletteAlpha);
        w.WriteBool(d.SoftEnabled);

        w.WriteColor3(d.BaseColor);
        w.WriteF32(d.BaseColorScale);
        w.WriteF32(d.FalloffStartAngle);
        w.WriteF32(d.FalloffStopAngle);
        w.WriteF32(d.FalloffStartOpacity);
        w.WriteF32(d.FalloffStopOpacity);
        w.WriteF32(d.LightingInfluence);
        w.WriteU8(d.EnvmapMinLOD);
        w.WriteF32(d.SoftDepth);

        if (v >= 11) w.WriteColor3(d.EmittanceColor ?? new[] { 1f, 1f, 1f });

        if (v >= 15)
        {
            w.WriteF32(d.AdaptativeEmissiveExposureOffset ?? 0f);
            w.WriteF32(d.AdaptativeEmissiveFinalExposureMin ?? 0f);
            w.WriteF32(d.AdaptativeEmissiveFinalExposureMax ?? 0f);
        }

        if (v >= 16) w.WriteBool(d.Glowmap ?? false);
        if (v >= 20) w.WriteBool(d.EffectPbrSpecular ?? false);

        return w.ToArray();
    }
}

public static class MaterialCodec
{
    public static IMaterialData Parse(byte[] data)
    {
        if (data.Length < 4) throw new InvalidDataException("Not a material file: fewer than 4 bytes.");
        var sig = BitConverter.ToUInt32(data, 0);
        if (sig == BgsmCodec.Signature) return BgsmCodec.Parse(data);
        if (sig == BgemCodec.Signature) return BgemCodec.Parse(data);
        throw new InvalidDataException(
            $"Not a BGSM or BGEM material: magic 0x{sig:X8} (expected 0x{BgsmCodec.Signature:X8} or 0x{BgemCodec.Signature:X8}).");
    }

    public static byte[] Write(IMaterialData data)
    {
        switch (data)
        {
            case BgsmData b:
                b.Header.Signature = BgsmCodec.Signature;
                return BgsmCodec.Write(b);
            case BgemData e:
                e.Header.Signature = BgemCodec.Signature;
                return BgemCodec.Write(e);
            default:
                throw new NotSupportedException($"No writer for {data.GetType().Name}.");
        }
    }

    public static string FormatName(IMaterialData data) => data is BgemData ? "BGEM" : "BGSM";
}
