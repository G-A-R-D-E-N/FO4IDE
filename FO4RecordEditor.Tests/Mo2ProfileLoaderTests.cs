using System.Collections;
using System.IO;
using FluentAssertions;
using FO4RecordEditor.Services;
using Mutagen.Bethesda.Plugins.Records;

namespace FO4RecordEditor.Tests;

// Integration test against a real MO2 instance on this machine. Skipped automatically if the
// instance isn't present, so it won't fail on other machines/CI.
public class Mo2ProfileLoaderTests : IDisposable
{
    private readonly Xunit.Abstractions.ITestOutputHelper _out;
    public Mo2ProfileLoaderTests(Xunit.Abstractions.ITestOutputHelper o) => _out = o;

    private readonly GlobalStateIsolation _state = new();
    public void Dispose() => _state.Dispose();

    private const string Instance = @"E:\Modlists\Fallen World Alpha 2";

    private static bool Available =>
        Directory.Exists(Path.Combine(Instance, "profiles")) &&
        File.Exists(Path.Combine(Instance, "ModOrganizer.ini"));

    [Fact]
    public void ReadsInstanceInfo()
    {
        if (!Available) return;   // not on this machine -> no-op
        var info = Mo2ProfileLoader.ReadInstanceInfo(Instance);
        info.Profile.Should().NotBeNullOrWhiteSpace();
        info.GameDataFolder.Should().EndWith("Data");
    }

    [Fact]
    public void LoadsProfileAndBuildsResolvableLinkCache()
    {
        if (!Available) return;   // not on this machine -> no-op
        var info = Mo2ProfileLoader.ReadInstanceInfo(Instance);

        var (env, plugins) = Mo2ProfileLoader.Load(Instance, info.Profile, info.GameDataFolder);

        plugins.Should().NotBeEmpty("the profile lists hundreds of enabled plugins");

        dynamic dynEnv = env;
        // LoadOrder enumerates and every listing exposes a non-null Mod + a filename.
        int listed = 0;
        foreach (var l in (IEnumerable)dynEnv.LoadOrder.ListedOrder)
        {
            ((object?)((dynamic)l).Mod).Should().NotBeNull();
            ((string)((dynamic)l).ModKey.FileName.String).Should().NotBeNullOrWhiteSpace();
            listed++;
        }
        listed.Should().Be(plugins.Count);

        // LinkCache resolves a winning record for the first major record of the first mod --
        // proves cross-plugin resolution works, not just that files opened.
        var cache = (Mutagen.Bethesda.Plugins.Cache.ILinkCache)dynEnv.LinkCache;
        cache.Should().NotBeNull();
    }

    [Fact]
    public void LoadsImplicitBaseMasters()
    {
        if (!Available) return;
        var info = Mo2ProfileLoader.ReadInstanceInfo(Instance);
        var (_, plugins) = Mo2ProfileLoader.Load(Instance, info.Profile, info.GameDataFolder);

        // FO4 force-loads these even though they're absent from plugins.txt; the loader must
        // include them or the base game is missing from the link cache.
        plugins.Should().Contain("Fallout4.esm");

        var cache = MutagenLoader.LinkCache!;
        // A purely-vanilla record (a game setting from Fallout4.esm) must now resolve.
        cache.PriorityOrder
            .SelectMany(m => m.EnumerateMajorRecords())
            .OfType<Mutagen.Bethesda.Fallout4.IGameSettingGetter>()
            .Should().Contain(g => g.FormKey.ModKey.FileName.String
                .Equals("Fallout4.esm", System.StringComparison.OrdinalIgnoreCase),
                "base-game records are present once Fallout4.esm is loaded");
    }

    [Fact]
    public void FormatsComponentFormLinkWithName()
    {
        if (!Available) return;
        var info = Mo2ProfileLoader.ReadInstanceInfo(Instance);
        Mo2ProfileLoader.Load(Instance, info.Profile, info.GameDataFolder);  // sets MutagenLoader.LinkCache

        var cache = MutagenLoader.LinkCache!;
        // A Component (CMPO) like c_Steel "Steel" -- resolve any one and format a link to it.
        var component = cache.PriorityOrder
            .SelectMany(m => m.EnumerateMajorRecords())
            .OfType<Mutagen.Bethesda.Fallout4.IComponentGetter>()
            .First();
        var link = Mutagen.Bethesda.Plugins.FormLinkInformation.Factory(component);

        var rendered = MutagenLoader.FormatFormLink(link);

        // xEdit-style: should carry the EditorID/name + bracketed FormID, not just the raw key.
        rendered.Should().Contain("[").And.Contain(component.FormKey.ToString());
        rendered.Should().NotBe(component.FormKey.ToString(), "the link should resolve to a name");
    }

    [Fact]
    public void BuildsMultiPluginConflictMatrix()
    {
        if (!Available) return;
        var info = Mo2ProfileLoader.ReadInstanceInfo(Instance);
        var (env, _) = Mo2ProfileLoader.Load(Instance, info.Profile, info.GameDataFolder);

        var cache = MutagenLoader.LinkCache!;
        // Find any record that is overridden by 2+ plugins (a real conflict set).
        Mutagen.Bethesda.Plugins.FormKey? conflicted = null;
        foreach (var rec in cache.PriorityOrder.SelectMany(m => m.EnumerateMajorRecords()).Take(20000))
        {
            if (cache.ResolveAllSimpleContexts(rec.FormKey).Take(2).Count() >= 2)
            { conflicted = rec.FormKey; break; }
        }
        if (conflicted == null) return;   // no conflicts in the sampled window -> nothing to assert

        var matrix = MutagenLoader.BuildConflictMatrix(env, conflicted.Value.ToString());
        matrix.Should().NotBeNull();
        matrix!.Plugins.Count.Should().BeGreaterThanOrEqualTo(2, "the record is carried by multiple plugins");
        matrix.Rows.Should().NotBeEmpty();
        // Winner is the last plugin in load order.
        matrix.Winner.Should().Be(matrix.Plugins[^1]);
    }

    [Fact]
    public void ConflictMatrix_DoesNotLeakReflectionInternals()
    {
        if (!Available) return;
        var info = Mo2ProfileLoader.ReadInstanceInfo(Instance);
        var (env, _) = Mo2ProfileLoader.Load(Instance, info.Profile, info.GameDataFolder);
        var cache = MutagenLoader.LinkCache!;

        // A constructible object usually has FormLink lists (Categories / Components) -- the
        // exact shape that used to make the walker recurse into System.Type / Assembly.
        var cobj = cache.PriorityOrder
            .SelectMany(m => m.EnumerateMajorRecords())
            .OfType<Mutagen.Bethesda.Fallout4.IConstructibleObjectGetter>()
            .FirstOrDefault();
        if (cobj == null) return;

        var matrix = MutagenLoader.BuildConflictMatrix(env, cobj.FormKey.ToString());
        matrix.Should().NotBeNull();
        matrix!.Rows.Should().NotBeEmpty("a COBJ expands to fields (so this can't pass vacuously)");

        foreach (var row in matrix.Rows)
        {
            row.Field.Should().NotContainAny(".Assembly", "Reflection", "DeclaringMethod", "GUID");
            foreach (var v in row.Values)
                v.Should().NotContainAny(".dll", "System.Private.CoreLib", "PublicKeyToken", "mscorlib");
        }
    }

    [Fact]
    public void OpenPlugin_ResolvesAnMo2SourcedPlugin_NotJustVanillaMasters()
    {
        _out.WriteLine($"Available={Available}");
        if (!Available) return;
        var info = Mo2ProfileLoader.ReadInstanceInfo(Instance);
        var (env, plugins) = Mo2ProfileLoader.Load(Instance, info.Profile, info.GameDataFolder);

        // Vanilla masters (Fallout4.esm/DLCs) AND Creation Club content (cc*.esl, which ships inside
        // the base install's Data folder, not a mods\ folder) also physically live in the game Data
        // folder, so both resolve even with the old DataFolderPath-only fallback and don't exercise
        // the bug. Need an ordinary mod plugin, which only ever lives in an MO2 mods\ or overwrite\
        // folder: prefer a known plugin if this instance has it, otherwise the first non-master
        // non-CC plugin.
        var implicitMasters = new HashSet<string>(
            Mutagen.Bethesda.Plugins.Implicits.Get(Mutagen.Bethesda.GameRelease.Fallout4).Listings.Select(m => m.FileName.String),
            System.StringComparer.OrdinalIgnoreCase);
        var modPlugin = plugins.FirstOrDefault(p => string.Equals(p, "HD LOD Textures.esp", System.StringComparison.OrdinalIgnoreCase))
            ?? plugins.FirstOrDefault(p => !implicitMasters.Contains(p) && !p.StartsWith("cc", System.StringComparison.OrdinalIgnoreCase));
        if (modPlugin == null) return;   // this instance's profile is somehow vanilla-only

        var result = WriteService.OpenPlugin(modPlugin, env);
        _out.WriteLine($"picked plugin: {modPlugin}");
        _out.WriteLine($"result: {result}");
        result.Should().NotContain("Could not locate",
            $"'{modPlugin}' was reported loaded by Mo2ProfileLoader.Load, so WriteService must be able to re-locate it on disk");
    }

}
