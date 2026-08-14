using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace FO4RecordEditor.Services;

[ClassInterface(ClassInterfaceType.AutoDual)]
[ComVisible(true)]
public class ArchiveInterop
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
        return HostServices.PickSavePath(title, filter);
    }

    public Task<string> List(string archivePath, string filter, int limit, string filterMode) =>
        Task.Run(() =>
        {
            try { return ArchiveService.ListArchiveJson(archivePath, string.IsNullOrWhiteSpace(filter) ? null : filter, limit, filterMode); }
            catch (Exception ex)
            {
                DebugLog.Exception("Archive.List", ex);
                return JsonConvert.SerializeObject(new { error = "Error: " + ex.Message });
            }
        });

    public Task<string> ExtractFile(string archivePath, string innerPath, string outPath) =>
        Task.Run(() =>
        {
            try { return ArchiveService.ExtractFile(archivePath, innerPath, outPath); }
            catch (Exception ex) { DebugLog.Exception("Archive.ExtractFile", ex); return "Error: " + ex.Message; }
        });

    public Task<string> ExtractSelected(string archivePath, string innerPathsJson, string outDir) =>
        Task.Run(() =>
        {
            try
            {
                var paths = JsonConvert.DeserializeObject<List<string>>(innerPathsJson) ?? new List<string>();
                return ArchiveService.ExtractSelected(archivePath, paths, outDir);
            }
            catch (Exception ex) { DebugLog.Exception("Archive.ExtractSelected", ex); return "Error: " + ex.Message; }
        });

    public Task<string> ExtractAll(string archivePath, string outDir, string filter, int limit, string filterMode) =>
        Task.Run(() =>
        {
            try { return ArchiveService.ExtractAll(archivePath, outDir, string.IsNullOrWhiteSpace(filter) ? null : filter, limit, filterMode); }
            catch (Exception ex) { DebugLog.Exception("Archive.ExtractAll", ex); return "Error: " + ex.Message; }
        });

    public Task<string> Compare(string archivePathA, string archivePathB) =>
        Task.Run(() =>
        {
            try { return ArchiveService.CompareArchivesJson(archivePathA, archivePathB); }
            catch (Exception ex)
            {
                DebugLog.Exception("Archive.Compare", ex);
                return JsonConvert.SerializeObject(new { error = "Error: " + ex.Message });
            }
        });

    public Task<string> Pack(string sourcePathsJson, string outputBa2, string format, string rootDir, bool compress) =>
        Task.Run(() =>
        {
            try
            {
                var paths = JsonConvert.DeserializeObject<List<string>>(sourcePathsJson) ?? new List<string>();
                return ArchiveService.Pack(paths, outputBa2, format, rootDir, compress);
            }
            catch (Exception ex) { DebugLog.Exception("Archive.Pack", ex); return "Error: " + ex.Message; }
        });

    public string OpenFolder(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path)) return "No path.";
            path = path.Trim().Trim('"');
            if (System.IO.File.Exists(path)) path = System.IO.Path.GetDirectoryName(path) ?? path;
            if (!System.IO.Directory.Exists(path)) return "Folder not found: " + path;
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = path, UseShellExecute = true });
            return "";
        }
        catch (Exception ex) { return "Error: " + ex.Message; }
    }
}
