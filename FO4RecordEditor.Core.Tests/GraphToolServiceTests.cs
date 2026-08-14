using System.IO;
using FluentAssertions;
using FO4RecordEditor.Services.Graph;

namespace FO4RecordEditor.Core.Tests;

public class GraphToolServiceTests
{
    private static string Roots => TestRoots.BaseStubs;

    private static T WithGraph<T>(GraphDocument document, System.Func<string, T> work)
    {
        var directory = Directory.CreateTempSubdirectory("fo4re-tool-");
        try
        {
            var path = Path.Combine(directory.FullName, "Fixture.fograph");
            File.WriteAllText(path, GraphDocumentJson.Serialize(document));
            return work(path);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static GraphDocument GoodGraph()
    {
        var palette = GraphTestEnvironment.Palette();
        var graph = new GraphBuilder("Fixture", "ObjectReference");
        var entry = graph.Node(palette, "event:ObjectReference.OnActivate");
        var notify = graph.Node(palette, "global:Debug.Notification");
        graph.Value(notify, "arg:asNotificationText", "string", "\"opened\"");
        graph.Wire(entry, PinIds.Exec, notify, PinIds.Exec);
        return graph.Document;
    }

    private static GraphDocument BrokenGraph()
    {
        var palette = GraphTestEnvironment.Palette();
        var graph = new GraphBuilder("Fixture", "ObjectReference");
        var entry = graph.Node(palette, "event:ObjectReference.OnLoad");
        var wait = graph.Node(palette, "global:Utility.Wait");
        graph.Wire(entry, PinIds.Exec, wait, PinIds.Exec);
        return graph.Document;
    }

    [Fact]
    public void Validating_a_good_graph_reports_ok_with_its_size()
    {
        var text = WithGraph(GoodGraph(), path => GraphToolService.Validate(path, Roots));

        text.Should().StartWith("RESULT: OK");
        text.Should().Contain("2 nodes").And.Contain("1 wires");
    }

    [Fact]
    public void Validating_a_broken_graph_names_the_node_and_the_pin()
    {
        var text = WithGraph(BrokenGraph(), path => GraphToolService.Validate(path, Roots));

        text.Should().StartWith("RESULT: 1 error");
        text.Should().Contain(GraphDiagnosticCodes.MissingRequiredInput);
        text.Should().Contain("node n").And.Contain("pin arg:afSeconds");
        text.Should().Contain("global:Utility.Wait", "naming the node type makes the report actionable");
    }

    [Fact]
    public void A_missing_graph_file_is_a_message_rather_than_an_exception()
    {
        GraphToolService.Validate("/no/such/graph.fograph", Roots)
            .Should().StartWith("RESULT: FAILED").And.Contain("No graph document");
    }

    [Fact]
    public void A_malformed_graph_file_is_a_message_rather_than_an_exception()
    {
        var directory = Directory.CreateTempSubdirectory("fo4re-tool-bad-");
        try
        {
            var path = Path.Combine(directory.FullName, "Bad.fograph");
            File.WriteAllText(path, "{ not json");
            GraphToolService.Validate(path, Roots).Should().StartWith("RESULT: FAILED");
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void Compiling_source_only_returns_the_generated_script()
    {
        var text = WithGraph(GoodGraph(), path =>
            GraphToolService.Compile(path, output: null, imports: Roots, sourceOnly: true));

        text.Should().StartWith("RESULT: OK");
        text.Should().Contain("Scriptname Fixture extends ObjectReference");
        text.Should().Contain("Debug.Notification(\"opened\")");
    }

    [Fact]
    public void Compiling_with_an_output_folder_writes_both_files()
    {
        var output = Directory.CreateTempSubdirectory("fo4re-tool-out-");
        try
        {
            var text = WithGraph(GoodGraph(), path =>
                GraphToolService.Compile(path, output.FullName, Roots));

            text.Should().StartWith("RESULT: OK");
            File.Exists(Path.Combine(output.FullName, "Fixture.psc")).Should().BeTrue();
            File.Exists(Path.Combine(output.FullName, "Fixture.pex")).Should().BeTrue();
            text.Should().Contain("Wrote ");
        }
        finally
        {
            output.Delete(recursive: true);
        }
    }

    [Fact]
    public void A_failed_compile_shows_the_generated_source_it_could_not_use()
    {

        var palette = GraphTestEnvironment.Palette();
        var graph = new GraphBuilder("Fixture", "ObjectReference");
        var entry = graph.Node(palette, "event:ObjectReference.OnLoad");
        var level = graph.Node(palette, "call:Actor.GetLevel");
        graph.Wire(entry, PinIds.Exec, level, PinIds.Exec);

        var text = WithGraph(graph.Document, path => GraphToolService.Compile(path, null, Roots));

        text.Should().StartWith("RESULT: FAILED");
        text.Should().Contain(GraphDiagnosticCodes.UnconnectedSelf);
        text.Should().Contain("node n");
    }

    [Fact]
    public void Searching_the_palette_returns_definition_ids_and_a_true_total()
    {
        var text = GraphToolService.SearchPalette("AddItem", Roots, limit: 5);

        text.Should().StartWith("RESULT:");
        text.Should().Contain("call:ObjectReference.AddItem");
    }

    [Fact]
    public void A_capped_search_says_how_much_it_is_hiding()
    {
        var text = GraphToolService.SearchPalette("Get", Roots, limit: 2);

        text.Should().Contain("more not shown", "a capped list is only honest if it says so");
    }

    [Fact]
    public void Describing_a_node_lists_its_pins_with_types_and_defaults()
    {
        var text = GraphToolService.DescribeNode("call:ObjectReference.AddItem", Roots);

        text.Should().Contain("call:ObjectReference.AddItem");
        text.Should().Contain("arg:akItemToAdd").And.Contain("Form");
        text.Should().Contain("arg:aiCount").And.Contain("optional").And.Contain("default 1");
        text.Should().Contain("Sequenced", "a call has control flow pins");
    }

    [Fact]
    public void Describing_a_pure_node_says_it_evaluates_inline()
    {
        GraphToolService.DescribeNode("op.add", Roots)
            .Should().Contain("Pure");
    }

    [Fact]
    public void An_unknown_node_type_is_a_message_rather_than_an_exception()
    {
        GraphToolService.DescribeNode("call:NoSuchScript.NoSuchThing", Roots)
            .Should().StartWith("RESULT: FAILED").And.Contain("No node type");
    }
}
