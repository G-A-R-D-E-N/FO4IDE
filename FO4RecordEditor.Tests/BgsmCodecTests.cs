using System.IO;
using FluentAssertions;
using FO4RecordEditor.Services.Materials;
using Xunit;

namespace FO4RecordEditor.Tests;

// BuildV2Reference hand-encodes a version-2 BGSM byte-for-byte, independently of BgsmCodec's own
// Writer (so this doesn't just test the codec against itself), following the exact field order in
// native/materials/src/{base.rs,bgsm.rs} (Bryant-21/py-creation-lib, GPL-3.0, permission granted).
// This same codec was also verified byte-identical against all 400 real .bgsm files found in this
// workspace (versions 1-2) before this suite was written; those files aren't embedded here since
// they're third-party/other-project assets, not something to ship in this now-public repo.
public class BgsmCodecTests
{
    private static void WriteBgsmStr(BinaryWriter bw, string s)
    {
        var terminated = s.Length == 0 ? "\0" : (s.EndsWith('\0') ? s : s + "\0");
        var bytes = System.Text.Encoding.UTF8.GetBytes(terminated);
        bw.Write((uint)bytes.Length);
        bw.Write(bytes);
    }

    private static byte[] BuildV2Reference()
    {
        var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        // -- header, version 2 --
        bw.Write((uint)0x4D534742);           // 'BGSM'
        bw.Write((uint)2);                     // version
        bw.Write((uint)2);                     // tile flags: tileU=true, tileV=false
        bw.Write(0f); bw.Write(0f); bw.Write(1f); bw.Write(1f);   // uOffset,vOffset,uScale,vScale
        bw.Write(1f);                          // alpha
        bw.Write((byte)0); bw.Write((uint)0); bw.Write((uint)0);  // alphaBlendMode 0/1/2
        bw.Write((byte)0); bw.Write((byte)0);  // alphaTestRef, alphaTest(bool as byte)
        bw.Write((byte)1); bw.Write((byte)1);  // zBufferWrite, zBufferTest
        bw.Write((byte)0); bw.Write((byte)0);  // ssr, wetSsr
        bw.Write((byte)0); bw.Write((byte)0); bw.Write((byte)0); bw.Write((byte)0); // decal,twoSided,decalNoFade,nonOccluder
        bw.Write((byte)0); bw.Write((byte)0); bw.Write(0f);       // refraction,refractionFalloff,refractionPower
        bw.Write((byte)0); bw.Write(0f);       // version<10: envMapping(bool), envMappingMaskScale(f32)
        bw.Write((byte)0);                     // grayscaleToPaletteColor
        // version<6: no maskWrites

        // -- body, version 2 (not >2, so texture-slot else-branch; no PBR/Translucency/Terrain blocks) --
        WriteBgsmStr(bw, "tex\\d.dds");
        WriteBgsmStr(bw, "tex\\n.dds");
        WriteBgsmStr(bw, "");                  // SmoothSpecTexture
        WriteBgsmStr(bw, "");                  // GreyscaleTexture
        WriteBgsmStr(bw, "");                  // EnvmapTexture
        WriteBgsmStr(bw, "");                  // GlowTexture
        WriteBgsmStr(bw, "");                  // InnerLayerTexture
        WriteBgsmStr(bw, "");                  // WrinklesTexture
        WriteBgsmStr(bw, "");                  // DisplacementTexture

        bw.Write((byte)0);                     // EnableEditorAlphaRef
        // version<8: RimLighting block
        bw.Write((byte)0); bw.Write(0f); bw.Write(0f); bw.Write((byte)0); bw.Write(0f);

        bw.Write((byte)1);                     // SpecularEnabled
        bw.Write(1f); bw.Write(1f); bw.Write(1f);  // SpecularColor
        bw.Write(1f);                          // SpecularMult
        bw.Write(0.8f);                        // Smoothness
        bw.Write(1f);                          // FresnelPower
        bw.Write(1f); bw.Write(1f); bw.Write(0f);  // wetness spec scale/power/minvar
        bw.Write(1f);                          // version<10: WetnessControlEnvMapScale
        bw.Write(1f); bw.Write(0f);            // WetnessControlFresnelPower, WetnessControlMetalness
        // version not >2: no PBR block

        WriteBgsmStr(bw, "");                  // RootMaterialPath
        bw.Write((byte)0);                     // AnisoLighting
        bw.Write((byte)0);                     // EmitEnabled=false -> no EmittanceColor
        bw.Write(0f);                          // EmittanceMult
        bw.Write((byte)0);                     // ModelSpaceNormals
        bw.Write((byte)0);                     // ExternalEmittance
        // version<12: no LumEmittance; version<13: no AdaptativeEmissive block
        bw.Write((byte)0);                     // version<8: BackLighting
        bw.Write((byte)1);                     // ReceiveShadows
        bw.Write((byte)0);                     // HideSecret
        bw.Write((byte)1);                     // CastShadows
        bw.Write((byte)0);                     // DissolveFade
        bw.Write((byte)0);                     // AssumeShadowmask
        bw.Write((byte)0);                     // Glowmap
        bw.Write((byte)0); bw.Write((byte)0);  // version<7: EnvironmentMappingWindow, EnvironmentMappingEye
        bw.Write((byte)0);                     // Hair
        bw.Write(1f); bw.Write(1f); bw.Write(1f);  // HairTintColor
        bw.Write((byte)0); bw.Write((byte)0); bw.Write((byte)0); bw.Write((byte)0); // Tree,Facegen,SkinTint,Tessellate
        // version<3: displacement/tessellation block
        bw.Write(0f); bw.Write(0f); bw.Write(0f); bw.Write(0f); bw.Write(0f);
        bw.Write(0f);                          // GrayscaleToPaletteScale
        bw.Write((byte)0);                     // version>=1: SkewSpecularAlpha
        // version<3: no Terrain block at all

        return ms.ToArray();
    }

    [Fact]
    public void Parse_ReadsIndependentlyHandBuiltV2File()
    {
        var bytes = BuildV2Reference();
        var d = BgsmCodec.Parse(bytes);

        d.Header.Version.Should().Be(2u);
        d.Header.TileU.Should().BeTrue();
        d.Header.TileV.Should().BeFalse();
        d.DiffuseTexture.Should().Be("tex\\d.dds\0");
        d.NormalTexture.Should().Be("tex\\n.dds\0");
        d.Smoothness.Should().Be(0.8f);
        d.SpecularColor.Should().Equal(1f, 1f, 1f);
        d.ReceiveShadows.Should().BeTrue();
        d.CastShadows.Should().BeTrue();
        d.HideSecret.Should().BeFalse();

        // Fields that don't exist at version 2 must stay null, not a default like false/0.
        d.Pbr.Should().BeNull();
        d.CustomPorosity.Should().BeNull();
        d.Terrain.Should().BeNull();
        d.LumEmittance.Should().BeNull();
        d.UseAdaptativeEmissive.Should().BeNull();
        d.Translucency.Should().BeNull();          // v<8 uses RimLighting instead
        d.RimLighting.Should().NotBeNull();
    }

    [Fact]
    public void Write_ReproducesHandBuiltV2FileByteForByte()
    {
        var reference = BuildV2Reference();
        var parsed = BgsmCodec.Parse(reference);
        var rewritten = BgsmCodec.Write(parsed);
        rewritten.Should().Equal(reference);
    }

    // Real corpus (400 files, all workspace .bgsm) only ever hit versions 1-2, so the higher-version
    // branches (Translucency/PBR/AdaptativeEmissive/Terrain) have no real-file coverage. This
    // round-trips a hand-populated v13 object through Write->Parse to catch a symmetric encode/decode
    // bug in those branches specifically, since no ground-truth v13 file exists to hand-encode against.
    [Fact]
    public void RoundTrip_Version13_ExercisesHighVersionBranches()
    {
        var d = new BgsmData
        {
            Header = new MaterialHeader
            {
                Signature = BgsmCodec.Signature, Version = 13,
                UOffset = 0, VOffset = 0, UScale = 1, VScale = 1, Alpha = 1,
                RefractionPower = 0, DepthBias = true, GrayscaleToPaletteColor = false, MaskWrites = 3,
            },
            DiffuseTexture = "a\\d.dds\0", NormalTexture = "a\\n.dds\0",
            SmoothSpecTexture = "\0", GreyscaleTexture = "\0",
            GlowTexture = "\0", WrinklesTexture = "\0", SpecularTexture = "\0",
            LightingTexture = "\0", FlowTexture = "\0",
            EnableEditorAlphaRef = true,
            Translucency = true, TranslucencyThickObject = false,
            TranslucencyMixAlbedoWithSubsurfaceColor = true,
            TranslucencySubsurfaceColor = new[] { 0.9f, 0.2f, 0.1f },
            TranslucencyTransmissiveScale = 0.5f, TranslucencyTurbulence = 0.1f,
            SpecularEnabled = true, SpecularColor = new[] { 0.5f, 0.6f, 0.7f }, SpecularMult = 1.2f,
            Smoothness = 0.4f, FresnelPower = 2f,
            WetnessControlSpecScale = 1, WetnessControlSpecPowerScale = 1, WetnessControlSpecMinvar = 0,
            WetnessControlFresnelPower = 1, WetnessControlMetalness = 0.3f,
            Pbr = true, CustomPorosity = true, PorosityValue = 0.25f,
            RootMaterialPath = "\0", AnisoLighting = false, EmitEnabled = true,
            EmittanceColor = new[] { 1f, 0.5f, 0f }, EmittanceMult = 2f,
            ModelSpaceNormals = true, ExternalEmittance = false,
            LumEmittance = 3f,
            UseAdaptativeEmissive = true, AdaptativeEmissiveExposureOffset = 0.1f,
            AdaptativeEmissiveFinalExposureMin = 0.2f, AdaptativeEmissiveFinalExposureMax = 0.9f,
            ReceiveShadows = true, HideSecret = false, CastShadows = true, DissolveFade = false,
            AssumeShadowmask = false, Glowmap = true,
            Hair = false, HairTintColor = new[] { 1f, 1f, 1f },
            Tree = false, Facegen = false, SkinTint = false, Tessellate = false,
            GrayscaleToPaletteScale = 0.1f, SkewSpecularAlpha = true,
            Terrain = true, TerrainThresholdFalloff = 0.1f, TerrainTilingDistance = 500f, TerrainRotationAngle = 45f,
        };

        var bytes = BgsmCodec.Write(d);
        var reparsed = BgsmCodec.Parse(bytes);

        reparsed.Header.Version.Should().Be(13u);
        reparsed.Header.DepthBias.Should().BeTrue();
        reparsed.Header.EnvMapping.Should().BeNull("version >= 10 uses DepthBias instead");
        reparsed.Translucency.Should().BeTrue();
        reparsed.TranslucencySubsurfaceColor.Should().Equal(0.9f, 0.2f, 0.1f);
        reparsed.RimLighting.Should().BeNull("version >= 8 uses Translucency instead");
        reparsed.Pbr.Should().BeTrue();
        reparsed.CustomPorosity.Should().BeTrue();
        reparsed.PorosityValue.Should().Be(0.25f);
        reparsed.LumEmittance.Should().Be(3f);
        reparsed.UseAdaptativeEmissive.Should().BeTrue();
        reparsed.AdaptativeEmissiveFinalExposureMax.Should().Be(0.9f);
        reparsed.Terrain.Should().BeTrue();
        reparsed.UnkInt1.Should().BeNull("UnkInt1 only exists at exactly version 3");
        reparsed.TerrainRotationAngle.Should().Be(45f);
        reparsed.EmittanceColor.Should().Equal(1f, 0.5f, 0f);

        // Full second round-trip must be byte-stable once parsed back.
        BgsmCodec.Write(reparsed).Should().Equal(bytes);
    }

    [Fact]
    public void Parse_RejectsWrongSignature()
    {
        var bad = new byte[] { (byte)'X', (byte)'X', (byte)'X', (byte)'X', 0, 0, 0, 0 };
        Action act = () => BgsmCodec.Parse(bad);
        act.Should().Throw<InvalidDataException>().WithMessage("*signature*");
    }
}
