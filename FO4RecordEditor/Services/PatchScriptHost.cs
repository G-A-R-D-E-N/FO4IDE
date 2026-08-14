using System.Text;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Mutagen.Bethesda.Fallout4;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Records;
using Noggog;

namespace FO4RecordEditor.Services;

public sealed class PatchScriptHost
{
    private readonly object? _env;
    private readonly string _patchPlugin;
    private readonly StringBuilder _log = new();
    private readonly HashSet<FormKey> _touched = new();

    public int Applied { get; private set; }

    public int Edits { get; private set; }

    public bool DryRun { get; }

    internal PatchScriptHost(object? env, string patchPlugin, bool dryRun)
    {
        _env = env;
        _patchPlugin = patchPlugin;
        DryRun = dryRun;
    }

    internal string LogText => _log.ToString();

    public IEnumerable<IConstructibleObjectGetter> Cobjs(string plugin) =>
        MutagenLoader.GetRecordsForBatch(_env, plugin, "ConstructibleObject").OfType<IConstructibleObjectGetter>();

    public IEnumerable<IMajorRecordGetter> Records(string type, string plugin) =>
        MutagenLoader.GetRecordsForBatch(_env, plugin, type);

    public IReadOnlyList<string> AllPlugins() => MutagenLoader.QueryLoadedPlugins(_env);

    public bool DeleteOverride(IConstructibleObjectGetter getter) =>
        MutagenLoader.RemoveFromEditableMod(_patchPlugin, getter.FormKey);

    public IFallout4MajorRecord New(string sig, string editorId)
    {
        var rec = WriteService.CreateForScript(_patchPlugin, _env, sig, editorId)
            ?? throw new InvalidOperationException($"Could not create '{sig}' '{editorId}' in '{_patchPlugin}' (unsupported signature or ESL range full).");
        Applied++;
        return rec;
    }

    public T New<T>(string sig, string editorId) where T : class, IFallout4MajorRecord =>
        (T)New(sig, editorId);

    public ConstructibleObject Cobj(IConstructibleObjectGetter getter) => (ConstructibleObject)OverrideRec(getter);

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

    public bool HasComponent(IConstructibleObjectGetter rec, string component)
    {
        if (rec.Components == null || !Resolve(component, out var fk)) return false;
        foreach (var c in rec.Components) if (c.Component.FormKey == fk) return true;
        return false;
    }

    public void AddComponent(ConstructibleObject rec, string component, int count = 1)
    {
        if (!Resolve(component, out var fk)) { _log.AppendLine($"  ! AddComponent: cannot resolve '{component}'"); return; }
        rec.Components ??= new ExtendedList<ConstructibleObjectComponent>();
        var item = new ConstructibleObjectComponent { Count = (uint)Math.Max(0, count) };
        item.Component.SetTo(fk);
        rec.Components.Add(item);
        Edits++;
    }

    public int RemoveComponent(ConstructibleObject rec, string component)
    {
        if (rec.Components == null || !Resolve(component, out var fk)) return 0;
        int removed = 0;
        for (int i = rec.Components.Count - 1; i >= 0; i--)
            if (rec.Components[i].Component.FormKey == fk) { rec.Components.RemoveAt(i); removed++; }
        if (removed > 0) Edits++;
        return removed;
    }

    public bool SetCount(ConstructibleObject rec, string component, int count)
    {
        if (rec.Components == null || !Resolve(component, out var fk)) return false;
        bool any = false;
        foreach (var c in rec.Components)
            if (c.Component.FormKey == fk) { c.Count = (uint)Math.Max(0, count); any = true; }
        if (any) Edits++;
        return any;
    }

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

    public void ClearComponents(ConstructibleObject rec) { rec.Components?.Clear(); Edits++; }

    public void AddCondition(ConstructibleObject rec, string function, string? param1 = null, string? param2 = null,
        string op = "==", float value = 1f, string? runOn = null, string? reference = null,
        string? compareGlobal = null, string? flags = null)
    {
        var cond = WriteService.BuildConditionTyped(_env, function, param1, param2, op, value, runOn, reference, compareGlobal, flags, out var err);
        if (cond == null) { _log.AppendLine($"  ! AddCondition: {err}"); return; }
        rec.Conditions.Add(cond);
        Edits++;
    }

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

    public void ClearConditions(ConstructibleObject rec) { rec.Conditions.Clear(); Edits++; }

    public bool Set(IFallout4MajorRecord rec, string field, string value)
    {
        bool ok = WriteService.TrySetField(rec, field, value, _env, out var msg);
        if (ok) Edits++; else _log.AppendLine($"  ! Set {field}: {msg}");
        return ok;
    }

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
        catch {  }
        return fallback;
    }

    public bool CnamIsResolvable(IConstructibleObjectGetter cobj)
    {
        if (cobj.CreatedObject.FormKey.IsNull) return false;
        var cache = MutagenLoader.LinkCache;
        return cache != null && cache.TryResolve<IMajorRecordGetter>(cobj.CreatedObject.FormKey, out _);
    }

    public bool RecordIsUsable(IConstructibleObjectGetter cobj)
    {
        if (!CnamIsResolvable(cobj)) return false;
        return !cobj.WorkbenchKeyword.FormKey.IsNull
            || (cobj.Components != null && cobj.Components.Count > 0);
    }

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

    public string? Resolve(string idOrEditorId) => Resolve(idOrEditorId, out var fk) ? fk.ToString() : null;

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

public sealed class PatchScriptGlobals
{
    public PatchScriptHost host { get; set; } = null!;
}

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
            typeof(IFallout4Mod).Assembly,
            typeof(FormKey).Assembly,
            typeof(IMajorRecordGetter).Assembly,
            typeof(ExtendedList<>).Assembly,
            typeof(Enumerable).Assembly);

    internal static TimeSpan ScriptTimeout { get; set; } = TimeSpan.FromMinutes(2);

    public static string Run(string code, object? env, string patchPlugin, bool dryRun)
    {
        if (string.IsNullOrWhiteSpace(code)) return "Provide a script in 'script'.";
        if (string.IsNullOrWhiteSpace(patchPlugin)) return "Provide 'patch_plugin' (the plugin to write overrides into).";

        var target = dryRun ? AddPreviewSuffix(patchPlugin) : patchPlugin;
        var host = new PatchScriptHost(env, target, dryRun);
        var sw = System.Diagnostics.Stopwatch.StartNew();

        string? error = null;
        try
        {

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
                IsBackground = true,
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
