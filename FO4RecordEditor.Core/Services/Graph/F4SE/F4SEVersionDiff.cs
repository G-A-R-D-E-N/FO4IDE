using System;
using System.Collections.Generic;
using System.Linq;

namespace FO4RecordEditor.Services.Graph.F4SE;

/// <summary>A binding whose signature is not the same in both versions.</summary>
public sealed record SignatureChange(string Class, string Function, string Older, string Newer)
{
    public override string ToString() => $"{Class}.{Function}: {Older} -> {Newer}";
}

/// <summary>What changed between two F4SE versions' native surfaces.</summary>
public sealed record F4SEVersionDelta
{
    public IReadOnlyList<NativeBinding> Added { get; init; } = Array.Empty<NativeBinding>();
    public IReadOnlyList<NativeBinding> Removed { get; init; } = Array.Empty<NativeBinding>();
    public IReadOnlyList<SignatureChange> Changed { get; init; } = Array.Empty<SignatureChange>();

    public bool Identical => Added.Count == 0 && Removed.Count == 0 && Changed.Count == 0;
}

/// <summary>
/// Compares the native surfaces of two F4SE versions.
/// </summary>
/// <remarks>
/// A graph targeting the 1.10.163 runtime that calls a native only a later F4SE registers would
/// compile and then fail at run time with nothing to explain it. Knowing the delta lets that be a
/// validation refusal naming the node instead.
/// </remarks>
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
            // A duplicate key means one version registers the same class and name twice, which the
            // binding validator refuses; here the first wins so the diff still reports something.
            map.TryAdd((binding.PapyrusClass.ToLowerInvariant(), binding.FunctionName.ToLowerInvariant()), binding);
        }
        return map;
    }

    /// <summary>
    /// The part of a binding a caller depends on.
    /// </summary>
    /// <remarks>
    /// Latency and <c>NoWait</c> are excluded. Both change how the VM schedules a call, not how a
    /// script writes one, so a change in either is not a signature change for a graph author.
    /// </remarks>
    private static string Signature(NativeBinding binding) =>
        $"{binding.ReturnType} {(binding.IsGlobal ? "global " : "")}"
        + $"({string.Join(", ", binding.Parameters.Select(p => p.Type.ToString()))})";

    private static IReadOnlyList<NativeBinding> Sorted(IEnumerable<NativeBinding> bindings) =>
        bindings.OrderBy(b => b.PapyrusClass, StringComparer.OrdinalIgnoreCase)
                .ThenBy(b => b.FunctionName, StringComparer.OrdinalIgnoreCase)
                .ToList();
}
