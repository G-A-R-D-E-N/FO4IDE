using System.IO;
using FluentAssertions;
using FO4RecordEditor.Services.Materials;
using Xunit;

namespace FO4RecordEditor.Tests;







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


        bw.Write((uint)0x4D534742);
        bw.Write((uint)2);
        bw.Write((uint)2);
        bw.Write(0f); bw.Write(0f); bw.Write(1f); bw.Write(1f);
        bw.Write(1f);
        bw.Write((byte)0); bw.Write((uint)0); bw.Write((uint)0);
        bw.Write((byte)0); bw.Write((byte)0);
        bw.Write((byte)1); bw.Write((byte)1);
        bw.Write((byte)0); bw.Write((byte)0);
        bw.Write((byte)0); bw.Write((byte)0); bw.Write((byte)0); bw.Write((byte)0);
        bw.Write((byte)0); bw.Write((byte)0); bw.Write(0f);
        bw.Write((byte)0); bw.Write(0f);
        bw.Write((byte)0);



        WriteBgsmStr(bw, "tex\\d.dds");
        WriteBgsmStr(bw, "tex\\n.dds");
        WriteBgsmStr(bw, "");
        WriteBgsmStr(bw, "");
        WriteBgsmStr(bw, "");
        WriteBgsmStr(bw, "");
        WriteBgsmStr(bw, "");
        WriteBgsmStr(bw, "");
        WriteBgsmStr(bw, "");

        bw.Write((byte)0);

        bw.Write((byte)0); bw.Write(0f); bw.Write(0f); bw.Write((byte)0); bw.Write(0f);

        bw.Write((byte)1);
        bw.Write(1f); bw.Write(1f); bw.Write(1f);
        bw.Write(1f);
        bw.Write(0.8f);
        bw.Write(1f);
        bw.Write(1f); bw.Write(1f); bw.Write(0f);
        bw.Write(1f);
        bw.Write(1f); bw.Write(0f);


        WriteBgsmStr(bw, "");
        bw.Write((byte)0);
        bw.Write((byte)0);
        bw.Write(0f);
        bw.Write((byte)0);
        bw.Write((byte)0);

        bw.Write((byte)0);
        bw.Write((byte)1);
        bw.Write((byte)0);
        bw.Write((byte)1);
        bw.Write((byte)0);
        bw.Write((byte)0);
        bw.Write((byte)0);
        bw.Write((byte)0); bw.Write((byte)0);
        bw.Write((byte)0);
        bw.Write(1f); bw.Write(1f); bw.Write(1f);
        bw.Write((byte)0); bw.Write((byte)0); bw.Write((byte)0); bw.Write((byte)0);

        bw.Write(0f); bw.Write(0f); bw.Write(0f); bw.Write(0f); bw.Write(0f);
        bw.Write(0f);
        bw.Write((byte)0);


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


        d.Pbr.Should().BeNull();
        d.CustomPorosity.Should().BeNull();
        d.Terrain.Should().BeNull();
        d.LumEmittance.Should().BeNull();
        d.UseAdaptativeEmissive.Should().BeNull();
        d.Translucency.Should().BeNull();
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
