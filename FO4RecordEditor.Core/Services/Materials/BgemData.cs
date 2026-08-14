namespace FO4RecordEditor.Services.Materials;

public sealed class BgemData : IMaterialData
{
    public MaterialHeader Header { get; set; } = new();

    public string BaseTexture { get; set; } = "";
    public string GrayscaleTexture { get; set; } = "";
    public string EnvmapTexture { get; set; } = "";
    public string NormalTexture { get; set; } = "";
    public string EnvmapMaskTexture { get; set; } = "";

    public string? SpecularTexture { get; set; }
    public string? LightingTexture { get; set; }
    public string? GlowTexture { get; set; }

    public string? GlassRoughnessScratch { get; set; }
    public string? GlassDirtOverlay { get; set; }
    public bool? GlassEnabled { get; set; }

    public float[]? GlassFresnelColor { get; set; }
    public float? GlassBlurScaleBase { get; set; }
    public float? GlassBlurScaleFactor { get; set; }
    public float? GlassRefractionScaleBase { get; set; }

    public bool? EnvironmentMapping { get; set; }
    public float? EnvironmentMappingMaskScale { get; set; }

    public bool BloodEnabled { get; set; }
    public bool EffectLightingEnabled { get; set; }
    public bool FalloffEnabled { get; set; }
    public bool FalloffColorEnabled { get; set; }
    public bool GrayscaleToPaletteAlpha { get; set; }
    public bool SoftEnabled { get; set; }

    public float[] BaseColor { get; set; } = new float[3];
    public float BaseColorScale { get; set; }
    public float FalloffStartAngle { get; set; }
    public float FalloffStopAngle { get; set; }
    public float FalloffStartOpacity { get; set; }
    public float FalloffStopOpacity { get; set; }
    public float LightingInfluence { get; set; }
    public byte EnvmapMinLOD { get; set; }
    public float SoftDepth { get; set; }

    public float[]? EmittanceColor { get; set; }
    public float? AdaptativeEmissiveExposureOffset { get; set; }
    public float? AdaptativeEmissiveFinalExposureMin { get; set; }
    public float? AdaptativeEmissiveFinalExposureMax { get; set; }
    public bool? Glowmap { get; set; }
    public bool? EffectPbrSpecular { get; set; }
}
