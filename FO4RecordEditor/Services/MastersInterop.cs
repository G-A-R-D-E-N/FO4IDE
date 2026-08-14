using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using FO4RecordEditor.ViewModels;
using Newtonsoft.Json;

namespace FO4RecordEditor.Services;

// WebView2 host object for the Masters panel: list/reorder a plugin's master table and toggle its
// ESL/Small flag via WriteService. Reads the env from the shell on each call (BackendInterop's
// pattern) so it always reflects the currently loaded modlist. Task<string> returns so a big plugin
// scan never blocks the UI thread.
[ClassInterface(ClassInterfaceType.AutoDual)]
[ComVisible(true)]
public class MastersInterop
{
    private readonly ShellViewModel _shell;
    public MastersInterop(ShellViewModel shell) => _shell = shell;
    private object? Env => _shell.GameEnvironment;

    /// <summary>Every plugin name currently available for editing (loaded env + loose-opened), for
    /// the panel's plugin picker.</summary>
    public Task<string> GetPlugins() =>
        Task.Run(() =>
        {
            try { return JsonConvert.SerializeObject(MutagenLoader.QueryLoadedPlugins(Env)); }
            catch (Exception ex) { DebugLog.Exception("Masters.GetPlugins", ex); return "[]"; }
        });

    /// <summary>Structured master list + current ESL flag (JSON). See WriteService.ListMastersJson.</summary>
    public Task<string> List(string plugin) =>
        Task.Run(() =>
        {
            try { return WriteService.ListMastersJson(plugin, Env); }
            catch (Exception ex)
            {
                DebugLog.Exception("Masters.List", ex);
                return JsonConvert.SerializeObject(new { error = "Error: " + ex.Message });
            }
        });

    /// <summary>orderJson: a JSON array of master names in the new order (must be an exact
    /// permutation of the plugin's current masters). Writes immediately -- see WriteService.
    /// ReorderMasters's own doc comment for why this bypasses save_plugin's automatic ordering.</summary>
    public Task<string> Reorder(string plugin, string orderJson) =>
        Task.Run(() =>
        {
            try
            {
                var order = JsonConvert.DeserializeObject<string[]>(orderJson) ?? Array.Empty<string>();
                return WriteService.ReorderMasters(plugin, order, Env);
            }
            catch (Exception ex) { DebugLog.Exception("Masters.Reorder", ex); return "Error: " + ex.Message; }
        });

    /// <summary>Set/clear the ESL (Small) header flag. In memory until save_plugin.</summary>
    public Task<string> SetLight(string plugin, bool light) =>
        Task.Run(() =>
        {
            try { return WriteService.SetLightFlag(plugin, light, Env); }
            catch (Exception ex) { DebugLog.Exception("Masters.SetLight", ex); return "Error: " + ex.Message; }
        });

    /// <summary>Write the plugin to disk (same contract as the 'save_plugin' MCP tool / Ctrl+S).</summary>
    public Task<string> SavePlugin(string plugin) =>
        Task.Run(() =>
        {
            try { return WriteService.SavePlugin(plugin, null, Env); }
            catch (Exception ex) { DebugLog.Exception("Masters.SavePlugin", ex); return "Error: " + ex.Message; }
        });
}
