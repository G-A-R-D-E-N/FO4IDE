using System;
using System.Linq;
using System.Runtime.InteropServices;
using FO4RecordEditor.Models;
using FO4RecordEditor.ViewModels;
using Newtonsoft.Json;

namespace FO4RecordEditor.Services;





[ClassInterface(ClassInterfaceType.AutoDual)]
[ComVisible(true)]
public class AppInterop
{
    private readonly ShellViewModel _shell;


    private readonly Action<string, double?>? _onProgress;

    public AppInterop(ShellViewModel shell, Action<string, double?>? onProgress = null)
    {
        _shell = shell;
        _onProgress = onProgress;
    }



    private IProgress<(string message, double? percent)>? MakeProgress() =>
        _onProgress == null
            ? null
            : new Progress<(string message, double? percent)>(p => _onProgress(p.message, p.percent));



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
