using System;
using System.Collections.Generic;
using System.Linq;
using FO4RecordEditor.Services.Papyrus;

namespace FO4RecordEditor.Core.Tests;

internal static class PexComparer
{

    public const string DefaultReferenceLabel = "CK";

    public const string DefaultOursLabel = "ours";

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

    public static string Normalise(PexInstruction instruction) =>
        instruction.Mnemonic + " " + string.Join(" ", instruction.Args.Select(Operand));

    public static string Operand(PexValue value) => value.Type switch
    {
        PexValueType.Identifier => NormaliseName(value.Str),
        PexValueType.String => "\"" + value.Str + "\"",
        PexValueType.Integer => value.Int.ToString(),
        PexValueType.Float => value.Float.ToString("0.000000"),
        PexValueType.Bool => value.Bool ? "true" : "false",
        _ => "None",
    };

    public static string NormaliseName(string name) =>
        name.StartsWith("::temp", StringComparison.OrdinalIgnoreCase) ? "::temp" :
        name.StartsWith("::mangled_", StringComparison.OrdinalIgnoreCase) ? "::mangled" :
        name.ToLowerInvariant();

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
