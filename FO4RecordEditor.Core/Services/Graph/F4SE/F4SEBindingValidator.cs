using System;
using System.Collections.Generic;
using System.Linq;

namespace FO4RecordEditor.Services.Graph.F4SE;

/// <summary>
/// Refuses a plugin the emitters could only render as broken C++ or broken Papyrus.
/// </summary>
/// <remarks>
/// The emitters are deliberately dumb renderers, so every rule that could produce a file the
/// toolchain would reject lives here instead. Nothing is repaired: a duplicate registration or an
/// unmappable type is reported and the emit refused, on the same reasoning the Papyrus back end
/// uses when it declines to guess an arity.
/// </remarks>
public static class F4SEBindingValidator
{
    /// <summary>The highest arity the <c>NativeFunctionN</c> template family provides.</summary>
    /// <remarks>Verified against the template definitions, which stop at ten parameters.</remarks>
    public const int MaximumArity = 10;

    public static IReadOnlyList<GraphDiagnostic> Validate(PluginBinding plugin)
    {
        var problems = new List<GraphDiagnostic>();

        if (string.IsNullOrWhiteSpace(plugin.Name) || !IsIdentifier(plugin.Name))
        {
            problems.Add(GraphDiagnostic.Error(
                GraphDiagnosticCodes.InvalidBindingName,
                $"'{plugin.Name}' is not usable as a plugin name: it becomes a C++ identifier and a file name."));
        }

        if (plugin.Modules.Count == 0)
        {
            problems.Add(GraphDiagnostic.Error(
                GraphDiagnosticCodes.NoModules, "A plugin has to declare at least one module to emit."));
        }

        var structOwners = plugin.AllStructs.ToList();
        var structNames = structOwners.Select(s => s.Name).ToList();
        var map = new F4SETypeMap(structNames);

        ValidateStructs(plugin, structOwners, map, problems);
        ValidateModules(plugin, problems);
        ValidateNatives(plugin, map, problems);

        return problems;
    }

    private static void ValidateModules(PluginBinding plugin, List<GraphDiagnostic> problems)
    {
        foreach (var module in plugin.Modules)
        {
            if (!IsIdentifier(module.Name))
            {
                problems.Add(GraphDiagnostic.Error(
                    GraphDiagnosticCodes.InvalidBindingName,
                    $"Module name '{module.Name}' is not a usable C++ identifier."));
            }
            if (!IsScriptName(module.ScriptName))
            {
                problems.Add(GraphDiagnostic.Error(
                    GraphDiagnosticCodes.InvalidBindingName,
                    $"Script name '{module.ScriptName}' is not a usable Papyrus script name."));
            }
        }

        foreach (var duplicate in plugin.Modules
            .GroupBy(m => m.ScriptName, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1))
        {
            problems.Add(GraphDiagnostic.Error(
                GraphDiagnosticCodes.DuplicateDeclaration,
                $"{duplicate.Count()} modules write into script '{duplicate.Key}', which would emit one file twice."));
        }
    }

    private static void ValidateStructs(
        PluginBinding plugin,
        IReadOnlyList<StructBinding> structs,
        F4SETypeMap map,
        List<GraphDiagnostic> problems)
    {
        var scriptNames = plugin.Modules.Select(m => m.ScriptName).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var module in plugin.Modules)
        {
            foreach (var declared in module.Structs)
            {
                if (!string.Equals(declared.OwnerScript, module.ScriptName, StringComparison.OrdinalIgnoreCase)
                    && !scriptNames.Contains(declared.OwnerScript))
                {
                    // The owner is baked into the VM type name, so an owner no emitted script
                    // declares produces a struct the VM will never resolve.
                    problems.Add(GraphDiagnostic.Error(
                        GraphDiagnosticCodes.StructOwnerMismatch,
                        $"Struct '{declared.Name}' names owner '{declared.OwnerScript}', "
                        + "which no module in this plugin emits."));
                }

                foreach (var member in declared.Members)
                {
                    if (map.TryToCpp(member.Type, member.Unsigned, out _, out var refusal)) continue;
                    problems.Add(GraphDiagnostic.Error(
                        GraphDiagnosticCodes.UnmappedNativeType,
                        $"Struct '{declared.Name}' member '{member.Name}' is typed "
                        + $"'{member.Type}', which cannot cross the boundary: {refusal}"));
                }
            }
        }

        foreach (var duplicate in structs
            .GroupBy(s => s.Name, StringComparer.Ordinal)
            .Where(g => g.Count() > 1))
        {
            // DECLARE_STRUCT typedefs the bare name, so two structs sharing one name collide in C++
            // even when their owners differ.
            problems.Add(GraphDiagnostic.Error(
                GraphDiagnosticCodes.DuplicateDeclaration,
                $"Struct name '{duplicate.Key}' is declared {duplicate.Count()} times; "
                + "the C++ typedef uses the bare name, so they would collide."));
        }
    }

    private static void ValidateNatives(
        PluginBinding plugin, F4SETypeMap map, List<GraphDiagnostic> problems)
    {
        foreach (var duplicate in plugin.AllNatives
            .GroupBy(n => (n.PapyrusClass.ToLowerInvariant(), n.FunctionName.ToLowerInvariant()))
            .Where(g => g.Count() > 1))
        {
            var first = duplicate.First();
            problems.Add(GraphDiagnostic.Error(
                GraphDiagnosticCodes.DuplicateNativeBinding,
                $"{first.PapyrusClass}.{first.FunctionName} is registered {duplicate.Count()} times."));
        }

        foreach (var native in plugin.AllNatives)
        {
            if (!IsIdentifier(native.FunctionName))
            {
                problems.Add(GraphDiagnostic.Error(
                    GraphDiagnosticCodes.InvalidBindingName,
                    $"'{native.FunctionName}' is not a usable Papyrus function name."));
            }

            if (native.Arity > MaximumArity)
            {
                problems.Add(GraphDiagnostic.Error(
                    GraphDiagnosticCodes.NativeArityUnsupported,
                    $"{native.PapyrusClass}.{native.FunctionName} takes {native.Arity} parameters, "
                    + $"and the NativeFunction template family stops at {MaximumArity}."));
            }

            if (!map.TryToCpp(native.ReturnType, native.ReturnUnsigned, out _, out var returnRefusal))
            {
                problems.Add(GraphDiagnostic.Error(
                    GraphDiagnosticCodes.UnmappedNativeType,
                    $"{native.PapyrusClass}.{native.FunctionName} returns '{native.ReturnType}', "
                    + $"which cannot cross the boundary: {returnRefusal}"));
            }

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var parameter in native.Parameters)
            {
                if (!names.Add(parameter.Name))
                {
                    problems.Add(GraphDiagnostic.Error(
                        GraphDiagnosticCodes.DuplicateVariableName,
                        $"{native.PapyrusClass}.{native.FunctionName} has two parameters named '{parameter.Name}'."));
                }

                if (map.TryToCpp(parameter.Type, parameter.Unsigned, out _, out var refusal)) continue;
                problems.Add(GraphDiagnostic.Error(
                    GraphDiagnosticCodes.UnmappedNativeType,
                    $"{native.PapyrusClass}.{native.FunctionName} parameter '{parameter.Name}' is typed "
                    + $"'{parameter.Type}', which cannot cross the boundary: {refusal}"));
            }

            // A trailing run of optionals is fine; one in the middle is not, because a caller cannot
            // supply the later positional arguments without it.
            int firstOptional = -1;
            for (int i = 0; i < native.Parameters.Count; i++)
            {
                if (native.Parameters[i].IsOptional)
                {
                    if (firstOptional < 0) firstOptional = i;
                }
                else if (firstOptional >= 0)
                {
                    problems.Add(GraphDiagnostic.Error(
                        GraphDiagnosticCodes.ArgumentCount,
                        $"{native.PapyrusClass}.{native.FunctionName} parameter "
                        + $"'{native.Parameters[i].Name}' is required but follows an optional one."));
                    break;
                }
            }
        }
    }

    private static bool IsIdentifier(string? text) =>
        !string.IsNullOrEmpty(text)
        && (char.IsLetter(text[0]) || text[0] == '_')
        && text.All(c => char.IsLetterOrDigit(c) || c == '_');

    /// <summary>A Papyrus script name, which may carry namespace segments separated by colons.</summary>
    private static bool IsScriptName(string? text) =>
        !string.IsNullOrEmpty(text)
        && text.Split(':').All(IsIdentifier);
}
