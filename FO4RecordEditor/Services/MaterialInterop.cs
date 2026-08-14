using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace FO4RecordEditor.Services;

// WebView2 host object for the Materials panel (lives inside the NIF panel in the UI): inspect and
// edit .bgsm shader fields via MaterialService. Same COM rules as NifInterop/PapyrusInterop:
// AutoDual, ComVisible, only string/bool params and string/Task<string> returns. File-parsing calls
// run on a background task so a large or malformed file never blocks the UI thread.
[ClassInterface(ClassInterfaceType.AutoDual)]
[ComVisible(true)]
public class MaterialInterop
{
    public string BrowseForFile(string title, string filter)
    {
        return HostServices.PickFile(title, filter);
    }

    /// <summary>Structured field list (JSON) for the panel to render editable controls from.</summary>
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

    /// <summary>Apply the panel's changed fields in one save. fieldsJson: {"FieldName":"newValue", ...}.
    /// outPath blank = overwrite in place.</summary>
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

    /// <summary>Open a folder (or the folder containing a file) in Windows Explorer. Returns "" on success.</summary>
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

    /// <summary>Stage a dropped file's bytes to a temp path and return it (drag-and-drop helper,
    /// since WebView2 hides dropped files' real OS paths). "ERR:" prefix on failure.</summary>
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
