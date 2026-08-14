using System;
using System.Collections.Generic;
using System.Linq;
using FO4RecordEditor.Services.Papyrus;

namespace FO4RecordEditor.Core.Tests;

/// <summary>
/// Compares two <see cref="PexFile"/>s for the part of their content that is a function of the
/// source they were compiled from.
/// </summary>
/// <remarks>
/// Extracted from <see cref="PapyrusDifferentialTests"/> so that every comparison in the suite is
/// measured by one ruler. The differential sweep uses it against the Creation Kit's output; the
/// graph roundtrip oracles use it against our own, where the bar is stricter because both sides are
/// ours and there is no compiler-build variance to absorb.
/// <para>
/// <b>Compared exactly:</b> parent class name, the set of states by name, the set of functions per
/// state by name, native and global flags, return type, parameter count, per-function instruction
/// count, and per instruction the mnemonic plus every operand's value, type, order and role. Jump
/// offsets are integer operands and so are compared like any other. Property flags, user flags and
/// both handler bodies are compared under the same rules.
/// </para>
/// <para>
/// <b>Normalised away:</b> temporary and mangled-local numbering, identifier case, and float
/// formatting. Which number a temporary got is the one thing that genuinely cannot be reproduced,
/// because the Creation Kit's counter is object wide and leaves gaps where a number was allocated
/// and dropped. Member declaration order within an object is not compared either: functions and
/// properties are matched by name, since the Creation Kit writes them in a hash order that differs
/// between two compiles of different files.
/// </para>
/// </remarks>
internal static class PexComparer
{
    /// <summary>The default label for the left hand side of a reported difference.</summary>
    public const string DefaultReferenceLabel = "CK";

    /// <summary>The default label for the right hand side of a reported difference.</summary>
    public const string DefaultOursLabel = "ours";

    /// <summary>
    /// The first difference between two compiled objects, or null when they agree.
    /// </summary>
    /// <param name="reference">The side treated as correct, named by <paramref name="referenceLabel"/>.</param>
    /// <param name="ours">The side under test.</param>
    /// <param name="referenceLabel">How the reference side is named in the returned message.</param>
    /// <param name="oursLabel">How the tested side is named in the returned message.</param>
    public static string? FirstDifference(
        PexFile reference,
        PexFile ours,
        string referenceLabel = DefaultReferenceLabel,
        string oursLabel = DefaultOursLabel)
    {
        var theirs = reference.Objects.FirstOrDefault();
        var mine = ours.Objects.FirstOrDefault();
        if (theirs == null || mine == null) return "no object";

        if (!theirs.ParentClassName.Equals(mine.ParentClassName, StringComparison.OrdinalIgnoreCase))
            return $"parent '{theirs.ParentClassName}' vs '{mine.ParentClassName}'";

        foreach (var state in theirs.States)
        {
            var ourState = mine.States.FirstOrDefault(
                s => s.Name.Equals(state.Name, StringComparison.OrdinalIgnoreCase));
            if (ourState == null) return $"missing state '{state.Name}'";

            foreach (var fn in state.Functions)
            {
                var ourFn = ourState.Functions.FirstOrDefault(
                    f => f.Name.Equals(fn.Name, StringComparison.OrdinalIgnoreCase));
                if (ourFn == null) return $"missing function {fn.Name}";
                var difference = CompareFunctions(fn, ourFn, referenceLabel, oursLabel);
                if (difference != null) return $"{fn.Name}: {difference}";
            }

            foreach (var fn in ourState.Functions)
            {
                if (!state.Functions.Any(f => f.Name.Equals(fn.Name, StringComparison.OrdinalIgnoreCase)))
                    return $"extra function {fn.Name}";
            }
        }

        foreach (var property in theirs.Properties)
        {
            var ourProperty = mine.Properties.FirstOrDefault(
                p => p.Name.Equals(property.Name, StringComparison.OrdinalIgnoreCase));
            if (ourProperty == null) return $"missing property {property.Name}";
            if (property.Flags != ourProperty.Flags)
                return $"property {property.Name} flags {property.Flags:X2} vs {ourProperty.Flags:X2}";
            if (property.UserFlags != ourProperty.UserFlags)
                return $"property {property.Name} user flags {property.UserFlags} vs {ourProperty.UserFlags}";

            if (property.ReadHandler != null && ourProperty.ReadHandler != null)
            {
                var difference = CompareFunctions(
                    property.ReadHandler, ourProperty.ReadHandler, referenceLabel, oursLabel);
                if (difference != null) return $"{property.Name}.Get: {difference}";
            }
            if (property.WriteHandler != null && ourProperty.WriteHandler != null)
            {
                var difference = CompareFunctions(
                    property.WriteHandler, ourProperty.WriteHandler, referenceLabel, oursLabel);
                if (difference != null) return $"{property.Name}.Set: {difference}";
            }
        }

        return null;
    }

    /// <summary>The first difference between two functions, or null when they agree.</summary>
    public static string? CompareFunctions(
        PexFunction theirs,
        PexFunction ours,
        string referenceLabel = DefaultReferenceLabel,
        string oursLabel = DefaultOursLabel)
    {
        if (theirs.IsNative != ours.IsNative) return "native flag";
        if (theirs.IsGlobal != ours.IsGlobal) return "global flag";
        if (!theirs.ReturnType.Equals(ours.ReturnType, StringComparison.OrdinalIgnoreCase))
            return $"returns {theirs.ReturnType} vs {ours.ReturnType}";
        if (theirs.Params.Count != ours.Params.Count) return "parameter count";

        int shared = Math.Min(theirs.Instructions.Count, ours.Instructions.Count);
        for (int i = 0; i < shared; i++)
        {
            string a = Normalise(theirs.Instructions[i]), b = Normalise(ours.Instructions[i]);
            if (a != b) return $"[{i}] {referenceLabel}: {a} | {oursLabel}: {b}";
        }

        return theirs.Instructions.Count == ours.Instructions.Count
            ? null
            : $"{theirs.Instructions.Count} instructions vs {ours.Instructions.Count}";
    }

    /// <summary>
    /// One instruction as text, with temporary and mangled-local names collapsed.
    /// </summary>
    public static string Normalise(PexInstruction instruction) =>
        instruction.Mnemonic + " " + string.Join(" ", instruction.Args.Select(Operand));

    /// <summary>One operand as text, comparable across two compiles.</summary>
    public static string Operand(PexValue value) => value.Type switch
    {
        PexValueType.Identifier => NormaliseName(value.Str),
        PexValueType.String => "\"" + value.Str + "\"",
        PexValueType.Integer => value.Int.ToString(),
        PexValueType.Float => value.Float.ToString("0.000000"),
        PexValueType.Bool => value.Bool ? "true" : "false",
        _ => "None",
    };

    /// <summary>An identifier operand with compiler-allocated numbering collapsed.</summary>
    public static string NormaliseName(string name) =>
        name.StartsWith("::temp", StringComparison.OrdinalIgnoreCase) ? "::temp" :
        name.StartsWith("::mangled_", StringComparison.OrdinalIgnoreCase) ? "::mangled" :
        name.ToLowerInvariant();

    /// <summary>
    /// Every function in an object, keyed <c>state.function</c>, for callers that want to compare
    /// coverage rather than stop at the first difference.
    /// </summary>
    /// <remarks>
    /// The empty state is the object's own body, so its functions are keyed by bare name.
    /// </remarks>
    public static IReadOnlyDictionary<string, PexFunction> FunctionsOf(PexObject obj)
    {
        var map = new Dictionary<string, PexFunction>(StringComparer.OrdinalIgnoreCase);
        foreach (var state in obj.States)
        {
            foreach (var fn in state.Functions)
            {
                var key = string.IsNullOrEmpty(state.Name) ? fn.Name : state.Name + "." + fn.Name;
                map[key] = fn;
            }
        }
        return map;
    }
}
