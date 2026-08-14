using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FO4RecordEditor.Services.Graph;

/// <summary>A pin on a particular node.</summary>
/// <remarks>
/// The node is an opaque string rather than a Guid so the canvas can mint ids without depending on
/// a secure-context crypto API, which is not dependable on the loopback origin the Linux shell
/// serves from. The pin is a stable name local to the node, which keeps saved documents readable
/// and diffable.
/// </remarks>
public readonly record struct PinRef(string Node, string Pin)
{
    public override string ToString() => $"{Node}:{Pin}";

    public bool IsEmpty => string.IsNullOrEmpty(Node) || string.IsNullOrEmpty(Pin);
}

/// <summary>
/// The structural kinds the lowering switches on.
/// </summary>
/// <remarks>
/// A closed set on purpose. The palette supplies unbounded variety inside these, because node types
/// are generated from thousands of scripts and cannot be a C# hierarchy, but the number of shapes
/// the emitter has to understand is small and fixed.
/// </remarks>
public enum GraphNodeKind
{
    /// <summary>Unresolvable against the current palette. Loaded rather than rejected.</summary>
    Unknown = 0,

    EventEntry,
    FunctionEntry,
    Return,
    Break,
    Continue,

    Branch,
    While,
    ForEach,

    Call,
    PropertyGet,
    PropertySet,
    VariableGet,
    VariableSet,
    LocalDeclare,

    Literal,
    Self,
    Parent,
    NoneValue,

    MemberGet,
    IndexGet,
    IndexSet,
    ArrayOp,
    NewArray,

    StructNew,
    StructGet,
    StructSet,

    Unary,
    Binary,
    Cast,
    TypeCheck,

    Reroute,
    Comment,
}

/// <summary>A literal typed into an unconnected data input.</summary>
public sealed class GraphPinValue
{
    /// <summary>One of int, float, bool, string, none, identifier.</summary>
    public string Type { get; set; } = "";

    /// <summary>The source text, exactly as it will be emitted.</summary>
    public string Value { get; set; } = "";

    public override string ToString() => $"{Type}:{Value}";
}

/// <summary>One node.</summary>
/// <remarks>
/// <see cref="Kind"/> is stored beside <see cref="Definition"/> deliberately. A document loaded
/// against roots that lack the referenced script still renders structurally and still produces a
/// useful refusal naming the missing script, rather than an unusable blank canvas.
/// <para>
/// Pins are never stored. They are re-derived from the palette on load, so a parameter renamed in a
/// base script surfaces as a dangling wire naming this node instead of silently compiling a call
/// with the wrong arguments.
/// </para>
/// </remarks>
public sealed class GraphNode
{
    public string Id { get; set; } = "";

    /// <summary>The palette entry this node instantiates.</summary>
    [JsonProperty("def")]
    public string Definition { get; set; } = "";

    public GraphNodeKind Kind { get; set; }

    public double X { get; set; }

    public double Y { get; set; }

    public string? Comment { get; set; }

    /// <summary>Per-node settings a definition needs, such as a variable name or an operator.</summary>
    public JObject Config { get; set; } = new();

    /// <summary>Literals on unconnected data inputs, keyed by pin id.</summary>
    public Dictionary<string, GraphPinValue> PinValues { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A string setting from <see cref="Config"/>, or null.</summary>
    public string? ConfigString(string key) => Config[key]?.Value<string>();

    public override string ToString() => $"{Kind} {Definition} ({Id})";
}

/// <summary>One connection.</summary>
public sealed class GraphWire
{
    public string Id { get; set; } = "";

    public PinRef From { get; set; }

    public PinRef To { get; set; }

    public override string ToString() => $"{From} -> {To}";
}

/// <summary>A script variable or property the graph declares.</summary>
public sealed class GraphVariable
{
    public string Name { get; set; } = "";

    public string Type { get; set; } = "";

    /// <summary>A property rather than a plain variable.</summary>
    public bool IsProperty { get; set; }

    /// <summary>The initial value as source text, or null.</summary>
    public string? Initial { get; set; }

    public List<string> Flags { get; set; } = new();
}

/// <summary>A struct the graph declares.</summary>
public sealed class GraphStruct
{
    public string Name { get; set; } = "";

    public List<GraphVariable> Members { get; set; } = new();
}

/// <summary>The script-level declarations.</summary>
public sealed class GraphScriptHeader
{
    public string ScriptName { get; set; } = "";

    public string? Extends { get; set; }

    public List<string> Flags { get; set; } = new();

    public List<string> Imports { get; set; } = new();

    public string? DocComment { get; set; }

    /// <summary>
    /// Which named state the script starts in, if any.
    /// </summary>
    /// <remarks>
    /// Script level rather than per entry node. Papyrus allows exactly one auto state, so carrying
    /// the flag on each entry would let two entries in different states both claim it and there
    /// would be no non-arbitrary way to resolve that.
    /// </remarks>
    public string? AutoState { get; set; }
}

/// <summary>What a graph document describes.</summary>
public enum GraphKind
{
    /// <summary>A Papyrus script: nodes are statements and expressions.</summary>
    PapyrusScript = 0,

    /// <summary>An F4SE binding surface: nodes are declarations.</summary>
    F4SEBinding = 1,
}

/// <summary>
/// A whole graph, as saved and loaded.
/// </summary>
/// <remarks>
/// This is the schema of record. The canvas mirrors it in TypeScript, and the two are kept in step
/// by the camelCase serializer settings here rather than by convention.
/// </remarks>
public sealed class GraphDocument
{
    /// <summary>The schema this build writes.</summary>
    public const int CurrentSchema = 1;

    public int Schema { get; set; } = CurrentSchema;

    public string Id { get; set; } = "";

    public GraphKind Kind { get; set; } = GraphKind.PapyrusScript;

    public GraphScriptHeader Header { get; set; } = new();

    public List<GraphVariable> Variables { get; set; } = new();

    public List<GraphStruct> Structs { get; set; } = new();

    /// <summary>
    /// Custom events this script declares and can raise.
    /// </summary>
    /// <remarks>
    /// Names only. Papyrus fixes the payload of every custom event at <c>Var[]</c>, so there is no
    /// per event signature to carry.
    /// </remarks>
    public List<string> CustomEvents { get; set; } = new();

    public List<GraphNode> Nodes { get; set; } = new();

    public List<GraphWire> Wires { get; set; } = new();

    private Dictionary<string, GraphNode>? _byId;

    /// <summary>The node with an id, or null.</summary>
    public GraphNode? Node(string? id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        _byId ??= Nodes
            .GroupBy(n => n.Id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        return _byId.TryGetValue(id, out var node) ? node : null;
    }

    /// <summary>Drops cached lookups after the node list is changed.</summary>
    public void Invalidate() => _byId = null;

    public IEnumerable<GraphWire> Into(PinRef pin) =>
        Wires.Where(w => Same(w.To, pin));

    public IEnumerable<GraphWire> OutOf(PinRef pin) =>
        Wires.Where(w => Same(w.From, pin));

    /// <summary>Every wire touching a node, in either direction.</summary>
    public IEnumerable<GraphWire> Touching(string nodeId) =>
        Wires.Where(w =>
            string.Equals(w.From.Node, nodeId, StringComparison.Ordinal)
            || string.Equals(w.To.Node, nodeId, StringComparison.Ordinal));

    private static bool Same(PinRef a, PinRef b) =>
        string.Equals(a.Node, b.Node, StringComparison.Ordinal)
        && string.Equals(a.Pin, b.Pin, StringComparison.OrdinalIgnoreCase);
}
