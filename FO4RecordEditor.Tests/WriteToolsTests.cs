using System.IO;
using FluentAssertions;
using FO4RecordEditor.Services;
using Mutagen.Bethesda.Fallout4;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Xunit;

namespace FO4RecordEditor.Tests;

// Exercises the COBJ authoring tools end to end (no game environment needed): build a recipe,
// set its components + conditions from JSON, save it, reload from disk, and verify the binary
// round-trips what we set. This proves the AI can finish a crafting fix without FO4Edit.
public class WriteToolsTests
{
    [Fact]
    public void SetComponentsAndConditions_RoundTripThroughBinary()
    {
        var name = $"WriteToolsTest_{Guid.NewGuid():N}.esp";
        WriteService.CreatePlugin(name);
        WriteService.CreateRecord(name, "COBJ", "FWTest_CO_Bandage", env: null);

        var components = """
            [{"component":"01FAA5:Fallout4.esm","count":1},
             {"component":"1223C7:Fallout4.esm","count":3}]
            """;
        WriteService.SetComponents(name, "FWTest_CO_Bandage", components, env: null)
            .Should().Contain("Set 2 component(s)");

        var conditions = """
            [{"function":"GetGlobalValue","param1":"000126:MAIM.esp","operator":"==","value":0,"runOn":"Subject"},
             {"function":"GetBaseValue","param1":"000164:S7System.esp","operator":">=","value":25,"runOn":"Reference","reference":"000014:Fallout4.esm"}]
            """;
        WriteService.SetConditions(name, "FWTest_CO_Bandage", conditions, env: null)
            .Should().Contain("Set 2 condition(s)");

        var path = Path.Combine(Path.GetTempPath(), name);
        // env: null -- no game environment in tests, so master ordering falls back to Mutagen's.
        WriteService.SavePlugin(name, path, null).Should().Contain("Saved");

        try
        {
            var mod = Fallout4Mod.CreateFromBinary(ModPath.FromPath(path), Fallout4Release.Fallout4);
            var cobj = mod.ConstructibleObjects.Single();

            cobj.Components!.Should().HaveCount(2);
            cobj.Components![0].Component.FormKey.Should().Be(FormKey.Factory("01FAA5:Fallout4.esm"));
            cobj.Components![0].Count.Should().Be(1u);
            cobj.Components![1].Count.Should().Be(3u);

            cobj.Conditions.Should().HaveCount(2);
            var first = (ConditionFloat)cobj.Conditions[0];
            first.CompareOperator.Should().Be(CompareOperator.EqualTo);
            first.ComparisonValue.Should().Be(0f);
            ((FunctionConditionData)first.Data).Function.Should().Be(Condition.Function.GetGlobalValue);

            var second = (ConditionFloat)cobj.Conditions[1];
            second.CompareOperator.Should().Be(CompareOperator.GreaterThanOrEqualTo);
            second.ComparisonValue.Should().Be(25f);
            ((FunctionConditionData)second.Data).RunOnType.Should().Be(Condition.RunOnType.Reference);
        }
        finally { try { File.Delete(path); } catch { } }
    }

    [Fact]
    public void GetRecord_RendersConditionsAndComponentsAsReadableLines()
    {
        var name = $"WriteToolsRead_{Guid.NewGuid():N}.esp";
        WriteService.CreatePlugin(name);
        WriteService.CreateRecord(name, "COBJ", "FWTest_Readable", env: null);
        WriteService.SetComponents(name, "FWTest_Readable",
            """[{"component":"01FAA5:Fallout4.esm","count":3}]""", env: null);
        WriteService.SetConditions(name, "FWTest_Readable",
            """[{"function":"GetGlobalValue","param1":"000126:MAIM.esp","operator":"==","value":0}]""", env: null);
        WriteService.SetField(name, "FWTest_Readable", "Description", "Filled with essentials.", env: null);

        // The field dump used by get_record must show one readable line per condition/component,
        // not a dozen reflection sub-rows (xEdit-beating + far fewer tokens for the AI).
        var dump = MutagenLoader.QueryRecordFields(null, name, "FWTest_Readable");

        dump.Should().Contain("x3");                       // component "<adhesive> x3"
        dump.Should().Contain("GetGlobalValue(").And.Contain("== 0");  // readable condition line
        dump.Should().NotContain("ParameterOneNumber");    // no raw struct sub-rows
        dump.Should().NotContain("RunOnType");
        dump.Should().NotContain(".Data[10]");
        // A TranslatedString (Description) must show its text on one line, not explode into
        // Description[0].Key / Description[0].Value localization rows.
        dump.Should().Contain("Filled with essentials.");
        dump.Should().NotContain("Description[0]");
    }

    [Fact]
    public void SetField_SupportsArrayIndexPaths()
    {
        var name = $"WriteToolsIdx_{Guid.NewGuid():N}.esp";
        WriteService.CreatePlugin(name);
        WriteService.CreateRecord(name, "COBJ", "FWTest_Idx", env: null);
        WriteService.SetComponents(name, "FWTest_Idx",
            """[{"component":"01FAA5:Fallout4.esm","count":1}]""", env: null);

        // Index into the component list and set a nested scalar + FormLink (e.g. the shape of
        // Effects[0].RunImmediately the AI couldn't reach before).
        WriteService.SetField(name, "FWTest_Idx", "Components[0].Count", "9", env: null).Should().Contain("Set");
        WriteService.SetField(name, "FWTest_Idx", "Components[0].Component", "1223C7:Fallout4.esm", env: null).Should().Contain("Set");

        var cobj = WriteService.GetMutable(name)!.ConstructibleObjects.Single();
        cobj.Components![0].Count.Should().Be(9u);
        cobj.Components![0].Component.FormKey.Should().Be(FormKey.Factory("1223C7:Fallout4.esm"));

        // Out-of-range index returns a friendly error instead of throwing.
        WriteService.SetField(name, "FWTest_Idx", "Components[5].Count", "1", env: null).Should().Contain("out of range");
    }

    [Fact]
    public void DeleteRecord_RemovesTheStub()
    {
        var name = $"WriteToolsDel_{Guid.NewGuid():N}.esp";
        WriteService.CreatePlugin(name);
        WriteService.CreateRecord(name, "COBJ", "FWTest_Stub", env: null);
        WriteService.DeleteRecord(name, "FWTest_Stub", env: null).Should().Contain("Removed");
        WriteService.GetMutable(name)!.ConstructibleObjects.Should().BeEmpty();
    }

    // The Conflicts-panel batch action must hand back authoritative counts: one valid record copies
    // as an override, one bogus reference fails, and the JSON summary reports total/ok/failed + the
    // failing FormKey (so the UI no longer guesses success by regex-matching a result string).
    [Fact]
    public void CopyAsOverrideMany_ReportsStructuredCounts()
    {
        var source = $"BatchSrc_{Guid.NewGuid():N}.esp";
        WriteService.CreatePlugin(source);
        WriteService.CreateRecord(source, "KYWD", "FWTest_BatchKw", env: null);

        // The created record's FormKey (the read tools see the mutable mod immediately).
        var dump = MutagenLoader.QueryRecordFields(null, source, "FWTest_BatchKw");
        var validFk = dump.Split('\n').First(l => l.TrimStart().StartsWith("FormKey:")).Split(':', 2)[1].Trim();

        var patch = $"BatchPatch_{Guid.NewGuid():N}.esp";
        var itemsJson = $$"""
            [{"formKey":"{{validFk}}","source":"{{source}}"},
             {"formKey":"000000:DoesNotExist.esp","source":"DoesNotExist.esp"}]
            """;

        var json = WriteService.CopyAsOverrideMany(env: null, itemsJson, patch);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("total").GetInt32().Should().Be(2);
        root.GetProperty("ok").GetInt32().Should().Be(1);
        root.GetProperty("failed").GetInt32().Should().Be(1);
        root.GetProperty("requiresOverwrite").GetBoolean().Should().BeFalse();

        var failures = root.GetProperty("failures");
        failures.GetArrayLength().Should().Be(1);
        failures[0].GetProperty("formKey").GetString().Should().Be("000000:DoesNotExist.esp");
    }
}
