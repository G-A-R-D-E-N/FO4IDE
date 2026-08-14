using System.IO;
using FluentAssertions;
using FO4RecordEditor.Services;
using Mutagen.Bethesda.Plugins.Records;

namespace FO4RecordEditor.Tests;

public class GmstDiagnosticTests : IDisposable
{
    private const string Instance = @"D:\Games\ModlistDownloads";
    private static bool Available => Directory.Exists(Path.Combine(Instance, "profiles"));

    private readonly GlobalStateIsolation _state = new();
    public void Dispose() => _state.Dispose();

    [Fact]
    public void RecordsStillExpandToFields()
    {
        if (!Available) return;
        var info = Mo2ProfileLoader.ReadInstanceInfo(Instance);
        var (env, _) = Mo2ProfileLoader.Load(Instance, info.Profile, info.GameDataFolder);
        var cache = MutagenLoader.LinkCache!;

        var gmst = cache.PriorityOrder
            .SelectMany(m => m.EnumerateMajorRecords())
            .OfType<Mutagen.Bethesda.Fallout4.IGameSettingGetter>()
            .FirstOrDefault();
        gmst.Should().NotBeNull();

        var matrix = MutagenLoader.BuildConflictMatrix(env, gmst!.FormKey.ToString());
        matrix.Should().NotBeNull();
        matrix!.Rows.Should().NotBeEmpty("a game setting expands to fields (Data/EditorID/...)");
        matrix.Rows.Should().Contain(r => r.Field == "Data" || r.Field.StartsWith("Data"),
            "the game setting's value field should be present");
    }
}
