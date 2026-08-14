using System.Text;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Mutagen.Bethesda.Fallout4;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Records;
using Noggog;

namespace FO4RecordEditor.Services;

/// <summary>
/// The `host` object injected into every patch script. It exposes a high-level API over the loaded
/// records and a target patch plugin so a C# script can do PER-RECORD edits -- the thing fixed op
/// sets (batch_patch_records) can't express. Discovery returns getters; <see cref="Cobj"/>/<see
/// cref="Override"/> forward a record into the patch as a mutable override; the component/condition
/// helpers edit that override. Nothing is written until the runner saves (apply mode); a dry run
/// executes the whole script against a throwaway patch and then discards it.
/// </summary>
public sealed class PatchScriptHost
{
    private readonly object? _env;
    private readonly string _patchPlugin;
    private readonly StringBuilder _log = new();
    private readonly HashSet<FormKey> _touched = new();

    /// <summary>Records forwarded into the patch (distinct).</summary>
    public int Applied { get; private set; }
    /// <summary>Individual field/component/condition edits performed.</summary>
    public int Edits { get; private set; }
    /// <summary>True on a preview run (the patch is discarded afterwards).</summary>
    public bool DryRun { get; }

    internal PatchScriptHost(object? env, string patchPlugin, bool dryRun)
    {
        _env = env;
        _patchPlugin = patchPlugin;
        DryRun = dryRun;
    }

    internal string LogText => _log.ToString();

    // ── discovery (getters; read-only) ────────────────────────────────────────────────────────
    /// <summary>All ConstructibleObject (COBJ) getters defined in a plugin.</summary>
    public IEnumerable<IConstructibleObjectGetter> Cobjs(string plugin) =>
        MutagenLoader.GetRecordsForBatch(_env, plugin, "ConstructibleObject").OfType<IConstructibleObjectGetter>();

    /// <summary>All records of a Mutagen type name (e.g. "Weapon", "Npc") defined in a plugin.</summary>
    public IEnumerable<IMajorRecordGetter> Records(string type, string plugin) =>
        MutagenLoader.GetRecordsForBatch(_env, plugin, type);

    /// <summary>Names of every plugin in the current load order (including loose mods opened for editing).</summary>
    public IReadOnlyList<string> AllPlugins() => MutagenLoader.QueryLoadedPlugins(_env);

    /// <summary>Remove a COBJ override from the patch plugin entirely (as if it was never added).
    /// Use this to undo a previous override -- the record then falls back to its previous winning
    /// version in the load order. Returns true when the record was found and removed.</summary>
    public bool DeleteOverride(IConstructibleObjectGetter getter) =>
        MutagenLoader.RemoveFromEditableMod(_patchPlugin, getter.FormKey);

    // ── create (author a brand-new record in the patch) ───────────────────────────────────────
    /// <summary>Create a NEW record in the patch plugin and return it for full Mutagen editing.
    /// sig = any create_record signature (e.g. "QUST", "PERK", "LVLI", "SPEL", "FURN"). This is the
    /// general escape hatch: cast the result to its concrete type and populate any struct list
    /// (Stages/Aliases/Effects/Entries) that the dedicated tools don't cover.</summary>
    public IFallout4MajorRecord New(string sig, string editorId)
    {
        var rec = WriteService.CreateForScript(_patchPlugin, _env, sig, editorId)
            ?? throw new InvalidOperationException($"Could not create '{sig}' '{editorId}' in '{_patchPlugin}' (unsupported signature or ESL range full).");
        Applied++;
        return rec;
    }

    /// <summary>Typed convenience over <see cref="New(string,string)"/>: host.New&lt;Quest&gt;("QUST","MyQuest").</summary>
    public T New<T>(string sig, string editorId) where T : class, IFallout4MajorRecord =>
        (T)New(sig, editorId);

    // ── override (forward a getter into the patch as a mutable record) ────────────────────────
    /// <summary>Forward a COBJ getter into the patch and return the mutable override.</summary>
    public ConstructibleObject Cobj(IConstructibleObjectGetter getter) => (ConstructibleObject)OverrideRec(getter);

    /// <summary>Forward any record getter into the patch and return the mutable override.</summary>
    public IFallout4MajorRecord Override(IMajorRecordGetter getter) => OverrideRec(getter);

    private IFallout4MajorRecord OverrideRec(IMajorRecordGetter getter)
    {
        var rec = WriteService.OverrideForScript(_patchPlugin, _env, getter)
            ?? throw new InvalidOperationException(
                $"Could not override {getter.EditorID ?? getter.FormKey.ToString()} into '{_patchPlugin}' " +
                $"(type '{getter.Registration.Name}' may not be overridable).");
        if (_touched.Add(getter.FormKey)) Applied++;
        return rec;
    }

    // ── components (COBJ) ─────────────────────────────────────────────────────────────────────
    /// <summary>True if the recipe lists this component (FormKey or EditorID). Works on a getter or override.</summary>
    public bool HasComponent(IConstructibleObjectGetter rec, string component)
    {
        if (rec.Components == null || !Resolve(component, out var fk)) return false;
        foreach (var c in rec.Components) if (c.Component.FormKey == fk) return true;
        return false;
    }

    /// <summary>Append a component (material + count) to a recipe override.</summary>
    public void AddComponent(ConstructibleObject rec, string component, int count = 1)
    {
        if (!Resolve(component, out var fk)) { _log.AppendLine($"  ! AddComponent: cannot resolve '{component}'"); return; }
        rec.Components ??= new ExtendedList<ConstructibleObjectComponent>();
        var item = new ConstructibleObjectComponent { Count = (uint)Math.Max(0, count) };
        item.Component.SetTo(fk);
        rec.Components.Add(item);
        Edits++;
    }

    /// <summary>Remove every entry for a component from a recipe override. Returns how many were removed.</summary>
    public int RemoveComponent(ConstructibleObject rec, string component)
    {
        if (rec.Components == null || !Resolve(component, out var fk)) return 0;
        int removed = 0;
        for (int i = rec.Components.Count - 1; i >= 0; i--)
            if (rec.Components[i].Component.FormKey == fk) { rec.Components.RemoveAt(i); removed++; }
        if (removed > 0) Edits++;
        return removed;
    }

    /// <summary>Set the count for an existing component (no-op if the recipe doesn't list it). Returns true if changed.</summary>
    public bool SetCount(ConstructibleObject rec, string component, int count)
    {
        if (rec.Components == null || !Resolve(component, out var fk)) return false;
        bool any = false;
        foreach (var c in rec.Components)
            if (c.Component.FormKey == fk) { c.Count = (uint)Math.Max(0, count); any = true; }
        if (any) Edits++;
        return any;
    }

    /// <summary>Replace a component's FormLink with another, keeping its count unless <paramref name="count"/> >= 0.
    /// Returns true if at least one entry was swapped.</summary>
    public bool Swap(ConstructibleObject rec, string oldComponent, string newComponent, int count = -1)
    {
        if (rec.Components == null || !Resolve(oldComponent, out var oldFk) || !Resolve(newComponent, out var newFk))
            return false;
        bool any = false;
        foreach (var c in rec.Components)
            if (c.Component.FormKey == oldFk) { c.Component.SetTo(newFk); if (count >= 0) c.Count = (uint)count; any = true; }
        if (any) Edits++;
        return any;
    }

    /// <summary>Clear all components on a recipe override.</summary>
    public void ClearComponents(ConstructibleObject rec) { rec.Components?.Clear(); Edits++; }

    // ── conditions (COBJ and any record with a Conditions list) ───────────────────────────────
    /// <summary>Append a condition. function = HasPerk/GetItemCount/GetGlobalValue/GetBaseValue/...;
    /// param1/param2 = a FormKey/EditorID or an integer (as a string); op = == != &gt; &gt;= &lt; &lt;=;
    /// value = constant compared against; runOn = Subject/Target/Reference; reference = ref FormKey when
    /// runOn=Reference; compareGlobal = global FormKey to compare against instead of a constant;
    /// flags = e.g. "UseOr".</summary>
    public void AddCondition(ConstructibleObject rec, string function, string? param1 = null, string? param2 = null,
        string op = "==", float value = 1f, string? runOn = null, string? reference = null,
        string? compareGlobal = null, string? flags = null)
    {
        var cond = WriteService.BuildConditionTyped(_env, function, param1, param2, op, value, runOn, reference, compareGlobal, flags, out var err);
        if (cond == null) { _log.AppendLine($"  ! AddCondition: {err}"); return; }
        rec.Conditions.Add(cond);
        Edits++;
    }

    /// <summary>Remove conditions matching a function (and optionally a param1 FormKey/EditorID or integer).
    /// Returns how many were removed.</summary>
    public int RemoveConditions(ConstructibleObject rec, string function, string? param1 = null)
    {
        if (!Enum.TryParse<Condition.Function>(function, ignoreCase: true, out var fn))
        { _log.AppendLine($"  ! RemoveConditions: unknown function '{function}'"); return 0; }

        FormKey? pfk = null; int? pnum = null;
        if (!string.IsNullOrWhiteSpace(param1))
        {
            if (int.TryParse(param1, out var n)) pnum = n;
            else if (Resolve(param1!, out var f)) pfk = f;
        }

        int removed = 0;
        for (int i = rec.Conditions.Count - 1; i >= 0; i--)
        {
            if (rec.Conditions[i].Data is not IFunctionConditionDataGetter d || d.Function != fn) continue;
            if (pfk.HasValue && d.ParameterOneRecord.FormKey != pfk.Value) continue;
            if (pnum.HasValue && d.ParameterOneNumber != pnum.Value) continue;
            rec.Conditions.RemoveAt(i); removed++;
        }
        if (removed > 0) Edits++;
        return removed;
    }

    /// <summary>Clear all conditions on a recipe override.</summary>
    public void ClearConditions(ConstructibleObject rec) { rec.Conditions.Clear(); Edits++; }

    // ── scalar / nested fields (any record) ───────────────────────────────────────────────────
    /// <summary>Set a scalar / nested field on an override (same path syntax as set_field). Returns true on success.</summary>
    public bool Set(IFallout4MajorRecord rec, string field, string value)
    {
        bool ok = WriteService.TrySetField(rec, field, value, _env, out var msg);
        if (ok) Edits++; else _log.AppendLine($"  ! Set {field}: {msg}");
        return ok;
    }

    // ── record-base helpers ───────────────────────────────────────────────────────────────────
    /// <summary>
    /// Returns the best available getter to use as the base for a COBJ override in the patch.
    /// Walks the full override chain from winning to original, skipping any plugin named in
    /// <paramref name="skipPlugins"/> (e.g. your own patch plugin and mid-chain intermediaries
    /// like FallenWorldCrafting_S7.esp that wrote partial/broken records). Returns the first
    /// non-skipped entry -- typically FallenWorldCrafting_Compat.esp's full record when that
    /// plugin overrides the same COBJ, or the original defining plugin otherwise. This preserves
    /// conditions added by Compat (CMWeapons, CMImmersiveMode, etc.) and ensures all fields
    /// (CNAM, FVPA, BNAM) are populated from a full-record plugin, never from a partial patch.
    /// Falls back to <paramref name="fallback"/> if the cache is unavailable or yields only
    /// skipped entries.
    /// </summary>
    public IConstructibleObjectGetter GetBestBase(IConstructibleObjectGetter fallback, params string[] skipPlugins)
    {
        var cache = MutagenLoader.LinkCache;
        if (cache == null) return fallback;
        var skip = new HashSet<string>(skipPlugins, StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var ctx in cache.ResolveAllSimpleContexts<IConstructibleObjectGetter>(fallback.FormKey))
            {
                if (!skip.Contains(ctx.ModKey.FileName.String))
                    return ctx.Record;
            }
        }
        catch { /* record not in cache; fall through */ }
        return fallback;
    }

    /// <summary>
    /// Returns true when the COBJ's Created Object (CNAM) is non-null and resolves to an
    /// existing record. Records where CNAM is broken or unresolvable should be skipped before
    /// deep-copying -- otherwise the override in the patch contains no meaningful data.
    /// </summary>
    public bool CnamIsResolvable(IConstructibleObjectGetter cobj)
    {
        if (cobj.CreatedObject.FormKey.IsNull) return false;
        var cache = MutagenLoader.LinkCache;
        return cache != null && cache.TryResolve<IMajorRecordGetter>(cobj.CreatedObject.FormKey, out _);
    }

    /// <summary>
    /// Stronger usability gate than <see cref="CnamIsResolvable"/>. Returns true only when the
    /// COBJ has a resolvable CNAM <em>and</em> at least a workbench keyword or a component list
    /// -- i.e. it is a full record, not a conditions-only partial override stub (e.g. the
    /// FallenWorldCrafting_S7.esp partial records that have only conditions and null everything
    /// else). Use this instead of <see cref="CnamIsResolvable"/> when iterating a plugin that
    /// may contain pipeline stubs.
    /// </summary>
    public bool RecordIsUsable(IConstructibleObjectGetter cobj)
    {
        if (!CnamIsResolvable(cobj)) return false;
        return !cobj.WorkbenchKeyword.FormKey.IsNull
            || (cobj.Components != null && cobj.Components.Count > 0);
    }

    /// <summary>
    /// Walks the full override chain for <paramref name="cobj"/> (winning → original), skipping
    /// plugins named in <paramref name="skipPlugins"/>, and returns the first non-empty
    /// <c>Categories</c> (FNAM keyword list) found. Use this after deep-copying from a shallow
    /// override -- e.g. a Compat record that only sets Conditions and leaves Categories null --
    /// so the FNAM keywords from the original or an intermediate plugin are not silently lost.
    /// Returns <c>null</c> when no entry in the chain has categories.
    /// </summary>
    public IReadOnlyList<IFormLinkGetter<IKeywordGetter>>? GetCategoriesFromChain(
        IConstructibleObjectGetter cobj, params string[] skipPlugins)
    {
        var cache = MutagenLoader.LinkCache;
        if (cache == null) return null;
        var skip = new HashSet<string>(skipPlugins, StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var ctx in cache.ResolveAllSimpleContexts<IConstructibleObjectGetter>(cobj.FormKey))
            {
                if (skip.Contains(ctx.ModKey.FileName.String)) continue;
                if (ctx.Record.Categories != null && ctx.Record.Categories.Count > 0)
                    return ctx.Record.Categories;
            }
        }
        catch { }
        return null;
    }

    /// <summary>
    /// Copies categories from <paramref name="categories"/> into <paramref name="target"/>,
    /// replacing any existing entries. Pair with <see cref="GetCategoriesFromChain"/> to
    /// restore FNAM keywords that a shallow Compat override silently nulled by not re-declaring
    /// them in its partial record. No-op when <paramref name="categories"/> is null or empty.
    /// </summary>
    public void CopyCategories(ConstructibleObject target,
        IReadOnlyList<IFormLinkGetter<IKeywordGetter>>? categories)
    {
        if (categories == null || categories.Count == 0) return;
        target.Categories ??= new ExtendedList<IFormLinkGetter<IKeywordGetter>>();
        target.Categories.Clear();
        foreach (var cat in categories)
            target.Categories.Add(new FormLink<IKeywordGetter>(cat.FormKey));
        Edits++;
    }

    // ── utility ───────────────────────────────────────────────────────────────────────────────
    /// <summary>Resolve a FormKey or EditorID to its "XXXXXX:Plugin.esp" string, or null if unknown.</summary>
    public string? Resolve(string idOrEditorId) => Resolve(idOrEditorId, out var fk) ? fk.ToString() : null;

    /// <summary>Write a line to the script's output log (shown back to you when the run finishes).</summary>
    public void Log(string message) => _log.AppendLine(message);

    private bool Resolve(string id, out FormKey fk)
    {
        if (FormKey.TryFactory(id, out fk)) return true;
        var cache = MutagenLoader.LinkCache;
        if (cache != null && cache.TryResolve<IMajorRecordGetter>(id, out var rec)) { fk = rec.FormKey; return true; }
        fk = MutagenLoader.ResolveEditorIdToFormKey(_env, id);
        return !fk.IsNull;
    }
}

/// <summary>Globals surface for a patch script: the script references <c>host</c>.</summary>
public sealed class PatchScriptGlobals
{
    public PatchScriptHost host { get; set; } = null!;
}

/// <summary>Compiles and runs a write-capable C# patch script authored by the AI.</summary>
public static class PatchScriptRunner
{
    private static readonly ScriptOptions _options = ScriptOptions.Default
        .AddImports(
            "System",
            "System.Linq",
            "System.Collections.Generic",
            "Mutagen.Bethesda.Fallout4",
            "Mutagen.Bethesda.Plugins",
            "Mutagen.Bethesda.Plugins.Records",
            "FO4RecordEditor.Services")
        .AddReferences(
            typeof(PatchScriptHost).Assembly,
            typeof(IFallout4Mod).Assembly,            // Mutagen.Bethesda.Fallout4
            typeof(FormKey).Assembly,                 // Mutagen.Bethesda.Core / Plugins
            typeof(IMajorRecordGetter).Assembly,
            typeof(ExtendedList<>).Assembly,          // Noggog
            typeof(Enumerable).Assembly);

    /// <summary>Wall-clock ceiling for one run_script call. Settable so tests can use a short one.</summary>
    internal static TimeSpan ScriptTimeout { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Run <paramref name="code"/> against the loaded env, forwarding edits into <paramref name="patchPlugin"/>.
    /// On a dry run the script executes against a throwaway "<patch>.preview" plugin that is discarded
    /// afterwards, so record edits are counted without touching the real patch.
    ///
    /// TRUST MODEL: this runs AI-authored C# in-process at full trust. Roslyn scripting applies no
    /// security boundary, so a script can reach System.IO, Process.Start and this app's own static
    /// write API by fully qualifying the type. That is accepted -- it is the user's own agent on the
    /// user's own machine -- but it means dry_run bounds ONLY Mutagen record edits, never filesystem,
    /// process or network side effects, which have already happened by the time this returns.
    /// </summary>
    public static string Run(string code, object? env, string patchPlugin, bool dryRun)
    {
        if (string.IsNullOrWhiteSpace(code)) return "Provide a script in 'script'.";
        if (string.IsNullOrWhiteSpace(patchPlugin)) return "Provide 'patch_plugin' (the plugin to write overrides into).";

        // Route a dry run into a disposable patch so the real one is never touched.
        var target = dryRun ? AddPreviewSuffix(patchPlugin) : patchPlugin;
        var host = new PatchScriptHost(env, target, dryRun);
        var sw = System.Diagnostics.Stopwatch.StartNew();

        string? error = null;
        try
        {
            // Run on a dedicated background thread and abandon it if it overruns, so a runaway
            // script cannot wedge the caller -- for the stdio MCP server, the calling thread IS the
            // server. A CancellationToken alone is NOT enough: Roslyn only observes it at await
            // points, so a tight `while(true){}` ignores it entirely (verified in
            // PatchScriptTimeoutTests). The token is still passed so cooperative scripts stop early.
            using var cts = new CancellationTokenSource(ScriptTimeout);
            Exception? threadError = null;
            var worker = new Thread(() =>
            {
                try
                {
                    CSharpScript.RunAsync(code, _options, new PatchScriptGlobals { host = host },
                            cancellationToken: cts.Token)
                        .GetAwaiter().GetResult();
                }
                catch (Exception ex) { threadError = ex; }
            })
            {
                IsBackground = true,   // an abandoned runaway must not keep the process alive
                Name = "run_script",
            };
            worker.Start();

            if (!worker.Join(ScriptTimeout))
            {
                cts.Cancel();
                error = $"Script exceeded the {ScriptTimeout.TotalSeconds:0}s limit and was abandoned. " +
                        "NOTE: .NET cannot forcibly kill a running thread, so if the script is in a tight " +
                        "loop it is still burning a CPU core in the background and may still be mutating " +
                        "state -- restart the editor to be sure. Any edits it made are in memory only; " +
                        "run reload_plugin to discard them.";
            }
            else if (threadError != null)
            {
                throw threadError;
            }
        }
        catch (CompilationErrorException ex)
        {
            error = "Compile error:\n" + string.Join("\n", ex.Diagnostics);
        }
        catch (Exception ex)
        {
            error = "Runtime error: " + (ex.InnerException?.Message ?? ex.Message);
        }
        sw.Stop();

        var sb = new StringBuilder();
        var log = host.LogText;
        if (log.Length > 0) sb.Append(log);

        if (error != null)
        {
            // A failed run must not leave half-built overrides behind.
            WriteService.DiscardScriptPatch(target);
            sb.AppendLine($"\nSCRIPT FAILED ({sw.ElapsedMilliseconds} ms) -- nothing saved.");
            sb.AppendLine(error);
            return sb.ToString();
        }

        sb.AppendLine($"\n{(dryRun ? "DRY RUN" : "APPLIED")}: {host.Applied} record(s) overridden, " +
                      $"{host.Edits} edit(s), {sw.ElapsedMilliseconds} ms.");

        if (dryRun)
        {
            WriteService.DiscardScriptPatch(target);
            // Scoped claim: dry_run discards the preview plugin's record edits. It cannot undo
            // anything the script did directly (File.WriteAllText, Process.Start, a WriteService
            // call), so promising "nothing was written" would be false for any such script.
            sb.AppendLine($"No plugin records were written -- the preview patch was discarded. " +
                          $"Re-run with dry_run=false to apply into '{patchPlugin}'. " +
                          "(dry_run only rolls back record edits; any file, process or network side " +
                          "effects the script performed directly have already happened.)");
        }
        else
        {
            sb.Append(WriteService.SaveScriptPatch(patchPlugin, env));
        }
        return sb.ToString();
    }

    private static string AddPreviewSuffix(string plugin)
    {
        var dot = plugin.LastIndexOf('.');
        return dot < 0 ? plugin + ".preview.esp" : plugin[..dot] + ".preview" + plugin[dot..];
    }
}
