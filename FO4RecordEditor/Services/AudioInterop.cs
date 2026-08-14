using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace FO4RecordEditor.Services;

[ClassInterface(ClassInterfaceType.AutoDual)]
[ComVisible(true)]
public class AudioInterop
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

    public Task<string> ConvertToXwm(string source, string output, int bitrateBps) =>
        Task.Run(() =>
        {
            try { return AudioService.ConvertToXwm(source, output, bitrateBps > 0 ? bitrateBps : null); }
            catch (Exception ex) { DebugLog.Exception("Audio.ConvertToXwm", ex); return "Error: " + ex.Message; }
        });

    public Task<string> ConvertFromXwm(string source, string output, string targetExt) =>
        Task.Run(() =>
        {
            try { return AudioService.ConvertFromXwm(source, output, targetExt); }
            catch (Exception ex) { DebugLog.Exception("Audio.ConvertFromXwm", ex); return "Error: " + ex.Message; }
        });

    public Task<string> MakeFuz(string audioSource, string lipPath, string fuzOutput, bool noLip) =>
        Task.Run(() =>
        {
            try { return AudioService.MakeFuz(audioSource, lipPath, fuzOutput, noLip); }
            catch (Exception ex) { DebugLog.Exception("Audio.MakeFuz", ex); return "Error: " + ex.Message; }
        });

    public Task<string> ExtractFuz(string fuzPath, string xwmOutput, string lipOutput, bool alsoWav) =>
        Task.Run(() =>
        {
            try { return AudioService.ExtractFuz(fuzPath, xwmOutput, lipOutput, alsoWav); }
            catch (Exception ex) { DebugLog.Exception("Audio.ExtractFuz", ex); return "Error: " + ex.Message; }
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
            var safe = Path.GetFileName(string.IsNullOrWhiteSpace(name) ? "dropped.audio" : name);
            var dir = Path.Combine(Path.GetTempPath(), "FO4RE_Audio_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, safe);
            File.WriteAllBytes(path, Convert.FromBase64String(base64));
            return path;
        }
        catch (Exception ex) { return "ERR:" + ex.Message; }
    }
}
