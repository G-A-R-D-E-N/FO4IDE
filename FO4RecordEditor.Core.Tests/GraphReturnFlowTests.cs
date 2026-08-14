using System.Linq;
using FluentAssertions;
using FO4RecordEditor.Services.Graph;

namespace FO4RecordEditor.Core.Tests;

public class GraphReturnFlowTests
{
    private static GraphDiagnostic RefusedForFallingOff(GraphDocument document)
    {
        var result = GraphTestEnvironment.Compile(document);

        result.Success.Should().BeFalse("control can leave this function without a Return");
        var match = result.Errors.FirstOrDefault(d => d.Code == GraphDiagnosticCodes.NotAllPathsReturn);
        match.Should().NotBeNull(
            $"expected {GraphDiagnosticCodes.NotAllPathsReturn}; got {GraphTestEnvironment.Describe(result.Diagnostics)}");
        return match!;
    }

    private static void Accepted(GraphDocument document)
    {
        var result = GraphTestEnvironment.Compile(document);

        result.Diagnostics.Should().NotContain(d => d.Code == GraphDiagnosticCodes.NotAllPathsReturn,
            GraphTestEnvironment.Describe(result.Diagnostics));
        result.Success.Should().BeTrue(GraphTestEnvironment.Describe(result.Diagnostics));
    }

    [Fact]
    public void A_returning_function_with_an_empty_body_is_refused_naming_the_entry()
    {
        var palette = GraphTestEnvironment.Palette();
        var graph = new GraphBuilder("Fixture", "ObjectReference");

        var entry = graph.Node(palette, BuiltinNodeDefinitions.FunctionEntry,
            ("name", "Score"), ("returns", "int"));

        var diagnostic = RefusedForFallingOff(graph.Document);

        diagnostic.NodeId.Should().Be(entry);
        diagnostic.RelatedNodes.Should().Contain(entry);
    }

    [Fact]
    public void A_returning_function_that_runs_off_the_end_of_a_straight_line_is_refused()
    {
        var palette = GraphTestEnvironment.Palette();
        var graph = new GraphBuilder("Fixture", "ObjectReference");

        var entry = graph.Node(palette, BuiltinNodeDefinitions.FunctionEntry,
            ("name", "Score"), ("returns", "int"));
        var disable = graph.Node(palette, "call:ObjectReference.Disable");

        graph.Wire(entry, PinIds.Exec, disable, PinIds.Exec);

        var diagnostic = RefusedForFallingOff(graph.Document);

        diagnostic.NodeId.Should().Be(entry);
        diagnostic.RelatedNodes.Should().Contain(disable, "the call is where the path leaves");
    }

    [Fact]
    public void A_branch_that_returns_on_only_one_arm_is_refused_naming_the_branch()
    {
        var palette = GraphTestEnvironment.Palette();
        var graph = new GraphBuilder("Fixture", "ObjectReference");

        var entry = graph.Node(palette, BuiltinNodeDefinitions.FunctionEntry,
            ("name", "Score"), ("returns", "int"));
        var enabled = graph.Node(palette, "call:ObjectReference.IsEnabled");
        var branch = graph.Node(palette, BuiltinNodeDefinitions.Branch);
        var yes = graph.Node(palette, BuiltinNodeDefinitions.Return);
        var one = graph.Node(palette, "literal.int");

        graph.Value(one, PinIds.Value, "int", "1");
        graph.Wire(entry, PinIds.Exec, enabled, PinIds.Exec);
        graph.Wire(enabled, PinIds.Then, branch, PinIds.Exec);
        graph.Wire(enabled, PinIds.Return, branch, PinIds.Condition);
        graph.Wire(branch, PinIds.Then, yes, PinIds.Exec);
        graph.Wire(one, PinIds.Value, yes, PinIds.Value);

        var diagnostic = RefusedForFallingOff(graph.Document);

        diagnostic.NodeId.Should().Be(entry);
        diagnostic.RelatedNodes.Should().Contain(branch);
        diagnostic.RelatedNodes.Should().NotContain(yes, "the arm that does return is not at fault");
    }

    [Fact]
    public void A_loop_whose_body_always_returns_still_has_to_return_after_the_loop()
    {

        var palette = GraphTestEnvironment.Palette();
        var graph = new GraphBuilder("Fixture", "ObjectReference");

        var entry = graph.Node(palette, BuiltinNodeDefinitions.FunctionEntry,
            ("name", "Score"), ("returns", "int"));
        var enabled = graph.Node(palette, "call:ObjectReference.IsEnabled");
        var loop = graph.Node(palette, BuiltinNodeDefinitions.While);
        var ret = graph.Node(palette, BuiltinNodeDefinitions.Return);
        var one = graph.Node(palette, "literal.int");

        graph.Value(one, PinIds.Value, "int", "1");
        graph.Wire(entry, PinIds.Exec, enabled, PinIds.Exec);
        graph.Wire(enabled, PinIds.Then, loop, PinIds.Exec);
        graph.Wire(enabled, PinIds.Return, loop, PinIds.Condition);
        graph.Wire(loop, PinIds.Body, ret, PinIds.Exec);
        graph.Wire(one, PinIds.Value, ret, PinIds.Value);

        var diagnostic = RefusedForFallingOff(graph.Document);

        diagnostic.RelatedNodes.Should().Contain(loop, "the unwired Completed pin is the way out");
    }

    [Fact]
    public void A_loop_followed_by_a_return_is_accepted()
    {
        var palette = GraphTestEnvironment.Palette();
        var graph = new GraphBuilder("Fixture", "ObjectReference");

        var entry = graph.Node(palette, BuiltinNodeDefinitions.FunctionEntry,
            ("name", "Score"), ("returns", "int"));
        var enabled = graph.Node(palette, "call:ObjectReference.IsEnabled");
        var loop = graph.Node(palette, BuiltinNodeDefinitions.While);
        var disable = graph.Node(palette, "call:ObjectReference.Disable");
        var ret = graph.Node(palette, BuiltinNodeDefinitions.Return);
        var one = graph.Node(palette, "literal.int");

        graph.Value(one, PinIds.Value, "int", "1");
        graph.Wire(entry, PinIds.Exec, enabled, PinIds.Exec);
        graph.Wire(enabled, PinIds.Then, loop, PinIds.Exec);
        graph.Wire(enabled, PinIds.Return, loop, PinIds.Condition);
        graph.Wire(loop, PinIds.Body, disable, PinIds.Exec);
        graph.Wire(disable, PinIds.Then, loop, PinIds.Exec);
        graph.Wire(loop, PinIds.Completed, ret, PinIds.Exec);
        graph.Wire(one, PinIds.Value, ret, PinIds.Value);

        Accepted(graph.Document);
    }

    [Fact]
    public void Nested_branches_that_all_reach_a_return_are_accepted()
    {
        var palette = GraphTestEnvironment.Palette();
        var graph = new GraphBuilder("Fixture", "ObjectReference");

        var entry = graph.Node(palette, BuiltinNodeDefinitions.FunctionEntry,
            ("name", "Score"), ("returns", "int"));
        var enabled = graph.Node(palette, "call:ObjectReference.IsEnabled");
        var outer = graph.Node(palette, BuiltinNodeDefinitions.Branch);
        var inner = graph.Node(palette, BuiltinNodeDefinitions.Branch);
        var first = graph.Node(palette, BuiltinNodeDefinitions.Return);
        var second = graph.Node(palette, BuiltinNodeDefinitions.Return);
        var third = graph.Node(palette, BuiltinNodeDefinitions.Return);
        var one = graph.Node(palette, "literal.int");
        var two = graph.Node(palette, "literal.int");
        var three = graph.Node(palette, "literal.int");

        graph.Value(one, PinIds.Value, "int", "1");
        graph.Value(two, PinIds.Value, "int", "2");
        graph.Value(three, PinIds.Value, "int", "3");
        graph.Wire(entry, PinIds.Exec, enabled, PinIds.Exec);
        graph.Wire(enabled, PinIds.Then, outer, PinIds.Exec);
        graph.Wire(enabled, PinIds.Return, outer, PinIds.Condition);
        graph.Wire(outer, PinIds.Then, inner, PinIds.Exec);
        graph.Wire(enabled, PinIds.Return, inner, PinIds.Condition);
        graph.Wire(inner, PinIds.Then, first, PinIds.Exec);
        graph.Wire(inner, PinIds.Else, second, PinIds.Exec);
        graph.Wire(outer, PinIds.Else, third, PinIds.Exec);
        graph.Wire(one, PinIds.Value, first, PinIds.Value);
        graph.Wire(two, PinIds.Value, second, PinIds.Value);
        graph.Wire(three, PinIds.Value, third, PinIds.Value);

        Accepted(graph.Document);
    }

    [Fact]
    public void A_void_function_that_falls_off_the_end_is_fine()
    {

        var palette = GraphTestEnvironment.Palette();
        var graph = new GraphBuilder("Fixture", "ObjectReference");

        var entry = graph.Node(palette, BuiltinNodeDefinitions.FunctionEntry, ("name", "Go"));
        var disable = graph.Node(palette, "call:ObjectReference.Disable");

        graph.Wire(entry, PinIds.Exec, disable, PinIds.Exec);

        Accepted(graph.Document);
    }

    [Fact]
    public void An_event_is_never_asked_to_return_a_value()
    {
        var palette = GraphTestEnvironment.Palette();
        var graph = new GraphBuilder("Fixture", "ObjectReference");

        var entry = graph.Node(palette, "event:ObjectReference.OnLoad");
        var disable = graph.Node(palette, "call:ObjectReference.Disable");

        graph.Wire(entry, PinIds.Exec, disable, PinIds.Exec);

        Accepted(graph.Document);
    }
}
