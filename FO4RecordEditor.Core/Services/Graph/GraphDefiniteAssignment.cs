using System;
using System.Collections.Generic;
using System.Linq;

namespace FO4RecordEditor.Services.Graph;

public static class GraphDefiniteAssignment
{
    public static IReadOnlyList<GraphDiagnostic> Check(
        GraphDocument document,
        IReadOnlyDictionary<string, NodeDefinition> definitions,
        GraphExecFlow flow)
    {
        var problems = new List<GraphDiagnostic>();

        foreach (var consumerId in flow.Reachable)
        {
            var consumer = document.Node(consumerId);
            if (consumer == null || !definitions.TryGetValue(consumerId, out var consumerDefinition)) continue;

            if (consumerDefinition.Kind is GraphNodeKind.EventEntry or GraphNodeKind.FunctionEntry) continue;

            foreach (var (producerId, pin) in Sources(document, definitions, consumer, consumerDefinition))
            {
                if (string.Equals(producerId, consumerId, StringComparison.Ordinal)) continue;
                if (!flow.IsReachable(producerId)) continue;
                if (flow.Dominates(producerId, consumerId)) continue;

                problems.Add(Describe(definitions, flow, producerId, consumerId, pin));
            }
        }

        return problems;
    }

    private static GraphDiagnostic Describe(
        IReadOnlyDictionary<string, NodeDefinition> definitions,
        GraphExecFlow flow,
        string producerId,
        string consumerId,
        PinRef pin)
    {
        var isLoop = definitions.TryGetValue(consumerId, out var consumerDefinition)
                     && consumerDefinition.Kind is GraphNodeKind.While or GraphNodeKind.ForEach;

        if (isLoop && flow.Dominates(consumerId, producerId))
        {
            return new GraphDiagnostic
            {
                Code = GraphDiagnosticCodes.LoopConditionFromLoopBody,
                Severity = GraphSeverity.Error,
                Message = "This loop reads a value produced inside its own body, so the value does "
                          + "not exist the first time the condition is tested. Produce it before the "
                          + "loop instead.",
                NodeId = consumerId,
                PinId = pin.Pin,
                RelatedNodes = new[] { producerId },
            };
        }

        return new GraphDiagnostic
        {
            Code = GraphDiagnosticCodes.UseBeforeAssignment,
            Severity = GraphSeverity.Error,
            Message = "This value comes from a node that does not run on every path that reaches "
                      + "here, so on some paths it would read as zero rather than fail to compile.",
            NodeId = consumerId,
            PinId = pin.Pin,
            RelatedNodes = new[] { producerId },
        };
    }

    private static IEnumerable<(string Producer, PinRef Pin)> Sources(
        GraphDocument document,
        IReadOnlyDictionary<string, NodeDefinition> definitions,
        GraphNode consumer,
        NodeDefinition consumerDefinition)
    {
        var found = new List<(string Producer, PinRef Pin)>();

        foreach (var inputPin in consumerDefinition.PinsFor(consumer, document))
        {
            if (inputPin.Kind != PinKind.Data || inputPin.Direction != PinDirection.In) continue;

            var pin = new PinRef(consumer.Id, inputPin.Id);
            Collect(pin, pin, new HashSet<string>(StringComparer.Ordinal));
        }

        return found.Distinct();

        void Collect(PinRef target, PinRef reportAt, HashSet<string> visited)
        {
            foreach (var wire in document.Into(target))
            {
                var sourceId = wire.From.Node;
                var source = document.Node(sourceId);
                if (source == null || !definitions.TryGetValue(sourceId, out var sourceDefinition)) continue;

                if (!sourceDefinition.IsPure)
                {
                    found.Add((sourceId, reportAt));
                    continue;
                }

                if (!visited.Add(sourceId)) continue;

                foreach (var upstream in sourceDefinition.PinsFor(source, document))
                {
                    if (upstream.Kind != PinKind.Data || upstream.Direction != PinDirection.In) continue;
                    Collect(new PinRef(sourceId, upstream.Id), reportAt, visited);
                }
            }
        }
    }
}
