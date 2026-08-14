using System;
using System.Collections.Generic;
using System.Linq;
using FO4RecordEditor.Services.Papyrus;

namespace FO4RecordEditor.Services.Graph;

/// <summary>
/// Allocates local names that cannot collide with anything already visible.
/// </summary>
/// <remarks>
/// Every keyword is reserved, and that matters more than it looks: <c>Length</c> is a keyword, so a
/// local called <c>length</c> would not even lex. Members inherited from the base chain are reserved
/// too, because a local shadowing an inherited property would compile and then mean something the
/// author did not write.
/// <para>
/// Comparison is case insensitive throughout, like the language.
/// </para>
/// </remarks>
public sealed class GraphNameTable
{
    private readonly HashSet<string> _taken = new(StringComparer.OrdinalIgnoreCase);

    public GraphNameTable(PapyrusScriptIndex? index = null, PapyrusScript? owner = null)
    {
        foreach (var keyword in PapyrusKeywords.All) _taken.Add(keyword);

        if (index == null || owner == null) return;

        foreach (var script in index.BaseChain(owner))
        {
            foreach (var function in script.Functions) _taken.Add(function.Name);
            foreach (var property in script.Properties) _taken.Add(property.Name);
            foreach (var variable in script.Variables) _taken.Add(variable.Name);
        }
    }

    /// <summary>Marks a name as unavailable.</summary>
    public void Reserve(string? name)
    {
        if (!string.IsNullOrWhiteSpace(name)) _taken.Add(name);
    }

    public bool IsReserved(string? name) => !string.IsNullOrEmpty(name) && _taken.Contains(name);

    /// <summary>
    /// A free name close to what was asked for.
    /// </summary>
    /// <remarks>
    /// Suffixes ascending numbers on collision, so a graph with three <c>GetPlayer</c> calls gets
    /// <c>player</c>, <c>player2</c>, <c>player3</c> rather than opaque temporaries.
    /// </remarks>
    public string Allocate(string? hint)
    {
        var stem = Clean(hint);

        if (_taken.Add(stem)) return stem;

        for (int suffix = 2; suffix < int.MaxValue; suffix++)
        {
            var candidate = stem + suffix;
            if (_taken.Add(candidate)) return candidate;
        }

        throw new InvalidOperationException("Ran out of names, which should not be reachable.");
    }

    private static string Clean(string? hint)
    {
        if (string.IsNullOrWhiteSpace(hint)) return "value";

        var kept = new string(hint.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());
        if (kept.Length == 0) return "value";
        if (char.IsDigit(kept[0])) kept = "_" + kept;
        return kept;
    }
}
