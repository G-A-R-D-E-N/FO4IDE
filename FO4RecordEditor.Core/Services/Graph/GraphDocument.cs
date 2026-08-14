using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FO4RecordEditor.Services.Graph;








public readonly record struct PinRef(string Node, string Pin)
{
    public override string ToString() => $"{Node}:{Pin}";

    public bool IsEmpty => string.IsNullOrEmpty(Node) || string.IsNullOrEmpty(Pin);
}









public enum GraphNodeKind
{

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


public sealed class GraphPinValue
{

    public string Type { get; set; } = "";


    public string Value { get; set; } = "";

    public override string ToString() => $"{Type}:{Value}";
}












public sealed class GraphNode
{
    public string Id { get; set; } = "";


    [JsonProperty("def")]
    public string Definition { get; set; } = "";

    public GraphNodeKind Kind { get; set; }

    public double X { get; set; }

    public double Y { get; set; }

    public string? Comment { get; set; }


    public JObject Config { get; set; } = new();


    public Dictionary<string, GraphPinValue> PinValues { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);


    public string? ConfigString(string key) => Config[key]?.Value<string>();

    public override string ToString() => $"{Kind} {Definition} ({Id})";
}


public sealed class GraphWire
{
    public string Id { get; set; } = "";

    public PinRef From { get; set; }

    public PinRef To { get; set; }

    public override string ToString() => $"{From} -> {To}";
}


public sealed class GraphVariable
{
    public string Name { get; set; } = "";

    public string Type { get; set; } = "";


    public bool IsProperty { get; set; }


    public string? Initial { get; set; }

    public List<string> Flags { get; set; } = new();
}


public sealed class GraphStruct
{
    public string Name { get; set; } = "";

    public List<GraphVariable> Members { get; set; } = new();
}


public sealed class GraphScriptHeader
{
    public string ScriptName { get; set; } = "";

    public string? Extends { get; set; }

    public List<string> Flags { get; set; } = new();

    public List<string> Imports { get; set; } = new();

    public string? DocComment { get; set; }









    public string? AutoState { get; set; }
}


public enum GraphKind
{

    PapyrusScript = 0,


    F4SEBinding = 1,
}








public sealed class GraphDocument
{

    public const int CurrentSchema = 1;

    public int Schema { get; set; } = CurrentSchema;

    public string Id { get; set; } = "";

    public GraphKind Kind { get; set; } = GraphKind.PapyrusScript;

    public GraphScriptHeader Header { get; set; } = new();

    public List<GraphVariable> Variables { get; set; } = new();

    public List<GraphStruct> Structs { get; set; } = new();








    public List<string> CustomEvents { get; set; } = new();

    public List<GraphNode> Nodes { get; set; } = new();

    public List<GraphWire> Wires { get; set; } = new();

    private Dictionary<string, GraphNode>? _byId;


    public GraphNode? Node(string? id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        _byId ??= Nodes
            .GroupBy(n => n.Id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        return _byId.TryGetValue(id, out var node) ? node : null;
    }


    public void Invalidate() => _byId = null;

    public IEnumerable<GraphWire> Into(PinRef pin) =>
        Wires.Where(w => Same(w.To, pin));

    public IEnumerable<GraphWire> OutOf(PinRef pin) =>
        Wires.Where(w => Same(w.From, pin));


    public IEnumerable<GraphWire> Touching(string nodeId) =>
        Wires.Where(w =>
            string.Equals(w.From.Node, nodeId, StringComparison.Ordinal)
            || string.Equals(w.To.Node, nodeId, StringComparison.Ordinal));

    private static bool Same(PinRef a, PinRef b) =>
        string.Equals(a.Node, b.Node, StringComparison.Ordinal)
        && string.Equals(a.Pin, b.Pin, StringComparison.OrdinalIgnoreCase);
}
