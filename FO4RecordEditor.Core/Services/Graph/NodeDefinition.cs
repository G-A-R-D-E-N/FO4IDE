using System;
using System.Collections.Generic;
using System.Linq;

namespace FO4RecordEditor.Services.Graph;

public enum PinDirection { In, Out }

public enum PinKind { Exec, Data }

public enum PinTypeForm
{

    Concrete,

    Generic,

    ArrayOfGeneric,

    ElementOfGeneric,

    SelfType,

    Any,
}

public sealed record PinTypeExpr
{
    public PinTypeForm Form { get; init; } = PinTypeForm.Concrete;

    public string TypeName { get; init; } = "";

    public bool IsArray { get; init; }

    public string Variable { get; init; } = "T";

    public static PinTypeExpr Concrete(string typeName, bool isArray = false) =>
        new() { Form = PinTypeForm.Concrete, TypeName = typeName, IsArray = isArray };

    public static PinTypeExpr Generic(string variable = "T") =>
        new() { Form = PinTypeForm.Generic, Variable = variable };

    public static PinTypeExpr ArrayOfGeneric(string variable = "T") =>
        new() { Form = PinTypeForm.ArrayOfGeneric, Variable = variable };

    public static PinTypeExpr ElementOfGeneric(string variable = "T") =>
        new() { Form = PinTypeForm.ElementOfGeneric, Variable = variable };

    public static readonly PinTypeExpr SelfType = new() { Form = PinTypeForm.SelfType };

    public static readonly PinTypeExpr Any = new() { Form = PinTypeForm.Any };

    public override string ToString() => Form switch
    {
        PinTypeForm.Concrete => TypeName + (IsArray ? "[]" : ""),
        PinTypeForm.Generic => Variable,
        PinTypeForm.ArrayOfGeneric => Variable + "[]",
        PinTypeForm.ElementOfGeneric => "elementof " + Variable,
        PinTypeForm.SelfType => "Self",
        _ => "var",
    };
}

public sealed record PinDefinition
{
    public required string Id { get; init; }

    public string Label { get; init; } = "";

    public required PinDirection Direction { get; init; }

    public required PinKind Kind { get; init; }

    public PinTypeExpr? Type { get; init; }

    public bool IsOptional { get; init; }

    public string? DeclaredDefault { get; init; }

    public string? Description { get; init; }

    public override string ToString() =>
        $"{Direction} {Kind} {Id}{(Type == null ? "" : " : " + Type)}";
}

public sealed record NodeDefinition
{
    public required string Id { get; init; }

    public required GraphNodeKind Kind { get; init; }

    public string Title { get; init; } = "";

    public string Category { get; init; } = "";

    public string? Summary { get; init; }

    public bool IsPure { get; init; }

    public string? OwnerScript { get; init; }

    public string? MemberName { get; init; }

    public bool IsGlobal { get; init; }

    public bool IsRemoteEvent { get; init; }

    public string? LocalNameHint { get; init; }

    public IReadOnlyList<PinDefinition> Pins { get; init; } = Array.Empty<PinDefinition>();

    public IEnumerable<PinDefinition> Inputs => Pins.Where(p => p.Direction == PinDirection.In);

    public IEnumerable<PinDefinition> Outputs => Pins.Where(p => p.Direction == PinDirection.Out);

    public IEnumerable<PinDefinition> ExecInputs =>
        Pins.Where(p => p.Kind == PinKind.Exec && p.Direction == PinDirection.In);

    public IEnumerable<PinDefinition> ExecOutputs =>
        Pins.Where(p => p.Kind == PinKind.Exec && p.Direction == PinDirection.Out);

    public IEnumerable<PinDefinition> DataInputs =>
        Pins.Where(p => p.Kind == PinKind.Data && p.Direction == PinDirection.In);

    public IEnumerable<PinDefinition> DataOutputs =>
        Pins.Where(p => p.Kind == PinKind.Data && p.Direction == PinDirection.Out);

    public PinDefinition? Pin(string? id) =>
        id == null ? null : Pins.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<PinDefinition> PinsFor(GraphNode node, GraphDocument? document = null)
    {
        switch (Kind)
        {
            case GraphNodeKind.VariableGet:
            case GraphNodeKind.VariableSet:
            {
                var name = node.ConfigString("name");
                var declared = document?.Variables.FirstOrDefault(v =>
                    string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase));

                var written = declared?.Type ?? node.ConfigString("type");
                if (string.IsNullOrWhiteSpace(written)) return Pins;

                var type = TypeFromWritten(written);
                return Pins.Select(p => p.Kind == PinKind.Data ? p with { Type = type } : p).ToList();
            }

            case GraphNodeKind.Cast:
            case GraphNodeKind.NewArray:
            {
                var written = node.ConfigString("type");
                if (string.IsNullOrWhiteSpace(written)) return Pins;

                var type = Kind == GraphNodeKind.NewArray
                    ? PinTypeExpr.Concrete(BaseOf(written), isArray: true)
                    : TypeFromWritten(written);

                return Pins
                    .Select(p => p.Id == PinIds.Return && p.Kind == PinKind.Data ? p with { Type = type } : p)
                    .ToList();
            }

            case GraphNodeKind.StructNew:
            {
                var written = node.ConfigString("type");
                if (string.IsNullOrWhiteSpace(written)) return Pins;
                var type = TypeFromWritten(written);
                return Pins
                    .Select(p => p.Direction == PinDirection.Out && p.Kind == PinKind.Data
                        ? p with { Type = type } : p)
                    .ToList();
            }

            default:
                return Pins;
        }
    }

    private static string BaseOf(string? written) =>
        string.IsNullOrEmpty(written) ? ""
        : written.EndsWith("[]", StringComparison.Ordinal) ? written[..^2]
        : written;

    private static PinTypeExpr TypeFromWritten(string? written)
    {
        var isArray = written != null && written.EndsWith("[]", StringComparison.Ordinal);
        return PinTypeExpr.Concrete(BaseOf(written), isArray);
    }

    public override string ToString() => $"{Id} ({Kind})";
}

public static class PinIds
{
    public const string Exec = "exec";
    public const string Then = "then";
    public const string Else = "else";
    public const string Body = "body";
    public const string Completed = "completed";
    public const string Self = "self";
    public const string Return = "ret";
    public const string Value = "value";
    public const string Target = "target";
    public const string Condition = "cond";
    public const string Index = "index";
    public const string Array = "array";
    public const string Element = "element";
    public const string Left = "a";
    public const string Right = "b";

    public const string ArgumentPrefix = "arg:";
    public const string ParameterPrefix = "param:";

    public static string Argument(string parameterName) => ArgumentPrefix + parameterName;

    public static string Parameter(string parameterName) => ParameterPrefix + parameterName;

    public static bool IsArgument(string pinId) =>
        pinId.StartsWith(ArgumentPrefix, StringComparison.OrdinalIgnoreCase);

    public static string ArgumentName(string pinId) =>
        IsArgument(pinId) ? pinId[ArgumentPrefix.Length..] : pinId;
}
