using FluentAssertions;
using FO4RecordEditor.Services;
using Xunit;

namespace FO4RecordEditor.Tests;

public class PluginToolExecutorTests
{

    [Fact]
    public void ListPlugins_NoneLoaded_ReturnsFriendlyMessage()
    {

        FO4RecordEditor.Services.MutagenLoader.LooseMods.Clear();
        FO4RecordEditor.Services.MutagenLoader.EditableMods.Clear();
        var exec = new PluginToolExecutor(() => null);
        exec.Execute("list_plugins", "{}").Should().Be("No plugins loaded.");
    }

    [Fact]
    public void UnknownTool_ReturnsUnknownMessage()
    {
        var exec = new PluginToolExecutor(() => null);
        exec.Execute("does_not_exist", "{}").Should().StartWith("Unknown tool");
    }

    [Fact]
    public void Execute_ParsesArgs_WithoutThrowing()
    {

        FO4RecordEditor.Services.MutagenLoader.LooseMods.Clear();
        FO4RecordEditor.Services.MutagenLoader.EditableMods.Clear();
        var exec = new PluginToolExecutor(() => null);

        exec.Execute("list_record_types", "{\"plugin\":\"Missing.esp\"}")
            .Should().Contain("not loaded");

        exec.Execute("get_record", "{\"plugin\":\"Missing.esp\",\"id\":\"001234:Missing.esp\"}")
            .Should().Contain("not found").And.Contain("Loaded plugins: none");
    }

    [Fact]
    public void ToolDefinitions_ExposesReadAndWriteTools()
    {

        var expected = new[]
        {
            "list_plugins", "list_record_types", "list_records", "list_records_summary",
            "search_records", "search_all", "resolve_editorid", "get_scripts", "diff_records",
            "get_record", "get_conflicts", "get_winning_record", "search_robco_configs",
            "scan_conflicts", "create_mod_group", "update_mod_group", "delete_mod_group",
            "list_mod_groups", "resolve_asset", "get_referenced_by", "get_problems", "scan_broken_refs",
            "scan_all_plugins", "open_plugin", "create_plugin", "create_record", "create_cell",
            "create_placed_object", "disable_previs", "set_field", "add_list_item", "attach_script",
            "set_script_property", "check_plugin", "compact_to_esl", "check_esl_eligibility",
            "renumber_formid",
            "clean_plugin", "backup_plugin", "save_plugin", "copy_as_override", "copy_as_new_record",
            "remove_identical_to_master", "create_merged_patch", "get_conditions",
            "deep_copy_as_override", "change_referencing_records", "element_add", "element_remove",
            "element_move", "element_clear", "element_describe", "set_conditions_at", "add_masters",
            "renumber_plugin_formids", "create_seq_file", "check_circular_leveled_lists",
            "delete_record",
            "remove_list_item", "set_components", "set_conditions", "add_leveled_entry",
            "set_perk_effects", "set_magic_effects", "set_quest_aliases", "set_quest_stages",
            "set_quest_objectives", "set_furniture_markers", "set_message_buttons",
            "compile_papyrus", "decompile_papyrus", "revert_overrides", "batch_patch_records",
            "run_script", "reload_plugin", "strip_masters_clean", "list_masters", "reorder_masters",
            "set_light_flag", "set_localized_flag", "nif_import", "nif_inspect", "nif_verify",
            "nif_fix",
            "archive_list", "archive_extract", "archive_extract_all",
            "papyrus_function_lookup", "papyrus_script_info", "papyrus_check",
            "graph_validate", "graph_compile", "graph_palette_search", "graph_node_info",
            "papyrus_outline", "papyrus_definition",
            "bgsm_inspect", "bgsm_set_field", "catalog_mod_folder", "audit_asset_usage",
            "audio_convert_to_xwm", "audio_convert_from_xwm", "audio_make_fuz", "audio_extract_fuz",
            "archive_pack", "cell_get_placed_references", "cell_search",
            "cleanup_placed_references", "precombine_plan",
        };

        var names = PluginToolExecutor.ToolDefinitions()
            .Select(t => (string)t.GetType().GetProperty("name")!.GetValue(t)!).ToList();

        names.Should().BeEquivalentTo(expected);
        PluginToolExecutor.McpToolDefinitions().Should().HaveCount(expected.Length);
    }
}
