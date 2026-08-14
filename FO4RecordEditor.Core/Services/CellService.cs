using System.Linq;
using Mutagen.Bethesda.Fallout4;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Noggog;
using JsonConvert = Newtonsoft.Json.JsonConvert;

namespace FO4RecordEditor.Services;

/// <summary>
/// Reads a CELL's placed references (REFR/ACHR/traps) for the 3D cell viewer -- position, rotation,
/// scale, and (for anything with a static model) the NIF path to render. No Mutagen call resolves
/// this in one shot: a placed reference only carries a FormLink to its base object, and only some
/// base object types (statics, activators, containers, doors, furniture, trees, flora, weapons, ...)
/// carry a Model subrecord at all -- actors and traps don't, so those come back with modelPath=null
/// and the viewer just draws a marker instead of a mesh for them.
///
/// This is read-only. It is NOT the write-side CreateCell/CreatePlacedObject path in
/// WriteService.Placed.cs, which walks a single mutable mod's own Cells tree instead (that's for
/// authoring a cell this plugin owns, not for viewing whichever cell is actually loaded).
///
/// A cell's REFR contents are UNIONED across the whole load order, not simple override-wins, unlike
/// almost every other record type in this app. Confirmed empirically, not assumed: the plain "winning
/// record" resolution (cache.TryResolve, same as get_record/list_records) returned 0 references for
/// a real busy interior cell whose individual per-plugin copies had thousands between them -- the
/// game engine loads every plugin's own Temporary/Persistent children for a given cell FormID
/// additively, so a plugin that overrides the cell's water height (say) without touching references
/// at all still "wins" the CELL record with EMPTY reference lists, silently hiding everyone else's
/// placed objects if you only look at the winning copy. So: scalar cell metadata (name, interior
/// flag) comes from the winning record, but the reference list comes from
/// MutagenLoader.GetRecordContexts walking every plugin's own copy in load order and merging
/// per-REFR-FormKey (later plugin wins that one reference; IsDeleted removes it), the same
/// last-plugin-wins semantics the game itself applies to an individual reference override.
/// </summary>
public static class CellService
{
    /// <summary>
    /// #64: resolve an EXTERIOR cell by worldspace + grid coordinate instead of by FormKey/EditorID.
    /// Verified against Mutagen's actual Worldspace layout before writing this, not assumed: a
    /// worldspace's exterior cells are NOT a flat list, they are nested Worldspace.SubCells
    /// (WorldspaceBlock) -> WorldspaceBlock.Items (WorldspaceSubBlock) -> WorldspaceSubBlock.Items
    /// (the actual Cell records), and each Cell's grid position is Cell.Grid.Point (a P2Int X/Y),
    /// not a FormLink back to its parent worldspace -- the relationship is purely structural
    /// (confirmed via Worldspace_Generated.cs / WorldspaceSubBlock_Generated.cs).
    ///
    /// Walks every plugin's own copy of the worldspace (GetRecordContexts, same reasoning as the
    /// interior-cell reference union below: a mod adding a new exterior cell authors its own copy of
    /// the block/subblock tree at that grid position) and returns the first cell FormKey found at
    /// (gridX, gridY) -- every plugin's copy of the SAME logical cell shares one FormKey, so the
    /// first hit is enough once resolved, this just widens the search past whichever plugin happens
    /// to be resolved as the worldspace's own "winning" copy.
    /// </summary>
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

    // Shared by GetPlacedReferencesJson and CleanupPlacedReferencesJson (#60): resolve either a
    // direct cell_id, or (when that's blank) a worldspace+grid pair via ResolveExteriorCellFormKey.
    /// <summary>Public wrapper so PrecombineService (#72) reaches the same cell_id / worldspace+grid
    /// resolution the cell tools use, rather than growing a second one.</summary>
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

        // Union every plugin's own copy of this cell's reference lists -- see class doc for why the
        // winning-record-only approach silently drops references.
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
            // IPlacedGetter itself carries no spatial data -- Position/Rotation live on the concrete
            // types (PlacedObject/PlacedNpc/APlacedTrap) via IPositionRotationGetter, which every
            // real-world placed type implements, so this only skips something genuinely degenerate.
            if (placed is not IPositionRotationGetter posRot) continue;

            string? modelPath = null;
            string? baseEditorId = null;
            string baseFormKey = "";
            // The base object's record type (Static/Light/Furniture/MoveableStatic/...) -- the useful
            // grouping for the viewer's layer list. `recordType` below is the PLACED record's own kind
            // (PlacedObject/PlacedNpc/PlacedTrap), NOT the base -- a PlacedObject is "PlacedObject"
            // regardless of what it's placed as, so grouping on baseType is what actually matters.
            string? baseType = null;
            float scale = 1f;
            // A shape with no texture of its own can render via a ground decal instead: the base is
            // a TextureSet (TXST) with no Model field at all, carrying a Decal subrecord (Diffuse
            // texture + Width/Height/Depth/Flags). Emitted alongside modelPath (which stays null for
            // these) so the viewer can draw a textured decal plane instead of a "no model" marker.
            string? decalDiffuse = null;
            float? decalWidth = null;
            float? decalHeight = null;
            // Static Collections (SCOL) bake a precombined Model.File, but many mods ship the SCOL
            // record without running the CK's "generate precombine" step, so that path never
            // resolves. The engine's own fallback is to render each member static at its own local
            // placement; this exposes that fallback so the viewer can do the same instead of showing
            // a "mesh unavailable" marker for what's often a large chunk of a cell's structure.
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
                        // Mutagen's lazy binary-overlay wrapper class is named "<RealType>BinaryOverlay";
                        // strip it so the layer list groups by the real record kind ("Static",
                        // "TextureSet", ...) instead of carrying this internal loading-detail suffix.
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
                            // ObjectBounds shares the world-unit convention everything else here
                            // renders in; its X/Y extent is a reasonable stand-in for the decal's
                            // footprint. Falls back to the Decal subrecord's own Width/Height only
                            // if ObjectBounds is degenerate (both zero).
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

    // "Special" data that makes a PlacedObject worth keeping over an apparent duplicate -- ported
    // from xEdit's "Remove duplicate references.pas" IsSpecial() check (EDID/VMAD/XESP/XTEL/XLKR/
    // Patrol/ActivateParents, or referenced by a PACK). PACK-reference checking is not included: it
    // needs a full referenced-by index across the load order for one field, which is a much bigger
    // cost than this check is worth here; flagged in the tool description, not silently dropped.
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

    /// <summary>
    /// xEdit's "Remove duplicate references" and "Remove excess references" (#60), combined behind a
    /// mode switch. Scoped to PlacedObject (REFR) specifically -- the large majority of real
    /// duplicate-clutter cases -- not xEdit's full signature list (REFR/ACHR/ACRE/PMIS/PHZD/PGRE/
    /// PARW/PBAR/PBEA/PCON/PFLA), matching what this codebase's own GetPlacedReferencesJson already
    /// resolves base-object data for. Dry-run by default.
    ///
    /// mode "dedupe": two PlacedObjects sharing the same base record (or base model, if byModel),
    /// rounded position/rotation, and scale are duplicates -- xEdit's own definition, ported from the
    /// real script, not invented. Keeps the one with "special" data over one without. If NEITHER is
    /// special, keeps the first-seen and removes the rest, deterministic by list order (the source
    /// script has no tie-breaking either -- ties just keep whichever the scan reaches first).
    ///
    /// If BOTH are special, neither is removed and neither is reported. "Special" means the
    /// reference carries an EditorID, a script, an enable parent, a teleport destination, linked
    /// references, a patrol or activate parents -- i.e. something else points at it or it does
    /// something on its own. Deleting one of a pair like that can break whatever refers to it, and
    /// there is no way to tell from the cell which one is safe, so this errs toward keeping both. A
    /// human can still remove one deliberately; a bulk pass should not.
    ///
    /// mode "excess": xEdit's script removes RANDOM temporary references over a cap. That is a
    /// reasonable choice for a human clicking a button on clutter they are not inspecting individually,
    /// but has no equivalent value for a scripted workflow, where which specific duplicate survives
    /// should be reproducible. This removes the LAST N over the cap (by list order) instead of random
    /// ones -- a deliberate deviation from the source script, not an oversight.
    /// </summary>
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

        // Union across the load order first (same reasoning as GetPlacedReferencesJson: a plugin can
        // "win" a CELL with an empty reference list while every other plugin's own refs still load),
        // so the plan is computed against what actually loads in-game, not just one plugin's copy.
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
                if (pSpecial && existingSpecial) continue; // both special: report neither, keep both
                if (existingSpecial) { toRemove.Add((p.FormKey, $"duplicate of {existing.FormKey} (existing is special)")); continue; }
                if (pSpecial) { seen[token] = p; toRemove.Add((existing.FormKey, $"duplicate of {p.FormKey} (this one is special)")); continue; }
                toRemove.Add((p.FormKey, $"duplicate of {existing.FormKey}"));
            }
        }
        else if (mode.Equals("excess", StringComparison.OrdinalIgnoreCase))
        {
            // Only Temporary refs count toward the cap, matching xEdit's "not GetIsPersistent" gate.
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

        // NormalizePlugin/FindPluginPath are private to WriteService (this is a different class) --
        // mirrors NormalizePlugin's own bare-name-vs-full-path logic rather than reaching into it.
        var patchName = (patchPlugin.Contains('\\') || patchPlugin.Contains('/')) && System.IO.Path.IsPathRooted(patchPlugin)
            ? System.IO.Path.GetFileName(patchPlugin) : patchPlugin;
        if (WriteService.GetMutable(patchName) == null)
        {
            WriteService.OpenPlugin(patchPlugin, env);   // silently no-ops if not found on disk
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
