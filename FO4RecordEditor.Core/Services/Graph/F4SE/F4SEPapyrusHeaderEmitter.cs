using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FO4RecordEditor.Services.Graph.F4SE;














public sealed class F4SEPapyrusHeaderEmitter
{
    public string Emit(PluginBinding plugin, ModuleBinding module)
    {
        var text = new StringBuilder();
        text.Append(HeaderLineFor(module)).Append('\n');

        if (module.Structs.Count > 0)
        {
            foreach (var declared in module.Structs)
            {
                text.Append('\n');
                text.Append($"struct {declared.Name}\n");
                foreach (var member in declared.Members)
                    text.Append($"\t{TypeTextFor(member.Type, module)} {member.Name}\n");
                text.Append("endstruct\n");
            }
        }

        var natives = module.Natives
            .Where(n => n.PapyrusClass.Equals(module.ScriptName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (natives.Count > 0) text.Append('\n');
        foreach (var native in natives)
            text.Append(DeclarationFor(native, module)).Append('\n');

        return text.ToString();
    }









    public static string HeaderLineFor(ModuleBinding module)
    {
        var line = new StringBuilder($"Scriptname {module.ScriptName}");
        if (!string.IsNullOrWhiteSpace(module.Extends)) line.Append($" extends {module.Extends}");
        line.Append(" Native Hidden");
        return line.ToString();
    }


    public string DeclarationFor(NativeBinding native, ModuleBinding module)
    {
        var parameters = native.Parameters.Select(p =>
        {
            var text = $"{TypeTextFor(p.Type, module)} {p.Name}";
            return p.IsOptional ? $"{text} = {p.DefaultLiteral}" : text;
        });

        var returnText = IsNone(native.ReturnType) ? "" : TypeTextFor(native.ReturnType, module) + " ";
        var tail = native.IsGlobal ? " native global" : " native";
        return $"{returnText}Function {native.FunctionName}({string.Join(", ", parameters)}){tail}";
    }











    public string TypeTextFor(PapyrusTypeText type, ModuleBinding module)
    {
        if (type.Name.Contains(':')) return type.ToString();

        var owner = _structOwners.TryGetValue(type.Name, out var found) ? found : null;
        if (owner == null || owner.Equals(module.ScriptName, StringComparison.OrdinalIgnoreCase))
            return type.ToString();

        return new PapyrusTypeText($"{owner}:{type.Name}", type.IsArray).ToString();
    }

    private readonly Dictionary<string, string> _structOwners;

    public F4SEPapyrusHeaderEmitter(PluginBinding? plugin = null)
    {
        _structOwners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (plugin == null) return;
        foreach (var declared in plugin.AllStructs) _structOwners[declared.Name] = declared.OwnerScript;
    }

    private static bool IsNone(PapyrusTypeText type) =>
        !type.IsArray && type.Name.Equals("None", StringComparison.OrdinalIgnoreCase);


    public static string ScriptFileName(ModuleBinding module) =>
        "Data/Scripts/Source/User/" + module.ScriptName.Replace(':', '/') + ".psc";








    public IReadOnlyList<OracleNative> DeclarationsOf(ModuleBinding module) =>
        module.Natives
            .Where(n => n.PapyrusClass.Equals(module.ScriptName, StringComparison.OrdinalIgnoreCase))
            .Select(n => new OracleNative(
                n.PapyrusClass,
                n.FunctionName,
                n.ReturnType,
                n.Parameters.Select(p => p.Type).ToList(),
                n.IsGlobal))
            .ToList();
}
