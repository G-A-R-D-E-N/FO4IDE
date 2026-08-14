using System;
using System.Linq;
using System.Runtime.InteropServices;
using FO4RecordEditor.Models;
using FO4RecordEditor.ViewModels;
using Newtonsoft.Json;

namespace FO4RecordEditor.Services;

// WebView2 host object. Use a clean AutoDual class with NO explicit COM interface and NO generic
// delegate in the constructor: both confuse the generated IDispatch type info and cause
// "Member not found" (0x80020003) / "Invalid number of parameters" (0x8002000E) from JS.
// Every method takes/returns only COM-friendly types (string / void).
[ClassInterface(ClassInterfaceType.AutoDual)]
[ComVisible(true)]
public class AppInterop
{
    private readonly ShellViewModel _shell;
    // Pushed progress to the React UI: (message, percent). percent is 0-100, or negative to hide the
    // bar. MainWindow supplies this and forwards each report to the page as a web message.
    private readonly Action<string, double?>? _onProgress;

    public AppInterop(ShellViewModel shell, Action<string, double?>? onProgress = null)
    {
        _shell = shell;
        _onProgress = onProgress;
    }

    // Marshal IProgress reports from the loader to the UI sink. Created on the UI thread (host-object
    // calls run there), so Progress<T> posts callbacks back to the UI thread where web messages are valid.
    private IProgress<(string message, double? percent)>? MakeProgress() =>
        _onProgress == null
            ? null
            : new Progress<(string message, double? percent)>(p => _onProgress(p.message, p.percent));

    /// <summary>Show a native folder picker for the MO2 instance folder (the one with 'mods' and
    /// 'profiles'). Returns the chosen path, or "" if cancelled or not a valid MO2 instance.</summary>
    public string BrowseForMo2Folder()
    {
        var last = _shell.Settings.Current.Mo2InstancePath;
        var seed = !string.IsNullOrWhiteSpace(last) && System.IO.Directory.Exists(last) ? last : "";
        var instance = HostServices.PickFolder(
            "Select your Mod Organizer 2 instance folder (the one containing 'mods' and 'profiles')", seed);
        if (string.IsNullOrWhiteSpace(instance)) return "";

        if (!System.IO.Directory.Exists(System.IO.Path.Combine(instance, "profiles")))
        {
            HostServices.ShowMessage(
                "That folder doesn't look like an MO2 instance -- it has no 'profiles' subfolder. " +
                "Pick the instance folder that contains 'mods', 'profiles', and 'ModOrganizer.ini'.");
            return "";
        }
        return instance;
    }

    // async Task (not void) so the JS caller can await the full load, then refresh the tree. The
    // finally hides the progress bar whether the load succeeded, failed, or loaded zero plugins.
    public async System.Threading.Tasks.Task OpenMo2Profile(string instancePath)
    {
        DebugLog.Interop(nameof(OpenMo2Profile), instancePath);
        try { await _shell.LoadMo2ProfileAsync(instancePath, MakeProgress()); }
        catch (Exception ex) { DebugLog.Exception($"OpenMo2Profile({instancePath})", ex); throw; }
        finally { _onProgress?.Invoke("", -1); }
    }

    public async System.Threading.Tasks.Task LoadEnvironment()
    {
        DebugLog.Interop(nameof(LoadEnvironment));
        try { await _shell.LoadEnvironmentAsync(MakeProgress()); }
        catch (Exception ex) { DebugLog.Exception("LoadEnvironment", ex); throw; }
        finally { _onProgress?.Invoke("", -1); }
    }

    /// <summary>Run the full load-order conflict scan (cached + shared with the AI's scan_conflicts
    /// tool), populate conflict state for tree tinting, and return a short summary.</summary>
    public async System.Threading.Tasks.Task<string> ScanConflicts()
    {
        DebugLog.Interop(nameof(ScanConflicts));
        var env = _shell.GameEnvironment;
        if (env == null) return "No environment loaded -- Load Env or Open MO2 first.";
        var prog = MakeProgress();
        try
        {
            var conflicts = await ConflictScanner.ScanAsync(env, m => prog?.Report((m, null)));
            ConflictState.Set(conflicts);
            var mods = conflicts.Count(c => c.InvolvesMod);
            DebugLog.Info("App", $"ScanConflicts: {conflicts.Count} records, {mods} involve mods");
            return $"{conflicts.Count} conflicting record(s); {mods} involve mods.";
        }
        catch (Exception ex) { DebugLog.Exception("ScanConflicts", ex); return "Scan failed: " + ex.Message; }
        finally { _onProgress?.Invoke("", -1); }
    }

    public async System.Threading.Tasks.Task<string> ScanBrokenRefs()
    {
        DebugLog.Interop(nameof(ScanBrokenRefs));
        var env = _shell.GameEnvironment;
        if (env == null) return "No environment loaded -- Load Env or Open MO2 first.";
        _onProgress?.Invoke("Scanning for broken references...", null);
        try
        {
            return await System.Threading.Tasks.Task.Run(() =>
            {
                try { return MutagenLoader.ScanAllPluginsForBrokenRefs(env); }
                catch (Exception ex) { DebugLog.Exception("ScanBrokenRefs", ex); return "Scan failed: " + ex.Message; }
            });
        }
        finally { _onProgress?.Invoke("", -1); }
    }

    /// <summary>Rebuild the plugin tree from the current load order + AI edits (fresh lazy nodes), so
    /// changes the AI just made show on re-expand. Does NOT reload the environment.</summary>
    public string RefreshTree()
    {
        DebugLog.Interop(nameof(RefreshTree));
        try { _shell.RefreshPluginTree(); }
        catch (System.Exception ex) { DebugLog.Exception("RefreshTree", ex); }
        return GetPlugins();
    }

    public string GetPlugins()
    {
        try
        {
            var data = _shell.Plugins.Select(p => new
            {
                p.Key,
                p.ConflictStatus,
                p.IsRecordNode,
                HasChildren = p.Children.Count > 0,
                p.FilePath
            }).ToList();
            return JsonConvert.SerializeObject(data);
        }
        catch (System.Exception ex)
        {
            DebugLog.Exception("GetPlugins", ex);
            return JsonConvert.SerializeObject(new[]
            {
                new { Key = "Error: " + ex.Message, ConflictStatus = 0, IsRecordNode = false, HasChildren = false, FilePath = "" }
            });
        }
    }

    public async System.Threading.Tasks.Task<string> GetChildren(string path)
    {
        DebugLog.Interop(nameof(GetChildren), path);
        var node = FindNode(path);
        if (node == null) { DebugLog.Debug("Interop", $"GetChildren: node not found for {path}"); return "[]"; }

        // Lazily materialize: a freshly-listed plugin/group has a single "Loading..." dummy child
        // carrying _NeedsGroups (plugin -> top-level groups) or _NeedsRecords ("plugin|SIG" -> records).
        // The old WPF Tree_Expanded handler loaded these on expand; in the WebView2 shell this method
        // is the expand. Load off the UI thread (large groups can be slow); the await resumes on the
        // WPF SynchronizationContext, so mutating node.Children below is back on the UI thread.
        var dummy = node.GetChild("Loading...");
        if (dummy != null)
        {
            var env = _shell.GameEnvironment;
            var needsGroups = dummy.GetValue("_NeedsGroups");
            var needsRecords = dummy.GetValue("_NeedsRecords");
            try
            {
                System.Collections.Generic.List<RecordNode>? loaded = null;
                if (needsGroups != null)
                    loaded = await System.Threading.Tasks.Task.Run(
                        () => MutagenLoader.GetGroups(needsGroups, env, node));
                else if (needsRecords != null)
                {
                    var parts = needsRecords.Split('|');
                    if (parts.Length == 2)
                        loaded = await System.Threading.Tasks.Task.Run(
                            () => MutagenLoader.GetRecords(parts[0], parts[1], env, node));
                }
                if (loaded != null)
                {
                    node.Children.Remove(dummy);
                    foreach (var c in loaded) node.Children.Add(c);
                }
            }
            catch (System.Exception ex)
            {
                DebugLog.Exception($"GetChildren({path})", ex);
                return JsonConvert.SerializeObject(new[]
                {
                    new { Key = "Error: " + ex.Message, ConflictStatus = 0, IsRecordNode = false, HasChildren = false }
                });
            }
        }

        var data = node.TreeChildren.Select(c => new
        {
            c.Key,
            c.ConflictStatus,
            c.IsRecordNode,
            HasChildren = c.TreeChildren.Any()
        });
        return JsonConvert.SerializeObject(data);
    }

    // The React UI renders a record by fetching backend.GetRecordTree; this just resolves the node
    // and returns its FormKey so the frontend knows what to load (no WPF tab to open anymore).
    public string OpenRecord(string path)
    {
        DebugLog.Interop(nameof(OpenRecord), path);
        var node = FindNode(path);
        var fk = node?.GetValue("FormKey") ?? "";
        if (string.IsNullOrEmpty(fk)) DebugLog.Debug("Interop", $"OpenRecord: no FormKey for {path}");
        return fk;
    }

    private RecordNode? FindNode(string path)
    {
        var parts = path.Split('\\', '/');
        var current = _shell.Plugins.FirstOrDefault(p => string.Equals(p.Key, parts[0], System.StringComparison.OrdinalIgnoreCase));
        if (current == null) return null;

        for (int i = 1; i < parts.Length; i++)
        {
            current = current.Children.FirstOrDefault(c => string.Equals(c.Key, parts[i], System.StringComparison.OrdinalIgnoreCase));
            if (current == null) return null;
        }
        return current;
    }
}
