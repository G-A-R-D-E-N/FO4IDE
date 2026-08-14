using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using FO4RecordEditor.ViewModels;
using Newtonsoft.Json;

namespace FO4RecordEditor.Services;





[ClassInterface(ClassInterfaceType.AutoDual)]
[ComVisible(true)]
public class MastersInterop
{
    private readonly ShellViewModel _shell;
    public MastersInterop(ShellViewModel shell) => _shell = shell;
    private object? Env => _shell.GameEnvironment;



    public Task<string> GetPlugins() =>
        Task.Run(() =>
        {
            try { return JsonConvert.SerializeObject(MutagenLoader.QueryLoadedPlugins(Env)); }
            catch (Exception ex) { DebugLog.Exception("Masters.GetPlugins", ex); return "[]"; }
        });


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


    public Task<string> SetLight(string plugin, bool light) =>
        Task.Run(() =>
        {
            try { return WriteService.SetLightFlag(plugin, light, Env); }
            catch (Exception ex) { DebugLog.Exception("Masters.SetLight", ex); return "Error: " + ex.Message; }
        });


    public Task<string> SavePlugin(string plugin) =>
        Task.Run(() =>
        {
            try { return WriteService.SavePlugin(plugin, null, Env); }
            catch (Exception ex) { DebugLog.Exception("Masters.SavePlugin", ex); return "Error: " + ex.Message; }
        });
}
