using System.IO;
using System.Linq;
using FluentAssertions;
using FO4RecordEditor.Services;
using Mutagen.Bethesda.Fallout4;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace FO4RecordEditor.Tests;



public class CellServiceTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    public CellServiceTests(ITestOutputHelper o) => _out = o;



    private readonly GlobalStateIsolation _state = new();
    public void Dispose() => _state.Dispose();

    private const string Instance = @"E:\Modlists\Fallen World Alpha 2";

    private static bool Available =>
        Directory.Exists(Path.Combine(Instance, "profiles")) &&
        File.Exists(Path.Combine(Instance, "ModOrganizer.ini"));

    [Fact]
    public void GetPlacedReferences_UnionsAcrossPlugins_NotJustTheWinningRecord()
    {
        if (!Available) return;
        var info = Mo2ProfileLoader.ReadInstanceInfo(Instance);
        var (env, _) = Mo2ProfileLoader.Load(Instance, info.Profile, info.GameDataFolder);

        var cache = MutagenLoader.LinkCache!;
        var cell = cache.PriorityOrder
            .SelectMany(m => m.EnumerateMajorRecords())
            .OfType<ICellGetter>()
            .Where(c => c.Flags.HasFlag(Cell.Flag.IsInteriorCell))
            .OrderByDescending(c => c.Persistent.Count + c.Temporary.Count)
            .FirstOrDefault();
        if (cell == null) return;






        var namingLowerBound = cell.Persistent.Count + cell.Temporary.Count;

        var json = CellService.GetPlacedReferencesJson(cell.FormKey.ToString(), env);
        var parsed = JObject.Parse(json);
        parsed["error"].Should().BeNull(json);

        var referenceCount = parsed["referenceCount"]!.Value<int>();
        _out.WriteLine($"cell={cell.FormKey} {cell.EditorID}  naive={namingLowerBound}  merged={referenceCount}");
        referenceCount.Should().BeGreaterThanOrEqualTo(namingLowerBound,
            "the merged reference count must never be smaller than a single plugin's own copy of the cell");

        var references = (JArray)parsed["references"]!;
        references.Should().HaveCount(referenceCount);

        references.Should().Contain(r =>
            (float)r["position"]!["x"]! != 0f || (float)r["position"]!["y"]! != 0f || (float)r["position"]!["z"]! != 0f);
    }

    [Fact]
    public void GetPlacedReferences_UnknownId_ReturnsError()
    {
        if (!Available) return;
        var info = Mo2ProfileLoader.ReadInstanceInfo(Instance);
        var (env, _) = Mo2ProfileLoader.Load(Instance, info.Profile, info.GameDataFolder);

        var json = CellService.GetPlacedReferencesJson("ThisCellDoesNotExist12345", env);
        var parsed = JObject.Parse(json);
        parsed["error"].Should().NotBeNull();
    }





    [Fact]
    public void GetPlacedReferences_ATextureSetBase_ReportsDecalFields_NotAModelPath()
    {
        if (!Available) return;
        var info = Mo2ProfileLoader.ReadInstanceInfo(Instance);
        var (env, _) = Mo2ProfileLoader.Load(Instance, info.Profile, info.GameDataFolder);

        var json = CellService.GetPlacedReferencesJson("SanctuaryBasementJahani", env);
        var parsed = JObject.Parse(json);
        parsed["error"].Should().BeNull(json);

        var refs = (JArray)parsed["references"]!;
        var decalRef = refs.FirstOrDefault(r => (string?)r["formKey"] == "1D515B:Fallout4.esm");
        decalRef.Should().NotBeNull("this specific reference is a known real ground decal");

        decalRef!["modelPath"]!.Type.Should().Be(Newtonsoft.Json.Linq.JTokenType.Null,
            "a TextureSet base has no Model field -- there is genuinely no mesh to show");
        ((string?)decalRef["recordType"]).Should().Be("PlacedObject",
            "the interface-based check must correct Mutagen's misleading overlay-wrapper type name");
        ((string?)decalRef["baseType"]).Should().Be("TextureSet");
        ((string?)decalRef["decalDiffuse"]).Should().Be("Landscape/Ground/CommonwealthDefault01Decal_d.DDS");
        ((float?)decalRef["decalWidth"]).Should().BeApproximately(15f, 0.5f, "ObjectBounds X-extent (7 - -8)");
        ((float?)decalRef["decalHeight"]).Should().BeApproximately(60f, 0.5f, "ObjectBounds Y-extent (30 - -30)");
    }




    [Fact]
    public void GetPlacedReferences_SwitchboardScol_FallsBackToMemberStatics()
    {
        if (!Available) return;
        var info = Mo2ProfileLoader.ReadInstanceInfo(Instance);
        var (env, _) = Mo2ProfileLoader.Load(Instance, info.Profile, info.GameDataFolder);

        var json = CellService.GetPlacedReferencesJson("Switchboard", env);
        var parsed = JObject.Parse(json);
        parsed["error"].Should().BeNull(json);

        var refs = (JArray)parsed["references"]!;
        var scolRefs = refs.Where(r => r["scolParts"] is JArray parts && parts.Count > 0).ToList();
        scolRefs.Should().NotBeEmpty("Switchboard has at least one un-precombined SCOL reference");

        var firstParts = (JArray)scolRefs[0]["scolParts"]!;
        var firstPart = (JObject)firstParts[0];
        ((string?)firstPart["modelPath"]).Should().NotBeNullOrWhiteSpace("each part must carry the MEMBER static's own resolvable model, not the SCOL's dead precombine path");

        var placements = (JArray)firstPart["placements"]!;
        placements.Should().NotBeEmpty();
        var firstPlacement = (JObject)placements[0];
        foreach (var field in new[] { "x", "y", "z", "rx", "ry", "rz", "scale" })
            firstPlacement[field].Should().NotBeNull($"placement.{field} must be present");
    }
}
