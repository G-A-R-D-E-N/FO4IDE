using System.IO;
using System.Linq;
using FluentAssertions;
using FO4RecordEditor.Services.Graph;
using FO4RecordEditor.Services.Papyrus;

namespace FO4RecordEditor.Core.Tests;










public class GraphRemoteEventTests
{
    private static string Source(GraphDocument document)
    {
        var result = GraphTestEnvironment.Compile(document);
        result.Success.Should().BeTrue(GraphTestEnvironment.Describe(result.Diagnostics));
        return result.Source!;
    }

    [Fact]
    public void A_remote_handler_is_written_with_the_raising_type_and_a_sender_parameter()
    {
        var palette = GraphTestEnvironment.Palette();
        var graph = new GraphBuilder("Fixture", "Quest");

        var entry = graph.Node(palette, NodePalette.RemoteEventId("ObjectReference", "OnLoad"));
        var notify = graph.Node(palette, "global:Debug.Notification");
        graph.Value(notify, "arg:asNotificationText", "string", "\"loaded\"");
        graph.Wire(entry, PinIds.Exec, notify, PinIds.Exec);

        Source(graph.Document).Should().Contain("Event ObjectReference.OnLoad(ObjectReference akSender)");
    }

    [Fact]
    public void A_remote_handler_keeps_the_source_events_own_parameters_after_the_sender()
    {
        var palette = GraphTestEnvironment.Palette();
        var graph = new GraphBuilder("Fixture", "Quest");

        var entry = graph.Node(palette, NodePalette.RemoteEventId("ObjectReference", "OnActivate"));
        var notify = graph.Node(palette, "global:Debug.Notification");
        graph.Value(notify, "arg:asNotificationText", "string", "\"used\"");
        graph.Wire(entry, PinIds.Exec, notify, PinIds.Exec);

        Source(graph.Document).Should().Contain(
            "Event ObjectReference.OnActivate(ObjectReference akSender, ObjectReference akActionRef)");
    }

    [Fact]
    public void A_custom_event_handler_takes_the_sender_and_a_var_array()
    {
        var palette = GraphTestEnvironment.Palette();
        var graph = new GraphBuilder("Fixture", "Quest");

        var entry = graph.Node(palette, NodePalette.RemoteEventId("FixtureEventSource", "AffinityChanged"));
        var notify = graph.Node(palette, "global:Debug.Notification");
        graph.Value(notify, "arg:asNotificationText", "string", "\"affinity\"");
        graph.Wire(entry, PinIds.Exec, notify, PinIds.Exec);

        Source(graph.Document).Should().Contain(
            "Event FixtureEventSource.AffinityChanged(FixtureEventSource akSender, Var[] akArgs)");
    }

    [Fact]
    public void The_sender_and_the_payload_are_readable_as_parameters()
    {

        var palette = GraphTestEnvironment.Palette();
        var graph = new GraphBuilder("Fixture", "Quest");

        var entry = graph.Node(palette, NodePalette.RemoteEventId("ObjectReference", "OnLoad"));
        var distance = graph.Node(palette, "call:ObjectReference.GetDistance");

        graph.Wire(entry, PinIds.Exec, distance, PinIds.Exec);
        graph.Wire(entry, PinIds.Parameter(NodePalette.RemoteSenderName), distance, PinIds.Self);
        graph.Wire(entry, PinIds.Parameter(NodePalette.RemoteSenderName), distance, "arg:akOther");

        Source(graph.Document).Should().Contain("akSender.GetDistance(akSender)");
    }

    [Fact]
    public void A_declared_custom_event_is_emitted()
    {
        var palette = GraphTestEnvironment.Palette();
        var graph = new GraphBuilder("Fixture", "ObjectReference");
        graph.CustomEvent("Ready");

        var entry = graph.Node(palette, "event:ObjectReference.OnLoad");
        var disable = graph.Node(palette, "call:ObjectReference.Disable");
        graph.Wire(entry, PinIds.Exec, disable, PinIds.Exec);

        Source(graph.Document).Should().Contain("CustomEvent Ready");
    }

    [Fact]
    public void A_custom_event_name_that_is_not_an_identifier_is_refused()
    {
        var palette = GraphTestEnvironment.Palette();
        var graph = new GraphBuilder("Fixture", "ObjectReference");
        graph.CustomEvent("not a name");

        var entry = graph.Node(palette, "event:ObjectReference.OnLoad");
        var disable = graph.Node(palette, "call:ObjectReference.Disable");
        graph.Wire(entry, PinIds.Exec, disable, PinIds.Exec);

        var result = GraphTestEnvironment.Compile(graph.Document);

        result.Success.Should().BeFalse();
        result.Errors.Should().Contain(d => d.Code == GraphDiagnosticCodes.InvalidScriptHeader,
            GraphTestEnvironment.Describe(result.Diagnostics));
    }

    [Fact]
    public void The_same_custom_event_declared_twice_is_refused()
    {
        var palette = GraphTestEnvironment.Palette();
        var graph = new GraphBuilder("Fixture", "ObjectReference");
        graph.CustomEvent("Ready");
        graph.CustomEvent("Ready");

        var entry = graph.Node(palette, "event:ObjectReference.OnLoad");
        var disable = graph.Node(palette, "call:ObjectReference.Disable");
        graph.Wire(entry, PinIds.Exec, disable, PinIds.Exec);

        var result = GraphTestEnvironment.Compile(graph.Document);

        result.Success.Should().BeFalse();
        result.Errors.Should().Contain(d => d.Code == GraphDiagnosticCodes.DuplicateDeclaration,
            GraphTestEnvironment.Describe(result.Diagnostics));
    }

    [Fact]
    public void A_local_override_and_a_remote_handler_of_the_same_event_coexist()
    {


        var palette = GraphTestEnvironment.Palette();
        var graph = new GraphBuilder("Fixture", "ObjectReference");

        var local = graph.Node(palette, "event:ObjectReference.OnLoad");
        var disable = graph.Node(palette, "call:ObjectReference.Disable");
        graph.Wire(local, PinIds.Exec, disable, PinIds.Exec);

        var remote = graph.Node(palette, NodePalette.RemoteEventId("ObjectReference", "OnLoad"));
        var enable = graph.Node(palette, "call:ObjectReference.Enable");
        graph.Wire(remote, PinIds.Exec, enable, PinIds.Exec);

        var result = GraphTestEnvironment.Compile(graph.Document);

        result.Diagnostics.Should().NotContain(d => d.Code == GraphDiagnosticCodes.DuplicateDeclaration,
            GraphTestEnvironment.Describe(result.Diagnostics));
        result.Success.Should().BeTrue(GraphTestEnvironment.Describe(result.Diagnostics));
    }

    [Fact]
    public void The_same_remote_handler_declared_twice_is_still_refused()
    {
        var palette = GraphTestEnvironment.Palette();
        var graph = new GraphBuilder("Fixture", "ObjectReference");

        var first = graph.Node(palette, NodePalette.RemoteEventId("ObjectReference", "OnLoad"));
        var disable = graph.Node(palette, "call:ObjectReference.Disable");
        graph.Wire(first, PinIds.Exec, disable, PinIds.Exec);

        var second = graph.Node(palette, NodePalette.RemoteEventId("ObjectReference", "OnLoad"));
        var enable = graph.Node(palette, "call:ObjectReference.Enable");
        graph.Wire(second, PinIds.Exec, enable, PinIds.Exec);

        var result = GraphTestEnvironment.Compile(graph.Document);

        result.Errors.Should().Contain(d => d.Code == GraphDiagnosticCodes.DuplicateDeclaration,
            GraphTestEnvironment.Describe(result.Diagnostics));
    }

    [Fact]
    public void The_palette_shows_the_signature_the_node_actually_emits()
    {


        var palette = GraphTestEnvironment.Palette();

        var withParameters = palette.Search("ObjectReference.OnActivate", limit: 20).Entries
            .FirstOrDefault(e => e.Id == NodePalette.RemoteEventId("ObjectReference", "OnActivate"));
        withParameters.Should().NotBeNull();
        withParameters!.Signature.Should().Be(
            "Event ObjectReference.OnActivate(ObjectReference akSender, ObjectReference akActionRef)");

        var withoutParameters = palette.Search("ObjectReference.OnLoad", limit: 20).Entries
            .FirstOrDefault(e => e.Id == NodePalette.RemoteEventId("ObjectReference", "OnLoad"));
        withoutParameters.Should().NotBeNull();
        withoutParameters!.Signature.Should().Be(
            "Event ObjectReference.OnLoad(ObjectReference akSender)");

        var custom = palette.Search("FixtureEventSource.AffinityChanged", limit: 20).Entries
            .FirstOrDefault(e => e.Id == NodePalette.RemoteEventId("FixtureEventSource", "AffinityChanged"));
        custom.Should().NotBeNull();
        custom!.Signature.Should().Be(
            "Event FixtureEventSource.AffinityChanged(FixtureEventSource akSender, Var[] akArgs)");
    }

    [Fact]
    public void A_custom_event_handler_decompiles_back_to_its_dotted_name()
    {




        var palette = GraphTestEnvironment.Palette();
        var graph = new GraphBuilder("Fixture", "Quest");

        var entry = graph.Node(palette, NodePalette.RemoteEventId("FixtureEventSource", "AffinityChanged"));
        var notify = graph.Node(palette, "global:Debug.Notification");
        graph.Value(notify, "arg:asNotificationText", "string", "\"affinity\"");
        graph.Wire(entry, PinIds.Exec, notify, PinIds.Exec);

        var result = GraphTestEnvironment.Compile(graph.Document);
        result.Success.Should().BeTrue(GraphTestEnvironment.Describe(result.Diagnostics));

        var directory = Directory.CreateTempSubdirectory("fo4re-remote-");
        try
        {
            var pexPath = Path.Combine(directory.FullName, "Fixture.pex");
            result.Pex!.WriteFile(pexPath);

            var text = PapyrusDecompiler.Decompile(pexPath, assembly: false);

            text.Should().Contain("FixtureEventSource.AffinityChanged");
            text.Should().NotContain("FixtureEventSource_AffinityChanged");
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
