using System.Linq;
using FluentAssertions;
using FO4RecordEditor.Services.Graph;

namespace FO4RecordEditor.Core.Tests;

/// <summary>
/// Loops whose body can be left early.
/// </summary>
/// <remarks>
/// Every graph in here used to overflow the stack rather than fail, which aborts the whole test
/// process instead of producing a diagnostic. The cause was region lowering walking a back edge: a
/// branch inside a loop with one terminating arm has no post-dominator, so the other arm was lowered
/// with no stopping point and ran back into the loop header. The iteration guard could not catch it
/// because the runaway is recursion, and the guard is per call.
/// <para>
/// These also pin the shape Break and Continue are built on: a terminal node in one arm collapses
/// the merge, so the rest of the body lands in the other arm on its own.
/// </para>
/// </remarks>
public class GraphLoopExitTests
{
    private static string Source(GraphDocument document)
    {
        var result = GraphTestEnvironment.Compile(document);
        result.Success.Should().BeTrue(GraphTestEnvironment.Describe(result.Diagnostics));
        return result.Source!;
    }

    [Fact]
    public void An_early_return_from_inside_a_loop_compiles()
    {
        var palette = GraphTestEnvironment.Palette();
        var graph = new GraphBuilder("Fixture", "ObjectReference");

        var entry = graph.Node(palette, BuiltinNodeDefinitions.FunctionEntry, ("name", "Go"));
        var enabled = graph.Node(palette, "call:ObjectReference.IsEnabled");
        var loop = graph.Node(palette, BuiltinNodeDefinitions.While);
        var first = graph.Node(palette, "global:Debug.Notification");
        var branch = graph.Node(palette, BuiltinNodeDefinitions.Branch);
        var ret = graph.Node(palette, BuiltinNodeDefinitions.Return);
        var rest = graph.Node(palette, "global:Debug.Notification");
        var after = graph.Node(palette, "global:Debug.Notification");

        graph.Value(first, "arg:asNotificationText", "string", "\"A\"");
        graph.Value(rest, "arg:asNotificationText", "string", "\"B\"");
        graph.Value(after, "arg:asNotificationText", "string", "\"after\"");

        graph.Wire(entry, PinIds.Exec, enabled, PinIds.Exec);
        graph.Wire(enabled, PinIds.Then, loop, PinIds.Exec);
        graph.Wire(enabled, PinIds.Return, loop, PinIds.Condition);
        graph.Wire(loop, PinIds.Body, first, PinIds.Exec);
        graph.Wire(first, PinIds.Then, branch, PinIds.Exec);
        graph.Wire(enabled, PinIds.Return, branch, PinIds.Condition);
        graph.Wire(branch, PinIds.Then, ret, PinIds.Exec);
        graph.Wire(branch, PinIds.Else, rest, PinIds.Exec);
        graph.Wire(rest, PinIds.Then, loop, PinIds.Exec);
        graph.Wire(loop, PinIds.Completed, after, PinIds.Exec);

        var source = Source(graph.Document);

        // The remaining body belongs to the else arm, and the loop is emitted exactly once.
        source.Should().Contain("Return");
        source.Should().Contain("Debug.Notification(\"B\")");
        source.Split("While (").Length.Should().Be(2, "the loop should be emitted once");

        var endWhile = source.IndexOf("EndWhile", System.StringComparison.Ordinal);
        source[endWhile..].Should().Contain("\"after\"", "the tail follows the loop");
        source[..endWhile].Should().Contain("\"B\"", "the rest of the body stays inside the loop");
    }

    [Fact]
    public void A_branch_arm_wired_straight_back_to_the_loop_header_compiles()
    {
        // Written by hand this is a continue. The arm's target IS the loop node, so lowering it
        // without a back edge check re-entered the loop immediately.
        var palette = GraphTestEnvironment.Palette();
        var graph = new GraphBuilder("Fixture", "ObjectReference");

        var entry = graph.Node(palette, "event:ObjectReference.OnLoad");
        var enabled = graph.Node(palette, "call:ObjectReference.IsEnabled");
        var loop = graph.Node(palette, BuiltinNodeDefinitions.While);
        var branch = graph.Node(palette, BuiltinNodeDefinitions.Branch);
        var work = graph.Node(palette, "global:Debug.Notification");

        graph.Value(work, "arg:asNotificationText", "string", "\"work\"");

        graph.Wire(entry, PinIds.Exec, enabled, PinIds.Exec);
        graph.Wire(enabled, PinIds.Then, loop, PinIds.Exec);
        graph.Wire(enabled, PinIds.Return, loop, PinIds.Condition);
        graph.Wire(loop, PinIds.Body, branch, PinIds.Exec);
        graph.Wire(enabled, PinIds.Return, branch, PinIds.Condition);
        graph.Wire(branch, PinIds.Then, loop, PinIds.Exec);
        graph.Wire(branch, PinIds.Else, work, PinIds.Exec);
        graph.Wire(work, PinIds.Then, loop, PinIds.Exec);

        var source = Source(graph.Document);

        source.Split("While (").Length.Should().Be(2, "the loop should be emitted once");
        source.Should().Contain("Debug.Notification(\"work\")");
    }

    [Fact]
    public void A_return_from_a_nested_loop_compiles()
    {
        var palette = GraphTestEnvironment.Palette();
        var graph = new GraphBuilder("Fixture", "ObjectReference");

        var entry = graph.Node(palette, BuiltinNodeDefinitions.FunctionEntry, ("name", "Go"));
        var enabled = graph.Node(palette, "call:ObjectReference.IsEnabled");
        var outer = graph.Node(palette, BuiltinNodeDefinitions.While);
        var inner = graph.Node(palette, BuiltinNodeDefinitions.While);
        var branch = graph.Node(palette, BuiltinNodeDefinitions.Branch);
        var ret = graph.Node(palette, BuiltinNodeDefinitions.Return);
        var work = graph.Node(palette, "global:Debug.Notification");

        graph.Value(work, "arg:asNotificationText", "string", "\"inner\"");

        graph.Wire(entry, PinIds.Exec, enabled, PinIds.Exec);
        graph.Wire(enabled, PinIds.Then, outer, PinIds.Exec);
        graph.Wire(enabled, PinIds.Return, outer, PinIds.Condition);
        graph.Wire(outer, PinIds.Body, inner, PinIds.Exec);
        graph.Wire(enabled, PinIds.Return, inner, PinIds.Condition);
        graph.Wire(inner, PinIds.Body, branch, PinIds.Exec);
        graph.Wire(enabled, PinIds.Return, branch, PinIds.Condition);
        graph.Wire(branch, PinIds.Then, ret, PinIds.Exec);
        graph.Wire(branch, PinIds.Else, work, PinIds.Exec);
        graph.Wire(work, PinIds.Then, inner, PinIds.Exec);
        graph.Wire(inner, PinIds.Completed, outer, PinIds.Exec);

        var source = Source(graph.Document);

        source.Split("While (").Length.Should().Be(3, "both loops, each emitted once");
        source.Split("EndWhile").Length.Should().Be(3);
    }
}
