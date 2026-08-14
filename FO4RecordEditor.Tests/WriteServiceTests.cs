using System.IO;
using System.Linq;
using FluentAssertions;
using FO4RecordEditor.Services;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Fallout4;
using Newtonsoft.Json.Linq;
using Xunit;

namespace FO4RecordEditor.Tests;

public class WriteServiceTests
{
    [Fact]
    public void Author_Plugin_AddHolotape_SetName_Save_ReadBack()
    {
        var plugin = $"WriteTest_{Guid.NewGuid():N}.esp";
        var exec = new PluginToolExecutor(() => null);

        // create_plugin
        exec.Execute("create_plugin", $"{{\"name\":\"{plugin}\"}}")
            .Should().Contain("Created new plugin");

        // it shows up in list_plugins (mutable mod mirrored into the read tools)
        exec.Execute("list_plugins", "{}").Should().Contain(plugin);

        // create_record BOOK
        var createResult = exec.Execute("create_record",
            $"{{\"plugin\":\"{plugin}\",\"type\":\"BOOK\",\"editorId\":\"Test_Holotape\"}}");
        createResult.Should().Contain("Created BOOK").And.Contain("Test_Holotape");

        // set_field Name
        exec.Execute("set_field",
            $"{{\"plugin\":\"{plugin}\",\"record\":\"Test_Holotape\",\"field\":\"Name\",\"value\":\"My Test Tape\"}}")
            .Should().Contain("Set Name");

        // read it back through get_record (proves the read tools see the new record + field)
        var dump = exec.Execute("get_record", $"{{\"plugin\":\"{plugin}\",\"id\":\"Test_Holotape\"}}");
        dump.Should().Contain("Test_Holotape").And.Contain("My Test Tape");

        // save_plugin writes a real ESP
        var outPath = Path.Combine(Path.GetTempPath(), plugin);
        var saveResult = exec.Execute("save_plugin",
            $"{{\"plugin\":\"{plugin}\",\"path\":\"{outPath.Replace("\\", "\\\\")}\"}}");
        saveResult.Should().Contain("Saved");
        File.Exists(outPath).Should().BeTrue();

        try { File.Delete(outPath); } catch { /* overlay may still map it briefly */ }
    }

    [Fact]
    public void CreateRecord_InEsl_AssignsFormIdWithinLightRange()
    {
        var plugin = $"EslTest_{Guid.NewGuid():N}.esl";   // light plugin
        var exec = new PluginToolExecutor(() => null);
        exec.Execute("create_plugin", $"{{\"name\":\"{plugin}\"}}");

        var result = exec.Execute("create_record",
            $"{{\"plugin\":\"{plugin}\",\"type\":\"BOOK\",\"editorId\":\"EslHolotape\"}}");
        result.Should().Contain("Created BOOK");

        // FormKey in the message must be within the ESL object range 0x000800-0x000FFF.
        var dump = exec.Execute("get_record", $"{{\"plugin\":\"{plugin}\",\"id\":\"EslHolotape\"}}");
        var formKey = dump.Split('\n').First(l => l.TrimStart().StartsWith("FormKey:")).Split(':', 2)[1].Trim();
        var idHex = formKey.Split(':')[0];
        var id = Convert.ToUInt32(idHex, 16);
        id.Should().BeInRange(0x800u, 0xFFFu);
    }

    [Fact]
    public void AddListItem_AppendsKeywordToRecord()
    {
        var plugin = $"ListTest_{Guid.NewGuid():N}.esp";
        var exec = new PluginToolExecutor(() => null);
        exec.Execute("create_plugin", $"{{\"name\":\"{plugin}\"}}");
        exec.Execute("create_record", $"{{\"plugin\":\"{plugin}\",\"type\":\"KYWD\",\"editorId\":\"List_Kw\"}}");
        exec.Execute("create_record", $"{{\"plugin\":\"{plugin}\",\"type\":\"WEAP\",\"editorId\":\"List_Gun\"}}");

        var kwDump = exec.Execute("get_record", $"{{\"plugin\":\"{plugin}\",\"id\":\"List_Kw\"}}");
        var kwKey = kwDump.Split('\n').First(l => l.TrimStart().StartsWith("FormKey:")).Split(':', 2)[1].Trim();

        var result = exec.Execute("add_list_item",
            $"{{\"plugin\":\"{plugin}\",\"record\":\"List_Gun\",\"field\":\"Keywords\",\"value\":\"{kwKey}\"}}");
        result.Should().Contain("Added").And.Contain("Keywords");

        // Verify the actual data: save and reload, check the weapon's Keywords via Mutagen.
        var outPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), plugin);
        exec.Execute("save_plugin",
            $"{{\"plugin\":\"{plugin}\",\"path\":\"{outPath.Replace("\\", "\\\\")}\"}}");

        using var reload = Mutagen.Bethesda.Fallout4.Fallout4Mod.CreateFromBinaryOverlay(
            Mutagen.Bethesda.Plugins.ModPath.FromPath(outPath), Mutagen.Bethesda.Fallout4.Fallout4Release.Fallout4);
        var gun = reload.Weapons.First(w => w.EditorID == "List_Gun");
        gun.Keywords.Should().NotBeNull();
        gun.Keywords!.Select(k => k.FormKey.ID).Should().Contain(0x800u);

        try { System.IO.File.Delete(outPath); } catch { }
    }

    [Fact]
    public void AttachScript_AndSetObjectProperty_WiresHolotapeToTerminal()
    {
        var plugin = $"VmadTest_{Guid.NewGuid():N}.esp";
        var exec = new PluginToolExecutor(() => null);
        exec.Execute("create_plugin", $"{{\"name\":\"{plugin}\"}}");
        exec.Execute("create_record", $"{{\"plugin\":\"{plugin}\",\"type\":\"BOOK\",\"editorId\":\"VmadTape\"}}");
        exec.Execute("create_record", $"{{\"plugin\":\"{plugin}\",\"type\":\"TERM\",\"editorId\":\"VmadTerminal\"}}");

        exec.Execute("attach_script",
            $"{{\"plugin\":\"{plugin}\",\"record\":\"VmadTape\",\"script\":\"MyHolotapeProgram\"}}")
            .Should().Contain("Attached script");

        var termDump = exec.Execute("get_record", $"{{\"plugin\":\"{plugin}\",\"id\":\"VmadTerminal\"}}");
        var termKey = termDump.Split('\n').First(l => l.TrimStart().StartsWith("FormKey:")).Split(':', 2)[1].Trim();

        exec.Execute("set_script_property",
            $"{{\"plugin\":\"{plugin}\",\"record\":\"VmadTape\",\"script\":\"MyHolotapeProgram\",\"name\":\"Terminal\",\"value\":\"{termKey}\",\"type\":\"object\"}}")
            .Should().Contain("Set script property");

        // Save + reload, verify the script + object property persisted via Mutagen.
        var outPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), plugin);
        exec.Execute("save_plugin", $"{{\"plugin\":\"{plugin}\",\"path\":\"{outPath.Replace("\\", "\\\\")}\"}}");

        using var reload = Mutagen.Bethesda.Fallout4.Fallout4Mod.CreateFromBinaryOverlay(
            Mutagen.Bethesda.Plugins.ModPath.FromPath(outPath), Mutagen.Bethesda.Fallout4.Fallout4Release.Fallout4);
        var tape = reload.Books.First(b => b.EditorID == "VmadTape");
        tape.VirtualMachineAdapter.Should().NotBeNull();
        var script = tape.VirtualMachineAdapter!.Scripts.First(s => s.Name == "MyHolotapeProgram");
        var objProp = script.Properties.OfType<Mutagen.Bethesda.Fallout4.IScriptObjectPropertyGetter>()
            .First(p => p.Name == "Terminal");
        objProp.Object.FormKey.ID.Should().Be(0x801u);   // the terminal (second record)

        try { System.IO.File.Delete(outPath); } catch { }
    }

    [Fact]
    public void CleanPlugin_UndeletesAndDisablesDeletedRecords()
    {
        var plugin = $"CleanTest_{Guid.NewGuid():N}.esp";
        WriteService.CreatePlugin(plugin);
        WriteService.CreateRecord(plugin, "MISC", "Clean_Widget", null);

        // Mark the record deleted, then clean.
        var mod = WriteService.GetMutable(plugin)!;
        var rec = (Mutagen.Bethesda.Fallout4.IFallout4MajorRecord)mod.EnumerateMajorRecords()
            .First(r => r.EditorID == "Clean_Widget");
        rec.IsDeleted = true;

        var exec = new PluginToolExecutor(() => null);
        exec.Execute("clean_plugin", $"{{\"plugin\":\"{plugin}\"}}")
            .Should().Contain("undeleted + disabled 1");

        var after = mod.EnumerateMajorRecords().First(r => r.EditorID == "Clean_Widget");
        after.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public void CompactToEsl_ReNumbersRecords_AndFixesReferences()
    {
        var plugin = $"CompactTest_{Guid.NewGuid():N}.esp";
        WriteService.CreatePlugin(plugin);
        var mod = WriteService.GetMutable(plugin)!;
        var rel = Mutagen.Bethesda.Fallout4.Fallout4Release.Fallout4;

        // Two records with HIGH (non-ESL) FormIDs, the weapon referencing the keyword.
        var kw = new Mutagen.Bethesda.Fallout4.Keyword(new Mutagen.Bethesda.Plugins.FormKey(mod.ModKey, 0x5000), rel) { EditorID = "Hi_Kw" };
        mod.Keywords.Add(kw);
        var gun = new Mutagen.Bethesda.Fallout4.Weapon(new Mutagen.Bethesda.Plugins.FormKey(mod.ModKey, 0x5001), rel) { EditorID = "Hi_Gun" };
        gun.Keywords = new() { new Mutagen.Bethesda.Plugins.FormLink<Mutagen.Bethesda.Fallout4.IKeywordGetter>(kw.FormKey) };
        mod.Weapons.Add(gun);

        var exec = new PluginToolExecutor(() => null);
        exec.Execute("compact_to_esl", $"{{\"plugin\":\"{plugin}\"}}")
            .Should().Contain("remapped 2");

        // Save + reload, verify IDs are in range AND the reference was repointed.
        var outPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), plugin);
        exec.Execute("save_plugin", $"{{\"plugin\":\"{plugin}\",\"path\":\"{outPath.Replace("\\", "\\\\")}\"}}");

        using var reload = Mutagen.Bethesda.Fallout4.Fallout4Mod.CreateFromBinaryOverlay(
            Mutagen.Bethesda.Plugins.ModPath.FromPath(outPath), rel);
        var rkw = reload.Keywords.First(k => k.EditorID == "Hi_Kw");
        var rgun = reload.Weapons.First(w => w.EditorID == "Hi_Gun");
        rkw.FormKey.ID.Should().BeInRange(0x800u, 0xFFFu);
        rgun.FormKey.ID.Should().BeInRange(0x800u, 0xFFFu);
        // the weapon's keyword link must now point at the keyword's NEW id
        rgun.Keywords!.Select(k => k.FormKey).Should().Contain(rkw.FormKey);

        try { System.IO.File.Delete(outPath); } catch { }
    }

    [Fact]
    public void ListMasters_ReportsUsedAndUnusedMasters()
    {
        var plugin = $"ListMastersTest_{Guid.NewGuid():N}.esp";
        WriteService.CreatePlugin(plugin);
        var mod = WriteService.GetMutable(plugin)!;
        var rel = Mutagen.Bethesda.Fallout4.Fallout4Release.Fallout4;

        mod.MasterReferences.Add(new Mutagen.Bethesda.Plugins.Records.MasterReference { Master = "UsedMaster.esm" });
        mod.MasterReferences.Add(new Mutagen.Bethesda.Plugins.Records.MasterReference { Master = "UnusedMaster.esm" });

        // A record that links into UsedMaster.esm but never into UnusedMaster.esm.
        var gun = new Mutagen.Bethesda.Fallout4.Weapon(new Mutagen.Bethesda.Plugins.FormKey(mod.ModKey, 0x800), rel) { EditorID = "Lm_Gun" };
        gun.Keywords = new()
        {
            new Mutagen.Bethesda.Plugins.FormLink<Mutagen.Bethesda.Fallout4.IKeywordGetter>(
                new Mutagen.Bethesda.Plugins.FormKey("UsedMaster.esm", 0x001234))
        };
        mod.Weapons.Add(gun);

        var exec = new PluginToolExecutor(() => null);
        var result = exec.Execute("list_masters", $"{{\"plugin\":\"{plugin}\"}}");

        result.Should().Contain("UsedMaster.esm").And.Contain("UnusedMaster.esm");
        result.Split('\n').First(l => l.Contains("UsedMaster.esm")).Should().Contain("used").And.NotContain("UNUSED");
        result.Split('\n').First(l => l.Contains("UnusedMaster.esm")).Should().Contain("UNUSED");
    }

    [Fact]
    public void ListMastersJson_ReturnsStructuredRowsAndLightFlag()
    {
        var plugin = $"ListMastersJsonTest_{Guid.NewGuid():N}.esp";
        WriteService.CreatePlugin(plugin);
        var mod = WriteService.GetMutable(plugin)!;
        var rel = Mutagen.Bethesda.Fallout4.Fallout4Release.Fallout4;

        mod.MasterReferences.Add(new Mutagen.Bethesda.Plugins.Records.MasterReference { Master = "UsedMaster.esm" });
        mod.MasterReferences.Add(new Mutagen.Bethesda.Plugins.Records.MasterReference { Master = "UnusedMaster.esm" });
        var gun = new Mutagen.Bethesda.Fallout4.Weapon(new Mutagen.Bethesda.Plugins.FormKey(mod.ModKey, 0x800), rel) { EditorID = "Lmj_Gun" };
        gun.Keywords = new()
        {
            new Mutagen.Bethesda.Plugins.FormLink<Mutagen.Bethesda.Fallout4.IKeywordGetter>(
                new Mutagen.Bethesda.Plugins.FormKey("UsedMaster.esm", 0x001234))
        };
        mod.Weapons.Add(gun);
        mod.ModHeader.Flags |= Mutagen.Bethesda.Fallout4.Fallout4ModHeader.HeaderFlag.Small;

        var json = Newtonsoft.Json.Linq.JObject.Parse(WriteService.ListMastersJson(plugin, null));
        json["light"]!.Value<bool>().Should().BeTrue();
        var masters = (Newtonsoft.Json.Linq.JArray)json["masters"]!;
        masters.Should().HaveCount(2);

        var used = masters.First(m => m["name"]!.Value<string>() == "UsedMaster.esm");
        used["used"]!.Value<bool>().Should().BeTrue();
        used["index"]!.Value<int>().Should().Be(0);

        var unused = masters.First(m => m["name"]!.Value<string>() == "UnusedMaster.esm");
        unused["used"]!.Value<bool>().Should().BeFalse();
        unused["index"]!.Value<int>().Should().Be(1);
    }

    [Fact]
    public void ListMastersJson_PluginNotOpen_ReturnsJsonError()
    {
        var json = Newtonsoft.Json.Linq.JObject.Parse(WriteService.ListMastersJson("NoSuchPlugin.esp", null));
        json["error"].Should().NotBeNull();
    }

    [Fact]
    public void ReorderMasters_RejectsNonPermutation()
    {
        var plugin = $"ReorderRejectTest_{Guid.NewGuid():N}.esp";
        WriteService.CreatePlugin(plugin);
        var mod = WriteService.GetMutable(plugin)!;
        mod.MasterReferences.Add(new Mutagen.Bethesda.Plugins.Records.MasterReference { Master = "A.esm" });
        mod.MasterReferences.Add(new Mutagen.Bethesda.Plugins.Records.MasterReference { Master = "B.esm" });

        var exec = new PluginToolExecutor(() => null);

        // Missing a master.
        exec.Execute("reorder_masters", $"{{\"plugin\":\"{plugin}\",\"order\":[\"A.esm\"]}}")
            .Should().Contain("EXACTLY");

        // Extra name not in the current set.
        exec.Execute("reorder_masters", $"{{\"plugin\":\"{plugin}\",\"order\":[\"A.esm\",\"C.esm\"]}}")
            .Should().Contain("Not a declared master");

        // Duplicate.
        exec.Execute("reorder_masters", $"{{\"plugin\":\"{plugin}\",\"order\":[\"A.esm\",\"A.esm\"]}}")
            .Should().Contain("more than once");
    }

    [Fact]
    public void ReorderMasters_WritesExactOrder_BypassingLoadOrderDerivation()
    {
        var outDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"ReorderWriteTest_{Guid.NewGuid():N}");
        System.IO.Directory.CreateDirectory(outDir);
        var plugin = $"ReorderWriteTest_{Guid.NewGuid():N}.esp";
        var fullPath = System.IO.Path.Combine(outDir, plugin);

        var exec = new PluginToolExecutor(() => null);
        exec.Execute("create_plugin", $"{{\"name\":\"{fullPath.Replace("\\", "\\\\")}\"}}");
        var mod = WriteService.GetMutable(plugin)!;
        // Deliberately out of alphabetical order, so a silent re-sort would be caught.
        mod.MasterReferences.Add(new Mutagen.Bethesda.Plugins.Records.MasterReference { Master = "Zeta.esm" });
        mod.MasterReferences.Add(new Mutagen.Bethesda.Plugins.Records.MasterReference { Master = "Alpha.esm" });

        var result = exec.Execute("reorder_masters",
            $"{{\"plugin\":\"{plugin}\",\"order\":[\"Alpha.esm\",\"Zeta.esm\"]}}");
        result.Should().Contain("Wrote").And.Contain("Alpha.esm, Zeta.esm");

        var written = WriteService.ReadMasterNames(fullPath);
        written.Should().Equal("Alpha.esm", "Zeta.esm");

        try { System.IO.Directory.Delete(outDir, true); } catch { }
    }

    [Fact]
    public void SetLightFlag_SetsAndClears()
    {
        var plugin = $"LightFlagTest_{Guid.NewGuid():N}.esp";
        WriteService.CreatePlugin(plugin);
        var mod = WriteService.GetMutable(plugin)!;
        mod.ModHeader.Flags.HasFlag(Mutagen.Bethesda.Fallout4.Fallout4ModHeader.HeaderFlag.Small).Should().BeFalse();

        var exec = new PluginToolExecutor(() => null);
        exec.Execute("set_light_flag", $"{{\"plugin\":\"{plugin}\",\"light\":true}}")
            .Should().Contain("Set the ESL");
        mod.ModHeader.Flags.HasFlag(Mutagen.Bethesda.Fallout4.Fallout4ModHeader.HeaderFlag.Small).Should().BeTrue();

        exec.Execute("set_light_flag", $"{{\"plugin\":\"{plugin}\",\"light\":true}}")
            .Should().Contain("already has");

        exec.Execute("set_light_flag", $"{{\"plugin\":\"{plugin}\",\"light\":false}}")
            .Should().Contain("Cleared");
        mod.ModHeader.Flags.HasFlag(Mutagen.Bethesda.Fallout4.Fallout4ModHeader.HeaderFlag.Small).Should().BeFalse();
    }

    [Fact]
    public void SetLightFlag_WarnsWhenRecordsAreOutOfEslRange()
    {
        var plugin = $"LightFlagWarnTest_{Guid.NewGuid():N}.esp";
        WriteService.CreatePlugin(plugin);
        var mod = WriteService.GetMutable(plugin)!;
        var rel = Mutagen.Bethesda.Fallout4.Fallout4Release.Fallout4;
        mod.Keywords.Add(new Mutagen.Bethesda.Fallout4.Keyword(new Mutagen.Bethesda.Plugins.FormKey(mod.ModKey, 0x5000), rel) { EditorID = "Hi_Kw" });

        var exec = new PluginToolExecutor(() => null);
        exec.Execute("set_light_flag", $"{{\"plugin\":\"{plugin}\",\"light\":true}}")
            .Should().Contain("WARNING").And.Contain("compact_to_esl first");
    }

    [Fact]
    public void CreateRecord_UnsupportedType_ReturnsHelpfulMessage()
    {
        var exec = new PluginToolExecutor(() => null);
        exec.Execute("create_plugin", "{\"name\":\"UnsupTest.esp\"}");
        exec.Execute("create_record", "{\"plugin\":\"UnsupTest.esp\",\"type\":\"ZZZZ\",\"editorId\":\"x\"}")
            .Should().Contain("not supported");
    }

    [Fact]
    public void SetField_Model_SetsNifPath()
    {
        var plugin = $"ModelTest_{Guid.NewGuid():N}.esp";
        var exec = new PluginToolExecutor(() => null);
        exec.Execute("create_plugin", $"{{\"name\":\"{plugin}\"}}");
        exec.Execute("create_record", $"{{\"plugin\":\"{plugin}\",\"type\":\"MISC\",\"editorId\":\"ModelWidget\"}}");

        var nif = "SetDressing\\\\Radio\\\\Radio1.nif";
        exec.Execute("set_field",
            $"{{\"plugin\":\"{plugin}\",\"record\":\"ModelWidget\",\"field\":\"Model\",\"value\":\"{nif}\"}}")
            .Should().Contain("Set Model");

        var dump = exec.Execute("get_record", $"{{\"plugin\":\"{plugin}\",\"id\":\"ModelWidget\"}}");
        dump.Should().Contain("Radio1.nif");
    }

    [Fact]
    public void SetField_FormLink_ByFormKey_WiresRecordsTogether()
    {
        var plugin = $"LinkTest_{Guid.NewGuid():N}.esp";
        var exec = new PluginToolExecutor(() => null);
        exec.Execute("create_plugin", $"{{\"name\":\"{plugin}\"}}");

        // A misc item to be the crafted output...
        exec.Execute("create_record", $"{{\"plugin\":\"{plugin}\",\"type\":\"MISC\",\"editorId\":\"Link_Widget\"}}");
        // ...and a constructible object whose CreatedObject FormLink points at it.
        exec.Execute("create_record", $"{{\"plugin\":\"{plugin}\",\"type\":\"COBJ\",\"editorId\":\"Link_Recipe\"}}");

        // Find the widget's FormKey from its dump.
        var widgetDump = exec.Execute("get_record", $"{{\"plugin\":\"{plugin}\",\"id\":\"Link_Widget\"}}");
        var formKey = widgetDump.Split('\n')
            .First(l => l.TrimStart().StartsWith("FormKey:")).Split(':', 2)[1].Trim();

        // Set the FormLink field by FormKey.
        var setResult = exec.Execute("set_field",
            $"{{\"plugin\":\"{plugin}\",\"record\":\"Link_Recipe\",\"field\":\"CreatedObject\",\"value\":\"{formKey}\"}}");
        setResult.Should().Contain("Set CreatedObject");

        // Confirm the link now resolves to the widget's FormKey in the recipe dump.
        var recipeDump = exec.Execute("get_record", $"{{\"plugin\":\"{plugin}\",\"id\":\"Link_Recipe\"}}");
        recipeDump.Should().Contain(formKey);
    }

    [Fact]
    public void RenumberFormId_ChangesId_AndFixesInPluginReferences()
    {
        var plugin = $"RenumTest_{Guid.NewGuid():N}.esp";
        WriteService.CreatePlugin(plugin);
        var mod = WriteService.GetMutable(plugin)!;
        var rel = Mutagen.Bethesda.Fallout4.Fallout4Release.Fallout4;

        // A keyword at 0x000801 and a weapon that links it.
        var kw = new Mutagen.Bethesda.Fallout4.Keyword(
            new Mutagen.Bethesda.Plugins.FormKey(mod.ModKey, 0x801), rel) { EditorID = "Renum_Kw" };
        mod.Keywords.Add(kw);
        var gun = new Mutagen.Bethesda.Fallout4.Weapon(
            new Mutagen.Bethesda.Plugins.FormKey(mod.ModKey, 0x802), rel) { EditorID = "Renum_Gun" };
        gun.Keywords = new() {
            new Mutagen.Bethesda.Plugins.FormLink<Mutagen.Bethesda.Fallout4.IKeywordGetter>(kw.FormKey) };
        mod.Weapons.Add(gun);

        // Renumber the keyword 0x801 -> 0x0F0F.
        WriteService.RenumberFormId(plugin, "Renum_Kw", "000F0F", null)
            .Should().Contain("Renumbered").And.Contain("000F0F");

        // Save + reload, assert the new id AND that the weapon's link followed.
        var outPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), plugin);
        WriteService.SavePlugin(plugin, outPath, null);   // env: null -- none in tests
        using var reload = Mutagen.Bethesda.Fallout4.Fallout4Mod.CreateFromBinaryOverlay(
            Mutagen.Bethesda.Plugins.ModPath.FromPath(outPath), rel);
        var rkw = reload.Keywords.First(k => k.EditorID == "Renum_Kw");
        var rgun = reload.Weapons.First(w => w.EditorID == "Renum_Gun");
        rkw.FormKey.ID.Should().Be(0x0F0Fu);
        rgun.Keywords!.Select(k => k.FormKey).Should().Contain(rkw.FormKey);

        try { System.IO.File.Delete(outPath); } catch { }
    }

    [Fact]
    public void RenumberFormId_TargetIdInUse_Rejected()
    {
        var plugin = $"RenumDup_{Guid.NewGuid():N}.esp";
        WriteService.CreatePlugin(plugin);
        var mod = WriteService.GetMutable(plugin)!;
        var rel = Mutagen.Bethesda.Fallout4.Fallout4Release.Fallout4;
        mod.Keywords.Add(new Mutagen.Bethesda.Fallout4.Keyword(
            new Mutagen.Bethesda.Plugins.FormKey(mod.ModKey, 0x801), rel) { EditorID = "Dup_A" });
        mod.Keywords.Add(new Mutagen.Bethesda.Fallout4.Keyword(
            new Mutagen.Bethesda.Plugins.FormKey(mod.ModKey, 0x802), rel) { EditorID = "Dup_B" });

        WriteService.RenumberFormId(plugin, "Dup_A", "000802", null)
            .Should().Contain("already used");
    }

    [Fact]
    public void SetPlacedReferenceTransform_MovesReferenceIntoOverrideInPatchPlugin()
    {
        // The reference lives in a "source" plugin (like a real cell would in a vanilla/mod master);
        // the move is saved into a SEPARATE patch plugin, the same shape as the real Cell Viewer
        // gizmo scenario (moves never write directly into the plugin that defines the reference).
        var srcPlugin = $"GizmoSrc_{Guid.NewGuid():N}.esp";
        var patchPlugin = $"GizmoPatch_{Guid.NewGuid():N}.esp";
        var exec = new PluginToolExecutor(() => null);

        exec.Execute("create_plugin", $"{{\"name\":\"{srcPlugin}\"}}");
        exec.Execute("create_record", $"{{\"plugin\":\"{srcPlugin}\",\"type\":\"STAT\",\"editorId\":\"Gizmo_Base\"}}");
        exec.Execute("create_cell", $"{{\"plugin\":\"{srcPlugin}\",\"editorId\":\"Gizmo_Cell\"}}");
        var placeResult = exec.Execute("create_placed_object",
            $"{{\"plugin\":\"{srcPlugin}\",\"cell\":\"Gizmo_Cell\",\"baseObject\":\"Gizmo_Base\"," +
            "\"x\":100,\"y\":200,\"z\":300,\"rotZ\":0}");
        placeResult.Should().Contain("Created REFR");

        var src = WriteService.GetMutable(srcPlugin)!;
        var refr = src.EnumerateMajorRecords().OfType<IPlacedObjectGetter>().First();
        var fk = refr.FormKey;

        var savedCache = MutagenLoader.LinkCache;
        try
        {
            MutagenLoader.LinkCache = ((IFallout4ModGetter)src).ToMutableLinkCache<IFallout4Mod, IFallout4ModGetter>();

            var result = WriteService.SetPlacedReferenceTransform(
                null, fk.ToString(), patchPlugin, 111f, 222f, 333f, 0.1f, 0.2f, 0.3f);
            result.Should().Contain("Moved").And.Contain(patchPlugin);

            // The SOURCE plugin's own copy must be untouched -- the move only exists as an override.
            var srcRefr = src.EnumerateMajorRecords().OfType<IPlacedObjectGetter>().First(r => r.FormKey == fk);
            srcRefr.Position.X.Should().Be(100f);

            var patch = WriteService.GetMutable(patchPlugin)!;
            var ovr = patch.EnumerateMajorRecords().OfType<IPlacedObjectGetter>().First(r => r.FormKey == fk);
            ovr.Position.X.Should().Be(111f);
            ovr.Position.Y.Should().Be(222f);
            ovr.Position.Z.Should().Be(333f);
            ovr.Rotation.X.Should().BeApproximately(0.1f, 0.0001f);
            ovr.Rotation.Y.Should().BeApproximately(0.2f, 0.0001f);
            ovr.Rotation.Z.Should().BeApproximately(0.3f, 0.0001f);

            // Save + reload the patch, prove it round-trips through a real ESP, not just in memory.
            var outPath = Path.Combine(Path.GetTempPath(), patchPlugin);
            WriteService.SavePlugin(patchPlugin, outPath, null);
            using var reload = Mutagen.Bethesda.Fallout4.Fallout4Mod.CreateFromBinaryOverlay(
                Mutagen.Bethesda.Plugins.ModPath.FromPath(outPath), Mutagen.Bethesda.Fallout4.Fallout4Release.Fallout4);
            var reloadedRefr = reload.EnumerateMajorRecords().OfType<IPlacedObjectGetter>().First(r => r.FormKey == fk);
            reloadedRefr.Position.X.Should().Be(111f);

            try { File.Delete(outPath); } catch { }
        }
        finally
        {
            MutagenLoader.LinkCache = savedCache;
        }
    }

    [Fact]
    public void SetPlacedReferenceTransform_NoLinkCache_ReturnsFriendlyError()
    {
        var savedCache = MutagenLoader.LinkCache;
        try
        {
            MutagenLoader.LinkCache = null;
            WriteService.SetPlacedReferenceTransform(null, "000801:Fallout4.esm", "AnyPatch.esp",
                0, 0, 0, 0, 0, 0).Should().Contain("No environment loaded");
        }
        finally { MutagenLoader.LinkCache = savedCache; }
    }

    [Fact]
    public void RenumberFormIdTool_ChangesId()
    {
        var plugin = $"RenumTool_{Guid.NewGuid():N}.esp";
        var exec = new PluginToolExecutor(() => null);
        exec.Execute("create_plugin", $"{{\"name\":\"{plugin}\"}}");
        exec.Execute("create_record", $"{{\"plugin\":\"{plugin}\",\"type\":\"MISC\",\"editorId\":\"Renum_Widget\"}}");

        exec.Execute("renumber_formid",
            $"{{\"plugin\":\"{plugin}\",\"record\":\"Renum_Widget\",\"new_id\":\"000ABC\"}}")
            .Should().Contain("Renumbered").And.Contain("000ABC");

        exec.Execute("get_record", $"{{\"plugin\":\"{plugin}\",\"id\":\"Renum_Widget\"}}")
            .Should().Contain("ABC:");   // FormKey line now shows the new object id
    }
}
