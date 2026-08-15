using FluentAssertions;
using FO4RecordEditor.Services;
using Mutagen.Bethesda.Fallout4;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;

namespace FO4RecordEditor.Core.Tests;

public sealed class PlacedObjectRotationTests
{
    [Fact]
    public void CreatePlacedObject_RoundTripsAllThreeRotationAxes()
    {
        var plugin = $"PlacedRotation_{Guid.NewGuid():N}.esp";
        var outPath = Path.Combine(Path.GetTempPath(), plugin);
        const string baseEditorId = "RotationBase";
        const string cellEditorId = "RotationCell";
        const string refEditorId = "RotationRef";
        const float rotX = 0.25f;
        const float rotY = -0.5f;
        const float rotZ = 1.75f;

        try
        {
            WriteService.CreatePlugin(plugin).Should().Contain("Created new plugin");
            WriteService.CreateRecord(plugin, "MISC", baseEditorId, null).Should().Contain("Created MISC");
            WriteService.CreateCell(plugin, cellEditorId, null, null).Should().Contain("Created interior CELL");

            var mod = WriteService.GetMutable(plugin);
            mod.Should().NotBeNull();
            var baseRecord = mod!.EnumerateMajorRecords().Single(r => r.EditorID == baseEditorId);

            WriteService.CreatePlacedObject(
                    plugin, cellEditorId, baseRecord.FormKey.ToString(), refEditorId,
                    10f, 20f, 30f, rotX, rotY, rotZ,
                    persistent: true, initiallyDisabled: false,
                    mapMarkerName: null, mapMarkerType: null, mapMarkerVisible: false,
                    env: null)
                .Should().Contain("Created REFR");

            WriteService.SavePlugin(plugin, outPath, null).Should().Contain("Saved");

            using var reload = Fallout4Mod.CreateFromBinaryOverlay(
                ModPath.FromPath(outPath), Fallout4Release.Fallout4);
            var placed = reload.EnumerateMajorRecords()
                .OfType<IPlacedObjectGetter>()
                .Single(r => r.EditorID == refEditorId);

            placed.Rotation.X.Should().BeApproximately(rotX, 0.0001f);
            placed.Rotation.Y.Should().BeApproximately(rotY, 0.0001f);
            placed.Rotation.Z.Should().BeApproximately(rotZ, 0.0001f);
        }
        finally
        {
            try { File.Delete(outPath); } catch { }
        }
    }
}
