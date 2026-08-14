using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace FO4RecordEditor.Services;

[ClassInterface(ClassInterfaceType.AutoDual)]
[ComVisible(true)]
public class MaterialInterop
{
    public string BrowseForFile(string title, string filter)
    {
        return HostServices.PickFile(title, filter);
    }

    public Task<string> Inspect(string path) =>
        Task.Run(() =>
        {
            try { return MaterialService.InspectJson(path); }
            catch (Exception ex)
            {
                DebugLog.Exception("Material.Inspect", ex);
                return JsonConvert.SerializeObject(new { error = "Error: " + ex.Message });
            }
        });

    public Task<string> SetFields(string path, string fieldsJson, string outPath) =>
        Task.Run(() =>
        {
            try
            {
                var fields = JsonConvert.DeserializeObject<Dictionary<string, string>>(fieldsJson)
                             ?? new Dictionary<string, string>();
                return MaterialService.SetFields(path, fields, string.IsNullOrWhiteSpace(outPath) ? null : outPath);
            }
            catch (Exception ex) { DebugLog.Exception("Material.SetFields", ex); return "Error: " + ex.Message; }
        });

    public string OpenFolder(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path)) return "No path.";
            path = path.Trim().Trim('"');
            if (File.Exists(path)) path = Path.GetDirectoryName(path) ?? path;
            if (!Directory.Exists(path)) return "Folder not found: " + path;
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = path, UseShellExecute = true });
            return "";
        }
        catch (Exception ex) { return "Error: " + ex.Message; }
    }

    public string StageDroppedFile(string name, string base64)
    {
        try
        {
            var safe = Path.GetFileName(string.IsNullOrWhiteSpace(name) ? "dropped.bgsm" : name);
            var dir = Path.Combine(Path.GetTempPath(), "FO4RE_Mat_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, safe);
            File.WriteAllBytes(path, Convert.FromBase64String(base64));
            return path;
        }
        catch (Exception ex) { return "ERR:" + ex.Message; }
    }
}
