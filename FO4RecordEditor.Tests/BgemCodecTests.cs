using System.IO;
using FluentAssertions;
using FO4RecordEditor.Services.Materials;
using Xunit;

namespace FO4RecordEditor.Tests;

// Same approach BgsmCodecTests takes: BuildV2Reference hand-encodes a version-2 BGEM byte-for-byte,
// independently of BgemCodec's own writer (so this is not the codec tested against itself),
// following the exact field order in native/materials/src/{base.rs,bgem.rs} (Bryant-21/
// py-creation-lib, GPL-3.0, permission granted).
//
// The codec was ALSO verified byte-identical against every real material in Fallout4 - Materials.ba2
// -- all 283 .bgem and all 6,623 .bgsm files round-tripped with zero differences and zero parse
// failures. Those files are not embedded here (third-party game assets, not something to ship in a
// public repo); RealMaterialsRoundTripByteForByte below re-runs that sweep whenever a real Data
// folder is reachable.
public class BgemCodecTests
{
    private readonly Xunit.Abstractions.ITestOutputHelper _out;
    public BgemCodecTests(Xunit.Abstractions.ITestOutputHelper o) => _out = o;

    private static void WriteMatStr(BinaryWriter bw, string s)
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

        // -- shared header, version 2 (base.rs) --
        bw.Write((uint)0x4D454742);           // 'BGEM'
        bw.Write((uint)2);                     // version
        bw.Write((uint)3);                     // tile flags: tileU=true, tileV=true
        bw.Write(0f); bw.Write(0f); bw.Write(1f); bw.Write(1f);   // uOffset,vOffset,uScale,vScale
        bw.Write(1f);                          // alpha
        bw.Write((byte)0); bw.Write((uint)6); bw.Write((uint)7);  // alphaBlendMode 0/1/2
        bw.Write((byte)0); bw.Write((byte)0);  // alphaTestRef, alphaTest
        bw.Write((byte)0); bw.Write((byte)1);  // zBufferWrite, zBufferTest
        bw.Write((byte)0); bw.Write((byte)0);  // ssr, wetSsr
        bw.Write((byte)0); bw.Write((byte)1); bw.Write((byte)0); bw.Write((byte)0); // decal,twoSided,decalNoFade,nonOccluder
        bw.Write((byte)0); bw.Write((byte)0); bw.Write(0f);       // refraction,refractionFalloff,refractionPower
        bw.Write((byte)0); bw.Write(1f);       // version<10: envMapping, envMappingMaskScale
        bw.Write((byte)0);                     // grayscaleToPaletteColor
        // version<6: no maskWrites

        // -- body, version 2 (bgem.rs): none of the >=10/11/15/16/20/21 blocks apply --
        WriteMatStr(bw, @"Textures\Effects\Glow_d.dds");   // BaseTexture
        WriteMatStr(bw, "");                                // GrayscaleTexture
        WriteMatStr(bw, "");                                // EnvmapTexture
        WriteMatStr(bw, @"Textures\Effects\Glow_n.dds");   // NormalTexture
        WriteMatStr(bw, "");                                // EnvmapMaskTexture

        bw.Write((byte)0);                     // BloodEnabled
        bw.Write((byte)1);                     // EffectLightingEnabled
        bw.Write((byte)1);                     // FalloffEnabled
        bw.Write((byte)0);                     // FalloffColorEnabled
        bw.Write((byte)0);                     // GrayscaleToPaletteAlpha
        bw.Write((byte)1);                     // SoftEnabled
        bw.Write(1f); bw.Write(0.5f); bw.Write(0.25f);            // BaseColor
        bw.Write(2f);                          // BaseColorScale
        bw.Write(10f);                         // FalloffStartAngle
        bw.Write(80f);                         // FalloffStopAngle
        bw.Write(0f);                          // FalloffStartOpacity
        bw.Write(1f);                          // FalloffStopOpacity
        bw.Write(0.75f);                       // LightingInfluence
        bw.Write((byte)3);                     // EnvmapMinLOD
        bw.Write(15f);                         // SoftDepth

        bw.Flush();
        return ms.ToArray();
    }

    [Fact]
    public void ParsesAHandEncodedV2Bgem()
    {
        var d = BgemCodec.Parse(BuildV2Reference());

        d.Header.Signature.Should().Be(BgemCodec.Signature);
        d.Header.Version.Should().Be(2u);
        d.Header.TileU.Should().BeTrue();
        d.Header.TileV.Should().BeTrue();
        d.Header.TwoSided.Should().BeTrue();

        d.BaseTexture.TrimEnd('\0').Should().Be(@"Textures\Effects\Glow_d.dds");
        d.NormalTexture.TrimEnd('\0').Should().Be(@"Textures\Effects\Glow_n.dds");
        d.EffectLightingEnabled.Should().BeTrue();
        d.FalloffEnabled.Should().BeTrue();
        d.SoftEnabled.Should().BeTrue();
        d.BaseColor.Should().Equal(1f, 0.5f, 0.25f);
        d.BaseColorScale.Should().Be(2f);
        d.FalloffStartAngle.Should().Be(10f);
        d.FalloffStopAngle.Should().Be(80f);
        d.LightingInfluence.Should().Be(0.75f);
        d.EnvmapMinLOD.Should().Be((byte)3);
        d.SoftDepth.Should().Be(15f);

        // Blocks this version does not carry must stay unset rather than defaulting to a value that
        // would then be written back out and change the file's length.
        d.SpecularTexture.Should().BeNull();
        d.GlassEnabled.Should().BeNull();
        d.EnvironmentMapping.Should().BeNull();
        d.EmittanceColor.Should().BeNull();
        d.Glowmap.Should().BeNull();
        d.EffectPbrSpecular.Should().BeNull();
    }

    [Fact]
    public void WritesBackByteForByte()
    {
        var original = BuildV2Reference();
        BgemCodec.Write(BgemCodec.Parse(original)).Should().Equal(original);
    }

    // A zero-length texture slot misaligns FO4's own parser and every field after it, which shows up
    // in game as a pink material rather than as an error. An empty slot must be len=1/0x00.
    [Fact]
    public void EmptyTextureSlotIsNullTerminatedNotZeroLength()
    {
        var d = BgemCodec.Parse(BuildV2Reference());
        d.GrayscaleTexture = "";
        var bytes = BgemCodec.Write(d);

        // GrayscaleTexture is the second string after the header; find it by re-parsing rather than
        // by offset, then assert on the encoding of the round-tripped value.
        BgemCodec.Parse(bytes).GrayscaleTexture.Should().Be("\0");
    }

    [Fact]
    public void MaterialCodecDispatchesOnMagicNotExtension()
    {
        var bgem = BuildV2Reference();
        MaterialCodec.Parse(bgem).Should().BeOfType<BgemData>();
        MaterialCodec.FormatName(MaterialCodec.Parse(bgem)).Should().Be("BGEM");

        var notMaterial = new byte[] { 0x4E, 0x49, 0x46, 0x00, 1, 2, 3, 4 };
        var act = () => MaterialCodec.Parse(notMaterial);
        act.Should().Throw<InvalidDataException>().WithMessage("*BGSM or BGEM*");
    }

    // Header.Signature is settable (bgsm_set_field can reach it) and is 0 on a constructed object.
    // Writing it verbatim would produce a file whose magic disagrees with its body.
    [Fact]
    public void WriteForcesTheSignatureTheConcreteTypeDemands()
    {
        var d = BgemCodec.Parse(BuildV2Reference());
        d.Header.Signature = 0;

        var bytes = MaterialCodec.Write(d);
        MaterialCodec.Parse(bytes).Should().BeOfType<BgemData>();
        BitConverter.ToUInt32(bytes, 0).Should().Be(BgemCodec.Signature);
    }

    // Re-runs the full sweep whenever a real Data folder is reachable. Follows the same fixture
    // convention as Ba2NextGenDecompressionTests: skip with a logged reason, or hard-fail when
    // FO4RE_REQUIRE_FIXTURES is set, so a skip is never quietly recorded as a pass.
    [Fact]
    public void RealMaterialsRoundTripByteForByte()
    {
        var archive = TestDataRoots.Archive("Fallout4 - Materials.ba2");
        if (archive == null)
        {
            const string msg = "Fixture archive not present: Fallout4 - Materials.ba2 "
                               + "(searched FO4RE_TEST_DATA and the known Data folders)";
            if (TestDataRoots.FixturesRequired) Assert.Fail(msg);
            _out.WriteLine("Skipped -- " + msg);
            return;
        }

        var reader = Mutagen.Bethesda.Archives.Archive.CreateReader(
            Mutagen.Bethesda.GameRelease.Fallout4, archive);

        int checkedCount = 0, differing = 0, failed = 0;
        foreach (var f in reader.Files)
        {
            var p = f.Path?.ToString() ?? "";
            if (!p.EndsWith(".bgsm", System.StringComparison.OrdinalIgnoreCase) &&
                !p.EndsWith(".bgem", System.StringComparison.OrdinalIgnoreCase)) continue;

            var bytes = f.GetBytes();
            checkedCount++;
            try
            {
                if (!MaterialCodec.Write(MaterialCodec.Parse(bytes)).AsSpan().SequenceEqual(bytes)) differing++;
            }
            catch { failed++; }
        }

        _out.WriteLine($"Checked {checkedCount} real materials: {differing} differing, {failed} parse failures.");
        checkedCount.Should().BeGreaterThan(0, "the vanilla materials archive should contain materials");
        differing.Should().Be(0, "every real material must survive a parse/write round trip unchanged");
        failed.Should().Be(0, "every real material must parse");
    }
}
