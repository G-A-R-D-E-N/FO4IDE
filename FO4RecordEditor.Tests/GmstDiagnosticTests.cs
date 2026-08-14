using System.IO;
using FluentAssertions;
using FO4RecordEditor.Services;
using Mutagen.Bethesda.Plugins.Records;

namespace FO4RecordEditor.Tests;

public class GmstDiagnosticTests : IDisposable
{
    private const string Instance = @"D:\Games\ModlistDownloads";
    private static bool Available => Directory.Exists(Path.Combine(Instance, "profiles"));

    // Load() writes the process-global environment (LinkCache, MasterIsEsl, PluginSourcePaths, ...)
    // by contract; restore it so this env doesn't leak into later test classes.
    private readonly GlobalStateIsolation _state = new();
    public void Dispose() => _state.Dispose();

    // Regression guard: a record (IMajorRecordGetter) also implements IFormLinkIdentifier, so a
    // careless "FormLinks are leaves" guard treated the whole record as a leaf and the walker
    // produced ZERO fields. This asserts a simple record (a game setting) still expands.
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
