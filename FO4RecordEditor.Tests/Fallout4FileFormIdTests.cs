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

public class Fallout4FileFormIdTests
{
    [Fact]
    public void SavePlugin_CustomActorValueUsesFinalMastOrdinal_AfterFallout4IsForced()
    {
        var release = Fallout4Release.Fallout4;
        var fullName = $"FullDependency_{Guid.NewGuid():N}.esm";
        var lightName = $"LightDependency_{Guid.NewGuid():N}.esl";
        var patchName = $"ConditionPatch_{Guid.NewGuid():N}.esp";
        var output = Path.Combine(Path.GetTempPath(), patchName);

        var fallout4 = new Fallout4Mod(ModKey.FromNameAndExtension("Fallout4.esm"), release);
        var full = new Fallout4Mod(ModKey.FromNameAndExtension(fullName), release);
        var keyword = new Keyword(new FormKey(full.ModKey, 0x1234), release)
        {
            EditorID = "FullMasterAnchor",
        };
        full.Keywords.Add(keyword);

        var light = new Fallout4Mod(ModKey.FromNameAndExtension(lightName), release);
        light.ModHeader.Flags |= Fallout4ModHeader.HeaderFlag.Small;
        var actorValue = new ActorValueInformation(new FormKey(light.ModKey, 0x812), release)
        {
            EditorID = "LightActorValue",
        };
        light.ActorValueInformation.Add(actorValue);

        WriteService.CreatePlugin(patchName);
        var patch = WriteService.GetMutable(patchName)!;
        patch.Weapons.Add(new Weapon(new FormKey(patch.ModKey, 0x800), release)
        {
            EditorID = "FullMasterReference",
            Keywords = new()
            {
                new FormLink<IKeywordGetter>(keyword.FormKey),
            },
        });

        var recipe = new ConstructibleObject(new FormKey(patch.ModKey, 0x801), release)
        {
            EditorID = "LightActorValueCondition",
        };
        var conditionData = new FunctionConditionData
        {
            Function = Condition.Function.GetBaseValue,
        };
        conditionData.ParameterOneRecord.SetTo(actorValue.FormKey);
        recipe.Conditions.Add(new ConditionFloat
        {
            ComparisonValue = 10,
            CompareOperator = CompareOperator.GreaterThanOrEqualTo,
            Data = conditionData,
        });
        patch.ConstructibleObjects.Add(recipe);

        var env = BuildEnvironment(fallout4, full, light, patch);
        var savedCache = MutagenLoader.LinkCache;
        var savedLightFlags = MutagenLoader.MasterIsEsl.ToArray();
        try
        {
            MutagenLoader.LinkCache = env.LinkCache;
            MutagenLoader.MasterIsEsl.Clear();
            MutagenLoader.MasterIsEsl["Fallout4.esm"] = false;
            MutagenLoader.MasterIsEsl[fullName] = false;
            MutagenLoader.MasterIsEsl[lightName] = true;

            WriteService.SavePlugin(patchName, output, env).Should().Contain("Saved");
            WriteService.ReadMasterNames(output).Should().Equal("Fallout4.esm", fullName, lightName);

            ReadSingleCtdaParameterOne(output).Should().Be(0x02000812u);
        }
        finally
        {
            MutagenLoader.LinkCache = savedCache;
            RestoreLightFlags(savedLightFlags);
            try { File.Delete(output); } catch { }
        }
    }

    [Fact]
    public void CreateSeqFile_LightPluginUsesSavedTotalMastCount_NotRuntimeFeSlot()
    {
        var release = Fallout4Release.Fallout4;
        var fullName = $"FullSeqDependency_{Guid.NewGuid():N}.esm";
        var lightName = $"LightSeqDependency_{Guid.NewGuid():N}.esl";
        var pluginName = $"LightSeq_{Guid.NewGuid():N}.esl";
        var pluginPath = Path.Combine(Path.GetTempPath(), pluginName);
        var outputDir = Path.Combine(Path.GetTempPath(), $"LightSeqOut_{Guid.NewGuid():N}");

        var fallout4 = new Fallout4Mod(ModKey.FromNameAndExtension("Fallout4.esm"), release);
        var falloutKeyword = new Keyword(new FormKey(fallout4.ModKey, 0x456), release)
        {
            EditorID = "FalloutSeqKeyword",
        };
        fallout4.Keywords.Add(falloutKeyword);
        var full = new Fallout4Mod(ModKey.FromNameAndExtension(fullName), release);
        var fullKeyword = new Keyword(new FormKey(full.ModKey, 0x1234), release)
        {
            EditorID = "FullSeqKeyword",
        };
        full.Keywords.Add(fullKeyword);

        var light = new Fallout4Mod(ModKey.FromNameAndExtension(lightName), release);
        light.ModHeader.Flags |= Fallout4ModHeader.HeaderFlag.Small;
        var lightKeyword = new Keyword(new FormKey(light.ModKey, 0x812), release)
        {
            EditorID = "LightSeqKeyword",
        };
        light.Keywords.Add(lightKeyword);

        WriteService.CreatePlugin(pluginName);
        var mod = WriteService.GetMutable(pluginName)!;
        mod.ModHeader.Flags |= Fallout4ModHeader.HeaderFlag.Small;
        mod.Weapons.Add(new Weapon(new FormKey(mod.ModKey, 0x800), release)
        {
            EditorID = "MasterAnchors",
            Keywords = new()
            {
                new FormLink<IKeywordGetter>(falloutKeyword.FormKey),
                new FormLink<IKeywordGetter>(fullKeyword.FormKey),
                new FormLink<IKeywordGetter>(lightKeyword.FormKey),
            },
        });
        mod.Quests.Add(new Quest(new FormKey(mod.ModKey, 0x801), release)
        {
            EditorID = "StartOnExistingSave",
            Data = new QuestData { Flags = Quest.Flag.StartGameEnabled },
        });

        var env = BuildEnvironment(fallout4, full, light, mod);
        var savedCache = MutagenLoader.LinkCache;
        var savedLightFlags = MutagenLoader.MasterIsEsl.ToArray();
        try
        {
            MutagenLoader.LinkCache = env.LinkCache;
            MutagenLoader.MasterIsEsl.Clear();
            MutagenLoader.MasterIsEsl["Fallout4.esm"] = false;
            MutagenLoader.MasterIsEsl[fullName] = false;
            MutagenLoader.MasterIsEsl[lightName] = true;

            WriteService.SavePlugin(pluginName, pluginPath, env).Should().Contain("Saved");
            WriteService.ReadMasterNames(pluginPath).Should().Equal("Fallout4.esm", fullName, lightName);
            mod.MasterReferences.Select(master => master.Master.FileName.String)
                .Should().Equal(
                    new[] { "Fallout4.esm", fullName, lightName },
                    "the editable model must mirror the MAST table that was actually written");

            WriteService.CreateSeqFile(env, pluginName, outputDir).Should().Contain("Wrote");
            var bytes = File.ReadAllBytes(Path.Combine(outputDir, Path.ChangeExtension(pluginName, ".seq")));
            bytes.Should().HaveCount(4);

            BinaryPrimitives.ReadUInt32LittleEndian(bytes).Should().Be(0x03000801u);
        }
        finally
        {
            MutagenLoader.LinkCache = savedCache;
            RestoreLightFlags(savedLightFlags);
            try { File.Delete(pluginPath); } catch { }
            try { Directory.Delete(outputDir, recursive: true); } catch { }
        }
    }

    private static Mo2ProfileLoader.Mo2GameEnvironment BuildEnvironment(params IFallout4ModGetter[] mods)
    {
        var listings = mods
            .Select(mod => (IModListingGetter<IFallout4ModGetter>)new ModListing<IFallout4ModGetter>(
                mod.ModKey,
                mod,
                enabled: true,
                ghostSuffix: string.Empty))
            .ToList();
        var loadOrder = new LoadOrder<IModListingGetter<IFallout4ModGetter>>(listings, disposeItems: false);
        var linkCache = loadOrder.ToImmutableLinkCache<IFallout4Mod, IFallout4ModGetter>();
        return new Mo2ProfileLoader.Mo2GameEnvironment
        {
            LoadOrder = loadOrder,
            LinkCache = linkCache,
            DataFolderPath = new DirectoryPath(Path.GetTempPath()),
            PluginPaths = new Dictionary<string, string>(),
        };
    }

    private static uint ReadSingleCtdaParameterOne(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var signature = Encoding.ASCII.GetBytes("CTDA");
        var matches = new List<int>();
        for (var i = 0; i <= bytes.Length - 6; i++)
        {
            if (bytes.AsSpan(i, 4).SequenceEqual(signature))
                matches.Add(i);
        }

        matches.Should().ContainSingle("the fixture writes exactly one condition subrecord");
        var offset = matches.Single();
        var payloadLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset + 4, 2));
        payloadLength.Should().BeGreaterThanOrEqualTo(16);
        return BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset + 6 + 12, 4));
    }

    private static void RestoreLightFlags(KeyValuePair<string, bool>[] saved)
    {
        MutagenLoader.MasterIsEsl.Clear();
        foreach (var pair in saved)
            MutagenLoader.MasterIsEsl[pair.Key] = pair.Value;
    }
}