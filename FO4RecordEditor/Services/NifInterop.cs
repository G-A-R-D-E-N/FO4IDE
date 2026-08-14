using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace FO4RecordEditor.Services;

[ClassInterface(ClassInterfaceType.AutoDual)]
[ComVisible(true)]
public class NifInterop
{
    public string BrowseForFile(string title, string filter)
    {
        return HostServices.PickFile(title, filter);
    }

    public string BrowseForFolder(string title)
    {
        return HostServices.PickFolder(title);
    }

    public string BrowseForSave(string title, string filter)
    {
        return HostServices.PickSavePath(title,
            string.IsNullOrWhiteSpace(filter) ? "NIF mesh|*.nif|All files|*.*" : filter);
    }

    public Task<string> Import(string objPath, string outNif, string material,
        string texDiffuse, string texNormal, bool collision, bool fromBlender) =>
        Task.Run(() =>
        {
            try { return NifService.Import(objPath, outNif, material, texDiffuse, texNormal, collision, fromBlender); }
            catch (Exception ex) { DebugLog.Exception("Nif.Import", ex); return "Error: " + ex.Message; }
        });

    public Task<string> Inspect(string nifPath) =>
        Task.Run(() =>
        {
            try { return NifService.Inspect(nifPath); }
            catch (Exception ex) { DebugLog.Exception("Nif.Inspect", ex); return "Error: " + ex.Message; }
        });

    public Task<string> Geo(string nifPath) =>
        Task.Run(() =>
        {
            try { return NifService.Geo(nifPath); }
            catch (Exception ex) { DebugLog.Exception("Nif.Geo", ex); return "Error: " + ex.Message; }
        });

    public Task<string> Verify(string nifPath) =>
        Task.Run(() =>
        {
            try { return NifService.Verify(nifPath); }
            catch (Exception ex) { DebugLog.Exception("Nif.Verify", ex); return "Error: " + ex.Message; }
        });

    public Task<string> Fix(string nifPath, string outNif) =>
        Task.Run(() =>
        {
            try { return NifService.Fix(nifPath, outNif); }
            catch (Exception ex) { DebugLog.Exception("Nif.Fix", ex); return "Error: " + ex.Message; }
        });

    public Task<string> Tree(string nifPath) =>
        Task.Run(() =>
        {
            try { return NifService.Tree(nifPath); }
            catch (Exception ex) { DebugLog.Exception("Nif.Tree", ex); return "Error: " + ex.Message; }
        });

    public Task<string> ApplyEdits(string nifPath, string editsJson, string outNif) =>
        Task.Run(() =>
        {
            try { return NifService.ApplyEdits(nifPath, editsJson, outNif); }
            catch (Exception ex) { DebugLog.Exception("Nif.ApplyEdits", ex); return "Error: " + ex.Message; }
        });

    public Task<string> GetTexture(string nifPath, string relTexPath, string textureRoot) =>
        Task.Run(() =>
        {
            try { return TextureService.GetTexturePngDataUrl(nifPath, relTexPath, textureRoot); }
            catch (Exception ex) { DebugLog.Exception("Nif.GetTexture", ex); return ""; }
        });

    public string OpenFolder(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path)) return "No path.";
            path = path.Trim().Trim('"');
            if (File.Exists(path)) path = Path.GetDirectoryName(path) ?? path;
            if (!Directory.Exists(path)) return "Folder not found: " + path;
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
            return "";
        }
        catch (Exception ex) { return "Error: " + ex.Message; }
    }

    public string StageDroppedFile(string name, string base64)
    {
        try
        {
            var safe = Path.GetFileName(string.IsNullOrWhiteSpace(name) ? "dropped.bin" : name);
            var dir = Path.Combine(Path.GetTempPath(), "FO4RE_Nif_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, safe);
            File.WriteAllBytes(path, Convert.FromBase64String(base64));
            return path;
        }
        catch (Exception ex) { return "ERR:" + ex.Message; }
    }
}
