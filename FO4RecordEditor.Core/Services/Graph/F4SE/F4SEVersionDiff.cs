using System;
using System.Collections.Generic;
using System.Linq;

namespace FO4RecordEditor.Services.Graph.F4SE;

public sealed record SignatureChange(string Class, string Function, string Older, string Newer)
{
    public override string ToString() => $"{Class}.{Function}: {Older} -> {Newer}";
}

public sealed record F4SEVersionDelta
{
    public IReadOnlyList<NativeBinding> Added { get; init; } = Array.Empty<NativeBinding>();
    public IReadOnlyList<NativeBinding> Removed { get; init; } = Array.Empty<NativeBinding>();
    public IReadOnlyList<SignatureChange> Changed { get; init; } = Array.Empty<SignatureChange>();

    public bool Identical => Added.Count == 0 && Removed.Count == 0 && Changed.Count == 0;
}

public static class F4SEVersionDiff
{
    public static F4SEVersionDelta Compare(
        IEnumerable<NativeBinding> older, IEnumerable<NativeBinding> newer)
    {
        var before = Index(older);
        var after = Index(newer);

        var added = after.Where(kv => !before.ContainsKey(kv.Key)).Select(kv => kv.Value).ToList();
        var removed = before.Where(kv => !after.ContainsKey(kv.Key)).Select(kv => kv.Value).ToList();

        var changed = new List<SignatureChange>();
        foreach (var (key, first) in before)
        {
            if (!after.TryGetValue(key, out var second)) continue;
            var a = Signature(first);
            var b = Signature(second);
            if (a != b) changed.Add(new SignatureChange(first.PapyrusClass, first.FunctionName, a, b));
        }

        return new F4SEVersionDelta
        {
            Added = Sorted(added),
            Removed = Sorted(removed),
            Changed = changed.OrderBy(c => c.Class, StringComparer.OrdinalIgnoreCase)
                             .ThenBy(c => c.Function, StringComparer.OrdinalIgnoreCase)
                             .ToList(),
        };
    }

    private static Dictionary<(string, string), NativeBinding> Index(IEnumerable<NativeBinding> bindings)
    {
        var map = new Dictionary<(string, string), NativeBinding>();
        foreach (var binding in bindings)
        {

            map.TryAdd((binding.PapyrusClass.ToLowerInvariant(), binding.FunctionName.ToLowerInvariant()), binding);
        }
        return map;
    }

    private static string Signature(NativeBinding binding) =>
        $"{binding.ReturnType} {(binding.IsGlobal ? "global " : "")}"
        + $"({string.Join(", ", binding.Parameters.Select(p => p.Type.ToString()))})";

    private static IReadOnlyList<NativeBinding> Sorted(IEnumerable<NativeBinding> bindings) =>
        bindings.OrderBy(b => b.PapyrusClass, StringComparer.OrdinalIgnoreCase)
                .ThenBy(b => b.FunctionName, StringComparer.OrdinalIgnoreCase)
                .ToList();
}
