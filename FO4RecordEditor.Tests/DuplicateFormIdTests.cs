using System.Buffers.Binary;
using System.IO;
using System.Text;
using FluentAssertions;
using FO4RecordEditor.Services;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Fallout4;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Order;
using Noggog;
using Xunit;

namespace FO4RecordEditor.Tests;

public class DuplicateFormIdTests
{
    [Fact]
    public void DuplicateFormIds_LoadLastOccurrence_AndAreReportedEverywhere()
    {
        var plugin = $"DuplicateFormId_{Guid.NewGuid():N}.esp";
        var path = Path.Combine(Path.GetTempPath(), plugin);
        var modKey = ModKey.FromNameAndExtension(plugin);
        var mod = new Fallout4Mod(modKey, Fallout4Release.Fallout4);
        mod.Keywords.Add(new Keyword(new FormKey(modKey, 0x800), Fallout4Release.Fallout4)
        {
            EditorID = "Duplicate_First",
        });
        mod.Keywords.Add(new Keyword(new FormKey(modKey, 0x801), Fallout4Release.Fallout4)
        {
            EditorID = "Duplicate_Last",
        });
        mod.WriteToBinary(path);
        PatchSecondKeywordToDuplicateFirst(path);

        IFallout4ModGetter? overlay = null;
        try
        {
            overlay = Fallout4Mod.CreateFromBinaryOverlay(ModPath.FromPath(path), Fallout4Release.Fallout4);
            var loaded = overlay.Keywords.ToArray();
            loaded.Should().ContainSingle("the tolerant loader keeps one record per FormID");
            loaded[0].EditorID.Should().Be("Duplicate_Last", "the engine and loader keep the last occurrence");

            MutagenLoader.LooseModPaths[plugin] = path;
            MutagenLoader.ReplaceLooseMod(plugin, overlay);

            var duplicates = MutagenLoader.GetDuplicateFormIds(plugin);
            duplicates.Should().ContainSingle();
            duplicates[0].RawFormId.Should().Be(0x00000800u);
            duplicates[0].Count.Should().Be(2);
            duplicates[0].RecordTypes.Should().Equal("KYWD", "KYWD");

            MutagenLoader.CheckPlugin(null, plugin)
                .Should().Contain("1 duplicate FormID group")
                .And.Contain("00000800")
                .And.Contain("last occurrence");

            var env = BuildEnvironment(overlay, path);
            MutagenLoader.LinkCache = env.LinkCache;

            MutagenLoader.GetRecordProblems(env, loaded[0].FormKey.ToString())
                .Should().ContainSingle(p => p.Description.Contains("DUPLICATE FORMID 00000800"));

            MutagenLoader.ScanBrokenRefs(env, plugin)
                .Should().Contain("duplicate FormID group")
                .And.Contain("00000800");
        }
        finally
        {
            MutagenLoader.LinkCache = null;
            MutagenLoader.ReleaseLooseMod(plugin);
            MutagenLoader.LooseModPaths.TryRemove(plugin, out _);
            DuplicateFormIdScanner.Invalidate(path);
            try { File.Delete(path); } catch { }
        }
    }

    [Fact]
    public void Problems_UseTheEnvironmentPathAndQueueAnUncachedScan()
    {
        var plugin = $"DuplicateEnvPath_{Guid.NewGuid():N}.esp";
        // Both files represent the same plugin, so each must be named exactly `plugin` (Mutagen's
        // RunMasterMatch throws "ModKeys were misaligned" if the target filename differs from the
        // mod's ModKey); distinct directories keep the env and loose paths distinct.
        var envDir = Path.Combine(Path.GetTempPath(), $"envdir_{Guid.NewGuid():N}");
        var looseDir = Path.Combine(Path.GetTempPath(), $"loosedir_{Guid.NewGuid():N}");
        Directory.CreateDirectory(envDir);
        Directory.CreateDirectory(looseDir);
        var envPath = Path.Combine(envDir, plugin);
        var loosePath = Path.Combine(looseDir, plugin);
        var modKey = ModKey.FromNameAndExtension(plugin);
        var mod = new Fallout4Mod(modKey, Fallout4Release.Fallout4);
        mod.Keywords.Add(new Keyword(new FormKey(modKey, 0x800), Fallout4Release.Fallout4)
        {
            EditorID = "EnvFirst",
        });
        mod.Keywords.Add(new Keyword(new FormKey(modKey, 0x801), Fallout4Release.Fallout4)
        {
            EditorID = "EnvLast",
        });
        mod.WriteToBinary(envPath);
        mod.WriteToBinary(loosePath);
        PatchSecondKeywordToDuplicateFirst(envPath);

        IFallout4ModGetter? overlay = null;
        try
        {
            overlay = Fallout4Mod.CreateFromBinaryOverlay(ModPath.FromPath(envPath), Fallout4Release.Fallout4);
            var loaded = overlay.Keywords.Single();
            var env = BuildEnvironment(overlay, envPath);
            MutagenLoader.LooseModPaths[plugin] = loosePath;
            DuplicateFormIdScanner.Invalidate(envPath);
            DuplicateFormIdScanner.Invalidate(loosePath);

            MutagenLoader.GetRecordProblems(env, loaded.FormKey.ToString())
                .Should().ContainSingle(p => p.Description.Contains("running in the background"),
                    "the Problems drawer should schedule an uncached raw scan rather than block on it");

            DuplicateFormIdScanner.Scan(envPath).Duplicates.Should().ContainSingle();
            DuplicateFormIdScanner.Scan(loosePath).Duplicates.Should().BeEmpty();

            MutagenLoader.GetRecordProblems(env, loaded.FormKey.ToString())
                .Should().ContainSingle(p => p.Description.Contains("DUPLICATE FORMID 00000800"),
                    "environment bytes must win over a same-named loose alias");
        }
        finally
        {
            (overlay as IDisposable)?.Dispose();
            MutagenLoader.LooseModPaths.TryRemove(plugin, out _);
            DuplicateFormIdScanner.Invalidate(envPath);
            DuplicateFormIdScanner.Invalidate(loosePath);
            try { Directory.Delete(envDir, recursive: true); } catch { }
            try { Directory.Delete(looseDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Problems_SurfaceCachedDuplicateScanErrors()
    {
        var plugin = $"DuplicateScanError_{Guid.NewGuid():N}.esp";
        var path = Path.Combine(Path.GetTempPath(), plugin);
        var modKey = ModKey.FromNameAndExtension(plugin);
        var mod = new Fallout4Mod(modKey, Fallout4Release.Fallout4);
        mod.Keywords.Add(new Keyword(new FormKey(modKey, 0x800), Fallout4Release.Fallout4)
        {
            EditorID = "ErrorKeyword",
        });
        File.WriteAllBytes(path, Encoding.ASCII.GetBytes("broken"));
        var env = BuildEnvironment(mod, path);
        DuplicateFormIdScanner.Scan(path).Error.Should().NotBeNull();

        try
        {
            MutagenLoader.GetRecordProblems(env, mod.Keywords.Single().FormKey.ToString())
                .Should().ContainSingle(p => p.Description.Contains("scan failed"));
        }
        finally
        {
            DuplicateFormIdScanner.Invalidate(path);
            try { File.Delete(path); } catch { }
        }
    }

    private static Mo2ProfileLoader.Mo2GameEnvironment BuildEnvironment(
        IFallout4ModGetter mod,
        string? pluginPath = null)
    {
        var listing = (IModListingGetter<IFallout4ModGetter>)new ModListing<IFallout4ModGetter>(
            mod.ModKey,
            mod,
            enabled: true,
            ghostSuffix: string.Empty);
        var loadOrder = new LoadOrder<IModListingGetter<IFallout4ModGetter>>([listing], disposeItems: false);
        var linkCache = loadOrder.ToImmutableLinkCache<IFallout4Mod, IFallout4ModGetter>();
        return new Mo2ProfileLoader.Mo2GameEnvironment
        {
            LoadOrder = loadOrder,
            LinkCache = linkCache,
            DataFolderPath = new DirectoryPath(Path.GetTempPath()),
            PluginPaths = pluginPath == null
                ? new Dictionary<string, string>()
                : new Dictionary<string, string> { [mod.ModKey.FileName.String] = pluginPath },
        };
    }

    private static void PatchSecondKeywordToDuplicateFirst(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var offsets = new List<int>();
        CollectRecordHeaders(bytes, 0, bytes.Length, "KYWD", offsets);
        offsets.Should().HaveCount(2);

        var firstRawFormId = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offsets[0] + 12, 4));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offsets[1] + 12, 4), firstRawFormId);
        File.WriteAllBytes(path, bytes);
    }

    private static void CollectRecordHeaders(
        byte[] bytes,
        int start,
        int end,
        string wantedSignature,
        List<int> offsets)
    {
        var position = start;
        while (position < end)
        {
            (end - position).Should().BeGreaterThanOrEqualTo(24);
            var signature = Encoding.ASCII.GetString(bytes, position, 4);
            var size = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(position + 4, 4)));
            if (signature == "GRUP")
            {
                size.Should().BeGreaterThanOrEqualTo(24);
                CollectRecordHeaders(bytes, position + 24, position + size, wantedSignature, offsets);
                position += size;
                continue;
            }

            if (signature == wantedSignature) offsets.Add(position);
            position += checked(24 + size);
        }
        position.Should().Be(end);
    }
}
