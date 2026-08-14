using System.Collections;
using System.IO;
using System.Text.Json;
using Mutagen.Bethesda.Fallout4;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Noggog;

namespace FO4RecordEditor.Services;

/// <summary>
/// Structured-record authoring that the generic <see cref="WriteService.SetField"/> /
/// <see cref="WriteService.AddListItem"/> path cannot express, because the target lists hold
/// STRUCTS (not FormLinks): leveled-list entries, perk entry-point effects, quest
/// stages/objectives/aliases, and spell/enchantment magic-effect chains. Each method mirrors the
/// JSON + reflection style of <see cref="WriteService.SetConditions"/> and reuses the shared
/// helpers (EnsureOpen / FindMutableRecord / ResolveFk / ApplyParam / ParseOperator).
/// </summary>
public static partial class WriteService
{
    // ── run_script escape hatch: create a brand-new record from a patch script ───────────────────
    /// <summary>Create a new record (any signature create_record supports) in the patch plugin and
    /// return the mutable instance, so a run_script can author NEW records (not just override
    /// existing ones). Mirrors OverrideForScript: opens/creates the patch plugin on first use.</summary>
    public static IFallout4MajorRecord? CreateForScript(string patchPlugin, object? env, string sig, string editorId)
    {
        var (name, path) = NormalizePlugin(patchPlugin);
        if (GetMutable(name) == null)
        {
            bool existsOnDisk = (path != null && File.Exists(path))
                || MutagenLoader.LooseModPaths.ContainsKey(name)
                || FindPluginPath(name, env) != null;
            if (existsOnDisk) OpenPlugin(patchPlugin, env); else CreatePlugin(patchPlugin);
        }
        var mod = GetMutable(name);
        if (mod == null) return null;
        var fk = NextFreeFormKey(mod);
        if (fk == null) return null;
        var rec = AddNewBySig(mod, sig, editorId, fk.Value);
        if (rec != null) { MutagenLoader.InvalidateModIndex(name); NotifyChanged(name); }
        return rec;
    }

    // ── leveled list entries (LVLI / LVLN) ───────────────────────────────────────────────────────
    /// <summary>Append a weighted entry (reference + level + count + chance-none) to a LeveledItem
    /// or LeveledNpc. These lists are structs, so add_list_item cannot build them.</summary>
    public static string AddLeveledEntry(string plugin, string recordId, string reference,
        int level, int count, double chanceNonePercent, object? env)
    {
        var mod = EnsureOpen(plugin, env, out var openMsg); if (mod == null) return openMsg;
        var rec = FindMutableRecord(mod, recordId); if (rec == null) return ToolError.Fail($"Record '{recordId}' not found in {plugin}.");
        if (!ResolveFk(env, reference, out var fk)) return $"Could not resolve reference '{reference}'.";

        var chance = new Percent(Math.Clamp(chanceNonePercent, 0, 100) / 100.0);
        // Leveled entries store level/count as Int16; clamp so large values don't wrap negative.
        short lvl = (short)Math.Clamp(level, 1, short.MaxValue);
        short cnt = (short)Math.Clamp(count, 1, short.MaxValue);
        try
        {
            switch (rec)
            {
                case LeveledItem li:
                {
                    var e = new LeveledItemEntry { Data = new LeveledItemEntryData
                        { Level = lvl, Count = cnt, ChanceNone = chance } };
                    e.Data.Reference.SetTo(fk);
                    (li.Entries ??= new ExtendedList<LeveledItemEntry>()).Add(e);
                    break;
                }
                case LeveledNpc ln:
                {
                    var e = new LeveledNpcEntry { Data = new LeveledNpcEntryData
                        { Level = lvl, Count = cnt, ChanceNone = chance } };
                    e.Data.Reference.SetTo(fk);
                    (ln.Entries ??= new ExtendedList<LeveledNpcEntry>()).Add(e);
                    break;
                }
                default:
                    return $"add_leveled_entry requires an LVLI or LVLN record, got {rec.GetType().Name}.";
            }
        }
        catch (Exception ex) { return $"Failed to add leveled entry: {ex.Message}"; }

        MutagenLoader.InvalidateModIndex(plugin); NotifyChanged(plugin);
        return $"Added leveled entry ({reference} lvl{level} x{count}) to {recordId} in {plugin}. save_plugin to persist.";
    }

    // ── perk effects (PERK) ──────────────────────────────────────────────────────────────────────
    /// <summary>Replace a perk's Effects list. JSON array of objects, each with "kind":
    /// "ability" {ability}, "modifyValue" {entryPoint, modification:Set|Add|Multiply, value},
    /// or "activateChoice" {buttonLabel, spell?, entryPoint?=Activate}. Common optional fields:
    /// rank, priority, and conditions (same condition objects as set_conditions, plus optional
    /// tabIndex). Entry-point names are APerkEntryPointEffect.EntryType values (e.g. Activate,
    /// CalculateMyCriticalHitChance, ModAttackDamage).</summary>
    public static string SetPerkEffects(string plugin, string recordId, string json, object? env)
    {
        var mod = EnsureOpen(plugin, env, out var openMsg); if (mod == null) return openMsg;
        var rec = FindMutableRecord(mod, recordId); if (rec == null) return ToolError.Fail($"Record '{recordId}' not found in {plugin}.");
        if (rec is not Perk perk) return $"set_perk_effects requires a PERK record, got {rec.GetType().Name}.";

        var built = new List<APerkEffect>();
        var failures = new List<string>();
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "[]" : json);
            int idx = 0;
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                idx++;
                var kind = (el.TryGetProperty("kind", out var k) ? k.GetString() : null)?.ToLowerInvariant() ?? "";
                APerkEffect? eff = null;
                switch (kind)
                {
                    case "ability":
                    {
                        // An ability effect with no resolvable ability does nothing in-game; refuse it
                        // rather than silently adding a dead effect.
                        if (!el.TryGetProperty("ability", out var abEl) || abEl.GetString() is not { } abStr
                            || !ResolveFk(env, abStr, out var abFk))
                        { failures.Add($"#{idx}: 'ability' kind needs a resolvable 'ability'"); continue; }
                        var ab = new PerkAbilityEffect();
                        ab.Ability.SetTo(abFk);
                        eff = ab;
                        break;
                    }
                    case "modifyvalue":
                    {
                        var mv = new PerkEntryPointModifyValue();
                        if (!TrySetEntryPoint(mv, el, failures, idx)) continue;
                        var modName = el.TryGetProperty("modification", out var mEl) ? mEl.GetString() : "Add";
                        if (Enum.TryParse<PerkEntryPointModifyValue.ModificationType>(modName, true, out var modT))
                            mv.Modification = modT;
                        if (el.TryGetProperty("value", out var vEl) && vEl.TryGetSingle(out var vf)) mv.Value = vf;
                        eff = mv;
                        break;
                    }
                    case "activatechoice":
                    {
                        var ac = new PerkEntryPointAddActivateChoice();
                        // default entry point for an Activate-choice perk is "Activate"
                        if (!el.TryGetProperty("entryPoint", out _))
                            ac.EntryPoint = APerkEntryPointEffect.EntryType.Activate;
                        else if (!TrySetEntryPoint(ac, el, failures, idx)) continue;
                        if (el.TryGetProperty("buttonLabel", out var blEl) && blEl.GetString() is { } bl)
                            ac.ButtonLabel = bl;
                        // 'spell' is optional, but if given it must resolve -- don't silently drop it.
                        if (el.TryGetProperty("spell", out var spEl) && spEl.GetString() is { Length: > 0 } spStr)
                        {
                            if (!ResolveFk(env, spStr, out var spFk))
                            { failures.Add($"#{idx}: unresolved spell '{spStr}'"); continue; }
                            ac.Spell.SetTo(spFk);
                        }
                        eff = ac;
                        break;
                    }
                    default:
                        failures.Add($"#{idx}: unknown kind '{kind}' (use ability|modifyValue|activateChoice)");
                        continue;
                }

                eff.Rank = (byte)JInt(el, "rank", 0);
                eff.Priority = (byte)JInt(el, "priority", 0);

                // conditions: each wrapped in a PerkCondition (tabIndex groups them)
                var tabs = new HashSet<byte>();
                if (el.TryGetProperty("conditions", out var condArr) && condArr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var c in condArr.EnumerateArray())
                    {
                        var cond = BuildConditionFromJson(c, env, out var cerr);
                        if (cond == null) { failures.Add($"#{idx} cond: {cerr}"); continue; }
                        byte tab = (byte)JInt(c, "tabIndex", 0);
                        var pc = new PerkCondition { RunOnTabIndex = tab };
                        pc.Conditions.Add(cond);
                        eff.Conditions.Add(pc);
                        tabs.Add(tab);
                    }
                }
                if (eff is APerkEntryPointEffect ep) ep.PerkConditionTabCount = (byte)tabs.Count;

                built.Add(eff);
            }
        }
        catch (Exception ex) { return $"Could not parse perk effects JSON: {ex.Message}."; }

        perk.Effects.Clear();
        foreach (var e in built) perk.Effects.Add(e);
        MutagenLoader.InvalidateModIndex(plugin); NotifyChanged(plugin);
        var msg = $"Set {built.Count} perk effect(s) on {recordId} in {plugin}.";
        if (failures.Count > 0) msg += $" Skipped {failures.Count}: {string.Join("; ", failures)}.";
        return msg + " save_plugin to persist.";
    }

    private static bool TrySetEntryPoint(APerkEntryPointEffect eff, JsonElement el, List<string> failures, int idx)
    {
        var name = el.TryGetProperty("entryPoint", out var epEl) ? epEl.GetString() : null;
        if (string.IsNullOrWhiteSpace(name)) { failures.Add($"#{idx}: missing entryPoint"); return false; }
        if (!Enum.TryParse<APerkEntryPointEffect.EntryType>(name, true, out var ep))
        { failures.Add($"#{idx}: unknown entryPoint '{name}'"); return false; }
        eff.EntryPoint = ep;
        return true;
    }

    // ── spell / enchantment magic-effect chains (SPEL / ENCH) ────────────────────────────────────
    /// <summary>Replace a Spell/ObjectEffect(ENCH) Effects list. JSON array of objects:
    /// {effect:"&lt;MGEF&gt;", magnitude?, area?, duration?, conditions?:[...]}.</summary>
    public static string SetMagicEffects(string plugin, string recordId, string json, object? env)
    {
        var mod = EnsureOpen(plugin, env, out var openMsg); if (mod == null) return openMsg;
        var rec = FindMutableRecord(mod, recordId); if (rec == null) return ToolError.Fail($"Record '{recordId}' not found in {plugin}.");
        // Restrict to Spell/ObjectEffect(ENCH) -- other records (ALCH/INGR/...) also expose an Effects
        // list, and we don't want a mistyped record to silently clobber them.
        if (rec is not (Spell or ObjectEffect))
            return $"set_magic_effects requires a SPEL or ENCH record, got {rec.GetType().Name}.";

        var effectsProp = rec.GetType().GetProperty("Effects");
        if (effectsProp?.GetValue(rec) is not IList effList)
            return $"set_magic_effects requires a SPEL or ENCH record with an Effects list, got {rec.GetType().Name}.";

        var built = new List<Effect>();
        var failures = new List<string>();
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "[]" : json);
            int idx = 0;
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                idx++;
                var ef = new Effect { Data = new EffectData
                    { Magnitude = JFloat(el, "magnitude", 0f), Area = JInt(el, "area", 0), Duration = JInt(el, "duration", 0) } };
                if (el.TryGetProperty("effect", out var meEl) && meEl.GetString() is { } meStr && ResolveFk(env, meStr, out var meFk))
                    ef.BaseEffect.SetTo(meFk);
                else failures.Add($"#{idx}: missing/unresolved effect (MGEF)");

                if (el.TryGetProperty("conditions", out var condArr) && condArr.ValueKind == JsonValueKind.Array)
                    foreach (var c in condArr.EnumerateArray())
                    {
                        var cond = BuildConditionFromJson(c, env, out var cerr);
                        if (cond == null) { failures.Add($"#{idx} cond: {cerr}"); continue; }
                        ef.Conditions.Add(cond);
                    }
                built.Add(ef);
            }
        }
        catch (Exception ex) { return $"Could not parse magic effects JSON: {ex.Message}."; }

        effList.Clear();
        foreach (var e in built) effList.Add(e);
        MutagenLoader.InvalidateModIndex(plugin); NotifyChanged(plugin);
        var msg = $"Set {built.Count} magic effect(s) on {recordId} in {plugin}.";
        if (failures.Count > 0) msg += $" Skipped {failures.Count}: {string.Join("; ", failures)}.";
        return msg + " save_plugin to persist.";
    }

    // ── quest internals (QUST) ───────────────────────────────────────────────────────────────────
    /// <summary>Replace a quest's reference aliases. JSON array: {id:int, name, forcedReference?,
    /// uniqueActor?, flags?}. Currently builds QuestReferenceAlias (the common forced/unique case,
    /// e.g. a player alias with forcedReference "000014:Fallout4.esm").</summary>
    public static string SetQuestAliases(string plugin, string recordId, string json, object? env)
    {
        var mod = EnsureOpen(plugin, env, out var openMsg); if (mod == null) return openMsg;
        var rec = FindMutableRecord(mod, recordId); if (rec == null) return ToolError.Fail($"Record '{recordId}' not found in {plugin}.");
        if (rec is not Quest q) return $"set_quest_aliases requires a QUST record, got {rec.GetType().Name}.";

        var built = new List<AQuestAlias>();
        var failures = new List<string>();
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "[]" : json);
            uint auto = 0;
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                var a = new QuestReferenceAlias { ID = (uint)JInt(el, "id", (int)auto) };
                auto = a.ID + 1;
                a.Name = (el.TryGetProperty("name", out var nEl) ? nEl.GetString() : null) ?? $"Alias{a.ID}";
                if (el.TryGetProperty("forcedReference", out var frEl) && frEl.GetString() is { } frStr && ResolveFk(env, frStr, out var frFk))
                    a.ForcedReference.SetTo(frFk);
                if (el.TryGetProperty("uniqueActor", out var uaEl) && uaEl.GetString() is { } uaStr && ResolveFk(env, uaStr, out var uaFk))
                    a.UniqueActor.SetTo(uaFk);
                if (el.TryGetProperty("flags", out var flEl) && flEl.GetString() is { } flStr)
                    TrySetNullableEnumFlags(a, "Flags", flStr);
                built.Add(a);
            }
        }
        catch (Exception ex) { return $"Could not parse aliases JSON: {ex.Message}."; }

        (q.Aliases ??= new ExtendedList<AQuestAlias>()).Clear();
        foreach (var a in built) q.Aliases.Add(a);
        MutagenLoader.InvalidateModIndex(plugin); NotifyChanged(plugin);
        var msg = $"Set {built.Count} quest alias(es) on {recordId} in {plugin}.";
        if (failures.Count > 0) msg += $" Skipped {failures.Count}: {string.Join("; ", failures)}.";
        return msg + " save_plugin to persist.";
    }

    /// <summary>Replace a quest's stages. JSON array: {index:int, logEntry?:string,
    /// flags?:"RunOnStart,RunOnStop", complete?:bool}.</summary>
    public static string SetQuestStages(string plugin, string recordId, string json, object? env)
    {
        var mod = EnsureOpen(plugin, env, out var openMsg); if (mod == null) return openMsg;
        var rec = FindMutableRecord(mod, recordId); if (rec == null) return ToolError.Fail($"Record '{recordId}' not found in {plugin}.");
        if (rec is not Quest q) return $"set_quest_stages requires a QUST record, got {rec.GetType().Name}.";

        var built = new List<QuestStage>();
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "[]" : json);
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                var st = new QuestStage { Index = (ushort)JInt(el, "index", 0) };
                if (el.TryGetProperty("flags", out var flEl) && flEl.GetString() is { } flStr
                    && Enum.TryParse<QuestStage.Flag>(flStr.Replace(" ", ""), true, out var stFlags))
                    st.Flags = stFlags;
                if (el.TryGetProperty("logEntry", out var leEl) && leEl.GetString() is { } leStr)
                {
                    var le = new QuestLogEntry { Entry = leStr };
                    if (el.TryGetProperty("complete", out var cEl) && cEl.ValueKind == JsonValueKind.True)
                        TrySetNullableEnumFlags(le, "Flags", "CompleteQuest");
                    st.LogEntries.Add(le);
                }
                built.Add(st);
            }
        }
        catch (Exception ex) { return $"Could not parse stages JSON: {ex.Message}."; }

        q.Stages.Clear();
        foreach (var s in built) q.Stages.Add(s);
        MutagenLoader.InvalidateModIndex(plugin); NotifyChanged(plugin);
        return $"Set {built.Count} quest stage(s) on {recordId} in {plugin}. save_plugin to persist.";
    }

    /// <summary>Replace a quest's objectives. JSON array: {index:int, displayText:string,
    /// flags?:"ORObjective"}.</summary>
    public static string SetQuestObjectives(string plugin, string recordId, string json, object? env)
    {
        var mod = EnsureOpen(plugin, env, out var openMsg); if (mod == null) return openMsg;
        var rec = FindMutableRecord(mod, recordId); if (rec == null) return ToolError.Fail($"Record '{recordId}' not found in {plugin}.");
        if (rec is not Quest q) return $"set_quest_objectives requires a QUST record, got {rec.GetType().Name}.";

        var built = new List<QuestObjective>();
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "[]" : json);
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                var ob = new QuestObjective
                {
                    Index = (ushort)JInt(el, "index", 0),
                    DisplayText = (el.TryGetProperty("displayText", out var dEl) ? dEl.GetString() : null) ?? "",
                };
                if (el.TryGetProperty("flags", out var flEl) && flEl.GetString() is { } flStr)
                    TrySetNullableEnumFlags(ob, "Flags", flStr);
                built.Add(ob);
            }
        }
        catch (Exception ex) { return $"Could not parse objectives JSON: {ex.Message}."; }

        q.Objectives.Clear();
        foreach (var o in built) q.Objectives.Add(o);
        MutagenLoader.InvalidateModIndex(plugin); NotifyChanged(plugin);
        return $"Set {built.Count} quest objective(s) on {recordId} in {plugin}. save_plugin to persist.";
    }

    /// <summary>
    /// Replace a MESG record's menu buttons. MenuButtons is an ExtendedList&lt;MessageButton&gt;, i.e. a
    /// STRUCT list, so add_list_item (FormLinks only) cannot author it. Buttons are returned by
    /// Message.Show() as their zero-based index, in this order.
    /// </summary>
    public static string SetMessageButtons(string plugin, string recordId, string json, object? env)
    {
        var mod = EnsureOpen(plugin, env, out var openMsg); if (mod == null) return openMsg;
        var rec = FindMutableRecord(mod, recordId); if (rec == null) return ToolError.Fail($"Record '{recordId}' not found in {plugin}.");
        if (rec is not Message m) return $"set_message_buttons requires a MESG record, got {rec.GetType().Name}.";

        var built = new List<MessageButton>();
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "[]" : json);
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                // Accept either a bare string ("Move") or an object ({"text":"Move"}).
                var text = el.ValueKind == JsonValueKind.String
                    ? el.GetString()
                    : (el.TryGetProperty("text", out var tEl) ? tEl.GetString() : null);
                if (string.IsNullOrEmpty(text)) return "Every button needs a non-empty 'text'.";
                built.Add(new MessageButton { Text = text });
            }
        }
        catch (Exception ex) { return $"Could not parse buttons JSON: {ex.Message}."; }

        m.MenuButtons.Clear();
        foreach (var b in built) m.MenuButtons.Add(b);

        // A MESG only renders as a button menu when the MessageBox flag is set; without it the game
        // shows a corner notification and Show() returns immediately with no selection.
        TrySetNullableEnumFlags(m, "Flags", "MessageBox");

        MutagenLoader.InvalidateModIndex(plugin); NotifyChanged(plugin);
        var idx = string.Join(", ", built.Select((b, i) => $"{i}={b.Text}"));
        return $"Set {built.Count} menu button(s) on {recordId} in {plugin} and flagged it MessageBox. Show() returns: {idx}. save_plugin to persist.";
    }

    /// <summary>
    /// Replace a FURN record's marker parameters -- the entry markers that tell the engine WHERE to
    /// put the actor using it. MarkerParameters is a STRUCT list, so nothing else in this tool could
    /// author it, and create_record FURN produces a shell with none. A furniture with zero markers
    /// cannot be entered at all: a bed reports "someone else is using it" and a workbench reads as
    /// unusable. Vanilla's equivalents all carry at least one {enabled, entryTypes:255} marker.
    /// JSON array: [{"enabled":true,"entryTypes":255,"offsetX":0,"offsetY":0,"offsetZ":0,"rotationZ":0}]
    /// or just [{}] for a single default front/back/left/right marker at the origin.
    /// </summary>
    public static string SetFurnitureMarkers(string plugin, string recordId, string json, object? env)
    {
        var mod = EnsureOpen(plugin, env, out var openMsg); if (mod == null) return openMsg;
        var rec = FindMutableRecord(mod, recordId); if (rec == null) return ToolError.Fail($"Record '{recordId}' not found in {plugin}.");
        if (rec is not Furniture f) return $"set_furniture_markers requires a FURN record, got {rec.GetType().Name}.";

        var built = new List<FurnitureMarkerParameters>();
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "[{}]" : json);
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                var m = new FurnitureMarkerParameters
                {
                    Enabled = !el.TryGetProperty("enabled", out var en) || en.GetBoolean(),
                    Offset = new Noggog.P3Float(
                        JFloat(el, "offsetX", 0f), JFloat(el, "offsetY", 0f), JFloat(el, "offsetZ", 0f)),
                    RotationZ = JFloat(el, "rotationZ", 0f),
                    EntryTypes = (Furniture.EntryParameterType)JInt(el, "entryTypes", 255),
                };
                built.Add(m);
            }
        }
        catch (Exception ex) { return $"Could not parse markers JSON: {ex.Message}."; }

        f.MarkerParameters ??= new Noggog.ExtendedList<FurnitureMarkerParameters>();
        f.MarkerParameters.Clear();
        foreach (var m in built) f.MarkerParameters.Add(m);

        MutagenLoader.InvalidateModIndex(plugin); NotifyChanged(plugin);
        return $"Set {built.Count} furniture marker(s) on {recordId} in {plugin}. save_plugin to persist.";
    }

    // ── shared JSON / reflection helpers ─────────────────────────────────────────────────────────
    // Build one FO4 Condition from a JSON element (same schema as set_conditions). Reuses the
    // private ApplyParam / ParseOperator / ResolveFk from the main partial.
    private static Condition? BuildConditionFromJson(JsonElement el, object? env, out string err)
    {
        err = "";
        var fnName = el.TryGetProperty("function", out var f) ? f.GetString() ?? "" : "";
        if (!Enum.TryParse<Condition.Function>(fnName, ignoreCase: true, out var fn)) { err = $"unknown function '{fnName}'"; return null; }

        var data = new FunctionConditionData { Function = fn };
        if (el.TryGetProperty("runOn", out var ro) && ro.GetString() is { } roStr && Enum.TryParse<Condition.RunOnType>(roStr, true, out var roType))
            data.RunOnType = roType;
        if (el.TryGetProperty("reference", out var rf) && rf.GetString() is { } rfStr && ResolveFk(env, rfStr, out var rfFk))
            data.Reference.SetTo(rfFk);
        ApplyParam(el, "param1", env, fk => data.ParameterOneRecord.SetTo(fk), n => data.ParameterOneNumber = n);
        ApplyParam(el, "param2", env, fk => data.ParameterTwoRecord.SetTo(fk), n => data.ParameterTwoNumber = n);

        var op = ParseOperator(el.TryGetProperty("operator", out var o) ? o.GetString() : null);
        Condition.Flag condFlags = default;
        if (el.TryGetProperty("flags", out var flagEl) && flagEl.GetString() is { Length: > 0 } flagStr)
            foreach (var part in flagStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                if (Enum.TryParse<Condition.Flag>(part, true, out var fl)) condFlags |= fl;

        if (el.TryGetProperty("compareGlobal", out var cg) && cg.GetString() is { } cgStr && ResolveFk(env, cgStr, out var cgFk))
        {
            var cond = new ConditionGlobal { CompareOperator = op, Data = data };
            cond.ComparisonValue.SetTo(cgFk);
            if (condFlags != default) cond.Flags = condFlags;
            return cond;
        }
        float val = el.TryGetProperty("value", out var v) && v.TryGetSingle(out var fv) ? fv : 1f;
        var c2 = new ConditionFloat { CompareOperator = op, ComparisonValue = val, Data = data };
        if (condFlags != default) c2.Flags = condFlags;
        return c2;
    }

    // Accept both JSON numbers and numeric strings ("5") -- LLMs frequently quote numbers, and
    // silently defaulting to 0 would produce wrong-but-valid records.
    private static int JInt(JsonElement el, string name, int def)
    {
        if (!el.TryGetProperty(name, out var p)) return def;
        if (p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var n)) return n;
        if (p.ValueKind == JsonValueKind.String && int.TryParse(p.GetString(), out var sn)) return sn;
        return def;
    }

    private static float JFloat(JsonElement el, string name, float def)
    {
        if (!el.TryGetProperty(name, out var p)) return def;
        if (p.ValueKind == JsonValueKind.Number && p.TryGetSingle(out var n)) return n;
        if (p.ValueKind == JsonValueKind.String && float.TryParse(p.GetString(),
                System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var sn)) return sn;
        return def;
    }

    // Parse a comma-separated flag list onto a (possibly Nullable) enum property via reflection,
    // so we don't hard-code each record's flag enum type.
    private static void TrySetNullableEnumFlags(object obj, string propName, string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return;
        var p = obj.GetType().GetProperty(propName);
        if (p == null) return;
        var t = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;
        if (!t.IsEnum) return;
        long acc = 0;
        foreach (var part in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            try { acc |= Convert.ToInt64(Enum.Parse(t, part, true)); } catch { /* skip unknown flag */ }
        p.SetValue(obj, Enum.ToObject(t, acc));
    }
}
