using System;
using System.Linq;
using FluentAssertions;
using FO4RecordEditor.Services.Graph;

namespace FO4RecordEditor.Core.Tests;

/// <summary>
/// Leaving a loop early, which Papyrus has no keyword for.
/// </summary>
/// <remarks>
/// Break becomes a bool that makes the loop condition false. Continue emits nothing at all: falling
/// off the end of the body is exactly where it goes, and because both nodes are terminal the branch
/// holding one has no post-dominator, so the rest of the body is already lowered into the sibling
/// arm. That is measured in <see cref="GraphFixtures"/> 27 and 28 against hand-written Papyrus,
/// not argued.
/// </remarks>
public class GraphBreakContinueTests
{
    private static string Source(GraphDocument document)
    {
        var result = GraphTestEnvironment.Compile(document);
        result.Success.Should().BeTrue(GraphTestEnvironment.Describe(result.Diagnostics));
        return result.Source!;
    }

    /// <summary>A loop whose body runs one node, then branches to <paramref name="exitKind"/>.</summary>
    private static GraphBuilder LoopWithExit(NodePalette palette, string exitKind, out string exitNode)
    {
        var graph = new GraphBuilder("Fixture", "ObjectReference");

        var entry = graph.Node(palette, "event:ObjectReference.OnLoad");
        var enabled = graph.Node(palette, "call:ObjectReference.IsEnabled");
        var loop = graph.Node(palette, BuiltinNodeDefinitions.While);
        var branch = graph.Node(palette, BuiltinNodeDefinitions.Branch);
        exitNode = graph.Node(palette, exitKind);
        var work = graph.Node(palette, "global:Debug.Notification");

        graph.Value(work, "arg:asNotificationText", "string", "\"work\"");
        graph.Wire(entry, PinIds.Exec, enabled, PinIds.Exec);
        graph.Wire(enabled, PinIds.Then, loop, PinIds.Exec);
        graph.Wire(enabled, PinIds.Return, loop, PinIds.Condition);
        graph.Wire(loop, PinIds.Body, branch, PinIds.Exec);
        graph.Wire(enabled, PinIds.Return, branch, PinIds.Condition);
        graph.Wire(branch, PinIds.Then, exitNode, PinIds.Exec);
        graph.Wire(branch, PinIds.Else, work, PinIds.Exec);
        graph.Wire(work, PinIds.Then, loop, PinIds.Exec);

        return graph;
    }

    [Fact]
    public void Break_makes_the_loop_condition_false()
    {
        var palette = GraphTestEnvironment.Palette();
        var graph = LoopWithExit(palette, BuiltinNodeDefinitions.Break, out _);

        var source = Source(graph.Document);

        source.Should().Contain("broke = false");
        source.Should().Contain("While (enabled && !broke)");
        source.Should().Contain("broke = true");
    }

    [Fact]
    public void Continue_emits_nothing_of_its_own()
    {
        var palette = GraphTestEnvironment.Palette();
        var graph = LoopWithExit(palette, BuiltinNodeDefinitions.Continue, out _);

        var source = Source(graph.Document);

        source.Should().NotContain("broke");
        source.Should().Contain("While (enabled)", "the condition is untouched");
        source.Should().Contain("Debug.Notification(\"work\")");
    }

    [Fact]
    public void A_loop_nobody_breaks_out_of_gets_no_sentinel()
    {
        // The sentinel is allocated on the first Break, so the common loop is unchanged.
        var palette = GraphTestEnvironment.Palette();
        var graph = LoopWithExit(palette, BuiltinNodeDefinitions.Continue, out _);

        Source(graph.Document).Should().NotContain("broke");
    }

    [Fact]
    public void Two_breaks_in_one_loop_share_one_sentinel()
    {
        var palette = GraphTestEnvironment.Palette();
        var graph = new GraphBuilder("Fixture", "ObjectReference");

        var entry = graph.Node(palette, "event:ObjectReference.OnLoad");
        var enabled = graph.Node(palette, "call:ObjectReference.IsEnabled");
        var loop = graph.Node(palette, BuiltinNodeDefinitions.While);
        var outer = graph.Node(palette, BuiltinNodeDefinitions.Branch);
        var firstBreak = graph.Node(palette, BuiltinNodeDefinitions.Break);
        var inner = graph.Node(palette, BuiltinNodeDefinitions.Branch);
        var secondBreak = graph.Node(palette, BuiltinNodeDefinitions.Break);
        var work = graph.Node(palette, "global:Debug.Notification");

        graph.Value(work, "arg:asNotificationText", "string", "\"work\"");
        graph.Wire(entry, PinIds.Exec, enabled, PinIds.Exec);
        graph.Wire(enabled, PinIds.Then, loop, PinIds.Exec);
        graph.Wire(enabled, PinIds.Return, loop, PinIds.Condition);
        graph.Wire(loop, PinIds.Body, outer, PinIds.Exec);
        graph.Wire(enabled, PinIds.Return, outer, PinIds.Condition);
        graph.Wire(outer, PinIds.Then, firstBreak, PinIds.Exec);
        graph.Wire(outer, PinIds.Else, inner, PinIds.Exec);
        graph.Wire(enabled, PinIds.Return, inner, PinIds.Condition);
        graph.Wire(inner, PinIds.Then, secondBreak, PinIds.Exec);
        graph.Wire(inner, PinIds.Else, work, PinIds.Exec);
        graph.Wire(work, PinIds.Then, loop, PinIds.Exec);

        var source = Source(graph.Document);

        source.Split("bool broke").Length.Should().Be(2, "one sentinel, not one per Break");
        source.Split("broke = true").Length.Should().Be(3, "both breaks set it");
    }

    [Fact]
    public void A_break_in_the_inner_loop_leaves_only_the_inner_loop()
    {
        // The reset sits immediately before the inner While, inside the outer body, so each outer
        // pass starts with the inner loop able to run again.
        var palette = GraphTestEnvironment.Palette();
        var graph = new GraphBuilder("Fixture", "ObjectReference");

        var entry = graph.Node(palette, "event:ObjectReference.OnLoad");
        var enabled = graph.Node(palette, "call:ObjectReference.IsEnabled");
        var outer = graph.Node(palette, BuiltinNodeDefinitions.While);
        var inner = graph.Node(palette, BuiltinNodeDefinitions.While);
        var branch = graph.Node(palette, BuiltinNodeDefinitions.Branch);
        var leave = graph.Node(palette, BuiltinNodeDefinitions.Break);
        var work = graph.Node(palette, "global:Debug.Notification");

        graph.Value(work, "arg:asNotificationText", "string", "\"work\"");
        graph.Wire(entry, PinIds.Exec, enabled, PinIds.Exec);
        graph.Wire(enabled, PinIds.Then, outer, PinIds.Exec);
        graph.Wire(enabled, PinIds.Return, outer, PinIds.Condition);
        graph.Wire(outer, PinIds.Body, inner, PinIds.Exec);
        graph.Wire(enabled, PinIds.Return, inner, PinIds.Condition);
        graph.Wire(inner, PinIds.Body, branch, PinIds.Exec);
        graph.Wire(enabled, PinIds.Return, branch, PinIds.Condition);
        graph.Wire(branch, PinIds.Then, leave, PinIds.Exec);
        graph.Wire(branch, PinIds.Else, work, PinIds.Exec);
        graph.Wire(work, PinIds.Then, inner, PinIds.Exec);
        graph.Wire(inner, PinIds.Completed, outer, PinIds.Exec);

        var source = Source(graph.Document);

        // Only the inner loop's condition carries the sentinel.
        var lines = source.Split('\n').Select(l => l.Trim()).ToList();
        var outerLine = lines.FindIndex(l => l == "While (enabled)");
        var innerLine = lines.FindIndex(l => l == "While (enabled && !broke)");
        var resetLine = lines.FindIndex(l => l == "broke = false");

        outerLine.Should().BeGreaterThan(-1, "the outer loop keeps its plain condition");
        innerLine.Should().BeGreaterThan(outerLine, "the guarded loop is the inner one");
        resetLine.Should().BeInRange(outerLine + 1, innerLine - 1,
            "the reset belongs inside the outer body, immediately before the inner loop");
    }

    [Fact]
    public void A_break_in_a_foreach_guards_the_bounds_check()
    {
        var palette = GraphTestEnvironment.Palette();
        var graph = new GraphBuilder("Fixture", "ObjectReference");
        graph.Variable("Items", "Form[]", isProperty: true);

        var entry = graph.Node(palette, "event:ObjectReference.OnLoad");
        var enabled = graph.Node(palette, "call:ObjectReference.IsEnabled");
        var loop = graph.Node(palette, BuiltinNodeDefinitions.ForEach);
        var items = graph.Node(palette, BuiltinNodeDefinitions.VariableGet, ("name", "Items"));
        var branch = graph.Node(palette, BuiltinNodeDefinitions.Branch);
        var leave = graph.Node(palette, BuiltinNodeDefinitions.Break);
        var work = graph.Node(palette, "global:Debug.Notification");

        graph.Value(work, "arg:asNotificationText", "string", "\"work\"");
        graph.Wire(entry, PinIds.Exec, enabled, PinIds.Exec);
        graph.Wire(enabled, PinIds.Then, loop, PinIds.Exec);
        graph.Wire(items, PinIds.Value, loop, PinIds.Array);
        graph.Wire(loop, PinIds.Body, branch, PinIds.Exec);
        graph.Wire(enabled, PinIds.Return, branch, PinIds.Condition);
        graph.Wire(branch, PinIds.Then, leave, PinIds.Exec);
        graph.Wire(branch, PinIds.Else, work, PinIds.Exec);

        var source = Source(graph.Document);

        source.Should().Contain("While (index < items.Length && !broke)");
        source.Should().Contain("index = index + 1", "the step still runs");
    }

    [Fact]
    public void A_break_does_not_count_as_leaving_the_function()
    {
        // Break reaches the loop's Completed target, which is judged there. Treating it as a path
        // out of the function would refuse this perfectly good returning function.
        var palette = GraphTestEnvironment.Palette();
        var graph = new GraphBuilder("Fixture", "ObjectReference");

        var entry = graph.Node(palette, BuiltinNodeDefinitions.FunctionEntry,
            ("name", "Count"), ("returns", "int"));
        var enabled = graph.Node(palette, "call:ObjectReference.IsEnabled");
        var loop = graph.Node(palette, BuiltinNodeDefinitions.While);
        var branch = graph.Node(palette, BuiltinNodeDefinitions.Branch);
        var leave = graph.Node(palette, BuiltinNodeDefinitions.Break);
        var work = graph.Node(palette, "global:Debug.Notification");
        var ret = graph.Node(palette, BuiltinNodeDefinitions.Return);
        var zero = graph.Node(palette, "literal.int");

        graph.Value(work, "arg:asNotificationText", "string", "\"work\"");
        graph.Value(zero, PinIds.Value, "int", "0");
        graph.Wire(entry, PinIds.Exec, enabled, PinIds.Exec);
        graph.Wire(enabled, PinIds.Then, loop, PinIds.Exec);
        graph.Wire(enabled, PinIds.Return, loop, PinIds.Condition);
        graph.Wire(loop, PinIds.Body, branch, PinIds.Exec);
        graph.Wire(enabled, PinIds.Return, branch, PinIds.Condition);
        graph.Wire(branch, PinIds.Then, leave, PinIds.Exec);
        graph.Wire(branch, PinIds.Else, work, PinIds.Exec);
        graph.Wire(work, PinIds.Then, loop, PinIds.Exec);
        graph.Wire(loop, PinIds.Completed, ret, PinIds.Exec);
        graph.Wire(zero, PinIds.Value, ret, PinIds.Value);

        var result = GraphTestEnvironment.Compile(graph.Document);

        result.Diagnostics.Should().NotContain(d => d.Code == GraphDiagnosticCodes.NotAllPathsReturn,
            GraphTestEnvironment.Describe(result.Diagnostics));
        result.Success.Should().BeTrue(GraphTestEnvironment.Describe(result.Diagnostics));
    }

    [Fact]
    public void A_loop_whose_only_exit_is_a_break_still_has_to_return()
    {
        // The neutrality must not become leniency: with Completed unwired there is no path to a
        // Return at all, and that still has to be refused.
        var palette = GraphTestEnvironment.Palette();
        var graph = new GraphBuilder("Fixture", "ObjectReference");

        var entry = graph.Node(palette, BuiltinNodeDefinitions.FunctionEntry,
            ("name", "Count"), ("returns", "int"));
        var enabled = graph.Node(palette, "call:ObjectReference.IsEnabled");
        var loop = graph.Node(palette, BuiltinNodeDefinitions.While);
        var leave = graph.Node(palette, BuiltinNodeDefinitions.Break);

        graph.Wire(entry, PinIds.Exec, enabled, PinIds.Exec);
        graph.Wire(enabled, PinIds.Then, loop, PinIds.Exec);
        graph.Wire(enabled, PinIds.Return, loop, PinIds.Condition);
        graph.Wire(loop, PinIds.Body, leave, PinIds.Exec);

        var result = GraphTestEnvironment.Compile(graph.Document);

        result.Success.Should().BeFalse();
        result.Errors.Should().Contain(d => d.Code == GraphDiagnosticCodes.NotAllPathsReturn,
            GraphTestEnvironment.Describe(result.Diagnostics));
    }

    [Theory]
    [InlineData(BuiltinNodeDefinitions.Break)]
    [InlineData(BuiltinNodeDefinitions.Continue)]
    public void A_loop_exit_with_no_loop_is_refused_naming_the_node(string kind)
    {
        var palette = GraphTestEnvironment.Palette();
        var graph = new GraphBuilder("Fixture", "ObjectReference");

        var entry = graph.Node(palette, "event:ObjectReference.OnLoad");
        var exit = graph.Node(palette, kind);
        graph.Wire(entry, PinIds.Exec, exit, PinIds.Exec);

        var result = GraphTestEnvironment.Compile(graph.Document);

        result.Success.Should().BeFalse();
        var diagnostic = result.Errors.FirstOrDefault(d => d.Code == GraphDiagnosticCodes.LoopExitOutsideLoop);
        diagnostic.Should().NotBeNull(GraphTestEnvironment.Describe(result.Diagnostics));
        diagnostic!.NodeId.Should().Be(exit);
    }

    [Fact]
    public void An_empty_then_arm_is_inverted_rather_than_left_empty()
    {
        // "If (x) Else ... EndIf" is valid and nobody writes it. Continue is what produces it.
        var palette = GraphTestEnvironment.Palette();
        var graph = LoopWithExit(palette, BuiltinNodeDefinitions.Continue, out _);

        var source = Source(graph.Document);

        source.Should().Contain("If (!enabled)");
        source.Should().NotContain("Else", "there is nothing for an else to hold");
    }
}
