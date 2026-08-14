using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FO4RecordEditor.Services.Papyrus;

namespace FO4RecordEditor.Services.Graph.F4SE;

/// <summary>One <c>native</c> declaration, as the Papyrus side sees it.</summary>
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

/// <summary>
/// The Papyrus-side truth about what F4SE adds, built by parsing rather than grepping.
/// </summary>
/// <remarks>
/// This exists to check the C++ scanner against something that did not come from the C++. The
/// declarations are read with <see cref="PapyrusParser"/> and taken off the syntax tree, so
/// parameter types, defaults, return types and <c>global</c> come out exactly rather than through a
/// second regex that could be wrong in the same way the first one is.
/// <para>
/// <b>The oracle is a set difference.</b> A merged script holds both the vanilla natives and the
/// ones F4SE adds, so comparing a whole merged file against the C++ would report every vanilla
/// native as missing. Subtracting the pristine vanilla copy of the same script leaves the additions.
/// </para>
/// <para>
/// <b>What it can and cannot say.</b> It is authoritative for name, class, arity, parameter types,
/// return type and global-ness. It says nothing about latency or <c>NoWait</c>: the shipped
/// <c>UI.psc</c> declares a plain native that the C++ registers as latent, so those two live only
/// on the C++ side and are reported separately rather than cross checked.
/// </para>
/// </remarks>
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

    /// <summary>
    /// How many sources needed a synthetic <c>Scriptname</c> line before they would parse.
    /// </summary>
    /// <remarks>
    /// The 0.7.8 tree ships 19 of its 29 modified scripts as headerless fragments. Prepending
    /// <c>Scriptname &lt;file name&gt;</c> is a deterministic, auditable repair, and any figure
    /// derived from that tree has to be reported as being after it rather than as a raw count.
    /// </remarks>
    public int FragmentsRepaired { get; }

    public IReadOnlyList<string> Problems { get; }

    public OracleNative? Find(string script, string name) =>
        Natives.FirstOrDefault(n =>
            n.Script.Equals(script, StringComparison.OrdinalIgnoreCase)
            && n.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Builds the oracle from a merged source root, subtracting a pristine vanilla root.
    /// </summary>
    /// <param name="mergedRoot">Scripts holding vanilla declarations plus F4SE's additions.</param>
    /// <param name="vanillaRoot">
    /// Pristine copies of the same scripts. When null, every native in the merged root is taken,
    /// which is only right for a root that holds nothing but additions.
    /// </param>
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

    /// <summary>
    /// Parses a source, supplying a <c>Scriptname</c> line when the file is a headerless fragment.
    /// </summary>
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
