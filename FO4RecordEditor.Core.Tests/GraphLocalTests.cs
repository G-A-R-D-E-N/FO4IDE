using System.Linq;
using FluentAssertions;
using FO4RecordEditor.Services.Graph;

namespace FO4RecordEditor.Core.Tests;

public class GraphLocalTests
{
    private static string Source(GraphDocument document)
    {
        var result = GraphTestEnvironment.Compile(document);
        result.Success.Should().BeTrue(GraphTestEnvironment.Describe(result.Diagnostics));
        return result.Source!;
    }

    [Fact]
    public void A_declared_local_is_emitted_and_assignable()
    {
        var palette = GraphTestEnvironment.Palette();
        var graph = new GraphBuilder("Fixture", "ObjectReference");

        var entry = graph.Node(palette, "event:ObjectReference.OnLoad");
        var local = graph.Node(palette, BuiltinNodeDefinitions.LocalDeclare,
            ("name", "counter"), ("type", "int"));
        var zero = graph.Node(palette, "literal.int");

        graph.Value(zero, PinIds.Value, "int", "0");
        graph.Wire(entry, PinIds.Exec, local, PinIds.Exec);
        graph.Wire(zero, PinIds.Value, local, PinIds.Value);

        var source = Source(graph.Document);

        source.Should().Contain("int counter");
        source.Should().Contain("counter = 0");
    }

    [Fact]
    public void A_declared_local_needs_no_initial_value()
    {
        var palette = GraphTestEnvironment.Palette();
        var graph = new GraphBuilder("Fixture", "ObjectReference");

        var entry = graph.Node(palette, "event:ObjectReference.OnLoad");
        var local = graph.Node(palette, BuiltinNodeDefinitions.LocalDeclare,
            ("name", "counter"), ("type", "int"));
        var disable = graph.Node(palette, "call:ObjectReference.Disable");

        graph.Wire(entry, PinIds.Exec, local, PinIds.Exec);
        graph.Wire(local, PinIds.Then, disable, PinIds.Exec);

        var source = Source(graph.Document);

        source.Should().Contain("int counter");
        source.Should().NotContain("counter =");
    }

    [Fact]
    public void A_local_can_be_read_and_written_back()
    {

        var palette = GraphTestEnvironment.Palette();
        var graph = new GraphBuilder("Fixture", "ObjectReference");

        var entry = graph.Node(palette, "event:ObjectReference.OnLoad");
        var local = graph.Node(palette, BuiltinNodeDefinitions.LocalDeclare,
            ("name", "counter"), ("type", "int"));
        var zero = graph.Node(palette, "literal.int");
        var set = graph.Node(palette, BuiltinNodeDefinitions.VariableSet,
            ("name", "counter"), ("type", "int"));
        var get = graph.Node(palette, BuiltinNodeDefinitions.VariableGet,
            ("name", "counter"), ("type", "int"));
        var one = graph.Node(palette, "literal.int");
        var sum = graph.Node(palette, "op.add");

        graph.Value(zero, PinIds.Value, "int", "0");
        graph.Value(one, PinIds.Value, "int", "1");
        graph.Wire(entry, PinIds.Exec, local, PinIds.Exec);
        graph.Wire(zero, PinIds.Value, local, PinIds.Value);
        graph.Wire(local, PinIds.Then, set, PinIds.Exec);
        graph.Wire(get, PinIds.Value, sum, PinIds.Left);
        graph.Wire(one, PinIds.Value, sum, PinIds.Right);
        graph.Wire(sum, PinIds.Return, set, PinIds.Value);

        var source = Source(graph.Document);

        source.Should().Contain("int counter");
        source.Should().Contain("counter = counter + 1");
    }

    [Fact]
    public void A_local_survives_being_declared_inside_a_loop_body()
    {

        var palette = GraphTestEnvironment.Palette();
        var graph = new GraphBuilder("Fixture", "ObjectReference");

        var entry = graph.Node(palette, "event:ObjectReference.OnLoad");
        var enabled = graph.Node(palette, "call:ObjectReference.IsEnabled");
        var loop = graph.Node(palette, BuiltinNodeDefinitions.While);
        var local = graph.Node(palette, BuiltinNodeDefinitions.LocalDeclare,
            ("name", "each"), ("type", "int"));

        graph.Wire(entry, PinIds.Exec, enabled, PinIds.Exec);
        graph.Wire(enabled, PinIds.Then, loop, PinIds.Exec);
        graph.Wire(enabled, PinIds.Return, loop, PinIds.Condition);
        graph.Wire(loop, PinIds.Body, local, PinIds.Exec);
        graph.Wire(local, PinIds.Then, loop, PinIds.Exec);

        var source = Source(graph.Document);

        source.Split("int each").Length.Should().Be(2, "declared once, at function scope");
    }

    [Fact]
    public void An_array_local_keeps_its_array_type()
    {
        var palette = GraphTestEnvironment.Palette();
        var graph = new GraphBuilder("Fixture", "ObjectReference");

        var entry = graph.Node(palette, "event:ObjectReference.OnLoad");
        var local = graph.Node(palette, BuiltinNodeDefinitions.LocalDeclare,
            ("name", "things"), ("type", "Form[]"));

        graph.Wire(entry, PinIds.Exec, local, PinIds.Exec);

        Source(graph.Document).Should().Contain("Form[] things");
    }

    [Fact]
    public void The_same_local_declared_twice_is_refused()
    {
        var palette = GraphTestEnvironment.Palette();
        var graph = new GraphBuilder("Fixture", "ObjectReference");

        var entry = graph.Node(palette, "event:ObjectReference.OnLoad");
        var first = graph.Node(palette, BuiltinNodeDefinitions.LocalDeclare,
            ("name", "counter"), ("type", "int"));
        var second = graph.Node(palette, BuiltinNodeDefinitions.LocalDeclare,
            ("name", "counter"), ("type", "int"));

        graph.Wire(entry, PinIds.Exec, first, PinIds.Exec);
        graph.Wire(first, PinIds.Then, second, PinIds.Exec);

        var result = GraphTestEnvironment.Compile(graph.Document);

        result.Success.Should().BeFalse();
        var diagnostic = result.Errors.FirstOrDefault(
            d => d.Code == GraphDiagnosticCodes.DuplicateDeclaration);
        diagnostic.Should().NotBeNull(GraphTestEnvironment.Describe(result.Diagnostics));
        diagnostic!.NodeId.Should().Be(second);
    }

    [Fact]
    public void The_same_name_in_two_functions_is_fine()
    {

        var palette = GraphTestEnvironment.Palette();
        var graph = new GraphBuilder("Fixture", "ObjectReference");

        var first = graph.Node(palette, BuiltinNodeDefinitions.FunctionEntry, ("name", "A"));
        var localA = graph.Node(palette, BuiltinNodeDefinitions.LocalDeclare,
            ("name", "counter"), ("type", "int"));
        graph.Wire(first, PinIds.Exec, localA, PinIds.Exec);

        var second = graph.Node(palette, BuiltinNodeDefinitions.FunctionEntry, ("name", "B"));
        var localB = graph.Node(palette, BuiltinNodeDefinitions.LocalDeclare,
            ("name", "counter"), ("type", "int"));
        graph.Wire(second, PinIds.Exec, localB, PinIds.Exec);

        var result = GraphTestEnvironment.Compile(graph.Document);

        result.Diagnostics.Should().NotContain(
            d => d.Code == GraphDiagnosticCodes.DuplicateDeclaration,
            GraphTestEnvironment.Describe(result.Diagnostics));
        result.Success.Should().BeTrue(GraphTestEnvironment.Describe(result.Diagnostics));
    }
}
