namespace FO4RecordEditor.Services.Materials;






public interface IMaterialData
{
    MaterialHeader Header { get; set; }
}







public sealed class MaterialHeader
{
    public uint Signature { get; set; }
    public uint Version { get; set; }
    public bool TileU { get; set; }
    public bool TileV { get; set; }
    public float UOffset { get; set; }
    public float VOffset { get; set; }
    public float UScale { get; set; }
    public float VScale { get; set; }
    public float Alpha { get; set; }
    public byte AlphaBlendMode0 { get; set; }
    public uint AlphaBlendMode1 { get; set; }
    public uint AlphaBlendMode2 { get; set; }
    public byte AlphaTestRef { get; set; }
    public bool AlphaTest { get; set; }
    public bool ZBufferWrite { get; set; }
    public bool ZBufferTest { get; set; }
    public bool Ssr { get; set; }
    public bool WetSsr { get; set; }
    public bool Decal { get; set; }
    public bool TwoSided { get; set; }
    public bool DecalNoFade { get; set; }
    public bool NonOccluder { get; set; }
    public bool Refraction { get; set; }
    public bool RefractionFalloff { get; set; }
    public float RefractionPower { get; set; }
    public bool? EnvMapping { get; set; }
    public float? EnvMappingMaskScale { get; set; }
    public bool? DepthBias { get; set; }
    public bool GrayscaleToPaletteColor { get; set; }
    public byte? MaskWrites { get; set; }
}






public sealed class BgsmData : IMaterialData
{
    public MaterialHeader Header { get; set; } = new();

    public string DiffuseTexture { get; set; } = "";
    public string NormalTexture { get; set; } = "";
    public string SmoothSpecTexture { get; set; } = "";
    public string GreyscaleTexture { get; set; } = "";
    public string? EnvmapTexture { get; set; }
    public string? GlowTexture { get; set; }
    public string? InnerLayerTexture { get; set; }
    public string? WrinklesTexture { get; set; }
    public string? DisplacementTexture { get; set; }
    public string? SpecularTexture { get; set; }
    public string? LightingTexture { get; set; }
    public string? FlowTexture { get; set; }
    public string? DistanceFieldAlphaTexture { get; set; }

    public bool EnableEditorAlphaRef { get; set; }

    public bool? RimLighting { get; set; }
    public float? RimPower { get; set; }
    public float? BackLightPower { get; set; }
    public bool? SubsurfaceLighting { get; set; }
    public float? SubsurfaceLightingRolloff { get; set; }
    public bool? Translucency { get; set; }
    public bool? TranslucencyThickObject { get; set; }
    public bool? TranslucencyMixAlbedoWithSubsurfaceColor { get; set; }
    public float[]? TranslucencySubsurfaceColor { get; set; }
    public float? TranslucencyTransmissiveScale { get; set; }
    public float? TranslucencyTurbulence { get; set; }

    public bool SpecularEnabled { get; set; }
    public float[] SpecularColor { get; set; } = new float[3];
    public float SpecularMult { get; set; }
    public float Smoothness { get; set; }
    public float FresnelPower { get; set; }
    public float WetnessControlSpecScale { get; set; }
    public float WetnessControlSpecPowerScale { get; set; }
    public float WetnessControlSpecMinvar { get; set; }
    public float? WetnessControlEnvMapScale { get; set; }
    public float WetnessControlFresnelPower { get; set; }
    public float WetnessControlMetalness { get; set; }

    public bool? Pbr { get; set; }
    public bool? CustomPorosity { get; set; }
    public float? PorosityValue { get; set; }

    public string RootMaterialPath { get; set; } = "";
    public bool AnisoLighting { get; set; }
    public bool EmitEnabled { get; set; }
    public float[]? EmittanceColor { get; set; }
    public float EmittanceMult { get; set; }
    public bool ModelSpaceNormals { get; set; }
    public bool ExternalEmittance { get; set; }
    public float? LumEmittance { get; set; }
    public bool? UseAdaptativeEmissive { get; set; }
    public float? AdaptativeEmissiveExposureOffset { get; set; }
    public float? AdaptativeEmissiveFinalExposureMin { get; set; }
    public float? AdaptativeEmissiveFinalExposureMax { get; set; }
    public bool? BackLighting { get; set; }

    public bool ReceiveShadows { get; set; }
    public bool HideSecret { get; set; }
    public bool CastShadows { get; set; }
    public bool DissolveFade { get; set; }
    public bool AssumeShadowmask { get; set; }
    public bool Glowmap { get; set; }
    public bool? EnvironmentMappingWindow { get; set; }
    public bool? EnvironmentMappingEye { get; set; }

    public bool Hair { get; set; }
    public float[] HairTintColor { get; set; } = new float[3];
    public bool Tree { get; set; }
    public bool Facegen { get; set; }
    public bool SkinTint { get; set; }
    public bool Tessellate { get; set; }

    public float? DisplacementTextureBias { get; set; }
    public float? DisplacementTextureScale { get; set; }
    public float? TessellationPnScale { get; set; }
    public float? TessellationBaseFactor { get; set; }
    public float? TessellationFadeDistance { get; set; }

    public float GrayscaleToPaletteScale { get; set; }
    public bool? SkewSpecularAlpha { get; set; }

    public bool? Terrain { get; set; }
    public uint? UnkInt1 { get; set; }
    public float? TerrainThresholdFalloff { get; set; }
    public float? TerrainTilingDistance { get; set; }
    public float? TerrainRotationAngle { get; set; }
}
