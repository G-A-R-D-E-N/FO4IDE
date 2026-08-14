namespace FO4RecordEditor.Services.Materials;

/// <summary>
/// FO4 .bgem ("BGEM") effect-material fields -- the format behind glowing, additive, refractive and
/// animated surfaces. Names/types/version-conditional layout ported field-for-field from
/// native/materials/src/bgem.rs::BgemData (Bryant-21/py-creation-lib, GPL-3.0, permission granted),
/// the same way BgsmData was; see BgemCodec for the read/write order this depends on.
/// </summary>
public sealed class BgemData : IMaterialData
{
    public MaterialHeader Header { get; set; } = new();

    public string BaseTexture { get; set; } = "";
    public string GrayscaleTexture { get; set; } = "";
    public string EnvmapTexture { get; set; } = "";
    public string NormalTexture { get; set; } = "";
    public string EnvmapMaskTexture { get; set; } = "";

    public string? SpecularTexture { get; set; }        // version >= 11
    public string? LightingTexture { get; set; }        // version >= 11
    public string? GlowTexture { get; set; }            // version >= 11

    public string? GlassRoughnessScratch { get; set; }  // version >= 21
    public string? GlassDirtOverlay { get; set; }       // version >= 21
    public bool? GlassEnabled { get; set; }             // version >= 21

    // Only present when GlassEnabled is true -- the payload is inline, not a fixed-size block, so
    // toggling GlassEnabled changes the file's length. See BgemCodec.Write.
    public float[]? GlassFresnelColor { get; set; }
    public float? GlassBlurScaleBase { get; set; }
    public float? GlassBlurScaleFactor { get; set; }    // version >= 22
    public float? GlassRefractionScaleBase { get; set; }

    public bool? EnvironmentMapping { get; set; }           // version >= 10
    public float? EnvironmentMappingMaskScale { get; set; } // version >= 10

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

    public float[]? EmittanceColor { get; set; }                      // version >= 11
    public float? AdaptativeEmissiveExposureOffset { get; set; }      // version >= 15
    public float? AdaptativeEmissiveFinalExposureMin { get; set; }    // version >= 15
    public float? AdaptativeEmissiveFinalExposureMax { get; set; }    // version >= 15
    public bool? Glowmap { get; set; }                                // version >= 16
    public bool? EffectPbrSpecular { get; set; }                      // version >= 20
}
