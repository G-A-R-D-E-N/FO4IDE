using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace FO4RecordEditor.Services;

// WebView2 host object for the Audio panel: convert to/from xWMA and merge/split .fuz, via
// AudioService. Same COM rules as the other panel interops: AutoDual, ComVisible, only
// string/bool/int params and string/Task<string> returns. Conversions run on a background task so a
// multi-minute music file never blocks the UI thread.
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

    /// <summary>Any ffmpeg-readable audio/video source -> .xwm. bitrateBps: one of xWMAEncode's
    /// supported bitrates (20000/32000/48000/64000/96000/160000/192000), or 0 for its default (48000).</summary>
    public Task<string> ConvertToXwm(string source, string output, int bitrateBps) =>
        Task.Run(() =>
        {
            try { return AudioService.ConvertToXwm(source, output, bitrateBps > 0 ? bitrateBps : null); }
            catch (Exception ex) { DebugLog.Exception("Audio.ConvertToXwm", ex); return "Error: " + ex.Message; }
        });

    /// <summary>.xwm -> WAV, and on to targetExt (mp3/flac/ogg/...) via ffmpeg if targetExt isn't "wav".</summary>
    public Task<string> ConvertFromXwm(string source, string output, string targetExt) =>
        Task.Run(() =>
        {
            try { return AudioService.ConvertFromXwm(source, output, targetExt); }
            catch (Exception ex) { DebugLog.Exception("Audio.ConvertFromXwm", ex); return "Error: " + ex.Message; }
        });

    /// <summary>Pack an audio source (encoded to xwm first if it isn't already) + optional .lip into a
    /// .fuz voice container.</summary>
    public Task<string> MakeFuz(string audioSource, string lipPath, string fuzOutput, bool noLip) =>
        Task.Run(() =>
        {
            try { return AudioService.MakeFuz(audioSource, lipPath, fuzOutput, noLip); }
            catch (Exception ex) { DebugLog.Exception("Audio.MakeFuz", ex); return "Error: " + ex.Message; }
        });

    /// <summary>Split a .fuz into its xwm/lip parts, optionally also decoding the xwm to .wav.</summary>
    public Task<string> ExtractFuz(string fuzPath, string xwmOutput, string lipOutput, bool alsoWav) =>
        Task.Run(() =>
        {
            try { return AudioService.ExtractFuz(fuzPath, xwmOutput, lipOutput, alsoWav); }
            catch (Exception ex) { DebugLog.Exception("Audio.ExtractFuz", ex); return "Error: " + ex.Message; }
        });

    /// <summary>Open a folder (or a file's folder) in Windows Explorer. Returns "" on success.</summary>
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

    /// <summary>Stage a dropped file's bytes to a temp path and return it (drag-and-drop helper, since
    /// WebView2 hides dropped files' real OS paths). "ERR:" prefix on failure.</summary>
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
