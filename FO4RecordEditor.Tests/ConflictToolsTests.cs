using System.IO;
using FluentAssertions;
using FO4RecordEditor.Services;

namespace FO4RecordEditor.Tests;

public class ConflictToolsTests : IDisposable
{
    private const string Instance = @"D:\Games\ModlistDownloads";
    private static bool Available => Directory.Exists(Path.Combine(Instance, "profiles"));

    private readonly GlobalStateIsolation _state = new();
    public void Dispose() => _state.Dispose();

    private static PluginToolExecutor MakeExecutor(out object env)
    {
        var info = Mo2ProfileLoader.ReadInstanceInfo(Instance);
        (env, _) = Mo2ProfileLoader.Load(Instance, info.Profile, info.GameDataFolder);
        var captured = env;
        return new PluginToolExecutor(() => captured, () => Instance);
    }

    [Fact]
    public void BuildConflictMatrix_DrillsIntoComponentsAndConditions()
    {
        if (!Available) return;
        MakeExecutor(out var env);
        var cache = MutagenLoader.LinkCache!;

        var cobj = cache.PriorityOrder
            .SelectMany(m => m.EnumerateMajorRecords())
            .OfType<Mutagen.Bethesda.Fallout4.IConstructibleObjectGetter>()
            .FirstOrDefault(c => c.Components is { Count: > 0 } && c.Conditions is { Count: > 0 });
        if (cobj == null) return;

        var matrix = MutagenLoader.BuildConflictMatrix(env, cobj.FormKey.ToString());
        matrix.Should().NotBeNull();
        var fields = matrix!.Rows.Select(r => r.Field).ToList();

        fields.Should().Contain(f => f.StartsWith("Components["));
        fields.Should().Contain(f => f.StartsWith("Conditions["));

        fields.Should().Contain(f => f.StartsWith("Components[") && f.Contains("]."));
        fields.Should().Contain(f => f.StartsWith("Conditions[") && f.Contains("]."));

        fields.Should().NotContain(f => f.StartsWith("Description["));
    }

    [Fact]
    public void GetConflicts_ShowsTheOverrideChainAndWinner()
    {
        if (!Available) return;
        var exec = MakeExecutor(out _);

        var result = exec.Execute("get_conflicts", """{"id":"054C84:Fallout4.esm"}""");
        result.Should().Contain("workshopScrapRecipe_Bush")
            .And.Contain("WINNER")
            .And.Contain("load order");
    }

    [Fact]
    public void GetWinningRecord_ReturnsTheEffectivePluginAndFields()
    {
        if (!Available) return;
        var exec = MakeExecutor(out _);

        var result = exec.Execute("get_winning_record", """{"id":"054C84:Fallout4.esm"}""");
        result.Should().Contain("Winning plugin:").And.Contain("workshopScrapRecipe_Bush");
    }

    [Fact]
    public void SearchRobcoConfigs_FindsRuntimePatchLines()
    {
        if (!Available) return;
        var exec = MakeExecutor(out _);

        var result = exec.Execute("search_robco_configs", """{"query":"filterByKeyword"}""");
        result.Should().Contain("RobCo Patcher matches").And.Contain(".ini");
    }
}
