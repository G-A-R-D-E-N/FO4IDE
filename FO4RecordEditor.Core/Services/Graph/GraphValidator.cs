using System;
using System.Collections.Generic;
using System.Linq;
using FO4RecordEditor.Services.Papyrus;

namespace FO4RecordEditor.Services.Graph;


public sealed class GraphValidation
{
    public IReadOnlyList<GraphDiagnostic> Diagnostics { get; init; } = Array.Empty<GraphDiagnostic>();


    public IReadOnlyDictionary<string, NodeDefinition> Definitions { get; init; } =
        new Dictionary<string, NodeDefinition>();


    public IReadOnlyDictionary<string, GenericBinding> Generics { get; init; } =
        new Dictionary<string, GenericBinding>();

    public bool SourcesComplete { get; init; } = true;

    public IEnumerable<GraphDiagnostic> Errors =>
        Diagnostics.Where(d => d.Severity == GraphSeverity.Error);

    public bool Ok => !Errors.Any();
}













public sealed class GraphValidator
{
    private readonly PapyrusScriptIndex _index;
    private readonly NodePalette _palette;

    public GraphValidator(PapyrusScriptIndex index, NodePalette palette)
    {
        _index = index ?? throw new ArgumentNullException(nameof(index));
        _palette = palette ?? throw new ArgumentNullException(nameof(palette));
    }

    public GraphValidation Validate(GraphDocument document)
    {
        document.Invalidate();

        var problems = new List<GraphDiagnostic>();
        var definitions = new Dictionary<string, NodeDefinition>(StringComparer.Ordinal);

        ValidateHeader(document, problems);
        ResolveDefinitions(document, definitions, problems);
        ValidateWires(document, definitions, problems);
        ValidateDeclarations(document, problems);

        var selfType = document.Header.ScriptName;
        var types = new GraphTypeResolver(_index, selfType, document.Header.Extends);
        var owner = OwnerScriptFor(document);
        var generics = SolveGenerics(document, definitions, types, owner);

        ValidateWireTypes(document, definitions, generics, types, owner, problems);
        ValidateRequiredInputs(document, definitions, problems);
        ValidateEntries(document, definitions, problems);

        return new GraphValidation
        {
            Diagnostics = problems,
            Definitions = definitions,
            Generics = generics,
            SourcesComplete = types.SourcesComplete,
        };
    }









    private PapyrusScript? OwnerScriptFor(GraphDocument document)
    {
        if (string.IsNullOrWhiteSpace(document.Header.ScriptName)) return null;

        var header = "Scriptname " + document.Header.ScriptName;
        if (!string.IsNullOrWhiteSpace(document.Header.Extends))
            header += " extends " + document.Header.Extends;

        var imports = string.Concat(document.Header.Imports.Select(i => "\nImport " + i));
        return PapyrusParser.Parse(header + imports + "\n", document.Header.ScriptName + ".psc");
    }

    private void ValidateHeader(GraphDocument document, List<GraphDiagnostic> problems)
    {
        var name = document.Header.ScriptName;
        if (string.IsNullOrWhiteSpace(name))
        {
            problems.Add(GraphDiagnostic.Error(
                GraphDiagnosticCodes.InvalidScriptHeader, "The graph has no script name."));
            return;
        }

        if (name.Split(':').Any(part => !IsIdentifier(part)))
        {
            problems.Add(GraphDiagnostic.Error(
                GraphDiagnosticCodes.InvalidScriptHeader,
                $"'{name}' is not a usable script name."));
        }

        var extends = document.Header.Extends;
        if (!string.IsNullOrWhiteSpace(extends) && _index.Resolve(extends) == null)
        {
            problems.Add(GraphDiagnostic.Error(
                GraphDiagnosticCodes.UndeclaredReference,
                $"This graph extends '{extends}', which is not on the import roots."));
        }
    }

    private void ResolveDefinitions(
        GraphDocument document,
        Dictionary<string, NodeDefinition> definitions,
        List<GraphDiagnostic> problems)
    {
        var seenIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var node in document.Nodes)
        {
            if (string.IsNullOrEmpty(node.Id))
            {
                problems.Add(GraphDiagnostic.Error(
                    GraphDiagnosticCodes.MalformedDocument, "A node has no id."));
                continue;
            }

            if (!seenIds.Add(node.Id))
            {
                problems.Add(GraphDiagnostic.Error(
                    GraphDiagnosticCodes.DuplicateDeclaration,
                    $"Node id '{node.Id}' appears more than once.", node.Id));
                continue;
            }

            var definition = _palette.Find(node.Definition);
            if (definition == null)
            {


                problems.Add(GraphDiagnostic.Error(
                    GraphDiagnosticCodes.UnknownNodeDefinition,
                    $"'{node.Definition}' is not on this palette. The script it comes from may not "
                    + "be on the import roots.", node.Id));
                continue;
            }

            definitions[node.Id] = definition;
        }
    }

    private static void ValidateWires(
        GraphDocument document,
        IReadOnlyDictionary<string, NodeDefinition> definitions,
        List<GraphDiagnostic> problems)
    {


        var dataInputs = new Dictionary<string, (PinRef Pin, List<GraphWire> Wires)>(StringComparer.OrdinalIgnoreCase);
        var execOutputs = new Dictionary<string, (PinRef Pin, List<GraphWire> Wires)>(StringComparer.OrdinalIgnoreCase);

        foreach (var wire in document.Wires)
        {
            var fromNode = document.Node(wire.From.Node);
            var toNode = document.Node(wire.To.Node);

            if (fromNode == null || toNode == null)
            {
                problems.Add(new GraphDiagnostic
                {
                    Code = GraphDiagnosticCodes.DanglingWire,
                    Severity = GraphSeverity.Error,
                    Message = "A wire names a node that is not in this graph.",
                    WireId = wire.Id,
                });
                continue;
            }

            if (!definitions.TryGetValue(fromNode.Id, out var fromDef)
                || !definitions.TryGetValue(toNode.Id, out var toDef))
            {
                continue;
            }

            var fromPin = fromDef.PinsFor(fromNode, document)
                .FirstOrDefault(p => string.Equals(p.Id, wire.From.Pin, StringComparison.OrdinalIgnoreCase));
            var toPin = toDef.PinsFor(toNode, document)
                .FirstOrDefault(p => string.Equals(p.Id, wire.To.Pin, StringComparison.OrdinalIgnoreCase));

            if (fromPin == null || toPin == null)
            {


                var missing = fromPin == null ? wire.From : wire.To;
                problems.Add(new GraphDiagnostic
                {
                    Code = GraphDiagnosticCodes.DanglingWire,
                    Severity = GraphSeverity.Error,
                    Message = $"Pin '{missing.Pin}' no longer exists on this node.",
                    NodeId = missing.Node,
                    PinId = missing.Pin,
                    WireId = wire.Id,
                });
                continue;
            }

            if (fromPin.Direction != PinDirection.Out || toPin.Direction != PinDirection.In)
            {
                problems.Add(new GraphDiagnostic
                {
                    Code = GraphDiagnosticCodes.WireDirection,
                    Severity = GraphSeverity.Error,
                    Message = "A wire has to run from an output pin to an input pin.",
                    NodeId = wire.From.Node,
                    PinId = wire.From.Pin,
                    WireId = wire.Id,
                    RelatedNodes = new[] { wire.To.Node },
                });
                continue;
            }

            if (fromPin.Kind != toPin.Kind)
            {
                problems.Add(new GraphDiagnostic
                {
                    Code = GraphDiagnosticCodes.PinKindMismatch,
                    Severity = GraphSeverity.Error,
                    Message = "A control flow pin cannot be wired to a value pin.",
                    NodeId = wire.To.Node,
                    PinId = wire.To.Pin,
                    WireId = wire.Id,
                    RelatedNodes = new[] { wire.From.Node },
                });
                continue;
            }

            if (toPin.Kind == PinKind.Data) Track(dataInputs, wire.To, wire);
            else Track(execOutputs, wire.From, wire);
        }

        Report(dataInputs, GraphDiagnosticCodes.MultipleDataSources,
            "A value input can only take one wire.", problems);
        Report(execOutputs, GraphDiagnosticCodes.MultipleExecSuccessors,
            "A control flow output can only lead to one node.", problems);

        static void Track(
            Dictionary<string, (PinRef Pin, List<GraphWire> Wires)> into, PinRef pin, GraphWire wire)
        {
            var key = pin.ToString();
            if (!into.TryGetValue(key, out var tracked))
                into[key] = tracked = (pin, new List<GraphWire>());
            tracked.Wires.Add(wire);
        }

        static void Report(
            Dictionary<string, (PinRef Pin, List<GraphWire> Wires)> tracked, string code, string message,
            List<GraphDiagnostic> problems)
        {
            foreach (var (_, entry) in tracked.Where(kv => kv.Value.Wires.Count > 1))
            {
                problems.Add(new GraphDiagnostic
                {
                    Code = code,
                    Severity = GraphSeverity.Error,
                    Message = $"{message} This one has {entry.Wires.Count}.",
                    NodeId = entry.Pin.Node,
                    PinId = entry.Pin.Pin,
                    WireId = entry.Wires[0].Id,
                });
            }
        }
    }

    private static void ValidateDeclarations(GraphDocument document, List<GraphDiagnostic> problems)
    {
        foreach (var duplicate in document.Variables
            .GroupBy(v => v.Name, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1))
        {
            problems.Add(GraphDiagnostic.Error(
                GraphDiagnosticCodes.DuplicateVariableName,
                $"'{duplicate.Key}' is declared {duplicate.Count()} times."));
        }

        foreach (var variable in document.Variables.Where(v => !IsIdentifier(v.Name)))
        {
            problems.Add(GraphDiagnostic.Error(
                GraphDiagnosticCodes.InvalidScriptHeader,
                $"'{variable.Name}' is not a usable variable name."));
        }
    }

    private Dictionary<string, GenericBinding> SolveGenerics(
        GraphDocument document,
        IReadOnlyDictionary<string, NodeDefinition> definitions,
        GraphTypeResolver types,
        PapyrusScript? owner)
    {
        var solved = new Dictionary<string, GenericBinding>(StringComparer.Ordinal);

        foreach (var node in document.Nodes)
        {
            if (!definitions.TryGetValue(node.Id, out var definition)) continue;
            solved[node.Id] = types.SolveGenerics(node, definition, document, SourceTypeOf);
        }

        return solved;



        (string TypeName, bool IsArray)? SourceTypeOf(PinRef pin)
        {
            var wire = document.Into(pin).FirstOrDefault() ?? document.OutOf(pin).FirstOrDefault();
            if (wire == null) return null;

            var other = string.Equals(wire.To.Node, pin.Node, StringComparison.Ordinal)
                        && string.Equals(wire.To.Pin, pin.Pin, StringComparison.OrdinalIgnoreCase)
                ? wire.From
                : wire.To;

            var otherNode = document.Node(other.Node);
            if (otherNode == null || !definitions.TryGetValue(other.Node, out var otherDef)) return null;

            var otherPin = otherDef.PinsFor(otherNode, document)
                .FirstOrDefault(p => string.Equals(p.Id, other.Pin, StringComparison.OrdinalIgnoreCase));
            if (otherPin?.Type == null) return null;
            if (otherPin.Type.Form != PinTypeForm.Concrete && otherPin.Type.Form != PinTypeForm.SelfType)
                return null;

            return types.TypeOf(otherPin.Type, new GenericBinding());
        }
    }

    private static void ValidateWireTypes(
        GraphDocument document,
        IReadOnlyDictionary<string, NodeDefinition> definitions,
        IReadOnlyDictionary<string, GenericBinding> generics,
        GraphTypeResolver types,
        PapyrusScript? owner,
        List<GraphDiagnostic> problems)
    {
        foreach (var wire in document.Wires)
        {
            var fromNode = document.Node(wire.From.Node);
            var toNode = document.Node(wire.To.Node);
            if (fromNode == null || toNode == null) continue;
            if (!definitions.TryGetValue(fromNode.Id, out var fromDef)) continue;
            if (!definitions.TryGetValue(toNode.Id, out var toDef)) continue;

            var fromPin = fromDef.PinsFor(fromNode, document)
                .FirstOrDefault(p => string.Equals(p.Id, wire.From.Pin, StringComparison.OrdinalIgnoreCase));
            var toPin = toDef.PinsFor(toNode, document)
                .FirstOrDefault(p => string.Equals(p.Id, wire.To.Pin, StringComparison.OrdinalIgnoreCase));
            if (fromPin?.Type == null || toPin?.Type == null) continue;
            if (fromPin.Kind != PinKind.Data || toPin.Kind != PinKind.Data) continue;

            var (fromType, fromArray) = types.TypeOf(
                fromPin.Type, generics.GetValueOrDefault(fromNode.Id) ?? new GenericBinding());
            var (toType, toArray) = types.TypeOf(
                toPin.Type, generics.GetValueOrDefault(toNode.Id) ?? new GenericBinding());

            if (string.IsNullOrEmpty(fromType) || string.IsNullOrEmpty(toType))
            {
                problems.Add(new GraphDiagnostic
                {
                    Code = GraphDiagnosticCodes.UndeclaredReference,
                    Severity = GraphSeverity.Error,
                    Message = "This pin's type could not be worked out. Wire the array pin first.",
                    NodeId = string.IsNullOrEmpty(fromType) ? wire.From.Node : wire.To.Node,
                    PinId = string.IsNullOrEmpty(fromType) ? wire.From.Pin : wire.To.Pin,
                    WireId = wire.Id,
                });
                continue;
            }

            var verdict = types.Judge(fromType, fromArray, toType, toArray, owner);
            if (verdict is GraphTypeResolver.WireVerdict.Implicit or GraphTypeResolver.WireVerdict.Unknown)
                continue;

            var array = (bool a) => a ? "[]" : "";
            problems.Add(new GraphDiagnostic
            {
                Code = verdict == GraphTypeResolver.WireVerdict.NeedsCast
                    ? GraphDiagnosticCodes.NarrowingWireNeedsCast
                    : GraphDiagnosticCodes.IncompatibleWireType,
                Severity = GraphSeverity.Error,
                Message = verdict == GraphTypeResolver.WireVerdict.NeedsCast
                    ? $"{fromType}{array(fromArray)} does not fit {toType}{array(toArray)} on its own. "
                      + "Put a Cast node in between."
                    : $"{fromType}{array(fromArray)} cannot be used as {toType}{array(toArray)}.",
                NodeId = wire.To.Node,
                PinId = wire.To.Pin,
                WireId = wire.Id,
                RelatedNodes = new[] { wire.From.Node },
            });
        }
    }

    private static void ValidateRequiredInputs(
        GraphDocument document,
        IReadOnlyDictionary<string, NodeDefinition> definitions,
        List<GraphDiagnostic> problems)
    {
        foreach (var node in document.Nodes)
        {
            if (!definitions.TryGetValue(node.Id, out var definition)) continue;

            foreach (var pin in definition.PinsFor(node, document))
            {
                if (pin.Kind != PinKind.Data || pin.Direction != PinDirection.In) continue;
                if (pin.IsOptional) continue;
                if (document.Into(new PinRef(node.Id, pin.Id)).Any()) continue;
                if (node.PinValues.ContainsKey(pin.Id)) continue;
                if (pin.DeclaredDefault != null) continue;




                if (string.Equals(pin.Id, PinIds.Self, StringComparison.OrdinalIgnoreCase)) continue;

                problems.Add(new GraphDiagnostic
                {
                    Code = GraphDiagnosticCodes.MissingRequiredInput,
                    Severity = GraphSeverity.Error,
                    Message = $"'{(pin.Label.Length > 0 ? pin.Label : pin.Id)}' needs a value.",
                    NodeId = node.Id,
                    PinId = pin.Id,
                });
            }
        }
    }

    private static void ValidateEntries(
        GraphDocument document,
        IReadOnlyDictionary<string, NodeDefinition> definitions,
        List<GraphDiagnostic> problems)
    {
        var entries = document.Nodes
            .Where(n => definitions.TryGetValue(n.Id, out var d)
                        && d.Kind is GraphNodeKind.EventEntry or GraphNodeKind.FunctionEntry)
            .ToList();

        if (entries.Count == 0 && document.Nodes.Count > 0)
        {
            problems.Add(GraphDiagnostic.Error(
                GraphDiagnosticCodes.NoEntryNodes,
                "This graph has no event or function to start from, so nothing would run."));
        }




        foreach (var duplicate in entries
            .GroupBy(n => (definitions[n.Id].IsRemoteEvent ? definitions[n.Id].OwnerScript + "." : "")
                          + EntryNameOf(n, definitions[n.Id]) + "/" + (n.ConfigString("state") ?? ""),
                StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1))
        {
            var first = duplicate.First();
            problems.Add(new GraphDiagnostic
            {
                Code = GraphDiagnosticCodes.DuplicateDeclaration,
                Severity = GraphSeverity.Error,
                Message = $"'{EntryNameOf(first, definitions[first.Id])}' is declared more than once.",
                NodeId = first.Id,
                RelatedNodes = duplicate.Skip(1).Select(n => n.Id).ToList(),
            });
        }

        ValidateStates(document, definitions, entries, problems);
        ValidateCustomEvents(document, problems);
    }


    private static void ValidateCustomEvents(GraphDocument document, List<GraphDiagnostic> problems)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var declared in document.CustomEvents)
        {
            if (!IsIdentifier(declared))
            {
                problems.Add(GraphDiagnostic.Error(
                    GraphDiagnosticCodes.InvalidScriptHeader,
                    $"'{declared}' is not a usable custom event name."));
                continue;
            }

            if (!seen.Add(declared))
            {
                problems.Add(GraphDiagnostic.Error(
                    GraphDiagnosticCodes.DuplicateDeclaration,
                    $"Custom event '{declared}' is declared more than once."));
            }
        }
    }










    private static void ValidateStates(
        GraphDocument document,
        IReadOnlyDictionary<string, NodeDefinition> definitions,
        IReadOnlyList<GraphNode> entries,
        List<GraphDiagnostic> problems)
    {
        var declared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            var state = entry.ConfigString("state");
            if (string.IsNullOrWhiteSpace(state)) continue;

            if (!IsIdentifier(state))
            {
                problems.Add(GraphDiagnostic.Error(
                    GraphDiagnosticCodes.InvalidScriptHeader,
                    $"'{state}' is not a usable state name.",
                    entry.Id));
                continue;
            }

            declared.Add(state);

            if (entry.ConfigString("global") == "true")
            {
                problems.Add(GraphDiagnostic.Error(
                    GraphDiagnosticCodes.InvalidScriptHeader,
                    "A global function belongs to the script, not to a state, so it cannot be "
                    + "placed in one.",
                    entry.Id));
            }
        }

        var auto = document.Header.AutoState;
        if (!string.IsNullOrWhiteSpace(auto) && !declared.Contains(auto))
        {
            problems.Add(GraphDiagnostic.Error(
                GraphDiagnosticCodes.InvalidScriptHeader,
                $"The script starts in state '{auto}', but no event or function declares that state."));
        }
    }


    public static string EntryNameOf(GraphNode node, NodeDefinition definition) =>
        definition.Kind == GraphNodeKind.FunctionEntry
            ? node.ConfigString("name") ?? "Unnamed"
            : definition.MemberName ?? definition.Title;

    private static bool IsIdentifier(string? text) =>
        !string.IsNullOrEmpty(text)
        && (char.IsLetter(text[0]) || text[0] == '_')
        && text.All(c => char.IsLetterOrDigit(c) || c == '_');
}
