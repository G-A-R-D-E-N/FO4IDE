using System.Linq;
using FluentAssertions;
using FO4RecordEditor.Services.Graph;
using FO4RecordEditor.Services.Papyrus;
using Xunit.Abstractions;

namespace FO4RecordEditor.Core.Tests;

public class GraphCompilerTests
{
    private readonly ITestOutputHelper _output;

    public GraphCompilerTests(ITestOutputHelper output) => _output = output;

    private GraphCompileResult CompileOk(GraphDocument document)
    {
        var result = GraphTestEnvironment.Compile(document);
        if (result.Source != null) _output.WriteLine(result.Source);
        result.Errors.Should().BeEmpty(GraphTestEnvironment.Describe(result.Diagnostics));
        result.Success.Should().BeTrue("the graph should reach a compiled object");
        return result;
    }

    [Fact]
    public void An_event_calling_a_global_compiles_to_pex()
    {
        var palette = GraphTestEnvironment.Palette();
        var graph = new GraphBuilder("Fixture", "ObjectReference");

        var entry = graph.Node(palette, "event:ObjectReference.OnActivate");
        var notify = graph.Node(palette, "global:Debug.Notification");
        graph.Value(notify, "arg:asNotificationText", "string", "\"opened\"");
        graph.Wire(entry, PinIds.Exec, notify, PinIds.Exec);

        var result = CompileOk(graph.Document);

        result.Source.Should().Contain("Scriptname Fixture extends ObjectReference");
        result.Source.Should().Contain("Event OnActivate(ObjectReference akActionRef)");
        result.Source.Should().Contain("Debug.Notification(\"opened\")");
        result.Source.Should().Contain("EndEvent");
        result.Pex!.Objects[0].ParentClassName.Should().BeEquivalentTo("ObjectReference");
    }

    [Fact]
    public void An_event_parameter_flows_into_a_call()
    {
        var palette = GraphTestEnvironment.Palette();
        var graph = new GraphBuilder("Fixture", "ObjectReference");

        var entry = graph.Node(palette, "event:ObjectReference.OnActivate");
        var distance = graph.Node(palette, "call:ObjectReference.GetDistance");
        graph.Wire(entry, PinIds.Exec, distance, PinIds.Exec);
        graph.Wire(entry, "param:akActionRef", distance, "arg:akOther");

        var result = CompileOk(graph.Document);

        result.Source.Should().Contain("GetDistance(akActionRef)");
    }

    [Fact]
    public void A_branch_emits_an_if_and_rejoins_once_after_the_merge()
    {

        var palette = GraphTestEnvironment.Palette();
        var graph = new GraphBuilder("Fixture", "ObjectReference");

        var entry = graph.Node(palette, "event:ObjectReference.OnLoad");
        var branch = graph.Node(palette, BuiltinNodeDefinitions.Branch);
        var isEnabled = graph.Node(palette, "call:ObjectReference.IsEnabled");
        var yes = graph.Node(palette, "global:Debug.Notification");
        var no = graph.Node(palette, "global:Debug.Notification");
        var after = graph.Node(palette, "global:Debug.Notification");

        graph.Value(yes, "arg:asNotificationText", "string", "\"on\"");
        graph.Value(no, "arg:asNotificationText", "string", "\"off\"");
        graph.Value(after, "arg:asNotificationText", "string", "\"done\"");

        graph.Wire(entry, PinIds.Exec, isEnabled, PinIds.Exec);
        graph.Wire(isEnabled, PinIds.Then, branch, PinIds.Exec);
        graph.Wire(isEnabled, PinIds.Return, branch, PinIds.Condition);
        graph.Wire(branch, PinIds.Then, yes, PinIds.Exec);
        graph.Wire(branch, PinIds.Else, no, PinIds.Exec);
        graph.Wire(yes, PinIds.Then, after, PinIds.Exec);
        graph.Wire(no, PinIds.Then, after, PinIds.Exec);

        var result = CompileOk(graph.Document);

        result.Source.Should().Contain("If (").And.Contain("Else").And.Contain("EndIf");
        Occurrences(result.Source!, "\"done\"").Should().Be(1, "the merge tail is emitted once");
    }

    [Fact]
    public void A_branch_with_no_else_emits_no_else_clause()
    {
        var palette = GraphTestEnvironment.Palette();
        var graph = new GraphBuilder("Fixture", "ObjectReference");

        var entry = graph.Node(palette, "event:ObjectReference.OnLoad");
        var isEnabled = graph.Node(palette, "call:ObjectReference.IsEnabled");
        var branch = graph.Node(palette, BuiltinNodeDefinitions.Branch);
        var yes = graph.Node(palette, "global:Debug.Notification");
        graph.Value(yes, "arg:asNotificationText", "string", "\"on\"");

        graph.Wire(entry, PinIds.Exec, isEnabled, PinIds.Exec);
        graph.Wire(isEnabled, PinIds.Then, branch, PinIds.Exec);
        graph.Wire(isEnabled, PinIds.Return, branch, PinIds.Condition);
        graph.Wire(branch, PinIds.Then, yes, PinIds.Exec);

        var result = CompileOk(graph.Document);

        result.Source.Should().Contain("If (");
        result.Source.Should().NotContain("Else");
    }

    [Fact]
    public void A_while_loop_emits_a_while_and_continues_after_it()
    {
        var palette = GraphTestEnvironment.Palette();
        var graph = new GraphBuilder("Fixture", "ObjectReference");

        var entry = graph.Node(palette, "event:ObjectReference.OnLoad");
        var loop = graph.Node(palette, BuiltinNodeDefinitions.While);
        var isEnabled = graph.Node(palette, "call:ObjectReference.IsEnabled");
        var body = graph.Node(palette, "global:Debug.Notification");
        var after = graph.Node(palette, "global:Debug.Notification");

        graph.Value(body, "arg:asNotificationText", "string", "\"tick\"");
        graph.Value(after, "arg:asNotificationText", "string", "\"done\"");

        graph.Wire(entry, PinIds.Exec, isEnabled, PinIds.Exec);
        graph.Wire(isEnabled, PinIds.Then, loop, PinIds.Exec);
        graph.Wire(isEnabled, PinIds.Return, loop, PinIds.Condition);
        graph.Wire(loop, PinIds.Body, body, PinIds.Exec);
        graph.Wire(body, PinIds.Then, loop, PinIds.Exec);
        graph.Wire(loop, PinIds.Completed, after, PinIds.Exec);

        var result = CompileOk(graph.Document);

        result.Source.Should().Contain("While (").And.Contain("EndWhile");
        result.Source.Should().Contain("\"tick\"").And.Contain("\"done\"");
    }

    [Fact]
    public void A_trailing_optional_that_is_not_supplied_disappears()
    {
        var palette = GraphTestEnvironment.Palette();
        var graph = new GraphBuilder("Fixture", "ObjectReference");

        var entry = graph.Node(palette, "event:ObjectReference.OnLoad");
        var add = graph.Node(palette, "call:ObjectReference.AddItem");
        var none = graph.Node(palette, BuiltinNodeDefinitions.NoneValue);

        graph.Wire(entry, PinIds.Exec, add, PinIds.Exec);
        graph.Wire(none, PinIds.Value, add, "arg:akItemToAdd");

        var result = CompileOk(graph.Document);

        result.Source.Should().Contain("AddItem(None)");
    }

    [Fact]
    public void A_skipped_middle_optional_forces_named_arguments()
    {

        var palette = GraphTestEnvironment.Palette();
        var graph = new GraphBuilder("Fixture", "ObjectReference");

        var entry = graph.Node(palette, "event:ObjectReference.OnLoad");
        var add = graph.Node(palette, "call:ObjectReference.AddItem");
        var none = graph.Node(palette, BuiltinNodeDefinitions.NoneValue);

        graph.Wire(entry, PinIds.Exec, add, PinIds.Exec);
        graph.Wire(none, PinIds.Value, add, "arg:akItemToAdd");
        graph.Value(add, "arg:abSilent", "bool", "true");

        var result = CompileOk(graph.Document);

        result.Source.Should().Contain("abSilent = true");
        result.Source.Should().NotContain("AddItem(None, true)");
    }

    [Fact]
    public void An_impure_call_used_twice_binds_one_local_rather_than_calling_twice()
    {

        var palette = GraphTestEnvironment.Palette();
        var graph = new GraphBuilder("Fixture", "ObjectReference");

        var entry = graph.Node(palette, "event:ObjectReference.OnLoad");
        var player = graph.Node(palette, "global:Game.GetPlayer");
        var move = graph.Node(palette, "call:ObjectReference.MoveTo");
        var distance = graph.Node(palette, "call:ObjectReference.GetDistance");

        graph.Wire(entry, PinIds.Exec, player, PinIds.Exec);
        graph.Wire(player, PinIds.Then, move, PinIds.Exec);
        graph.Wire(move, PinIds.Then, distance, PinIds.Exec);
        graph.Wire(player, PinIds.Return, move, "arg:akTarget");
        graph.Wire(player, PinIds.Return, distance, "arg:akOther");

        var result = CompileOk(graph.Document);

        Occurrences(result.Source!, "Game.GetPlayer()").Should().Be(1);
        result.Source.Should().Contain("Actor player");
    }

    [Fact]
    public void A_call_whose_result_nothing_uses_emits_no_local()
    {
        var palette = GraphTestEnvironment.Palette();
        var graph = new GraphBuilder("Fixture", "ObjectReference");

        var entry = graph.Node(palette, "event:ObjectReference.OnLoad");
        var enabled = graph.Node(palette, "call:ObjectReference.IsEnabled");
        graph.Wire(entry, PinIds.Exec, enabled, PinIds.Exec);

        var result = CompileOk(graph.Document);

        result.Source.Should().Contain("IsEnabled()");
        result.Source.Should().NotContain("bool enabled");
    }

    [Fact]
    public void An_operator_tree_inlines_at_its_use_site()
    {
        var palette = GraphTestEnvironment.Palette();
        var graph = new GraphBuilder("Fixture", "ObjectReference");

        var entry = graph.Node(palette, "event:ObjectReference.OnLoad");
        var wait = graph.Node(palette, "global:Utility.Wait");
        var add = graph.Node(palette, "op.add");
        var left = graph.Node(palette, "literal.float");
        var right = graph.Node(palette, "literal.float");

        graph.Value(left, PinIds.Value, "float", "1.5");
        graph.Value(right, PinIds.Value, "float", "2.5");
        graph.Wire(entry, PinIds.Exec, wait, PinIds.Exec);
        graph.Wire(left, PinIds.Value, add, PinIds.Left);
        graph.Wire(right, PinIds.Value, add, PinIds.Right);
        graph.Wire(add, PinIds.Return, wait, "arg:afSeconds");

        var result = CompileOk(graph.Document);

        result.Source.Should().Contain("Utility.Wait(1.5 + 2.5)");
    }

    [Fact]
    public void A_variable_set_and_get_round_trips_through_a_declaration()
    {
        var palette = GraphTestEnvironment.Palette();
        var graph = new GraphBuilder("Fixture", "ObjectReference");
        graph.Variable("Counter", "int");

        var entry = graph.Node(palette, "event:ObjectReference.OnLoad");
        var set = graph.Node(palette, BuiltinNodeDefinitions.VariableSet, ("name", "Counter"));
        var literal = graph.Node(palette, "literal.int");
        var notify = graph.Node(palette, "global:Debug.Trace");
        var get = graph.Node(palette, BuiltinNodeDefinitions.VariableGet, ("name", "Counter"));

        graph.Value(literal, PinIds.Value, "int", "7");
        graph.Wire(entry, PinIds.Exec, set, PinIds.Exec);
        graph.Wire(literal, PinIds.Value, set, PinIds.Value);
        graph.Wire(set, PinIds.Then, notify, PinIds.Exec);
        graph.Wire(get, PinIds.Value, notify, "arg:asTextToPrint");

        var result = CompileOk(graph.Document);

        result.Source.Should().Contain("int Counter");
        result.Source.Should().Contain("Counter = 7");
        result.Source.Should().Contain("Debug.Trace(Counter)");
    }

    [Fact]
    public void A_property_is_declared_auto_by_default()
    {
        var palette = GraphTestEnvironment.Palette();
        var graph = new GraphBuilder("Fixture", "ObjectReference");
        graph.Variable("Target", "ObjectReference", isProperty: true);

        var entry = graph.Node(palette, "event:ObjectReference.OnLoad");
        _ = entry;

        var result = CompileOk(graph.Document);

        result.Source.Should().Contain("ObjectReference Property Target Auto");
    }

    [Fact]
    public void A_call_on_this_scripts_own_chain_needs_no_receiver()
    {
        var palette = GraphTestEnvironment.Palette();
        var graph = new GraphBuilder("Fixture", "ObjectReference");

        var entry = graph.Node(palette, "event:ObjectReference.OnLoad");
        var disable = graph.Node(palette, "call:ObjectReference.Disable");
        graph.Wire(entry, PinIds.Exec, disable, PinIds.Exec);

        var result = CompileOk(graph.Document);

        result.Source.Should().Contain("Disable()");
        result.Source.Should().NotContain(".Disable(");
    }

    [Fact]
    public void A_call_on_another_type_takes_its_receiver_from_the_target_pin()
    {
        var palette = GraphTestEnvironment.Palette();
        var graph = new GraphBuilder("Fixture", "ObjectReference");

        var entry = graph.Node(palette, "event:ObjectReference.OnLoad");
        var player = graph.Node(palette, "global:Game.GetPlayer");
        var level = graph.Node(palette, "call:Actor.GetLevel");
        var trace = graph.Node(palette, "global:Debug.Trace");

        graph.Wire(entry, PinIds.Exec, player, PinIds.Exec);
        graph.Wire(player, PinIds.Then, level, PinIds.Exec);
        graph.Wire(level, PinIds.Then, trace, PinIds.Exec);
        graph.Wire(player, PinIds.Return, level, PinIds.Self);
        graph.Wire(level, PinIds.Return, trace, "arg:asTextToPrint");

        var result = CompileOk(graph.Document);

        result.Source.Should().Contain(".GetLevel()");
    }

    [Fact]
    public void Stopping_after_source_yields_source_and_no_compiled_object()
    {
        var palette = GraphTestEnvironment.Palette();
        var graph = new GraphBuilder("Fixture", "ObjectReference");
        graph.Node(palette, "event:ObjectReference.OnLoad");

        var result = GraphTestEnvironment.Compile(graph.Document, stopAfterSource: true);

        result.Source.Should().NotBeNullOrEmpty();
        result.SourceMap.Should().NotBeNull();
        result.Pex.Should().BeNull();
    }

    [Fact]
    public void The_generated_source_always_reparses_cleanly()
    {

        var palette = GraphTestEnvironment.Palette();
        var graph = new GraphBuilder("Fixture", "ObjectReference");

        var entry = graph.Node(palette, "event:ObjectReference.OnActivate");
        var notify = graph.Node(palette, "global:Debug.Notification");
        graph.Value(notify, "arg:asNotificationText", "string", "\"hello\"");
        graph.Wire(entry, PinIds.Exec, notify, PinIds.Exec);

        var result = CompileOk(graph.Document);

        PapyrusParser.Parse(result.Source!, "Fixture.psc")
            .Diagnostics.Where(d => d.Severity == PapyrusSeverity.Error)
            .Should().BeEmpty();
    }

    [Fact]
    public void The_source_map_attributes_every_statement_to_a_node()
    {
        var palette = GraphTestEnvironment.Palette();
        var graph = new GraphBuilder("Fixture", "ObjectReference");

        var entry = graph.Node(palette, "event:ObjectReference.OnActivate");
        var notify = graph.Node(palette, "global:Debug.Notification");
        graph.Value(notify, "arg:asNotificationText", "string", "\"hello\"");
        graph.Wire(entry, PinIds.Exec, notify, PinIds.Exec);

        var result = CompileOk(graph.Document);
        var offset = result.Source!.IndexOf("Debug.Notification", System.StringComparison.Ordinal);

        var entryMapped = result.SourceMap!.Find(offset);
        entryMapped.Should().NotBeNull("the call statement should map back to the node that made it");
        entryMapped!.Value.NodeId.Should().Be(notify);

        result.SourceMap.FunctionAt(offset)!.Value.NodeId
            .Should().Be(entry, "the enclosing callable maps to its entry node");
    }

    [Fact]
    public void Validation_and_compilation_never_disagree()
    {

        var palette = GraphTestEnvironment.Palette();
        var graph = new GraphBuilder("Fixture", "ObjectReference");

        var entry = graph.Node(palette, "event:ObjectReference.OnLoad");
        var add = graph.Node(palette, "call:ObjectReference.AddItem");
        graph.Wire(entry, PinIds.Exec, add, PinIds.Exec);

        var compiler = GraphTestEnvironment.Compiler();
        var validation = compiler.Validate(graph.Document);
        var compiled = compiler.Compile(graph.Document);

        validation.Errors.Select(d => d.Code)
            .Should().BeSubsetOf(compiled.Errors.Select(d => d.Code));
    }

    private static int Occurrences(string text, string needle)
    {
        int count = 0, index = 0;
        while ((index = text.IndexOf(needle, index, System.StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }
        return count;
    }
}
