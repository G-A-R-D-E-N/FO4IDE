using FluentAssertions;
using FO4RecordEditor.Services.Rendering;
using Xunit;

namespace FO4RecordEditor.Tests.Rendering;

public class FriendlyNamesTests
{
    [Fact]
    public void Maps_known_names_and_auto_splits_unknowns()
    {
        FriendlyNames.Label("EditorID").Should().Be("Editor ID");
        FriendlyNames.Label("CreatedObject").Should().Be("Created Object");



        FriendlyNames.Label("WorkbenchKeyword").Should().Be("Workbench");



        FriendlyNames.Label("SomeUnmappedField").Should().Be("Some Unmapped Field");
        FriendlyNames.Label("[0]").Should().Be("[0]");
    }

    [Fact]
    public void Auto_split_handles_acronyms()
    {
        FriendlyNames.Label("AIData").Should().Be("AI Data");
        FriendlyNames.Label("NPCAttackSound").Should().Be("NPC Attack Sound");
        FriendlyNames.Label("SoundIDs").Should().Be("Sound IDs");
        FriendlyNames.Label("HasDistantLOD").Should().Be("Has Distant LOD");
        FriendlyNames.Label("HTMLParser").Should().Be("HTML Parser");
        FriendlyNames.Label("_HasData").Should().Be("Has Data");
    }

    [Fact]
    public void LabelPath_relabels_plain_names_but_leaves_paths_and_indices_intact()
    {
        FriendlyNames.LabelPath("MarkerColor").Should().Be("Marker Color");
        FriendlyNames.LabelPath("Components[0].Component").Should().Be("Components[0].Component");
        FriendlyNames.LabelPath("[0]").Should().Be("[0]");
    }
}
