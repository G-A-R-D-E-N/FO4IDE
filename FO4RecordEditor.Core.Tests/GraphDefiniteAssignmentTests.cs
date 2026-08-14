using System.Linq;
using FluentAssertions;
using FO4RecordEditor.Services.Graph;

namespace FO4RecordEditor.Core.Tests;









public class GraphDefiniteAssignmentTests
{
    private static GraphDiagnostic Refused(GraphDocument document, string expectedCode)
    {
        var result = GraphTestEnvironment.Compile(document);

        result.Success.Should().BeFalse("this value is not assigned on every path");
        var match = result.Errors.FirstOrDefault(d => d.Code == expectedCode);
        match.Should().NotBeNull(
            $"expected {expectedCode}; got {GraphTestEnvironment.Describe(result.Diagnostics)}");
        return match!;
    }

    private static void Accepted(GraphDocument document)
    {
        var result = GraphTestEnvironment.Compile(document);

        result.Diagnostics.Should().NotContain(
            d => d.Code == GraphDiagnosticCodes.UseBeforeAssignment
                 || d.Code == GraphDiagnosticCodes.LoopConditionFromLoopBody,
            GraphTestEnvironment.Describe(result.Diagnostics));
        result.Success.Should().BeTrue(GraphTestEnvironment.Describe(result.Diagnostics));
    }

    [Fact]
    public void A_value_produced_on_one_arm_and_read_after_the_merge_is_refused()
    {


        var palette = GraphTestEnvironment.Palette();
        var graph = new GraphBuilder("Fixture", "ObjectReference");

        var entry = graph.Node(palette, "event:ObjectReference.OnLoad");
        var enabled = graph.Node(palette, "call:ObjectReference.IsEnabled");
        var branch = graph.Node(palette, BuiltinNodeDefinitions.Branch);
        var count = graph.Node(palette, "call:ObjectReference.GetItemCount");
        var add = graph.Node(palette, "call:ObjectReference.AddItem");
        var none = graph.Node(palette, BuiltinNodeDefinitions.NoneValue);

        graph.Wire(entry, PinIds.Exec, enabled, PinIds.Exec);
        graph.Wire(enabled, PinIds.Then, branch, PinIds.Exec);
        graph.Wire(enabled, PinIds.Return, branch, PinIds.Condition);
        graph.Wire(branch, PinIds.Then, count, PinIds.Exec);
        graph.Wire(count, PinIds.Then, add, PinIds.Exec);
        graph.Wire(branch, PinIds.Else, add, PinIds.Exec);
        graph.Wire(none, PinIds.Value, add, "arg:akItemToAdd");
        graph.Wire(count, PinIds.Return, add, "arg:aiCount");

        var diagnostic = Refused(graph.Document, GraphDiagnosticCodes.UseBeforeAssignment);

        diagnostic.NodeId.Should().Be(add);
        diagnostic.PinId.Should().Be("arg:aiCount");
        diagnostic.RelatedNodes.Should().Contain(count);
    }

    [Fact]
    public void An_operator_between_the_call_and_the_use_does_not_hide_it()
    {

        var palette = GraphTestEnvironment.Palette();
        var graph = new GraphBuilder("Fixture", "ObjectReference");

        var entry = graph.Node(palette, "event:ObjectReference.OnLoad");
        var enabled = graph.Node(palette, "call:ObjectReference.IsEnabled");
        var branch = graph.Node(palette, BuiltinNodeDefinitions.Branch);
        var count = graph.Node(palette, "call:ObjectReference.GetItemCount");
        var add = graph.Node(palette, "call:ObjectReference.AddItem");
        var none = graph.Node(palette, BuiltinNodeDefinitions.NoneValue);
        var sum = graph.Node(palette, "op.add");
        var one = graph.Node(palette, "literal.int");

        graph.Value(one, PinIds.Value, "int", "1");
        graph.Wire(entry, PinIds.Exec, enabled, PinIds.Exec);
        graph.Wire(enabled, PinIds.Then, branch, PinIds.Exec);
        graph.Wire(enabled, PinIds.Return, branch, PinIds.Condition);
        graph.Wire(branch, PinIds.Then, count, PinIds.Exec);
        graph.Wire(count, PinIds.Then, add, PinIds.Exec);
        graph.Wire(branch, PinIds.Else, add, PinIds.Exec);
        graph.Wire(none, PinIds.Value, add, "arg:akItemToAdd");
        graph.Wire(count, PinIds.Return, sum, PinIds.Left);
        graph.Wire(one, PinIds.Value, sum, PinIds.Right);
        graph.Wire(sum, PinIds.Return, add, "arg:aiCount");

        var diagnostic = Refused(graph.Document, GraphDiagnosticCodes.UseBeforeAssignment);

        diagnostic.NodeId.Should().Be(add, "the operator has no position of its own");
        diagnostic.RelatedNodes.Should().Contain(count);
    }

    [Fact]
    public void A_value_produced_inside_a_loop_and_read_after_it_is_refused()
    {

        var palette = GraphTestEnvironment.Palette();
        var graph = new GraphBuilder("Fixture", "ObjectReference");

        var entry = graph.Node(palette, "event:ObjectReference.OnLoad");
        var enabled = graph.Node(palette, "call:ObjectReference.IsEnabled");
        var loop = graph.Node(palette, BuiltinNodeDefinitions.While);
        var count = graph.Node(palette, "call:ObjectReference.GetItemCount");
        var add = graph.Node(palette, "call:ObjectReference.AddItem");
        var none = graph.Node(palette, BuiltinNodeDefinitions.NoneValue);

        graph.Wire(entry, PinIds.Exec, enabled, PinIds.Exec);
        graph.Wire(enabled, PinIds.Then, loop, PinIds.Exec);
        graph.Wire(enabled, PinIds.Return, loop, PinIds.Condition);
        graph.Wire(loop, PinIds.Body, count, PinIds.Exec);
        graph.Wire(count, PinIds.Then, loop, PinIds.Exec);
        graph.Wire(loop, PinIds.Completed, add, PinIds.Exec);
        graph.Wire(none, PinIds.Value, add, "arg:akItemToAdd");
        graph.Wire(count, PinIds.Return, add, "arg:aiCount");

        var diagnostic = Refused(graph.Document, GraphDiagnosticCodes.UseBeforeAssignment);

        diagnostic.NodeId.Should().Be(add);
        diagnostic.RelatedNodes.Should().Contain(count);
    }

    [Fact]
    public void A_loop_condition_fed_from_the_loop_body_is_refused_by_name()
    {


        var palette = GraphTestEnvironment.Palette();
        var graph = new GraphBuilder("Fixture", "ObjectReference");

        var entry = graph.Node(palette, "event:ObjectReference.OnLoad");
        var loop = graph.Node(palette, BuiltinNodeDefinitions.While);
        var enabled = graph.Node(palette, "call:ObjectReference.IsEnabled");

        graph.Wire(entry, PinIds.Exec, loop, PinIds.Exec);
        graph.Wire(loop, PinIds.Body, enabled, PinIds.Exec);
        graph.Wire(enabled, PinIds.Then, loop, PinIds.Exec);
        graph.Wire(enabled, PinIds.Return, loop, PinIds.Condition);

        var diagnostic = Refused(graph.Document, GraphDiagnosticCodes.LoopConditionFromLoopBody);

        diagnostic.NodeId.Should().Be(loop);
        diagnostic.PinId.Should().Be(PinIds.Condition);
        diagnostic.RelatedNodes.Should().Contain(enabled);
    }

    [Fact]
    public void A_value_produced_before_the_branch_is_readable_after_the_merge()
    {
        var palette = GraphTestEnvironment.Palette();
        var graph = new GraphBuilder("Fixture", "ObjectReference");

        var entry = graph.Node(palette, "event:ObjectReference.OnLoad");
        var count = graph.Node(palette, "call:ObjectReference.GetItemCount");
        var enabled = graph.Node(palette, "call:ObjectReference.IsEnabled");
        var branch = graph.Node(palette, BuiltinNodeDefinitions.Branch);
        var disable = graph.Node(palette, "call:ObjectReference.Disable");
        var add = graph.Node(palette, "call:ObjectReference.AddItem");
        var none = graph.Node(palette, BuiltinNodeDefinitions.NoneValue);

        graph.Wire(entry, PinIds.Exec, count, PinIds.Exec);
        graph.Wire(count, PinIds.Then, enabled, PinIds.Exec);
        graph.Wire(enabled, PinIds.Then, branch, PinIds.Exec);
        graph.Wire(enabled, PinIds.Return, branch, PinIds.Condition);
        graph.Wire(branch, PinIds.Then, disable, PinIds.Exec);
        graph.Wire(disable, PinIds.Then, add, PinIds.Exec);
        graph.Wire(branch, PinIds.Else, add, PinIds.Exec);
        graph.Wire(none, PinIds.Value, add, "arg:akItemToAdd");
        graph.Wire(count, PinIds.Return, add, "arg:aiCount");

        Accepted(graph.Document);
    }

    [Fact]
    public void A_value_produced_and_read_on_the_same_arm_is_accepted()
    {
        var palette = GraphTestEnvironment.Palette();
        var graph = new GraphBuilder("Fixture", "ObjectReference");

        var entry = graph.Node(palette, "event:ObjectReference.OnLoad");
        var enabled = graph.Node(palette, "call:ObjectReference.IsEnabled");
        var branch = graph.Node(palette, BuiltinNodeDefinitions.Branch);
        var count = graph.Node(palette, "call:ObjectReference.GetItemCount");
        var add = graph.Node(palette, "call:ObjectReference.AddItem");
        var none = graph.Node(palette, BuiltinNodeDefinitions.NoneValue);

        graph.Wire(entry, PinIds.Exec, enabled, PinIds.Exec);
        graph.Wire(enabled, PinIds.Then, branch, PinIds.Exec);
        graph.Wire(enabled, PinIds.Return, branch, PinIds.Condition);
        graph.Wire(branch, PinIds.Then, count, PinIds.Exec);
        graph.Wire(count, PinIds.Then, add, PinIds.Exec);
        graph.Wire(none, PinIds.Value, add, "arg:akItemToAdd");
        graph.Wire(count, PinIds.Return, add, "arg:aiCount");

        Accepted(graph.Document);
    }

    [Fact]
    public void A_value_produced_earlier_in_a_loop_body_is_readable_later_in_it()
    {
        var palette = GraphTestEnvironment.Palette();
        var graph = new GraphBuilder("Fixture", "ObjectReference");

        var entry = graph.Node(palette, "event:ObjectReference.OnLoad");
        var enabled = graph.Node(palette, "call:ObjectReference.IsEnabled");
        var loop = graph.Node(palette, BuiltinNodeDefinitions.While);
        var count = graph.Node(palette, "call:ObjectReference.GetItemCount");
        var add = graph.Node(palette, "call:ObjectReference.AddItem");
        var none = graph.Node(palette, BuiltinNodeDefinitions.NoneValue);

        graph.Wire(entry, PinIds.Exec, enabled, PinIds.Exec);
        graph.Wire(enabled, PinIds.Then, loop, PinIds.Exec);
        graph.Wire(enabled, PinIds.Return, loop, PinIds.Condition);
        graph.Wire(loop, PinIds.Body, count, PinIds.Exec);
        graph.Wire(count, PinIds.Then, add, PinIds.Exec);
        graph.Wire(add, PinIds.Then, loop, PinIds.Exec);
        graph.Wire(none, PinIds.Value, add, "arg:akItemToAdd");
        graph.Wire(count, PinIds.Return, add, "arg:aiCount");

        Accepted(graph.Document);
    }
}
