using System.IO;
using FluentAssertions;
using FO4RecordEditor.Services;
using Mutagen.Bethesda.Fallout4;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Xunit;

namespace FO4RecordEditor.Tests;




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



        var dump = MutagenLoader.QueryRecordFields(null, name, "FWTest_Readable");

        dump.Should().Contain("x3");
        dump.Should().Contain("GetGlobalValue(").And.Contain("== 0");
        dump.Should().NotContain("ParameterOneNumber");
        dump.Should().NotContain("RunOnType");
        dump.Should().NotContain(".Data[10]");


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



        WriteService.SetField(name, "FWTest_Idx", "Components[0].Count", "9", env: null).Should().Contain("Set");
        WriteService.SetField(name, "FWTest_Idx", "Components[0].Component", "1223C7:Fallout4.esm", env: null).Should().Contain("Set");

        var cobj = WriteService.GetMutable(name)!.ConstructibleObjects.Single();
        cobj.Components![0].Count.Should().Be(9u);
        cobj.Components![0].Component.FormKey.Should().Be(FormKey.Factory("1223C7:Fallout4.esm"));


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




    [Fact]
    public void CopyAsOverrideMany_ReportsStructuredCounts()
    {
        var source = $"BatchSrc_{Guid.NewGuid():N}.esp";
        WriteService.CreatePlugin(source);
        WriteService.CreateRecord(source, "KYWD", "FWTest_BatchKw", env: null);


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
