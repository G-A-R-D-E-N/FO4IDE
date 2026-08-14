using System;
using System.Collections.Generic;
using System.Linq;

namespace FO4RecordEditor.Services.Graph;

public enum PinDirection { In, Out }

/// <summary>
/// Whether a pin carries control flow or a value.
/// </summary>
/// <remarks>
/// The distinction is the whole basis of the lowering: exec edges become statement order, data edges
/// become expressions. Wiring one kind to the other is refused rather than coerced.
/// </remarks>
public enum PinKind { Exec, Data }

/// <summary>How a pin's type is determined.</summary>
public enum PinTypeForm
{
    /// <summary>A named type, fixed by the definition.</summary>
    Concrete,

    /// <summary>A type variable, solved from whatever the node is wired to.</summary>
    Generic,

    /// <summary>An array of the type variable.</summary>
    ArrayOfGeneric,

    /// <summary>The element type of the type variable.</summary>
    ElementOfGeneric,

    /// <summary>The script the graph itself declares.</summary>
    SelfType,

    /// <summary>Anything. Used for var-typed pins.</summary>
    Any,
}

/// <summary>
/// A pin's type, which is not always a name.
/// </summary>
/// <remarks>
/// Array operations are the only real polymorphism in Papyrus: <c>Add</c> takes the element type of
/// whatever array it is called on. One type variable per node covers every case, because every
/// generic in the language is reachable from a single array pin, so no unification loop is needed.
/// </remarks>
public sealed record PinTypeExpr
{
    public PinTypeForm Form { get; init; } = PinTypeForm.Concrete;

    /// <summary>The type name, when the form is concrete.</summary>
    public string TypeName { get; init; } = "";

    public bool IsArray { get; init; }

    /// <summary>The type variable name, when the form is generic.</summary>
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

/// <summary>One pin on a node type.</summary>
public sealed record PinDefinition
{
    public required string Id { get; init; }

    public string Label { get; init; } = "";

    public required PinDirection Direction { get; init; }

    public required PinKind Kind { get; init; }

    /// <summary>Null for exec pins.</summary>
    public PinTypeExpr? Type { get; init; }

    public bool IsOptional { get; init; }

    /// <summary>The default declared in the source, rendered as Papyrus text.</summary>
    public string? DeclaredDefault { get; init; }

    /// <summary>Prose for a tooltip, when documentation was available.</summary>
    public string? Description { get; init; }

    public override string ToString() =>
        $"{Direction} {Kind} {Id}{(Type == null ? "" : " : " + Type)}";
}

/// <summary>
/// A node type: the shape the canvas draws and the lowering reads.
/// </summary>
/// <remarks>
/// Node types are data rather than classes, because the palette is generated at run time from
/// however many scripts the user has. A subclass per node type is not expressible when the set runs
/// to tens of thousands.
/// </remarks>
public sealed record NodeDefinition
{
    public required string Id { get; init; }

    public required GraphNodeKind Kind { get; init; }

    public string Title { get; init; } = "";

    /// <summary>The owning script, or a built-in group such as Flow or Math.</summary>
    public string Category { get; init; } = "";

    public string? Summary { get; init; }

    /// <summary>
    /// No exec pins, so the node's value inlines at each use.
    /// </summary>
    /// <remarks>
    /// Never inferred. Papyrus annotates nothing as pure, and a compiler that guesses will reorder
    /// side effects across an exec boundary, so this is set only from a curated allowlist and from
    /// the structural kinds that cannot have effects.
    /// </remarks>
    public bool IsPure { get; init; }

    public string? OwnerScript { get; init; }

    public string? MemberName { get; init; }

    public bool IsGlobal { get; init; }

    /// <summary>
    /// An entry that handles an event raised by another object, written <c>Event Owner.Name(...)</c>.
    /// </summary>
    /// <remarks>
    /// A flag rather than something read back out of the definition id, so the emitter never has to
    /// parse an id to decide how to write a signature. It covers both remote events and custom
    /// events, because Papyrus gives them the same shape: measured across 697 shipped and mod
    /// scripts, all 76 dotted handlers are <c>Event Type.Name(Type akSender, ...)</c>, differing
    /// only in whether the tail is the source event's parameters or a single <c>Var[]</c>.
    /// </remarks>
    public bool IsRemoteEvent { get; init; }

    /// <summary>A readable stem for the local this node's result binds to.</summary>
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

    /// <summary>
    /// The pins for one node, which a few definitions vary by the node's configuration.
    /// </summary>
    /// <remarks>
    /// Variable access nodes take their type from the declaration they name, so their pins cannot
    /// be fixed on the definition. Everything else returns the definition's own list.
    /// </remarks>
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

                // A script variable declares its own type. A function local has nowhere to declare
                // one, so the node carries it, the same way a Cast and a New Array do. Without the
                // fallback a local reads as var and refuses to flow into anything typed.
                var written = declared?.Type ?? node.ConfigString("type");
                if (string.IsNullOrWhiteSpace(written)) return Pins;

                var type = TypeFromWritten(written);
                return Pins.Select(p => p.Kind == PinKind.Data ? p with { Type = type } : p).ToList();
            }

            // A cast, a type check and a new array all name their type on the node rather than in
            // the definition, so their result pin cannot be typed until the node exists. Leaving
            // these as var would make a cast's output refuse to flow anywhere, which is the whole
            // point of placing one.
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

/// <summary>The pin identifiers the built-in nodes and the generated ones use.</summary>
/// <remarks>
/// Fixed spellings, because they are written into saved documents and a change would orphan wires.
/// </remarks>
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
