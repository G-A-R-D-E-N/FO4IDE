using System.Buffers.Binary;
using System.IO;
using System.Text;
using FluentAssertions;
using FO4RecordEditor.Services;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Fallout4;
using Mutagen.Bethesda.Plugins;
using Xunit;

namespace FO4RecordEditor.Tests;

public class ArmorResistanceCurveTests
{
    [Theory]
    [InlineData(151, 8, false)]
    [InlineData(152, 12, true)]
    public void DamaEntries_UseTheContainingArmorFormVersionForTheirBinaryStride(
        int formVersion,
        int expectedStride,
        bool expectCurveTable)
    {
        var plugin = $"ArmorCurve_{formVersion}_{Guid.NewGuid():N}.esp";
        var editorId = $"ArmorCurve_{formVersion}";
        var path = Path.Combine(Path.GetTempPath(), plugin);

        WriteService.CreatePlugin(plugin).Should().Contain("Created");
        WriteService.CreateRecord(plugin, "ARMO", editorId, env: null).Should().Contain("Created");

        var armor = WriteService.GetMutable(plugin)!.Armors.Single();
        armor.FormVersion = checked((ushort)formVersion);
        armor.Resistances = new();

        // Exercise the same generic element and field-editing paths exposed to the UI and MCP tools.
        ElementService.AddElement(plugin, editorId, "Resistances", template: null, env: null)
            .Should().Contain("Added a ArmorResistance");
        ElementService.AddElement(plugin, editorId, "Resistances", template: null, env: null)
            .Should().Contain("Added a ArmorResistance");

        SetResistance(plugin, editorId, index: 0, damageType: "000800:Fallout4.esm", value: 37,
            curveTable: "000900:Fallout4.esm");
        SetResistance(plugin, editorId, index: 1, damageType: "000801:Fallout4.esm", value: 91,
            curveTable: "000901:Fallout4.esm");

        WriteService.SavePlugin(plugin, path, env: null).Should().Contain("Saved");

        IFallout4ModGetter? overlay = null;
        try
        {
            var dama = ReadSingleSubrecordPayload(path, "DAMA");
            dama.Should().HaveCount(2 * expectedStride,
                "two legacy entries are 16 bytes and two form-version-152 entries are 24 bytes");

            AssertRawEntry(dama, offset: 0, expectedDamageType: 0x00000800u, expectedValue: 37u,
                expectCurveTable ? 0x00000900u : null);
            AssertRawEntry(dama, offset: expectedStride, expectedDamageType: 0x00000801u, expectedValue: 91u,
                expectCurveTable ? 0x00000901u : null);

            var parsed = Fallout4Mod.CreateFromBinary(ModPath.FromPath(path), Fallout4Release.Fallout4);
            AssertParsedEntries(((IArmorGetter)parsed.Armors.Single()).Resistances!, expectCurveTable);

            // The overlay path is the regression target: its parent list used to index every DAMA
            // entry at a fixed size. Two legacy entries prove the second one begins at byte 8, while
            // two next-gen entries prove the version-152 stride is 12. Reading CurveTable on both
            // legacy entries also proves an absent tail cannot bleed into the next entry or slice
            // beyond the final entry.
            overlay = Fallout4Mod.CreateFromBinaryOverlay(ModPath.FromPath(path), Fallout4Release.Fallout4);
            AssertParsedEntries(overlay.Armors.Single().Resistances!, expectCurveTable);
        }
        finally
        {
            (overlay as IDisposable)?.Dispose();
            try { File.Delete(path); } catch { }
        }
    }

    private static void SetResistance(
        string plugin,
        string editorId,
        int index,
        string damageType,
        uint value,
        string curveTable)
    {
        WriteService.SetField(plugin, editorId, $"Resistances[{index}].DamageType", damageType, env: null)
            .Should().Contain("Set");
        WriteService.SetField(plugin, editorId, $"Resistances[{index}].Value", value.ToString(), env: null)
            .Should().Contain("Set");
        WriteService.SetField(plugin, editorId, $"Resistances[{index}].CurveTable", curveTable, env: null)
            .Should().Contain("Set");
    }

    private static void AssertRawEntry(
        byte[] dama,
        int offset,
        uint expectedDamageType,
        uint expectedValue,
        uint? expectedCurveTable)
    {
        BinaryPrimitives.ReadUInt32LittleEndian(dama.AsSpan(offset, 4)).Should().Be(expectedDamageType);
        BinaryPrimitives.ReadUInt32LittleEndian(dama.AsSpan(offset + 4, 4)).Should().Be(expectedValue);
        if (expectedCurveTable is { } curve)
            BinaryPrimitives.ReadUInt32LittleEndian(dama.AsSpan(offset + 8, 4)).Should().Be(curve);
    }

    private static void AssertParsedEntries(
        IReadOnlyList<IArmorResistanceGetter> entries,
        bool expectCurveTable)
    {
        entries.Should().HaveCount(2);
        entries[0].DamageType.FormKey.Should().Be(FormKey.Factory("000800:Fallout4.esm"));
        entries[0].Value.Should().Be(37u);
        entries[1].DamageType.FormKey.Should().Be(FormKey.Factory("000801:Fallout4.esm"));
        entries[1].Value.Should().Be(91u);

        if (!expectCurveTable)
        {
            entries[0].CurveTable.IsNull.Should().BeTrue();
            entries[1].CurveTable.IsNull.Should().BeTrue();
            return;
        }

        entries[0].CurveTable.FormKey.Should().Be(FormKey.Factory("000900:Fallout4.esm"));
        entries[1].CurveTable.FormKey.Should().Be(FormKey.Factory("000901:Fallout4.esm"));
    }

    private static byte[] ReadSingleSubrecordPayload(string path, string signature)
    {
        var bytes = File.ReadAllBytes(path);
        var marker = Encoding.ASCII.GetBytes(signature);
        var matches = new List<int>();
        for (var i = 0; i <= bytes.Length - 6; i++)
        {
            if (bytes.AsSpan(i, 4).SequenceEqual(marker)) matches.Add(i);
        }

        matches.Should().ContainSingle($"the fixture writes exactly one {signature} subrecord");
        var offset = matches.Single();
        var length = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset + 4, 2));
        return bytes.AsSpan(offset + 6, length).ToArray();
    }
}
