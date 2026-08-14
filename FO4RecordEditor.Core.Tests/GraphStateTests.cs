using System.Linq;
using FluentAssertions;
using FO4RecordEditor.Services.Graph;

namespace FO4RecordEditor.Core.Tests;

public class GraphStateTests
{
    private static string Source(GraphDocument document)
    {
        var result = GraphTestEnvironment.Compile(document);
        result.Success.Should().BeTrue(GraphTestEnvironment.Describe(result.Diagnostics));
        return result.Source!;
    }

    private static GraphDiagnostic Refused(GraphDocument document, string expectedCode)
    {
        var result = GraphTestEnvironment.Compile(document);

        result.Success.Should().BeFalse("this graph should not compile");
        var match = result.Errors.FirstOrDefault(d => d.Code == expectedCode);
        match.Should().NotBeNull(
            $"expected {expectedCode}; got {GraphTestEnvironment.Describe(result.Diagnostics)}");
        return match!;
    }

    private static GraphBuilder TwoStates(NodePalette palette, out string idle, out string busy)
    {
        var graph = new GraphBuilder("Fixture", "ObjectReference");

        idle = graph.Node(palette, "event:ObjectReference.OnLoad");
        var disable = graph.Node(palette, "call:ObjectReference.Disable");
        graph.Wire(idle, PinIds.Exec, disable, PinIds.Exec);

        busy = graph.Node(palette, "event:ObjectReference.OnLoad", ("state", "Busy"));
        var enable = graph.Node(palette, "call:ObjectReference.Enable");
        graph.Wire(busy, PinIds.Exec, enable, PinIds.Exec);

        return graph;
    }

    [Fact]
    public void A_handler_carrying_a_state_is_emitted_inside_that_state()
    {
        var palette = GraphTestEnvironment.Palette();
        var graph = TwoStates(palette, out _, out _);

        var source = Source(graph.Document);

        source.Should().Contain("State Busy");
        source.Should().Contain("EndState");

        var stateStart = source.IndexOf("State Busy", System.StringComparison.Ordinal);
        var stateEnd = source.IndexOf("EndState", System.StringComparison.Ordinal);
        source[stateStart..stateEnd].Should().Contain("Enable()");
        source[stateStart..stateEnd].Should().NotContain("Disable()");
    }

    [Fact]
    public void The_starting_state_is_written_as_an_auto_state()
    {
        var palette = GraphTestEnvironment.Palette();
        var graph = TwoStates(palette, out _, out _);
        graph.AutoState("Busy");

        Source(graph.Document).Should().Contain("Auto State Busy");
    }

    [Fact]
    public void Only_the_starting_state_is_written_as_auto()
    {
        var palette = GraphTestEnvironment.Palette();
        var graph = new GraphBuilder("Fixture", "ObjectReference");
        graph.AutoState("Waiting");

        var waiting = graph.Node(palette, "event:ObjectReference.OnLoad", ("state", "Waiting"));
        var disable = graph.Node(palette, "call:ObjectReference.Disable");
        graph.Wire(waiting, PinIds.Exec, disable, PinIds.Exec);

        var busy = graph.Node(palette, "event:ObjectReference.OnLoad", ("state", "Busy"));
        var enable = graph.Node(palette, "call:ObjectReference.Enable");
        graph.Wire(busy, PinIds.Exec, enable, PinIds.Exec);

        var source = Source(graph.Document);

        source.Should().Contain("Auto State Waiting");
        source.Should().Contain("State Busy");
        source.Should().NotContain("Auto State Busy");
    }

    [Fact]
    public void A_starting_state_nothing_declares_is_refused()
    {

        var palette = GraphTestEnvironment.Palette();
        var graph = TwoStates(palette, out _, out _);
        graph.AutoState("Missing");

        var diagnostic = Refused(graph.Document, GraphDiagnosticCodes.InvalidScriptHeader);

        diagnostic.Message.Should().Contain("Missing");
    }

    [Fact]
    public void A_state_name_that_is_not_an_identifier_is_refused()
    {
        var palette = GraphTestEnvironment.Palette();
        var graph = new GraphBuilder("Fixture", "ObjectReference");

        var entry = graph.Node(palette, "event:ObjectReference.OnLoad", ("state", "not a name"));
        var disable = graph.Node(palette, "call:ObjectReference.Disable");
        graph.Wire(entry, PinIds.Exec, disable, PinIds.Exec);

        var diagnostic = Refused(graph.Document, GraphDiagnosticCodes.InvalidScriptHeader);

        diagnostic.NodeId.Should().Be(entry);
    }

    [Fact]
    public void A_global_function_cannot_be_placed_in_a_state()
    {
        var palette = GraphTestEnvironment.Palette();
        var graph = new GraphBuilder("Fixture", "ObjectReference");

        var entry = graph.Node(palette, BuiltinNodeDefinitions.FunctionEntry,
            ("name", "Helper"), ("global", "true"), ("state", "Busy"));
        var notify = graph.Node(palette, "global:Debug.Notification");

        graph.Value(notify, "arg:asNotificationText", "string", "\"hi\"");
        graph.Wire(entry, PinIds.Exec, notify, PinIds.Exec);

        var diagnostic = Refused(graph.Document, GraphDiagnosticCodes.InvalidScriptHeader);

        diagnostic.NodeId.Should().Be(entry);
        diagnostic.Message.Should().Contain("global");
    }

    [Fact]
    public void The_same_event_in_two_different_states_is_not_a_duplicate()
    {

        var palette = GraphTestEnvironment.Palette();
        var graph = TwoStates(palette, out _, out _);

        var result = GraphTestEnvironment.Compile(graph.Document);

        result.Diagnostics.Should().NotContain(d => d.Code == GraphDiagnosticCodes.DuplicateDeclaration,
            GraphTestEnvironment.Describe(result.Diagnostics));
        result.Success.Should().BeTrue(GraphTestEnvironment.Describe(result.Diagnostics));
    }

    [Fact]
    public void The_same_event_twice_in_one_state_is_still_a_duplicate()
    {
        var palette = GraphTestEnvironment.Palette();
        var graph = new GraphBuilder("Fixture", "ObjectReference");

        var first = graph.Node(palette, "event:ObjectReference.OnLoad", ("state", "Busy"));
        var disable = graph.Node(palette, "call:ObjectReference.Disable");
        graph.Wire(first, PinIds.Exec, disable, PinIds.Exec);

        var second = graph.Node(palette, "event:ObjectReference.OnLoad", ("state", "Busy"));
        var enable = graph.Node(palette, "call:ObjectReference.Enable");
        graph.Wire(second, PinIds.Exec, enable, PinIds.Exec);

        Refused(graph.Document, GraphDiagnosticCodes.DuplicateDeclaration);
    }

    [Fact]
    public void Sibling_entries_do_not_make_each_other_look_unreachable()
    {

        var palette = GraphTestEnvironment.Palette();
        var graph = new GraphBuilder("Fixture", "ObjectReference");

        var load = graph.Node(palette, "event:ObjectReference.OnLoad");
        var disable = graph.Node(palette, "call:ObjectReference.Disable");
        graph.Wire(load, PinIds.Exec, disable, PinIds.Exec);

        var activate = graph.Node(palette, "event:ObjectReference.OnActivate");
        var enable = graph.Node(palette, "call:ObjectReference.Enable");
        graph.Wire(activate, PinIds.Exec, enable, PinIds.Exec);

        var result = GraphTestEnvironment.Compile(graph.Document);

        result.Diagnostics.Should().NotContain(d => d.Code == GraphDiagnosticCodes.UnreachableExec,
            GraphTestEnvironment.Describe(result.Diagnostics));
    }

    [Fact]
    public void A_node_no_entry_reaches_is_still_reported()
    {

        var palette = GraphTestEnvironment.Palette();
        var graph = new GraphBuilder("Fixture", "ObjectReference");

        var load = graph.Node(palette, "event:ObjectReference.OnLoad");
        var disable = graph.Node(palette, "call:ObjectReference.Disable");
        graph.Wire(load, PinIds.Exec, disable, PinIds.Exec);

        var stranded = graph.Node(palette, "call:ObjectReference.Enable");
        var alsoStranded = graph.Node(palette, "call:ObjectReference.Disable");
        graph.Wire(stranded, PinIds.Then, alsoStranded, PinIds.Exec);

        var result = GraphTestEnvironment.Compile(graph.Document);

        result.Diagnostics.Should().Contain(
            d => d.Code == GraphDiagnosticCodes.UnreachableExec && d.NodeId == alsoStranded,
            GraphTestEnvironment.Describe(result.Diagnostics));
    }
}
