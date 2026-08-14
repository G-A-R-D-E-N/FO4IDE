using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using FO4RecordEditor.ViewModels;
using Newtonsoft.Json;

namespace FO4RecordEditor.Services;

[ClassInterface(ClassInterfaceType.AutoDual)]
[ComVisible(true)]
public class CellInterop
{
    private readonly ShellViewModel _shell;
    public CellInterop(ShellViewModel shell) => _shell = shell;
    private object? Env => _shell.GameEnvironment;

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

    public Task<string> GetPlugins() =>
        Task.Run(() =>
        {
            try { return JsonConvert.SerializeObject(MutagenLoader.QueryLoadedPlugins(Env)); }
            catch (Exception ex) { DebugLog.Exception("Cell.GetPlugins", ex); return "[]"; }
        });

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

    public string GetGeometryBatchProgress() =>
        JsonConvert.SerializeObject(new { done = NifService.GeoBatchDone, total = NifService.GeoBatchTotal });

    public Task<string> SetPlacedReferenceTransform(
        string formKey, string patchPlugin, float x, float y, float z, float rx, float ry, float rz) =>
        Task.Run(() => DebugLog.Guard(nameof(SetPlacedReferenceTransform),
            () => WriteService.SetPlacedReferenceTransform(Env, formKey, patchPlugin, x, y, z, rx, ry, rz),
            $"{formKey} -> {patchPlugin}"));
}
