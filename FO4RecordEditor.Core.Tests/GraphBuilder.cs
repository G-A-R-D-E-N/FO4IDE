using System;
using System.Collections.Generic;
using System.Linq;
using FO4RecordEditor.Services.Graph;
using FO4RecordEditor.Services.Papyrus;

namespace FO4RecordEditor.Core.Tests;

/// <summary>
/// Builds graph documents for tests without hand-writing JSON.
/// </summary>
/// <remarks>
/// Fixtures written as JSON would all need editing whenever the schema moved. Going through the
/// model instead means a schema change is a compile error in one file rather than silent breakage
/// spread across dozens of documents.
/// </remarks>
internal sealed class GraphBuilder
{
    private readonly GraphDocument _document = new();
    private int _sequence;

    public GraphBuilder(string scriptName = "Fixture", string? extends = null)
    {
        _document.Id = "test";
        _document.Header.ScriptName = scriptName;
        _document.Header.Extends = extends;
    }

    public GraphDocument Document
    {
        get
        {
            _document.Invalidate();
            return _document;
        }
    }

    /// <summary>Declares which named state the script starts in.</summary>
    public GraphBuilder AutoState(string state)
    {
        _document.Header.AutoState = state;
        return this;
    }

    /// <summary>Declares a custom event this script can raise.</summary>
    public GraphBuilder CustomEvent(string name)
    {
        _document.CustomEvents.Add(name);
        return this;
    }

    public GraphBuilder Flag(string flag)
    {
        _document.Header.Flags.Add(flag);
        return this;
    }

    public GraphBuilder Variable(string name, string type, bool isProperty = false, string? initial = null)
    {
        _document.Variables.Add(new GraphVariable
        {
            Name = name, Type = type, IsProperty = isProperty, Initial = initial,
        });
        return this;
    }

    /// <summary>Adds a node and returns its id.</summary>
    public string Node(string definitionId, GraphNodeKind kind, params (string Key, string Value)[] config)
    {
        var id = "n" + (++_sequence);
        var node = new GraphNode { Id = id, Definition = definitionId, Kind = kind };
        foreach (var (key, value) in config) node.Config[key] = value;
        _document.Nodes.Add(node);
        _document.Invalidate();
        return id;
    }

    /// <summary>Adds a node whose kind is taken from the palette.</summary>
    public string Node(NodePalette palette, string definitionId, params (string Key, string Value)[] config)
    {
        var definition = palette.Find(definitionId)
                         ?? throw new ArgumentException($"'{definitionId}' is not on the palette.");
        return Node(definitionId, definition.Kind, config);
    }

    /// <summary>Sets a literal on an unconnected input pin.</summary>
    public GraphBuilder Value(string nodeId, string pinId, string type, string value)
    {
        var node = _document.Node(nodeId) ?? throw new ArgumentException($"No node '{nodeId}'.");
        node.PinValues[pinId] = new GraphPinValue { Type = type, Value = value };
        return this;
    }

    public GraphBuilder Wire(string fromNode, string fromPin, string toNode, string toPin)
    {
        _document.Wires.Add(new GraphWire
        {
            Id = "w" + (++_sequence),
            From = new PinRef(fromNode, fromPin),
            To = new PinRef(toNode, toPin),
        });
        return this;
    }

    /// <summary>Chains exec pins in order, using each node's first exec output.</summary>
    public GraphBuilder Sequence(NodePalette palette, params string[] nodeIds)
    {
        for (int i = 0; i + 1 < nodeIds.Length; i++)
        {
            var node = _document.Node(nodeIds[i])!;
            var definition = palette.Find(node.Definition)!;
            var outPin = definition.ExecOutputs.FirstOrDefault()?.Id
                         ?? throw new ArgumentException($"'{node.Definition}' has no exec output.");

            var nextNode = _document.Node(nodeIds[i + 1])!;
            var nextDefinition = palette.Find(nextNode.Definition)!;
            var inPin = nextDefinition.ExecInputs.FirstOrDefault()?.Id
                        ?? throw new ArgumentException($"'{nextNode.Definition}' has no exec input.");

            Wire(nodeIds[i], outPin, nodeIds[i + 1], inPin);
        }
        return this;
    }
}

/// <summary>Shared setup for the graph tests.</summary>
internal static class GraphTestEnvironment
{
    /// <summary>An index over the checked-in stub tree.</summary>
    public static PapyrusScriptIndex Index() =>
        PapyrusCompiler.IndexFor(new[] { TestRoots.BaseStubs, TestRoots.GraphScripts });

    public static NodePalette Palette() => new(Index());

    public static GraphCompiler Compiler() => new(Index());

    /// <summary>Compiles with the test-suite setting that turns surviving errors into faults.</summary>
    public static GraphCompileResult Compile(GraphDocument document, bool stopAfterSource = false) =>
        Compiler().Compile(document, new GraphCompileOptions
        {
            StopAfterSource = stopAfterSource,
            TreatPapyrusErrorsAsInternalFaults = true,
        });

    public static string Describe(IEnumerable<GraphDiagnostic> diagnostics) =>
        string.Join(" | ", diagnostics.Select(d => d.ToString()));
}
