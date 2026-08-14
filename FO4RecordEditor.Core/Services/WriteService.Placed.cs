using Mutagen.Bethesda.Fallout4;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Noggog;

namespace FO4RecordEditor.Services;

/// <summary>
/// Cells and placed references (REFR).
///
/// These cannot go through create_record/AddNewBySig. That switch adds top-level records to a
/// group on the mod (mod.Weapons.Add(x) and friends), but a placed object is not top-level: it
/// lives inside a Cell's Persistent or Temporary list, and Cells themselves live in a
/// Block -> SubBlock -> Cell tree under mod.Cells. Both need their own construction path.
///
/// The block/sub-block plumbing mirrors Mutagen's own ModContextExt cell-copy logic, which is the
/// reference for how a cell must be parented in this mod type.
/// </summary>
public static partial class WriteService
{
    /// <summary>
    /// Create an interior CELL. Interior cells are self-contained (no worldspace, no vanilla cell
    /// override), which makes them the safe place to park references a plugin owns outright -- e.g.
    /// map markers that a script relocates at runtime.
    /// </summary>
    public static string CreateCell(string plugin, string editorId, string? name, object? env)
    {
        var mod = EnsureOpen(plugin, env, out var openMsg);
        if (mod == null) return openMsg;
        if (string.IsNullOrWhiteSpace(editorId)) return "Provide an editorId for the new cell.";

        if (FindCellInMod(mod, editorId) != null)
            return $"A cell '{editorId}' already exists in {plugin}.";

        var fk = NextFreeFormKey(mod);
        if (fk == null) return EslFullMessage(plugin);

        var cell = new Cell(fk.Value, Fallout4Release.Fallout4)
        {
            EditorID = editorId,
            Flags = Cell.Flag.IsInteriorCell,
        };
        if (!string.IsNullOrWhiteSpace(name)) cell.Name = name;

        AttachCellToBlocks(mod, cell);

        MutagenLoader.InvalidateModIndex(plugin);
        NotifyChanged(plugin);
        return $"Created interior CELL '{editorId}' [{cell.FormKey}] in {plugin}. " +
               $"Add references to it with create_placed_object, then save_plugin.";
    }

    /// <summary>
    /// Create a placed reference (REFR) inside a cell this plugin owns.
    /// Set mapMarkerName to attach map-marker data, which is what turns a reference over the vanilla
    /// MapMarker base (000010:Fallout4.esm) into an actual map marker rather than an invisible static.
    /// </summary>
    public static string CreatePlacedObject(
        string plugin, string cellId, string baseId, string? editorId,
        float x, float y, float z, float rotZ,
        bool persistent, bool initiallyDisabled,
        string? mapMarkerName, string? mapMarkerType, bool mapMarkerVisible,
        object? env)
    {
        var mod = EnsureOpen(plugin, env, out var openMsg);
        if (mod == null) return openMsg;

        ICell? cell = FindCellInMod(mod, cellId);
        // #64: the cell isn't already a copy this plugin owns -- try to create that override on the
        // fly. copy_as_override (ResolveConflict/AddOverrideReturning) cannot do this itself: it walks
        // the mod's own TOP-LEVEL group properties via reflection, and Cell is nested inside
        // CellBlock/CellSubBlock (interior) or a Worldspace's block tree (exterior), not a top-level
        // group -- the same limitation WriteService.CellEdit.cs already documented for placed
        // references. ILinkCache.TryResolveContext + IModContext.GetOrAddAsOverride resolves and
        // creates the FULL parent chain regardless of interior/exterior, so this is the one fallback
        // that actually covers both -- previously this doc comment told callers to "copy_as_override
        // an existing cell in first," which never worked for ANY cell, not just exterior ones.
        if (cell == null)
        {
            var cellFk = MutagenLoader.ResolveId(env, cellId);
            if (!cellFk.IsNull &&
                MutagenLoader.LinkCache is Mutagen.Bethesda.Plugins.Cache.ILinkCache<IFallout4Mod, IFallout4ModGetter> cache &&
                cache.TryResolveContext<ICell, ICellGetter>(cellFk, out var ctx))
            {
                try { cell = ctx.GetOrAddAsOverride(mod); }
                catch (Exception ex) { return $"Could not create an override for cell '{cellId}' in {plugin}: {ex.Message}"; }
            }
        }
        if (cell == null)
            return $"Cell '{cellId}' not found in {plugin} and could not be resolved via the load order " +
                   $"either -- create one with create_cell, or check the FormKey/EditorID.";

        if (!ResolveFk(env, baseId, out var baseFk))
            return $"Base object '{baseId}' is not a FormKey and no loaded record has that EditorID.";

        PlacedObjectMapMarker? marker = null;
        if (mapMarkerName != null)
        {
            var type = PlacedObjectMapMarker.Types.Cave;
            if (!string.IsNullOrWhiteSpace(mapMarkerType) &&
                !Enum.TryParse(mapMarkerType, ignoreCase: true, out type))
                return $"'{mapMarkerType}' is not a valid map marker type. Valid: " +
                       string.Join(", ", Enum.GetNames<PlacedObjectMapMarker.Types>());

            marker = new PlacedObjectMapMarker
            {
                Name = mapMarkerName,
                Type = type,
                Flags = mapMarkerVisible ? PlacedObjectMapMarker.Flag.Visible : default,
            };
        }

        var fk = NextFreeFormKey(mod);
        if (fk == null) return EslFullMessage(plugin);

        var refr = new PlacedObject(fk.Value, Fallout4Release.Fallout4)
        {
            Position = new P3Float(x, y, z),
            Rotation = new P3Float(0f, 0f, rotZ),
            MapMarker = marker,
        };
        if (!string.IsNullOrWhiteSpace(editorId)) refr.EditorID = editorId;
        refr.Base.SetTo(baseFk);

        int flags = 0;
        if (persistent) flags |= (int)PlacedObject.DefaultMajorFlag.Persistent;
        if (initiallyDisabled) flags |= (int)PlacedObject.DefaultMajorFlag.InitiallyDisabled;
        refr.MajorRecordFlagsRaw = flags;

        // The Persistent major flag and the Persistent list must agree: the flag alone does not move
        // the reference between a cell's two lists, and the game reads the list it is actually in.
        if (persistent) cell.Persistent.Add(refr);
        else cell.Temporary.Add(refr);

        MutagenLoader.InvalidateModIndex(plugin);
        NotifyChanged(plugin);

        var where = persistent ? "Persistent" : "Temporary";
        var markerNote = marker != null ? $", map marker \"{mapMarkerName}\" ({marker.Type})" : "";
        return $"Created REFR{(editorId != null ? $" '{editorId}'" : "")} [{refr.FormKey}] over {baseFk} " +
               $"in cell '{cell.EditorID ?? cell.FormKey.ToString()}' ({where}){markerNote} in {plugin}. " +
               $"save_plugin to persist.";
    }

    private static string EslFullMessage(string plugin) =>
        $"'{plugin}' is a light plugin (ESL) and its FormID range (0x800-0xFFF) is full. " +
        $"Save it as a .esp (FormID restriction lifted) to add more records.";

    private static Cell? FindCellInMod(IFallout4Mod mod, string id)
    {
        FormKey.TryFactory(id, out var fk);
        foreach (var block in mod.Cells.Records)
            foreach (var sub in block.SubBlocks)
                foreach (var cell in sub.Cells)
                {
                    if (!fk.IsNull && cell.FormKey == fk) return cell;
                    if (string.Equals(cell.EditorID, id, StringComparison.OrdinalIgnoreCase)) return cell;
                }
        return null;
    }

    /// <summary>
    /// Parent a new interior cell into the Block/SubBlock tree, creating either level on demand.
    /// Bethesda's interior convention keys the groups off the cell's own FormID:
    /// block = id % 10, sub-block = (id / 10) % 10.
    /// </summary>
    private static void AttachCellToBlocks(IFallout4Mod mod, Cell cell)
    {
        var id = cell.FormKey.ID;
        var blockNum = (int)(id % 10);
        var subBlockNum = (int)((id / 10) % 10);

        var block = mod.Cells.Records.FirstOrDefault(b => b.BlockNumber == blockNum);
        if (block == null)
        {
            block = new CellBlock { BlockNumber = blockNum, GroupType = GroupTypeEnum.InteriorCellBlock };
            mod.Cells.Records.Add(block);
        }

        var sub = block.SubBlocks.FirstOrDefault(s => s.BlockNumber == subBlockNum);
        if (sub == null)
        {
            sub = new CellSubBlock { BlockNumber = subBlockNum, GroupType = GroupTypeEnum.InteriorCellSubBlock };
            block.SubBlocks.Add(sub);
        }

        sub.Cells.Add(cell);
    }
}
