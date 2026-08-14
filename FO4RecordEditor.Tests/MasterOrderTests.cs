using System.IO;
using FluentAssertions;
using FO4RecordEditor.Services;
using Xunit;

namespace FO4RecordEditor.Tests;

// Commit 8aa6b43 made SavePlugin order MAST by the real load order -- writing a dependent ESM
// before its dependency makes Fallout 4 hang on load with no crash log. Four internal callers
// (SaveRecordEditsToPatch, RevertOverridesFrom, BatchPatchRecords, SaveScriptPatch) kept calling
// the 2-arg overload, so batch_patch_records and run_script still emitted unordered masters.
public class MasterOrderTests
{
    // Builds a plugin whose records reference two different masters, so the MAST table has an
    // order that can be wrong. Returns the saved path.
    private static (PluginToolExecutor exec, string plugin, string path) BuildMultiMasterPlugin()
    {
        var plugin = $"MasterOrder_{Guid.NewGuid():N}.esp";
        var exec = new PluginToolExecutor(() => null);

        exec.Execute("create_plugin", $"{{\"name\":\"{plugin}\"}}")
            .Should().Contain("Created new plugin");
        exec.Execute("create_record",
            $"{{\"plugin\":\"{plugin}\",\"type\":\"WEAP\",\"editorId\":\"MO_TestWeapon\"}}")
            .Should().Contain("Created WEAP");

        // Two FormLinks into two different masters -> two MAST entries.
        exec.Execute("add_list_item",
            $"{{\"plugin\":\"{plugin}\",\"record\":\"MO_TestWeapon\",\"field\":\"Keywords\"," +
            "\"value\":\"0965E0:Fallout4.esm\"}");
        exec.Execute("add_list_item",
            $"{{\"plugin\":\"{plugin}\",\"record\":\"MO_TestWeapon\",\"field\":\"Keywords\"," +
            "\"value\":\"000800:DLCCoast.esm\"}");

        var path = Path.Combine(Path.GetTempPath(), plugin);
        return (exec, plugin, path);
    }

    [Fact]
    public void ReadMasterNames_ReportsWhatWasActuallyWritten()
    {
        var (exec, plugin, path) = BuildMultiMasterPlugin();
        try
        {
            exec.Execute("save_plugin",
                $"{{\"plugin\":\"{plugin}\",\"path\":\"{path.Replace("\\", "\\\\")}\"}}");

            File.Exists(path).Should().BeTrue();
            var masters = WriteService.ReadMasterNames(path);
            masters.Should().Contain("Fallout4.esm");
            masters.Should().Contain("DLCCoast.esm");
        }
        finally { try { File.Delete(path); } catch { } }
    }

    // Without an env the ordering silently degraded and the save still reported plain success.
    [Fact]
    public void SaveWithoutEnv_WarnsThatMasterOrderWasNotSet()
    {
        var (exec, plugin, path) = BuildMultiMasterPlugin();
        try
        {
            var result = exec.Execute("save_plugin",
                $"{{\"plugin\":\"{plugin}\",\"path\":\"{path.Replace("\\", "\\\\")}\"}}");

            WriteService.ReadMasterNames(path).Count.Should().BeGreaterThan(1);
            result.Should().Contain("master order was NOT set from the load order");
        }
        finally { try { File.Delete(path); } catch { } }
    }

    // A single-master plugin has nothing to mis-order, so it must not carry the scary warning.
    [Fact]
    public void SingleMasterPlugin_IsNotWarnedAbout()
    {
        var plugin = $"MasterOrder_{Guid.NewGuid():N}.esp";
        var exec = new PluginToolExecutor(() => null);
        exec.Execute("create_plugin", $"{{\"name\":\"{plugin}\"}}");
        exec.Execute("create_record",
            $"{{\"plugin\":\"{plugin}\",\"type\":\"WEAP\",\"editorId\":\"MO_Solo\"}}");
        exec.Execute("add_list_item",
            $"{{\"plugin\":\"{plugin}\",\"record\":\"MO_Solo\",\"field\":\"Keywords\"," +
            "\"value\":\"0965E0:Fallout4.esm\"}");

        var path = Path.Combine(Path.GetTempPath(), plugin);
        try
        {
            var result = exec.Execute("save_plugin",
                $"{{\"plugin\":\"{plugin}\",\"path\":\"{path.Replace("\\", "\\\\")}\"}}");

            WriteService.ReadMasterNames(path).Should().HaveCount(1);
            result.Should().NotContain("master order was NOT set");
        }
        finally { try { File.Delete(path); } catch { } }
    }
}
