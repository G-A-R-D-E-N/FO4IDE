using System.Linq;
using FluentAssertions;
using FO4RecordEditor.Services.Graph;

namespace FO4RecordEditor.Core.Tests;

/// <summary>
/// Invalid graphs, and the exact diagnostic each one has to produce.
/// </summary>
/// <remarks>
/// Every assertion checks the code and the node id as structured fields rather than searching the
/// message text. That is what makes "the gate turns red and names the node" a property of the
/// compiler rather than of how a message happens to be worded, and it is what the canvas relies on
/// to paint the offending pin.
/// </remarks>
public class GraphNegativeTests
{
    private static GraphDiagnostic Refused(GraphDocument document, string expectedCode)
    {
        var result = GraphTestEnvironment.Compile(document);

        result.Success.Should().BeFalse("this graph should not compile");
        var match = result.Errors.FirstOrDefault(d => d.Code == expectedCode);
        match.Should().NotBeNull(
            $"expected {expectedCode}; got {GraphTestEnvironment.Describe(result.Diagnostics)}");
        return match!;
    }

    [Fact]
    public void An_actor_wired_to_an_int_input_is_refused_naming_the_target_pin()
    {
        var palette = GraphTestEnvironment.Palette();
        var graph = new GraphBuilder("Fixture", "ObjectReference");

        var entry = graph.Node(palette, "event:ObjectReference.OnLoad");
        var player = graph.Node(palette, "global:Game.GetPlayer");
        var add = graph.Node(palette, "call:ObjectReference.AddItem");
        var none = graph.Node(palette, BuiltinNodeDefinitions.NoneValue);

        graph.Wire(entry, PinIds.Exec, player, PinIds.Exec);
        graph.Wire(player, PinIds.Then, add, PinIds.Exec);
        graph.Wire(none, PinIds.Value, add, "arg:akItemToAdd");
        graph.Wire(player, PinIds.Return, add, "arg:aiCount");

        var diagnostic = Refused(graph.Document, GraphDiagnosticCodes.IncompatibleWireType);

        diagnostic.NodeId.Should().Be(add);
        diagnostic.PinId.Should().Be("arg:aiCount");
        diagnostic.RelatedNodes.Should().Contain(player);
    }

    [Fact]
    public void A_narrowing_wire_demands_a_cast_rather_than_being_inserted_silently()
    {
        // float into int loses information, so the author owns the decision by placing a Cast node.
        var palette = GraphTestEnvironment.Palette();
        var graph = new GraphBuilder("Fixture", "ObjectReference");

        var entry = graph.Node(palette, "event:ObjectReference.OnLoad");
        var add = graph.Node(palette, "call:ObjectReference.AddItem");
        var none = graph.Node(palette, BuiltinNodeDefinitions.NoneValue);
        var number = graph.Node(palette, "literal.float");

        graph.Value(number, PinIds.Value, "float", "1.5");
        graph.Wire(entry, PinIds.Exec, add, PinIds.Exec);
        graph.Wire(none, PinIds.Value, add, "arg:akItemToAdd");
        graph.Wire(number, PinIds.Value, add, "arg:aiCount");

        var diagnostic = Refused(graph.Document, GraphDiagnosticCodes.NarrowingWireNeedsCast);

        diagnostic.NodeId.Should().Be(add);
        diagnostic.PinId.Should().Be("arg:aiCount");
        diagnostic.Message.Should().Contain("Cast");
    }

    [Fact]
    public void Two_wires_into_one_value_input_are_refused()
    {
        var palette = GraphTestEnvironment.Palette();
        var graph = new GraphBuilder("Fixture", "ObjectReference");

        var entry = graph.Node(palette, "event:ObjectReference.OnLoad");
        var wait = graph.Node(palette, "global:Utility.Wait");
        var first = graph.Node(palette, "literal.float");
        var second = graph.Node(palette, "literal.float");

        graph.Value(first, PinIds.Value, "float", "1.0");
        graph.Value(second, PinIds.Value, "float", "2.0");
        graph.Wire(entry, PinIds.Exec, wait, PinIds.Exec);
        graph.Wire(first, PinIds.Value, wait, "arg:afSeconds");
        graph.Wire(second, PinIds.Value, wait, "arg:afSeconds");

        var diagnostic = Refused(graph.Document, GraphDiagnosticCodes.MultipleDataSources);

        diagnostic.NodeId.Should().Be(wait);
        diagnostic.PinId.Should().Be("arg:afSeconds");
    }

    [Fact]
    public void Two_wires_out_of_one_control_flow_output_are_refused()
    {
        var palette = GraphTestEnvironment.Palette();
        var graph = new GraphBuilder("Fixture", "ObjectReference");

        var entry = graph.Node(palette, "event:ObjectReference.OnLoad");
        var first = graph.Node(palette, "global:Debug.Notification");
        var second = graph.Node(palette, "global:Debug.Notification");

        graph.Value(first, "arg:asNotificationText", "string", "\"a\"");
        graph.Value(second, "arg:asNotificationText", "string", "\"b\"");
        graph.Wire(entry, PinIds.Exec, first, PinIds.Exec);
        graph.Wire(entry, PinIds.Exec, second, PinIds.Exec);

        var diagnostic = Refused(graph.Document, GraphDiagnosticCodes.MultipleExecSuccessors);

        diagnostic.NodeId.Should().Be(entry);
        diagnostic.PinId.Should().Be(PinIds.Exec);
    }

    [Fact]
    public void A_control_flow_pin_wired_to_a_value_pin_is_refused()
    {
        var palette = GraphTestEnvironment.Palette();
        var graph = new GraphBuilder("Fixture", "ObjectReference");

        var entry = graph.Node(palette, "event:ObjectReference.OnLoad");
        var wait = graph.Node(palette, "global:Utility.Wait");
        graph.Wire(entry, PinIds.Exec, wait, "arg:afSeconds");

        var diagnostic = Refused(graph.Document, GraphDiagnosticCodes.PinKindMismatch);

        diagnostic.NodeId.Should().Be(wait);
        diagnostic.PinId.Should().Be("arg:afSeconds");
    }

    [Fact]
    public void A_wire_between_two_outputs_is_refused()
    {
        var palette = GraphTestEnvironment.Palette();
        var graph = new GraphBuilder("Fixture", "ObjectReference");

        var entry = graph.Node(palette, "event:ObjectReference.OnLoad");
        var first = graph.Node(palette, "literal.int");
        var second = graph.Node(palette, "literal.int");
        _ = entry;
        graph.Wire(first, PinIds.Value, second, PinIds.Value);

        Refused(graph.Document, GraphDiagnosticCodes.WireDirection);
    }

    [Fact]
    public void A_missing_required_input_is_refused_naming_the_pin()
    {
        var palette = GraphTestEnvironment.Palette();
        var graph = new GraphBuilder("Fixture", "ObjectReference");

        var entry = graph.Node(palette, "event:ObjectReference.OnLoad");
        var wait = graph.Node(palette, "global:Utility.Wait");
        graph.Wire(entry, PinIds.Exec, wait, PinIds.Exec);

        var diagnostic = Refused(graph.Document, GraphDiagnosticCodes.MissingRequiredInput);

        diagnostic.NodeId.Should().Be(wait);
        diagnostic.PinId.Should().Be("arg:afSeconds");
    }

    [Fact]
    public void A_node_whose_definition_is_not_on_the_palette_is_refused_rather_than_guessed()
    {
        // Mirrors the back end's refusal of an unresolved callee: arity is not derivable once
        // optional parameters exist, so guessing would emit a call with the wrong argument count.
        var palette = GraphTestEnvironment.Palette();
        var graph = new GraphBuilder("Fixture", "ObjectReference");

        var entry = graph.Node(palette, "event:ObjectReference.OnLoad");
        var missing = graph.Node("call:SomeModsScript.DoThing", GraphNodeKind.Call);
        graph.Wire(entry, PinIds.Exec, missing, PinIds.Exec);

        var diagnostic = Refused(graph.Document, GraphDiagnosticCodes.UnknownNodeDefinition);

        diagnostic.NodeId.Should().Be(missing);
        diagnostic.Message.Should().Contain("import roots");
    }

    [Fact]
    public void A_wire_to_a_pin_that_no_longer_exists_is_refused_naming_the_node()
    {
        // The case the "pins are never stored" decision exists to produce.
        var palette = GraphTestEnvironment.Palette();
        var graph = new GraphBuilder("Fixture", "ObjectReference");

        var entry = graph.Node(palette, "event:ObjectReference.OnLoad");
        var notify = graph.Node(palette, "global:Debug.Notification");
        graph.Wire(entry, PinIds.Exec, notify, PinIds.Exec);
        graph.Wire(entry, "param:wasRenamedAway", notify, "arg:asNotificationText");

        var diagnostic = Refused(graph.Document, GraphDiagnosticCodes.DanglingWire);

        diagnostic.NodeId.Should().Be(entry);
        diagnostic.PinId.Should().Be("param:wasRenamedAway");
    }

    [Fact]
    public void A_control_flow_loop_back_to_a_node_that_is_not_a_loop_is_refused()
    {
        // Papyrus has no goto, so this shape simply cannot be written as a script.
        var palette = GraphTestEnvironment.Palette();
        var graph = new GraphBuilder("Fixture", "ObjectReference");

        var entry = graph.Node(palette, "event:ObjectReference.OnLoad");
        var first = graph.Node(palette, "global:Debug.Notification");
        var second = graph.Node(palette, "global:Debug.Notification");

        graph.Value(first, "arg:asNotificationText", "string", "\"a\"");
        graph.Value(second, "arg:asNotificationText", "string", "\"b\"");
        graph.Wire(entry, PinIds.Exec, first, PinIds.Exec);
        graph.Wire(first, PinIds.Then, second, PinIds.Exec);
        graph.Wire(second, PinIds.Then, first, PinIds.Exec);

        var diagnostic = Refused(graph.Document, GraphDiagnosticCodes.ExecCycle);

        diagnostic.NodeId.Should().Be(first);
        diagnostic.RelatedNodes.Should().Contain(second);
        diagnostic.Message.Should().Contain("goto");
    }

    [Fact]
    public void A_graph_with_no_entry_node_is_refused()
    {
        var palette = GraphTestEnvironment.Palette();
        var graph = new GraphBuilder("Fixture", "ObjectReference");
        graph.Node(palette, "global:Debug.Notification");

        Refused(graph.Document, GraphDiagnosticCodes.NoEntryNodes);
    }

    [Fact]
    public void Two_handlers_for_the_same_event_are_refused()
    {
        var palette = GraphTestEnvironment.Palette();
        var graph = new GraphBuilder("Fixture", "ObjectReference");

        var first = graph.Node(palette, "event:ObjectReference.OnLoad");
        var second = graph.Node(palette, "event:ObjectReference.OnLoad");

        var diagnostic = Refused(graph.Document, GraphDiagnosticCodes.DuplicateDeclaration);

        diagnostic.NodeId.Should().Be(first);
        diagnostic.RelatedNodes.Should().Contain(second);
    }

    [Fact]
    public void A_duplicate_variable_name_is_refused()
    {
        var palette = GraphTestEnvironment.Palette();
        var graph = new GraphBuilder("Fixture", "ObjectReference");
        graph.Variable("Counter", "int").Variable("counter", "float");
        graph.Node(palette, "event:ObjectReference.OnLoad");

        Refused(graph.Document, GraphDiagnosticCodes.DuplicateVariableName);
    }

    [Fact]
    public void A_call_on_a_foreign_type_with_no_target_is_refused_naming_the_self_pin()
    {
        var palette = GraphTestEnvironment.Palette();
        var graph = new GraphBuilder("Fixture", "ObjectReference");

        var entry = graph.Node(palette, "event:ObjectReference.OnLoad");
        var level = graph.Node(palette, "call:Actor.GetLevel");
        graph.Wire(entry, PinIds.Exec, level, PinIds.Exec);

        var diagnostic = Refused(graph.Document, GraphDiagnosticCodes.UnconnectedSelf);

        diagnostic.NodeId.Should().Be(level);
        diagnostic.PinId.Should().Be(PinIds.Self);
    }

    [Fact]
    public void An_unknown_parent_script_is_refused()
    {
        var palette = GraphTestEnvironment.Palette();
        var graph = new GraphBuilder("Fixture", "NoSuchBaseScript");
        graph.Node(palette, BuiltinNodeDefinitions.FunctionEntry, ("name", "Go"));

        Refused(graph.Document, GraphDiagnosticCodes.UndeclaredReference);
    }

    [Fact]
    public void A_graph_with_no_script_name_is_refused()
    {
        var document = new GraphDocument();
        document.Header.ScriptName = "";

        Refused(document, GraphDiagnosticCodes.InvalidScriptHeader);
    }

    [Fact]
    public void A_returning_function_whose_return_node_carries_no_value_is_refused()
    {
        var palette = GraphTestEnvironment.Palette();
        var graph = new GraphBuilder("Fixture", "ObjectReference");

        var entry = graph.Node(palette, BuiltinNodeDefinitions.FunctionEntry,
            ("name", "Count"), ("returns", "int"));
        var ret = graph.Node(palette, BuiltinNodeDefinitions.Return);
        graph.Wire(entry, PinIds.Exec, ret, PinIds.Exec);

        var diagnostic = Refused(graph.Document, GraphDiagnosticCodes.ReturnValueMissing);

        diagnostic.NodeId.Should().Be(ret);
        diagnostic.PinId.Should().Be(PinIds.Value);
    }

    [Fact]
    public void A_void_function_whose_return_node_carries_a_value_is_refused()
    {
        var palette = GraphTestEnvironment.Palette();
        var graph = new GraphBuilder("Fixture", "ObjectReference");

        var entry = graph.Node(palette, BuiltinNodeDefinitions.FunctionEntry, ("name", "Go"));
        var ret = graph.Node(palette, BuiltinNodeDefinitions.Return);
        var number = graph.Node(palette, "literal.int");

        graph.Value(number, PinIds.Value, "int", "1");
        graph.Wire(entry, PinIds.Exec, ret, PinIds.Exec);
        graph.Wire(number, PinIds.Value, ret, PinIds.Value);

        var diagnostic = Refused(graph.Document, GraphDiagnosticCodes.ReturnValueUnexpected);

        diagnostic.NodeId.Should().Be(ret);
    }

    [Fact]
    public void Every_refusal_names_a_node_or_says_why_it_cannot()
    {
        // A diagnostic with no node is unusable by the canvas, so the only ones allowed to omit it
        // are the document-level ones that genuinely have no single node to blame.
        var documentLevel = new[]
        {
            GraphDiagnosticCodes.InvalidScriptHeader,
            GraphDiagnosticCodes.UndeclaredReference,
            GraphDiagnosticCodes.NoEntryNodes,
            GraphDiagnosticCodes.DuplicateVariableName,
            GraphDiagnosticCodes.MalformedDocument,
            GraphDiagnosticCodes.UnsupportedSchema,
        };

        var palette = GraphTestEnvironment.Palette();
        var graph = new GraphBuilder("Fixture", "ObjectReference");
        var entry = graph.Node(palette, "event:ObjectReference.OnLoad");
        var wait = graph.Node(palette, "global:Utility.Wait");
        graph.Wire(entry, PinIds.Exec, wait, PinIds.Exec);

        var result = GraphTestEnvironment.Compile(graph.Document);

        foreach (var diagnostic in result.Errors)
        {
            if (documentLevel.Contains(diagnostic.Code)) continue;
            diagnostic.NodeId.Should().NotBeNullOrEmpty(
                $"{diagnostic.Code} has to name the node it is about");
        }
    }
}
