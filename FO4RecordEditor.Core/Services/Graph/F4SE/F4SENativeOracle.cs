using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FO4RecordEditor.Services.Papyrus;

namespace FO4RecordEditor.Services.Graph.F4SE;


public sealed record OracleNative(
    string Script,
    string Name,
    PapyrusTypeText ReturnType,
    IReadOnlyList<PapyrusTypeText> ParameterTypes,
    bool IsGlobal)
{
    public int Arity => ParameterTypes.Count;

    public (string Script, string Name) Key => (Script, Name);

    public override string ToString() => $"{ReturnType} {Script}.{Name}/{Arity}";
}





















public sealed class F4SENativeOracle
{
    private F4SENativeOracle(
        IReadOnlyList<OracleNative> natives,
        int scriptsRead,
        int fragmentsRepaired,
        IReadOnlyList<string> problems)
    {
        Natives = natives;
        ScriptsRead = scriptsRead;
        FragmentsRepaired = fragmentsRepaired;
        Problems = problems;
    }

    public IReadOnlyList<OracleNative> Natives { get; }

    public int ScriptsRead { get; }









    public int FragmentsRepaired { get; }

    public IReadOnlyList<string> Problems { get; }

    public OracleNative? Find(string script, string name) =>
        Natives.FirstOrDefault(n =>
            n.Script.Equals(script, StringComparison.OrdinalIgnoreCase)
            && n.Name.Equals(name, StringComparison.OrdinalIgnoreCase));









    public static F4SENativeOracle Build(string mergedRoot, string? vanillaRoot)
    {
        var problems = new List<string>();
        var natives = new List<OracleNative>();
        int scripts = 0, repaired = 0;

        if (!Directory.Exists(mergedRoot))
        {
            problems.Add($"merged root '{mergedRoot}' does not exist");
            return new F4SENativeOracle(natives, 0, 0, problems);
        }

        var vanillaByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (vanillaRoot != null && Directory.Exists(vanillaRoot))
        {
            foreach (var path in Directory.GetFiles(vanillaRoot, "*.psc"))
                vanillaByName[Path.GetFileNameWithoutExtension(path)] = path;
        }

        foreach (var path in Directory.GetFiles(mergedRoot, "*.psc").OrderBy(p => p, StringComparer.Ordinal))
        {
            scripts++;
            var merged = ParseRepaired(path, problems, ref repaired);
            if (merged == null) continue;

            var script = merged.Name;
            var mine = NativesOf(merged, script);

            if (vanillaByName.TryGetValue(Path.GetFileNameWithoutExtension(path), out var vanillaPath))
            {
                var vanilla = ParseRepaired(vanillaPath, problems, ref repaired);
                if (vanilla != null)
                {
                    var baseline = NativesOf(vanilla, script)
                        .Select(n => (n.Name.ToLowerInvariant(), n.Arity))
                        .ToHashSet();
                    mine = mine.Where(n => !baseline.Contains((n.Name.ToLowerInvariant(), n.Arity))).ToList();
                }
            }

            natives.AddRange(mine);
        }

        return new F4SENativeOracle(natives, scripts, repaired, problems);
    }




    private static PapyrusScript? ParseRepaired(string path, List<string> problems, ref int repaired)
    {
        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            problems.Add($"{Path.GetFileName(path)}: {ex.Message}");
            return null;
        }

        var script = PapyrusParser.Parse(text, path);
        if (!string.IsNullOrEmpty(script.Name)) return script;

        repaired++;
        var name = Path.GetFileNameWithoutExtension(path);
        var patched = PapyrusParser.Parse($"Scriptname {name}{Environment.NewLine}{text}", path);
        if (string.IsNullOrEmpty(patched.Name))
        {
            problems.Add($"{Path.GetFileName(path)}: still has no script name after the synthetic header");
            return null;
        }
        return patched;
    }

    private static List<OracleNative> NativesOf(PapyrusScript script, string scriptName) =>
        script.Functions
            .Where(f => f.IsNative)
            .Select(f => new OracleNative(
                scriptName,
                f.Name,
                TypeTextOf(f.ReturnType),
                f.Parameters.Select(p => TypeTextOf(p.Type)).ToList(),
                f.IsGlobal))
            .ToList();

    private static PapyrusTypeText TypeTextOf(PapyrusTypeRef? reference) =>
        reference == null ? new PapyrusTypeText("None") : new PapyrusTypeText(reference.Name, reference.IsArray);
}
