using System;
using System.Collections.Generic;
using System.Linq;
using FO4RecordEditor.Services.Papyrus;

namespace FO4RecordEditor.Services.Graph;

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

    public void Reserve(string? name)
    {
        if (!string.IsNullOrWhiteSpace(name)) _taken.Add(name);
    }

    public bool IsReserved(string? name) => !string.IsNullOrEmpty(name) && _taken.Contains(name);

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
