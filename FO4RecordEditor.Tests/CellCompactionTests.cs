using System.IO;
using FluentAssertions;
using FO4RecordEditor.Services;
using Xunit;
using Xunit.Abstractions;

namespace FO4RecordEditor.Tests;





public class CellCompactionTests
{
    private readonly ITestOutputHelper _out;
    public CellCompactionTests(ITestOutputHelper output) => _out = output;

    private static (PluginToolExecutor exec, string plugin) BuildCellFixture()
    {
        var plugin = $"CellCompact_{Guid.NewGuid():N}.esp";
        var exec = new PluginToolExecutor(() => null);

        exec.Execute("create_plugin", $"{{\"name\":\"{plugin}\"}}").Should().Contain("Created new plugin");
        exec.Execute("create_cell", $"{{\"plugin\":\"{plugin}\",\"editorId\":\"CC_HoldingCell\"}}")
            .Should().Contain("CC_HoldingCell");
        exec.Execute("create_placed_object",
            $"{{\"plugin\":\"{plugin}\",\"cell\":\"CC_HoldingCell\",\"baseObject\":\"000010:Fallout4.esm\"," +
            "\"editorId\":\"CC_Marker\",\"persistent\":true}")
            .Should().Contain("CC_Marker");

        return (exec, plugin);
    }

    [Fact]
    public void CompactToEsl_OnAPluginWithACellAndPlacedObject()
    {
        var (exec, plugin) = BuildCellFixture();



        exec.Execute("create_record", $"{{\"plugin\":\"{plugin}\",\"type\":\"WEAP\",\"editorId\":\"CC_Weapon\"}}");
        var renum = exec.Execute("renumber_formid",
            $"{{\"plugin\":\"{plugin}\",\"record\":\"CC_Weapon\",\"new_id\":\"001500\"}}");
        _out.WriteLine("renumber_formid -> " + renum);

        var result = exec.ExecuteWithStatus("compact_to_esl", $"{{\"plugin\":\"{plugin}\"}}");
        _out.WriteLine("compact_to_esl -> isError=" + result.IsError + "\n" + result.Text);




        if (!result.IsError)
        {
            var dump = exec.Execute("list_records", $"{{\"plugin\":\"{plugin}\",\"type\":\"Weapon\"}}");
            _out.WriteLine("weapons after -> " + dump);
            dump.Should().NotContain("001500");
        }
    }




    [Fact]
    public void RenumberFormId_OnACellPlacedRecord_FailsLoudly()
    {
        var (exec, plugin) = BuildCellFixture();

        var result = exec.ExecuteWithStatus("renumber_formid",
            $"{{\"plugin\":\"{plugin}\",\"record\":\"CC_Marker\",\"new_id\":\"000900\"}}");
        _out.WriteLine("renumber_formid(REFR) -> isError=" + result.IsError + "\n" + result.Text);

        result.IsError.Should().BeTrue();
        result.Text.Should().Contain("Could not locate the group holding");


        var dump = exec.Execute("get_record", $"{{\"plugin\":\"{plugin}\",\"id\":\"CC_Marker\"}}");
        dump.Should().NotContain("000900");
    }




    [Fact]
    public void CompactToEsl_RefusesWhenAnOutOfRangeRecordIsCellPlaced()
    {
        var plugin = $"CellCompact_{Guid.NewGuid():N}.esp";
        var exec = new PluginToolExecutor(() => null);
        exec.Execute("create_plugin", $"{{\"name\":\"{plugin}\"}}");



        for (int i = 0; i < 0x800; i++)
            exec.Execute("create_record",
                $"{{\"plugin\":\"{plugin}\",\"type\":\"WEAP\",\"editorId\":\"CC_Fill{i}\"}}");
        exec.Execute("create_cell", $"{{\"plugin\":\"{plugin}\",\"editorId\":\"CC_Cell2\"}}");
        exec.Execute("create_placed_object",
            $"{{\"plugin\":\"{plugin}\",\"cell\":\"CC_Cell2\",\"baseObject\":\"000010:Fallout4.esm\"," +
            "\"editorId\":\"CC_HighMarker\",\"persistent\":true}");




        for (int i = 0; i < 8; i++)
            exec.Execute("delete_record", $"{{\"plugin\":\"{plugin}\",\"id\":\"CC_Fill{i}\"}}")
                .Should().Contain("Removed");

        var result = exec.ExecuteWithStatus("compact_to_esl", $"{{\"plugin\":\"{plugin}\"}}");
        _out.WriteLine("compact_to_esl -> isError=" + result.IsError + "\n" + result.Text);

        result.IsError.Should().BeTrue();
        result.Text.Should().Contain("nested groups");
        result.Text.Should().Contain("Nothing was modified");
    }
}
