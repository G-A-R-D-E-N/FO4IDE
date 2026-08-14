using System;
using System.Collections.Generic;
using System.Linq;

namespace FO4RecordEditor.Services.Graph;

/// <summary>
/// Checks that every value read on the canvas was actually produced on every path that reaches the
/// read.
/// </summary>
/// <remarks>
/// An impure node's output becomes a local assigned where that node runs. Papyrus declares locals at
/// function scope, so reading one on a path that never assigned it compiles cleanly and yields the
/// type's zero value at runtime. A wire drawn out of one arm of a branch into something after the
/// merge is the ordinary way to write that mistake, and it looks correct on the canvas: the wire is
/// right there.
/// <para>
/// The question is answered with dominance rather than with emission order. The lowering walks each
/// arm before the merge, so by the time it reaches the merge the local looks bound; only the control
/// flow graph knows that one arm was optional.
/// </para>
/// <para>
/// Pure producers are exempt because they are rebuilt inline at each use and never become a local.
/// A read through a chain of pure nodes is therefore attributed to the exec node at the end of the
/// chain, which is where the expression is actually evaluated.
/// </para>
/// </remarks>
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

            // The entry reads nothing: its data pins are outputs that name parameters.
            if (consumerDefinition.Kind is GraphNodeKind.EventEntry or GraphNodeKind.FunctionEntry) continue;

            foreach (var (producerId, pin) in Sources(document, definitions, consumer, consumerDefinition))
            {
                if (string.Equals(producerId, consumerId, StringComparison.Ordinal)) continue;
                if (!flow.IsReachable(producerId)) continue;   // already refused as unreachable
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

    /// <summary>
    /// The impure producers this node reads, paired with the pin on this node that reaches them.
    /// </summary>
    /// <remarks>
    /// Pure producers are walked through rather than reported, so a chain of operators between an
    /// impure call and its consumer does not hide the call. The pin reported stays the one on the
    /// consumer, because that is the pin the canvas can paint.
    /// </remarks>
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

                // A pure node is inlined at the use, so its own inputs are read here too. The guard
                // is against a data cycle, which has its own diagnostic and must not hang this pass.
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
