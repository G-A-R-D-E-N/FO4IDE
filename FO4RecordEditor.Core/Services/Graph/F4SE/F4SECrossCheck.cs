using System;
using System.Collections.Generic;
using System.Linq;

namespace FO4RecordEditor.Services.Graph.F4SE;

/// <summary>One binding that disagrees with the Papyrus declaration of the same function.</summary>
public sealed record CrossCheckMismatch(string Class, string Function, string What, string Cpp, string Psc)
{
    public override string ToString() => $"{Class}.{Function} {What}: cpp={Cpp} psc={Psc}";
}

/// <summary>What comparing the C++ registrations against the Papyrus declarations found.</summary>
public sealed record CrossCheckResult
{
    public required int Recovered { get; init; }
    public required int Declared { get; init; }
    public required int Matched { get; init; }

    /// <summary>Registered in C++ with no Papyrus declaration to check against.</summary>
    public IReadOnlyList<NativeBinding> CppOnly { get; init; } = Array.Empty<NativeBinding>();

    /// <summary>Declared in Papyrus with no C++ registration found.</summary>
    public IReadOnlyList<OracleNative> PscOnly { get; init; } = Array.Empty<OracleNative>();

    public IReadOnlyList<CrossCheckMismatch> Mismatches { get; init; } = Array.Empty<CrossCheckMismatch>();

    /// <summary>Latent registrations, which the Papyrus side cannot express and so cannot confirm.</summary>
    public int LatentOnlyInCpp { get; init; }

    /// <summary>NoWait registrations, likewise invisible to Papyrus.</summary>
    public int NoWaitOnlyInCpp { get; init; }

    public bool Agrees => CppOnly.Count == 0 && PscOnly.Count == 0 && Mismatches.Count == 0;
}

/// <summary>
/// Compares recovered C++ registrations against the Papyrus declarations of the same functions.
/// </summary>
/// <remarks>
/// Two independently produced descriptions of the same boundary, so agreement is evidence that the
/// scanner and the type map are both right. Latency and <c>NoWait</c> are excluded from matching on
/// purpose and counted separately, because no <c>.psc</c> can carry them: overstating what this
/// check covers would be worse than the gap itself.
/// </remarks>
public static class F4SECrossCheck
{
    /// <summary>Compares recovered registrations against the declarations an oracle produced.</summary>
    public static CrossCheckResult Compare(IEnumerable<NativeBinding> recovered, F4SENativeOracle oracle) =>
        Compare(recovered, oracle.Natives);

    /// <summary>
    /// Compares recovered registrations against a set of declarations.
    /// </summary>
    /// <remarks>
    /// Taking the declarations rather than the oracle keeps this usable by a caller that already
    /// has them, including the emit-then-read-back round trip, which has no source tree to build an
    /// oracle from.
    /// </remarks>
    public static CrossCheckResult Compare(
        IEnumerable<NativeBinding> recovered, IEnumerable<OracleNative> declarations)
    {
        var bindings = recovered.ToList();
        var declared = declarations.ToList();

        var byKey = declared.ToDictionary(
            n => (n.Script.ToLowerInvariant(), n.Name.ToLowerInvariant()), n => n);

        var cppOnly = new List<NativeBinding>();
        var mismatches = new List<CrossCheckMismatch>();
        var seen = new HashSet<(string, string)>();
        int matched = 0;

        foreach (var binding in bindings)
        {
            var key = (binding.PapyrusClass.ToLowerInvariant(), binding.FunctionName.ToLowerInvariant());
            if (!byKey.TryGetValue(key, out var native))
            {
                cppOnly.Add(binding);
                continue;
            }

            seen.Add(key);
            matched++;

            if (binding.Arity != native.Arity)
            {
                mismatches.Add(new CrossCheckMismatch(
                    binding.PapyrusClass, binding.FunctionName, "arity",
                    binding.Arity.ToString(), native.Arity.ToString()));
                continue;
            }

            if (binding.IsGlobal != native.IsGlobal)
            {
                mismatches.Add(new CrossCheckMismatch(
                    binding.PapyrusClass, binding.FunctionName, "global",
                    binding.IsGlobal.ToString(), native.IsGlobal.ToString()));
            }

            if (!SameType(binding.ReturnType, native.ReturnType))
            {
                mismatches.Add(new CrossCheckMismatch(
                    binding.PapyrusClass, binding.FunctionName, "return",
                    binding.ReturnType.ToString(), native.ReturnType.ToString()));
            }

            for (int i = 0; i < binding.Arity; i++)
            {
                if (SameType(binding.Parameters[i].Type, native.ParameterTypes[i])) continue;
                mismatches.Add(new CrossCheckMismatch(
                    binding.PapyrusClass, binding.FunctionName, $"parameter {i}",
                    binding.Parameters[i].Type.ToString(), native.ParameterTypes[i].ToString()));
            }
        }

        var pscOnly = declared
            .Where(n => !seen.Contains((n.Script.ToLowerInvariant(), n.Name.ToLowerInvariant())))
            .ToList();

        return new CrossCheckResult
        {
            Recovered = bindings.Count,
            Declared = declared.Count,
            Matched = matched,
            CppOnly = cppOnly,
            PscOnly = pscOnly,
            Mismatches = mismatches,
            LatentOnlyInCpp = bindings.Count(b => b.IsLatent),
            NoWaitOnlyInCpp = bindings.Count(b => b.NoWait),
        };
    }

    /// <summary>
    /// Whether two written types are the same type.
    /// </summary>
    /// <remarks>
    /// Papyrus is case insensitive, so names compare case insensitively.
    /// <para>
    /// A struct owned by another script is written <c>Owner:Struct</c> in Papyrus, while the C++
    /// side has only the bare typedef name the <c>DECLARE_STRUCT</c> macro produced. The shipped
    /// <c>ObjectReference.ApplyMaterialSwap</c> returns <c>MatSwap:RemapData[]</c> against a C++
    /// <c>VMArray&lt;RemapData&gt;</c>, and both spellings are correct. So a qualified name is
    /// compared on the part after the colon when the other side carries no qualifier.
    /// </para>
    /// </remarks>
    private static bool SameType(PapyrusTypeText a, PapyrusTypeText b)
    {
        if (a.IsArray != b.IsArray) return false;
        if (a.Name.Equals(b.Name, StringComparison.OrdinalIgnoreCase)) return true;

        bool aQualified = a.Name.Contains(':');
        bool bQualified = b.Name.Contains(':');

        // Two qualified names that differ really are different types, so only an unqualified name
        // is allowed to match a qualified one.
        if (aQualified == bQualified) return false;

        return Unqualified(a.Name).Equals(Unqualified(b.Name), StringComparison.OrdinalIgnoreCase);
    }

    private static string Unqualified(string name)
    {
        int colon = name.LastIndexOf(':');
        return colon < 0 ? name : name[(colon + 1)..];
    }
}
