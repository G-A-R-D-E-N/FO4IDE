using System.IO;
using FluentAssertions;
using FO4RecordEditor.Services;
using Xunit;
using Xunit.Abstractions;

namespace FO4RecordEditor.Tests;

// compact_to_esl / renumber_formid build their remap set from a deep EnumerateMajorRecords() walk
// (which includes CELLs and their PlacedObjects) but re-key by walking only top-level IGroup
// properties and casting each element to IMajorRecordGetter. Cell groups hold CellBlocks, not
// records. This fixture pins down what actually happens.
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

        // Push a top-level record out of the ESL range so compact_to_esl has work to do and must
        // walk the groups (including Cells).
        exec.Execute("create_record", $"{{\"plugin\":\"{plugin}\",\"type\":\"WEAP\",\"editorId\":\"CC_Weapon\"}}");
        var renum = exec.Execute("renumber_formid",
            $"{{\"plugin\":\"{plugin}\",\"record\":\"CC_Weapon\",\"new_id\":\"001500\"}}");
        _out.WriteLine("renumber_formid -> " + renum);

        var result = exec.ExecuteWithStatus("compact_to_esl", $"{{\"plugin\":\"{plugin}\"}}");
        _out.WriteLine("compact_to_esl -> isError=" + result.IsError + "\n" + result.Text);

        // Whatever the outcome, it must not be a silent wrong answer: either it compacts correctly,
        // or it reports a failure. A success message alongside surviving out-of-range records is the
        // one thing that must never happen.
        if (!result.IsError)
        {
            var dump = exec.Execute("list_records", $"{{\"plugin\":\"{plugin}\",\"type\":\"Weapon\"}}");
            _out.WriteLine("weapons after -> " + dump);
            dump.Should().NotContain("001500");
        }
    }

    // Resolved the open question: Fallout4Mod.Cells is Fallout4ListGroup<CellBlock>, which does NOT
    // implement IGroup, so the re-key loop never reaches cell-placed records. It does not throw --
    // it skips them silently. renumber_formid therefore cannot move one, and must say so.
    [Fact]
    public void RenumberFormId_OnACellPlacedRecord_FailsLoudly()
    {
        var (exec, plugin) = BuildCellFixture();

        var result = exec.ExecuteWithStatus("renumber_formid",
            $"{{\"plugin\":\"{plugin}\",\"record\":\"CC_Marker\",\"new_id\":\"000900\"}}");
        _out.WriteLine("renumber_formid(REFR) -> isError=" + result.IsError + "\n" + result.Text);

        result.IsError.Should().BeTrue();
        result.Text.Should().Contain("Could not locate the group holding");

        // The record must be untouched -- not duplicated onto the new id.
        var dump = exec.Execute("get_record", $"{{\"plugin\":\"{plugin}\",\"id\":\"CC_Marker\"}}");
        dump.Should().NotContain("000900");
    }

    // A CELL or PlacedObject outside 0x800-0xFFF is in compact_to_esl's remap set (deep walk) but
    // cannot be re-keyed (top-level walk). Before the guard, RemapLinks ran anyway and repointed
    // every reference onto FormKeys no record owned -- reported as a clean success.
    [Fact]
    public void CompactToEsl_RefusesWhenAnOutOfRangeRecordIsCellPlaced()
    {
        var plugin = $"CellCompact_{Guid.NewGuid():N}.esp";
        var exec = new PluginToolExecutor(() => null);
        exec.Execute("create_plugin", $"{{\"name\":\"{plugin}\"}}");

        // Fill 0x800-0xFFF with top-level records so the cell and its placed object are allocated
        // above the ESL range...
        for (int i = 0; i < 0x800; i++)
            exec.Execute("create_record",
                $"{{\"plugin\":\"{plugin}\",\"type\":\"WEAP\",\"editorId\":\"CC_Fill{i}\"}}");
        exec.Execute("create_cell", $"{{\"plugin\":\"{plugin}\",\"editorId\":\"CC_Cell2\"}}");
        exec.Execute("create_placed_object",
            $"{{\"plugin\":\"{plugin}\",\"cell\":\"CC_Cell2\",\"baseObject\":\"000010:Fallout4.esm\"," +
            "\"editorId\":\"CC_HighMarker\",\"persistent\":true}");

        // ...then free some low slots, so compaction is genuinely possible and we reach the
        // nested-group guard rather than the "won't fit the ESL range" capacity check.
        // delete_record takes 'id', not 'record' -- passing the wrong name binds to "" and no-ops.
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
