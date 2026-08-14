using System.Linq;
using Mutagen.Bethesda.Fallout4;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Noggog;
using JsonConvert = Newtonsoft.Json.JsonConvert;

namespace FO4RecordEditor.Services;

public static class CellService
{

    public static string? ResolveExteriorCellFormKey(object? env, string worldspaceId, int gridX, int gridY, out string? error)
    {
        var wsFk = MutagenLoader.ResolveId(env, worldspaceId.Trim());
        if (wsFk.IsNull) { error = $"Could not resolve worldspace '{worldspaceId}' to a loaded WRLD record."; return null; }

        foreach (var (_, rec) in MutagenLoader.GetRecordContexts(env, wsFk))
        {
            if (rec is not IWorldspaceGetter ws) continue;
            foreach (var block in ws.SubCells)
                foreach (var subBlock in block.Items)
                    foreach (var cell in subBlock.Items)
                        if (cell.Grid != null && cell.Grid.Point.X == gridX && cell.Grid.Point.Y == gridY)
                        {
                            error = null;
                            return cell.FormKey.ToString();
                        }
        }
        error = $"No exterior cell found at grid ({gridX},{gridY}) in worldspace '{worldspaceId}'.";
        return null;
    }

    public static bool TryResolveCellIdPublic(object? env, ref string cellId, string? worldspace, int? gridX, int? gridY, out string? error)
        => TryResolveCellId(env, ref cellId, worldspace, gridX, gridY, out error);

    private static bool TryResolveCellId(object? env, ref string cellId, string? worldspace, int? gridX, int? gridY, out string? error)
    {
        if (string.IsNullOrWhiteSpace(cellId) && !string.IsNullOrWhiteSpace(worldspace) && gridX.HasValue && gridY.HasValue)
        {
            var resolved = ResolveExteriorCellFormKey(env, worldspace, gridX.Value, gridY.Value, out var gridErr);
            if (resolved == null) { error = gridErr; return false; }
            cellId = resolved;
        }
        if (string.IsNullOrWhiteSpace(cellId))
        {
            error = "Provide 'cell_id' (a FormKey like '001234:Fallout4.esm', or an EditorID), or 'worldspace' + 'grid_x' + 'grid_y' for an exterior cell.";
            return false;
        }
        error = null;
        return true;
    }

    public static string GetPlacedReferencesJson(string cellId, object? env, string? worldspace = null, int? gridX = null, int? gridY = null)
    {
        if (!TryResolveCellId(env, ref cellId, worldspace, gridX, gridY, out var resolveErr))
            return JsonConvert.SerializeObject(new { error = resolveErr });

        var cache = MutagenLoader.LinkCache;
        if (cache == null)
            return JsonConvert.SerializeObject(new { error = "No environment loaded. Load Env or Open MO2 first." });

        var fk = MutagenLoader.ResolveId(env, cellId.Trim());
        if (fk.IsNull || !cache.TryResolve<ICellGetter>(fk, out var winningCell))
            return JsonConvert.SerializeObject(new { error = $"Could not resolve '{cellId}' to a loaded CELL record." });

        var interior = winningCell.Flags.HasFlag(Cell.Flag.IsInteriorCell);

        var merged = new Dictionary<FormKey, IPlacedGetter>();
        foreach (var (_, rec) in MutagenLoader.GetRecordContexts(env, fk))
        {
            if (rec is not ICellGetter cellCopy) continue;
            foreach (var placed in cellCopy.Persistent.Concat(cellCopy.Temporary))
            {
                if (placed.IsDeleted) { merged.Remove(placed.FormKey); continue; }
                merged[placed.FormKey] = placed;
            }
        }

        var refs = new List<object>();
        var withModel = 0;
        foreach (var placed in merged.Values)
        {

            if (placed is not IPositionRotationGetter posRot) continue;

            string? modelPath = null;
            string? baseEditorId = null;
            string baseFormKey = "";

            string? baseType = null;
            float scale = 1f;

            string? decalDiffuse = null;
            float? decalWidth = null;
            float? decalHeight = null;

            List<object>? scolParts = null;

            if (placed is IPlacedObjectGetter po)
            {
                scale = po.Scale ?? 1f;
                if (!po.Base.FormKey.IsNull)
                {
                    baseFormKey = po.Base.FormKey.ToString();
                    if (cache.TryResolve<IMajorRecordGetter>(po.Base.FormKey, out var baseRec))
                    {
                        baseEditorId = baseRec.EditorID;

                        baseType = baseRec.GetType().Name;
                        if (baseType.EndsWith("BinaryOverlay", StringComparison.Ordinal))
                            baseType = baseType[..^"BinaryOverlay".Length];
                        if (baseRec is IModeledGetter modeled) modelPath = modeled.Model?.File;
                        if (baseRec is IStaticCollectionGetter scol)
                        {
                            var parts = new List<object>();
                            foreach (var part in scol.Parts)
                            {
                                if (part.Static.IsNull) continue;
                                if (!cache.TryResolve<IMajorRecordGetter>(part.Static.FormKey, out var partRec)) continue;
                                if (partRec is not IModeledGetter partModeled || string.IsNullOrWhiteSpace(partModeled.Model?.File)) continue;
                                var placements = part.Placements;
                                if (placements == null || placements.Count == 0) continue;
                                parts.Add(new
                                {
                                    modelPath = partModeled.Model!.File,
                                    placements = placements.Select(p => new
                                    {
                                        x = p.Position.X, y = p.Position.Y, z = p.Position.Z,
                                        rx = p.Rotation.X, ry = p.Rotation.Y, rz = p.Rotation.Z,
                                        scale = p.Scale,
                                    }).ToList(),
                                });
                            }
                            if (parts.Count > 0) scolParts = parts;
                        }
                        if (baseRec is ITextureSetGetter txst && !string.IsNullOrWhiteSpace(txst.Diffuse))
                        {
                            decalDiffuse = txst.Diffuse;

                            var b = baseRec is IObjectBoundedGetter ob ? ob.ObjectBounds : null;
                            var bw = b != null ? Math.Abs(b.Second.X - b.First.X) : 0f;
                            var bh = b != null ? Math.Abs(b.Second.Y - b.First.Y) : 0f;
                            if (bw > 0.01f && bh > 0.01f) { decalWidth = bw; decalHeight = bh; }
                            else if (txst.Decal != null) { decalWidth = txst.Decal.MinWidth; decalHeight = txst.Decal.MinHeight; }
                        }
                    }
                }
            }

            if (modelPath != null) withModel++;

            string recordType = placed switch
            {
                IPlacedObjectGetter => "PlacedObject",
                IPlacedNpcGetter => "PlacedNpc",
                IAPlacedTrapGetter => "PlacedTrap",
                _ => placed.GetType().Name,
            };

            refs.Add(new
            {
                formKey = placed.FormKey.ToString(),
                editorId = placed.EditorID,
                recordType,
                baseType,
                baseFormKey,
                baseEditorId,
                modelPath,
                decalDiffuse,
                decalWidth,
                decalHeight,
                scolParts,
                position = new { x = posRot.Position.X, y = posRot.Position.Y, z = posRot.Position.Z },
                rotation = new { x = posRot.Rotation.X, y = posRot.Rotation.Y, z = posRot.Rotation.Z },
                scale,
            });
        }

        return JsonConvert.SerializeObject(new
        {
            cellFormKey = winningCell.FormKey.ToString(),
            cellEditorId = winningCell.EditorID,
            cellName = winningCell.Name?.String,
            interior,
            referenceCount = refs.Count,
            withModelCount = withModel,
            references = refs,
        });
    }

    private static bool IsSpecialPlacedObject(IPlacedObjectGetter p) =>
        !string.IsNullOrEmpty(p.EditorID) ||
        p.VirtualMachineAdapter != null ||
        p.EnableParent != null ||
        p.TeleportDestination != null ||
        p.LinkedReferences.Count > 0 ||
        p.Patrol != null ||
        p.ActivateParents != null;

    private static string DedupeToken(IPlacedObjectGetter p, Mutagen.Bethesda.Plugins.Cache.ILinkCache cache, bool byModel)
    {
        string baseId = "";
        if (!p.Base.FormKey.IsNull && cache.TryResolve<IMajorRecordGetter>(p.Base.FormKey, out var baseRec))
            baseId = byModel && baseRec is IModeledGetter modeled ? (modeled.Model?.File ?? "") : (baseRec.EditorID ?? baseRec.FormKey.ToString());
        var scale = p.Scale ?? 1f;
        return $"{baseId}|{scale}|{Math.Round(p.Position.X)}|{Math.Round(p.Position.Y)}|{Math.Round(p.Position.Z)}|" +
               $"{Math.Round(p.Rotation.X)}|{Math.Round(p.Rotation.Y)}|{Math.Round(p.Rotation.Z)}";
    }

    public static string CleanupPlacedReferencesJson(object? env, string cellId, string? worldspace, int? gridX, int? gridY,
        string mode, int maxCount, bool byModel, string patchPlugin, bool apply)
    {
        if (!TryResolveCellId(env, ref cellId, worldspace, gridX, gridY, out var resolveErr))
            return JsonConvert.SerializeObject(new { error = resolveErr });

        if (MutagenLoader.LinkCache is not Mutagen.Bethesda.Plugins.Cache.ILinkCache<IFallout4Mod, IFallout4ModGetter> cache)
            return JsonConvert.SerializeObject(new { error = "No environment loaded. Load Env or Open MO2 first." });

        var fk = MutagenLoader.ResolveId(env, cellId.Trim());
        if (fk.IsNull || !cache.TryResolve<ICellGetter>(fk, out var winningCell))
            return JsonConvert.SerializeObject(new { error = $"Could not resolve '{cellId}' to a loaded CELL record." });

        var merged = new Dictionary<FormKey, IPlacedObjectGetter>();
        foreach (var (_, rec) in MutagenLoader.GetRecordContexts(env, fk))
        {
            if (rec is not ICellGetter cellCopy) continue;
            foreach (var placed in cellCopy.Temporary.Concat(cellCopy.Persistent))
            {
                if (placed.IsDeleted) { merged.Remove(placed.FormKey); continue; }
                if (placed is IPlacedObjectGetter po) merged[placed.FormKey] = po;
            }
        }

        var toRemove = new List<(FormKey formKey, string reason)>();
        if (mode.Equals("dedupe", StringComparison.OrdinalIgnoreCase))
        {
            var seen = new Dictionary<string, IPlacedObjectGetter>();
            foreach (var p in merged.Values)
            {
                var token = DedupeToken(p, cache, byModel);
                if (!seen.TryGetValue(token, out var existing)) { seen[token] = p; continue; }

                bool pSpecial = IsSpecialPlacedObject(p), existingSpecial = IsSpecialPlacedObject(existing);
                if (pSpecial && existingSpecial) continue;
                if (existingSpecial) { toRemove.Add((p.FormKey, $"duplicate of {existing.FormKey} (existing is special)")); continue; }
                if (pSpecial) { seen[token] = p; toRemove.Add((existing.FormKey, $"duplicate of {p.FormKey} (this one is special)")); continue; }
                toRemove.Add((p.FormKey, $"duplicate of {existing.FormKey}"));
            }
        }
        else if (mode.Equals("excess", StringComparison.OrdinalIgnoreCase))
        {

            var temp = merged.Values.Where(p => (p.MajorRecordFlagsRaw & (int)PlacedObject.DefaultMajorFlag.Persistent) == 0).ToList();
            if (temp.Count > maxCount)
                foreach (var p in temp.Skip(maxCount))
                    toRemove.Add((p.FormKey, $"excess (cap {maxCount}, cell has {temp.Count} temporary refs)"));
        }
        else
        {
            return JsonConvert.SerializeObject(new { error = $"Unknown mode '{mode}'. Use 'dedupe' or 'excess'." });
        }

        if (!apply)
            return JsonConvert.SerializeObject(new
            {
                dryRun = true,
                cellFormKey = fk.ToString(),
                wouldRemove = toRemove.Count,
                plan = toRemove.Select(r => new { formKey = r.formKey.ToString(), reason = r.reason }),
            });

        if (string.IsNullOrWhiteSpace(patchPlugin))
            return JsonConvert.SerializeObject(new { error = "Choose a patch plugin to write the removals into." });

        var patchName = (patchPlugin.Contains('\\') || patchPlugin.Contains('/')) && System.IO.Path.IsPathRooted(patchPlugin)
            ? System.IO.Path.GetFileName(patchPlugin) : patchPlugin;
        if (WriteService.GetMutable(patchName) == null)
        {
            WriteService.OpenPlugin(patchPlugin, env);
            if (WriteService.GetMutable(patchName) == null) WriteService.CreatePlugin(patchPlugin);
        }
        var patch = WriteService.GetMutable(patchName);
        if (patch == null) return JsonConvert.SerializeObject(new { error = $"Could not open or create patch plugin '{patchName}'." });

        if (!cache.TryResolveContext<ICell, ICellGetter>(fk, out var ctx))
            return JsonConvert.SerializeObject(new { error = $"Could not resolve {fk}'s parent chain via the link cache." });

        ICell ovr;
        try { ovr = ctx.GetOrAddAsOverride(patch); }
        catch (Exception ex) { return JsonConvert.SerializeObject(new { error = $"Could not create the cell override in '{patchName}': {ex.Message}" }); }

        var removeSet = new HashSet<FormKey>(toRemove.Select(r => r.formKey));
        var before = ovr.Persistent.Count + ovr.Temporary.Count;
        ovr.Persistent.RemoveWhere(p => removeSet.Contains(p.FormKey));
        ovr.Temporary.RemoveWhere(p => removeSet.Contains(p.FormKey));
        var removed = before - (ovr.Persistent.Count + ovr.Temporary.Count);

        MutagenLoader.InvalidateModIndex(patchName);
        WriteService.NotifyPluginChanged(patchName);

        return JsonConvert.SerializeObject(new
        {
            dryRun = false,
            cellFormKey = fk.ToString(),
            removed,
            planned = toRemove.Count,
            plan = toRemove.Select(r => new { formKey = r.formKey.ToString(), reason = r.reason }),
        });
    }
}
