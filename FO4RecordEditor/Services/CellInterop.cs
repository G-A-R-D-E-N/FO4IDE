using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using FO4RecordEditor.ViewModels;
using Newtonsoft.Json;

namespace FO4RecordEditor.Services;

// WebView2 host object for the Cell Viewer panel: read a CELL's placed references (CellService) and
// batch-resolve+convert their meshes to geometry (NifService.GeoBatch). Reads the env from the shell
// on each call (MastersInterop's pattern) so it always reflects the currently loaded modlist.
// Task<string> returns so a busy cell's reference union / a batch of niftool conversions never block
// the UI thread.
[ClassInterface(ClassInterfaceType.AutoDual)]
[ComVisible(true)]
public class CellInterop
{
    private readonly ShellViewModel _shell;
    public CellInterop(ShellViewModel shell) => _shell = shell;
    private object? Env => _shell.GameEnvironment;

    /// <summary>Placed-reference list (position/rotation/scale/model path) for a cell, unioned across
    /// the whole load order. cellId: a FormKey ("001234:Fallout4.esm") or an EditorID. See
    /// CellService.GetPlacedReferencesJson's doc comment for the union-not-override semantics.</summary>
    public Task<string> GetPlacedReferences(string cellId) =>
        Task.Run(() =>
        {
            try { return CellService.GetPlacedReferencesJson(cellId, Env); }
            catch (Exception ex)
            {
                DebugLog.Exception("Cell.GetPlacedReferences", ex);
                return JsonConvert.SerializeObject(new { error = "Error: " + ex.Message });
            }
        });

    /// <summary>#67: the exterior half of the same lookup -- resolve a cell by worldspace + grid
    /// coordinate, then read its placed references. A separate method rather than optional arguments
    /// on GetPlacedReferences because this is a COM AutoDual host object, which does not expose
    /// overloads or default parameters to the WebView2 script side.</summary>
    public Task<string> GetPlacedReferencesAtGrid(string worldspace, int gridX, int gridY) =>
        Task.Run(() =>
        {
            try { return CellService.GetPlacedReferencesJson("", Env, worldspace, gridX, gridY); }
            catch (Exception ex)
            {
                DebugLog.Exception("Cell.GetPlacedReferencesAtGrid", ex);
                return JsonConvert.SerializeObject(new { error = "Error: " + ex.Message });
            }
        });

    /// <summary>Type-ahead worldspace search for the exterior-cell picker -- the panel has to offer a
    /// worldspace before an X/Y grid coordinate means anything. See
    /// MutagenLoader.SearchWorldspaceRecords.</summary>
    public Task<string> SearchWorldspaces(string query, int limit) =>
        Task.Run(() =>
        {
            try
            {
                var hits = MutagenLoader.SearchWorldspaceRecords(Env, query ?? "", limit <= 0 ? 100 : limit);
                return JsonConvert.SerializeObject(hits);
            }
            catch (Exception ex)
            {
                DebugLog.Exception("Cell.SearchWorldspaces", ex);
                return "[]";
            }
        });

    /// <summary>Batch-resolve+convert unique Data-relative NIF paths (from GetPlacedReferences'
    /// modelPath fields) to geometry JSON, one niftool call per unique mesh. relModelPathsJson: a
    /// JSON array of strings. See NifService.GeoBatch.</summary>
    public Task<string> GetGeometryBatch(string relModelPathsJson) =>
        Task.Run(() =>
        {
            try
            {
                var paths = JsonConvert.DeserializeObject<string[]>(relModelPathsJson) ?? Array.Empty<string>();
                return NifService.GeoBatch(paths);
            }
            catch (Exception ex)
            {
                DebugLog.Exception("Cell.GetGeometryBatch", ex);
                return JsonConvert.SerializeObject(new { error = "Error: " + ex.Message });
            }
        });

    /// <summary>Every plugin name currently loaded (env + loose-opened) -- the panel uses an empty
    /// result to show "load your modlist first" instead of a silent/blind cell-id text box.</summary>
    public Task<string> GetPlugins() =>
        Task.Run(() =>
        {
            try { return JsonConvert.SerializeObject(MutagenLoader.QueryLoadedPlugins(Env)); }
            catch (Exception ex) { DebugLog.Exception("Cell.GetPlugins", ex); return "[]"; }
        });

    /// <summary>Type-ahead cell search across the whole load order (EditorID/Name/FormKey substring,
    /// de-duplicated to the winning version per FormKey) -- backs the picker dropdown so the user
    /// browses/searches instead of typing an exact FormKey or EditorID from memory. Uses
    /// MutagenLoader.SearchCellRecords, NOT SearchAllRecords -- the latter forces a full per-signature
    /// index of every record type in every plugin (cached forever), which on a real modlist eagerly
    /// grows the process by gigabytes for a search that only ever needed CELL records.</summary>
    public Task<string> SearchCells(string query, int limit) =>
        Task.Run(() =>
        {
            try
            {
                var hits = MutagenLoader.SearchCellRecords(Env, query ?? "", limit <= 0 ? 25 : limit);
                return JsonConvert.SerializeObject(hits);
            }
            catch (Exception ex)
            {
                DebugLog.Exception("Cell.SearchCells", ex);
                return "[]";
            }
        });

    /// <summary>Resolve+convert a texture slot for one of the cell's placed meshes to a PNG data URL --
    /// same TextureService pipeline the NIF panel's own "View" mode already uses successfully for
    /// BA2-packed vanilla textures (loose file search, then archive extraction, Texconv DDS->PNG,
    /// cached by source path + write time). relModelPath is the SAME Data-relative path
    /// GetPlacedReferences/GetGeometryBatch use (e.g. "Meshes\Furniture\ParkBench01.nif"); re-resolved
    /// here rather than threading the resolved path through GeoBatch's response, to avoid a schema
    /// change -- ResolveNif is a cheap lookup, not a conversion.</summary>
    public Task<string> GetTexture(string relModelPath, string relTexPath) =>
        Task.Run(() =>
        {
            try
            {
                var resolved = TextureService.ResolveNif(relModelPath);
                if (resolved == null) return "";
                return TextureService.GetTexturePngDataUrl(resolved, relTexPath, "");
            }
            catch (Exception ex) { DebugLog.Exception("Cell.GetTexture", ex); return ""; }
        });

    /// <summary>Poll during an in-flight GetGeometryBatch call for a real N/total progress readout,
    /// instead of an indeterminate spinner -- see NifService's GeoBatchDone/GeoBatchTotal.</summary>
    public string GetGeometryBatchProgress() =>
        JsonConvert.SerializeObject(new { done = NifService.GeoBatchDone, total = NifService.GeoBatchTotal });

    /// <summary>Gizmo drag-end save: write a moved/rotated placed reference's new Position/Rotation
    /// into an override in patchPlugin (created if it doesn't exist yet) and leave it open in memory --
    /// the caller still has to call backend.SavePlugin(patchPlugin, "") to write it to disk, same as
    /// every other patch-plugin edit in this app. See WriteService.SetPlacedReferenceTransform.</summary>
    public Task<string> SetPlacedReferenceTransform(
        string formKey, string patchPlugin, float x, float y, float z, float rx, float ry, float rz) =>
        Task.Run(() => DebugLog.Guard(nameof(SetPlacedReferenceTransform),
            () => WriteService.SetPlacedReferenceTransform(Env, formKey, patchPlugin, x, y, z, rx, ry, rz),
            $"{formKey} -> {patchPlugin}"));
}
