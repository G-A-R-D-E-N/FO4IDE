using Mutagen.Bethesda.Fallout4;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Newtonsoft.Json;

namespace FO4RecordEditor.Services;
















public static class PrecombineService
{


    private const int FlagDeleted = 0x0000_0020;
    private const int FlagInitiallyDisabled = 0x0000_0800;



    private const int InstanceCap = 25;

    public sealed record Instance(string Reference, string? EditorId, float[] Position, float[] Rotation, float Scale);

    public sealed record ModelGroup(string ModelPath, int InstanceCount, List<Instance> Instances);

    public sealed record Skipped(string Reason, int Count, List<string> Examples);






    private enum Skip
    {
        NotAPlacedObject,
        DeletedOrDisabled,
        HasScript,
        HasEnableParent,
        HasTeleport,
        HasActivateParent,
        HasLinkedReference,
        NotOwnedByThisPlugin,
        BaseUnresolved,
        BaseNotStatic,
        BaseHasScript,
        BaseHasMaterialSwap,
        BaseHasNoModel,
    }

    private static string Describe(Skip r) => r switch
    {
        Skip.NotAPlacedObject => "not a placed object (actor, projectile, hazard, ...)",
        Skip.DeletedOrDisabled => "deleted or initially disabled",
        Skip.HasScript => "has a script attached",
        Skip.HasEnableParent => "has an enable parent",
        Skip.HasTeleport => "is a door with a teleport destination",
        Skip.HasActivateParent => "has an activate parent",
        Skip.HasLinkedReference => "is linked to another reference",
        Skip.NotOwnedByThisPlugin => "belongs to another plugin (this plugin only overrides it)",
        Skip.BaseUnresolved => "base object could not be resolved in the load order",
        Skip.BaseNotStatic => "base object is not a Static",
        Skip.BaseHasScript => "base Static has a script attached",
        Skip.BaseHasMaterialSwap => "base Static uses a material swap",
        Skip.BaseHasNoModel => "base Static has no model path",
        _ => r.ToString(),
    };












    public static string BuildPlanJson(object? env, string plugin, string cellId, int minInstances = 2,
                                       string? worldspace = null, int? gridX = null, int? gridY = null,
                                       bool includeInstances = false, int groupLimit = 40)
    {
        if (minInstances < 1) minInstances = 1;

        var mod = MutagenLoader.ResolveModPublic(plugin, env);
        if (mod is not IFallout4ModGetter target)
            return JsonConvert.SerializeObject(new { error = $"Plugin '{plugin}' is not loaded. Open it first." });
        var ownModKey = target.ModKey;

        var resolvedId = cellId ?? "";
        if (!CellService.TryResolveCellIdPublic(env, ref resolvedId, worldspace, gridX, gridY, out var resolveErr))
            return JsonConvert.SerializeObject(new { error = resolveErr });

        var cache = MutagenLoader.LinkCache;
        if (cache == null) return JsonConvert.SerializeObject(new { error = "No environment loaded." });

        var cellFk = MutagenLoader.ResolveId(env, resolvedId);
        if (cellFk.IsNull || !cache.TryResolve<ICellGetter>(cellFk, out var cell))
            return JsonConvert.SerializeObject(new { error = $"Could not resolve '{cellId}' to a CELL." });




        var interior = (cell.Flags & Cell.Flag.IsInteriorCell) != 0;
        if (!interior)
            return JsonConvert.SerializeObject(new
            {
                error = $"'{cell.EditorID ?? cell.FormKey.ToString()}' is an exterior cell. Phase 1 is " +
                        "interior-only; exterior precombines interact with worldspace object LOD and are a separate scope.",
            });

        var groups = new Dictionary<string, List<Instance>>(StringComparer.OrdinalIgnoreCase);
        var rejects = new Dictionary<Skip, (int Count, List<string> Examples)>();
        int considered = 0;

        void Drop(Skip reason, IPlacedGetter p)
        {
            rejects.TryGetValue(reason, out var entry);
            entry.Examples ??= new List<string>();
            entry.Count++;
            if (entry.Examples.Count < 5) entry.Examples.Add(p.EditorID ?? p.FormKey.ToString());
            rejects[reason] = entry;
        }

        foreach (var placed in cell.Temporary)
        {
            considered++;

            if (placed is not IPlacedObjectGetter p) { Drop(Skip.NotAPlacedObject, placed); continue; }
            if (p.IsDeleted || (p.MajorRecordFlagsRaw & (FlagDeleted | FlagInitiallyDisabled)) != 0)
            { Drop(Skip.DeletedOrDisabled, p); continue; }
            if (p.VirtualMachineAdapter != null) { Drop(Skip.HasScript, p); continue; }
            if (p.EnableParent != null) { Drop(Skip.HasEnableParent, p); continue; }
            if (p.TeleportDestination != null) { Drop(Skip.HasTeleport, p); continue; }
            if (p.ActivateParents != null) { Drop(Skip.HasActivateParent, p); continue; }
            if (p.LinkedReferences.Count > 0) { Drop(Skip.HasLinkedReference, p); continue; }




            if (p.FormKey.ModKey != ownModKey) { Drop(Skip.NotOwnedByThisPlugin, p); continue; }

            if (p.Base.FormKey.IsNull || !cache.TryResolve<IMajorRecordGetter>(p.Base.FormKey, out var baseRec))
            { Drop(Skip.BaseUnresolved, p); continue; }
            if (baseRec is not IStaticGetter stat) { Drop(Skip.BaseNotStatic, p); continue; }
            if (stat.VirtualMachineAdapter != null) { Drop(Skip.BaseHasScript, p); continue; }
            if (!stat.Model?.MaterialSwap.IsNull ?? false) { Drop(Skip.BaseHasMaterialSwap, p); continue; }

            var model = stat.Model?.File?.Trim();
            if (string.IsNullOrEmpty(model)) { Drop(Skip.BaseHasNoModel, p); continue; }



            var key = model!.Replace('/', '\\').ToLowerInvariant();
            if (!groups.TryGetValue(key, out var list)) groups[key] = list = new List<Instance>();


            var posRot = (Mutagen.Bethesda.Fallout4.IPositionRotationGetter)p;
            list.Add(new Instance(
                p.FormKey.ToString(),
                p.EditorID,
                new[] { posRot.Position.X, posRot.Position.Y, posRot.Position.Z },
                new[] { posRot.Rotation.X, posRot.Rotation.Y, posRot.Rotation.Z },
                p.Scale ?? 1f));
        }

        var kept = groups
            .Where(g => g.Value.Count >= minInstances)
            .Select(g => new ModelGroup(g.Key, g.Value.Count, g.Value.OrderBy(i => i.Reference, StringComparer.Ordinal).ToList()))
            .OrderBy(g => g.ModelPath, StringComparer.Ordinal)
            .ToList();

        var belowThreshold = groups.Count - kept.Count;




        if (groupLimit < 1) groupLimit = 1;
        var groupsOmitted = Math.Max(0, kept.Count - groupLimit);
        var shown = kept.OrderByDescending(g => g.InstanceCount).Take(groupLimit)
                        .OrderBy(g => g.ModelPath, StringComparer.Ordinal).ToList();

        return JsonConvert.SerializeObject(new
        {
            cell = cell.EditorID ?? cell.FormKey.ToString(),
            cellFormKey = cell.FormKey.ToString(),
            plugin = ownModKey.FileName.String,
            interior = true,
            temporaryReferences = considered,
            eligibleReferences = kept.Sum(g => g.InstanceCount),


            groups = shown.Select(g => new
            {
                modelPath = g.ModelPath,
                instanceCount = g.InstanceCount,
                instances = includeInstances
                    ? g.Instances.Take(InstanceCap).Select(i => new
                    {
                        reference = i.Reference,
                        editorId = i.EditorId,
                        position = new { x = i.Position[0], y = i.Position[1], z = i.Position[2] },
                        rotation = new { x = i.Rotation[0], y = i.Rotation[1], z = i.Rotation[2] },
                        scale = i.Scale,
                    }).ToList<object>()
                    : null,
                instancesOmitted = includeInstances ? Math.Max(0, g.InstanceCount - InstanceCap) : g.InstanceCount,
            }),
            groupCount = kept.Count,
            groupsShown = shown.Count,
            groupsOmitted,
            groupsBelowThreshold = belowThreshold,
            minInstances,
            skipped = rejects
                .OrderByDescending(kv => kv.Value.Count)
                .Select(kv => new { reason = Describe(kv.Key), count = kv.Value.Count, examples = kv.Value.Examples })
                .ToList(),
        });
    }
}
