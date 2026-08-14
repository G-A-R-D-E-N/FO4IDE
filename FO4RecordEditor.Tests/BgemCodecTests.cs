using System.IO;
using FluentAssertions;
using FO4RecordEditor.Services.Materials;
using Xunit;

namespace FO4RecordEditor.Tests;











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


        bw.Write((uint)0x4D454742);
        bw.Write((uint)2);
        bw.Write((uint)3);
        bw.Write(0f); bw.Write(0f); bw.Write(1f); bw.Write(1f);
        bw.Write(1f);
        bw.Write((byte)0); bw.Write((uint)6); bw.Write((uint)7);
        bw.Write((byte)0); bw.Write((byte)0);
        bw.Write((byte)0); bw.Write((byte)1);
        bw.Write((byte)0); bw.Write((byte)0);
        bw.Write((byte)0); bw.Write((byte)1); bw.Write((byte)0); bw.Write((byte)0);
        bw.Write((byte)0); bw.Write((byte)0); bw.Write(0f);
        bw.Write((byte)0); bw.Write(1f);
        bw.Write((byte)0);



        WriteMatStr(bw, @"Textures\Effects\Glow_d.dds");
        WriteMatStr(bw, "");
        WriteMatStr(bw, "");
        WriteMatStr(bw, @"Textures\Effects\Glow_n.dds");
        WriteMatStr(bw, "");

        bw.Write((byte)0);
        bw.Write((byte)1);
        bw.Write((byte)1);
        bw.Write((byte)0);
        bw.Write((byte)0);
        bw.Write((byte)1);
        bw.Write(1f); bw.Write(0.5f); bw.Write(0.25f);
        bw.Write(2f);
        bw.Write(10f);
        bw.Write(80f);
        bw.Write(0f);
        bw.Write(1f);
        bw.Write(0.75f);
        bw.Write((byte)3);
        bw.Write(15f);

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



    [Fact]
    public void EmptyTextureSlotIsNullTerminatedNotZeroLength()
    {
        var d = BgemCodec.Parse(BuildV2Reference());
        d.GrayscaleTexture = "";
        var bytes = BgemCodec.Write(d);



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



    [Fact]
    public void WriteForcesTheSignatureTheConcreteTypeDemands()
    {
        var d = BgemCodec.Parse(BuildV2Reference());
        d.Header.Signature = 0;

        var bytes = MaterialCodec.Write(d);
        MaterialCodec.Parse(bytes).Should().BeOfType<BgemData>();
        BitConverter.ToUInt32(bytes, 0).Should().Be(BgemCodec.Signature);
    }




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
