using System.Linq;
using FluentAssertions;
using FO4RecordEditor.Services.Graph;
using Newtonsoft.Json.Linq;

namespace FO4RecordEditor.Core.Tests;

/// <summary>
/// The saved shape of a graph: what survives a round trip, and what a damaged document does.
/// </summary>
/// <remarks>
/// This is the contract the canvas mirrors in TypeScript, so the casing assertions are not
/// cosmetic. A silent casing change here would leave the two halves reading different fields with
/// no error on either side.
/// </remarks>
public class GraphDocumentTests
{
    private static GraphDocument Sample()
    {
        var document = new GraphDocument
        {
            Id = "doc1",
            Header = new GraphScriptHeader
            {
                ScriptName = "MyMod:DoorScript",
                Extends = "ObjectReference",
                Flags = { "Conditional" },
                Imports = { "Debug" },
                DocComment = "A door.",
                AutoState = "Waiting",
            },
            Variables =
            {
                new GraphVariable { Name = "Target", Type = "ObjectReference", IsProperty = true },
            },
            Nodes =
            {
                new GraphNode
                {
                    Id = "n1", Definition = "event:ObjectReference.OnActivate",
                    Kind = GraphNodeKind.EventEntry, X = 40, Y = 120,
                },
                new GraphNode
                {
                    Id = "n2", Definition = "global:Debug.Notification",
                    Kind = GraphNodeKind.Call, X = 380, Y = 120,
                    PinValues =
                    {
                        ["arg:asNotificationText"] = new GraphPinValue { Type = "string", Value = "\"opened\"" },
                    },
                },
            },
            Wires =
            {
                new GraphWire
                {
                    Id = "w1",
                    From = new PinRef("n1", PinIds.Exec),
                    To = new PinRef("n2", PinIds.Exec),
                },
            },
        };
        return document;
    }

    [Fact]
    public void A_document_survives_a_round_trip_unchanged()
    {
        var json = GraphDocumentJson.Serialize(Sample());
        GraphDocumentJson.TryDeserialize(json, out var back, out var error).Should().BeTrue(error?.Message);

        back!.Id.Should().Be("doc1");
        back.Schema.Should().Be(GraphDocument.CurrentSchema);
        back.Header.ScriptName.Should().Be("MyMod:DoorScript");
        back.Header.Extends.Should().Be("ObjectReference");
        back.Header.Flags.Should().ContainSingle("Conditional");

        // Every header field, not just the ones the canvas edits. A field that does not survive the
        // trip is one the canvas silently drops when an author opens a graph and saves it.
        back.Header.Imports.Should().ContainSingle("Debug");
        back.Header.DocComment.Should().Be("A door.");
        back.Header.AutoState.Should().Be("Waiting");
        back.Variables.Should().ContainSingle().Which.IsProperty.Should().BeTrue();

        back.Nodes.Should().HaveCount(2);
        back.Node("n2")!.PinValues["arg:asNotificationText"].Value.Should().Be("\"opened\"");

        var wire = back.Wires.Should().ContainSingle().Subject;
        wire.From.Should().Be(new PinRef("n1", PinIds.Exec));
        wire.To.Should().Be(new PinRef("n2", PinIds.Exec));
    }

    [Fact]
    public void The_json_is_camel_cased_to_match_the_rest_of_the_bridge()
    {
        var json = JObject.Parse(GraphDocumentJson.Serialize(Sample()));

        json.Property("schema").Should().NotBeNull();
        json.Property("header").Should().NotBeNull();
        json["header"]!["scriptName"].Should().NotBeNull();
        json["nodes"]![0]!["def"].Should().NotBeNull("the definition id is stored under a short name");
        json["wires"]![0]!["from"]!["node"].Should().NotBeNull();
    }

    [Fact]
    public void Pins_are_not_stored_because_they_are_re_derived_from_the_palette()
    {
        // The load bearing schema decision: a renamed parameter has to surface as a dangling wire
        // naming the node, not as a call silently compiled with the wrong arguments.
        var json = JObject.Parse(GraphDocumentJson.Serialize(Sample()));

        json["nodes"]![0]!["pins"].Should().BeNull();
    }

    [Fact]
    public void A_node_kind_this_build_does_not_know_loads_as_unknown()
    {
        const string json = """
            { "schema": 1, "nodes": [ { "id": "n1", "def": "future:Thing", "kind": "timeTravel" } ] }
            """;

        GraphDocumentJson.TryDeserialize(json, out var document, out var error).Should().BeTrue(error?.Message);
        document!.Nodes.Should().ContainSingle().Which.Kind.Should().Be(GraphNodeKind.Unknown);
    }

    [Fact]
    public void An_unknown_field_from_a_newer_canvas_is_ignored_rather_than_fatal()
    {
        const string json = """
            { "schema": 1, "somethingNew": 42, "nodes": [ { "id": "n1", "def": "branch", "kind": "branch", "wobble": true } ] }
            """;

        GraphDocumentJson.TryDeserialize(json, out var document, out _).Should().BeTrue();
        document!.Nodes.Should().ContainSingle();
    }

    [Fact]
    public void A_document_from_a_future_schema_is_refused_with_a_named_code()
    {
        var json = GraphDocumentJson.Serialize(Sample() ).Replace("\"schema\": 1", "\"schema\": 99");

        GraphDocumentJson.TryDeserialize(json, out var document, out var error).Should().BeFalse();
        document.Should().BeNull();
        error!.Code.Should().Be(GraphDiagnosticCodes.UnsupportedSchema);
        error.Message.Should().Contain("99");
    }

    [Fact]
    public void Malformed_json_is_a_diagnostic_rather_than_an_exception()
    {
        // The canvas calls this on every open and every autosave restore, so a throw would show up
        // as a blank panel with nothing to explain it.
        GraphDocumentJson.TryDeserialize("{ this is not json", out var document, out var error)
            .Should().BeFalse();
        document.Should().BeNull();
        error!.Code.Should().Be(GraphDiagnosticCodes.MalformedDocument);
    }

    [Fact]
    public void An_empty_document_is_a_diagnostic()
    {
        GraphDocumentJson.TryDeserialize("", out _, out var error).Should().BeFalse();
        error!.Code.Should().Be(GraphDiagnosticCodes.MalformedDocument);
    }

    [Fact]
    public void A_wire_naming_a_node_that_does_not_exist_still_loads()
    {
        // Refusing it belongs to the validator, which can name the node. The loader refusing would
        // leave the author with a file they cannot open to fix.
        const string json = """
            { "schema": 1, "nodes": [], "wires": [ { "id": "w1", "from": { "node": "gone", "pin": "exec" }, "to": { "node": "alsoGone", "pin": "exec" } } ] }
            """;

        GraphDocumentJson.TryDeserialize(json, out var document, out _).Should().BeTrue();
        document!.Wires.Should().ContainSingle();
        document.Node("gone").Should().BeNull();
    }

    [Fact]
    public void Missing_optional_sections_default_to_empty_rather_than_null()
    {
        GraphDocumentJson.TryDeserialize("""{ "schema": 1 }""", out var document, out _).Should().BeTrue();

        document!.Nodes.Should().NotBeNull().And.BeEmpty();
        document.Wires.Should().NotBeNull().And.BeEmpty();
        document.Variables.Should().NotBeNull().And.BeEmpty();
        document.Header.Should().NotBeNull();
    }

    [Fact]
    public void Wire_lookups_find_both_ends_and_ignore_pin_case()
    {
        var document = Sample();

        document.Into(new PinRef("n2", "EXEC")).Should().ContainSingle();
        document.OutOf(new PinRef("n1", "exec")).Should().ContainSingle();
        document.Touching("n1").Should().ContainSingle();
        document.Touching("nobody").Should().BeEmpty();
    }

    [Fact]
    public void Node_lookup_is_cached_and_can_be_invalidated()
    {
        var document = Sample();
        document.Node("n1").Should().NotBeNull();

        document.Nodes.Add(new GraphNode { Id = "n3", Definition = "branch", Kind = GraphNodeKind.Branch });
        document.Node("n3").Should().BeNull("the cache was built before the node was added");

        document.Invalidate();
        document.Node("n3").Should().NotBeNull();
    }

    [Fact]
    public void Node_configuration_is_a_free_form_bag()
    {
        var node = new GraphNode { Id = "n1", Definition = BuiltinNodeDefinitions.VariableGet };
        node.Config["name"] = "Counter";

        var json = GraphDocumentJson.Serialize(new GraphDocument { Nodes = { node } });
        GraphDocumentJson.TryDeserialize(json, out var back, out _).Should().BeTrue();

        back!.Nodes[0].ConfigString("name").Should().Be("Counter");
        back.Nodes[0].ConfigString("absent").Should().BeNull();
    }
}
