using System;
using System.Collections.Generic;
using System.Linq;

namespace FO4RecordEditor.Services.Graph.F4SE;


public sealed record CrossCheckMismatch(string Class, string Function, string What, string Cpp, string Psc)
{
    public override string ToString() => $"{Class}.{Function} {What}: cpp={Cpp} psc={Psc}";
}


public sealed record CrossCheckResult
{
    public required int Recovered { get; init; }
    public required int Declared { get; init; }
    public required int Matched { get; init; }


    public IReadOnlyList<NativeBinding> CppOnly { get; init; } = Array.Empty<NativeBinding>();


    public IReadOnlyList<OracleNative> PscOnly { get; init; } = Array.Empty<OracleNative>();

    public IReadOnlyList<CrossCheckMismatch> Mismatches { get; init; } = Array.Empty<CrossCheckMismatch>();


    public int LatentOnlyInCpp { get; init; }


    public int NoWaitOnlyInCpp { get; init; }

    public bool Agrees => CppOnly.Count == 0 && PscOnly.Count == 0 && Mismatches.Count == 0;
}










public static class F4SECrossCheck
{

    public static CrossCheckResult Compare(IEnumerable<NativeBinding> recovered, F4SENativeOracle oracle) =>
        Compare(recovered, oracle.Natives);









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














    private static bool SameType(PapyrusTypeText a, PapyrusTypeText b)
    {
        if (a.IsArray != b.IsArray) return false;
        if (a.Name.Equals(b.Name, StringComparison.OrdinalIgnoreCase)) return true;

        bool aQualified = a.Name.Contains(':');
        bool bQualified = b.Name.Contains(':');



        if (aQualified == bQualified) return false;

        return Unqualified(a.Name).Equals(Unqualified(b.Name), StringComparison.OrdinalIgnoreCase);
    }

    private static string Unqualified(string name)
    {
        int colon = name.LastIndexOf(':');
        return colon < 0 ? name : name[(colon + 1)..];
    }
}
