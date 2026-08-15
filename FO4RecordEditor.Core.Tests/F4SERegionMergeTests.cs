using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using FO4RecordEditor.Services.Graph.F4SE;

namespace FO4RecordEditor.Core.Tests;










public class F4SERegionMergeTests
{
    private static string Generated(string name, string stub = "\t\tSTUB;") =>
        $$"""
        void {{name}}(StaticFunctionTag * base)
        {
        		{{F4SERegionMerge.Begin(name)}}
        {{stub}}
        		{{F4SERegionMerge.End(name)}}
        }
        """;

    [Fact]
    public void New_body_markers_are_preprocessor_directives()
    {
        F4SERegionMerge.Begin("Alpha").Should().Be("#pragma region FO4IDE_BODY Alpha");
        F4SERegionMerge.End("Alpha").Should().Be("#pragma endregion FO4IDE_BODY Alpha");
    }

    [Fact]
    public void A_legacy_comment_marked_body_survives_regeneration()
    {
        var prefix = "/" + "/";
        var previous = "void Alpha(StaticFunctionTag * base)\n{\n\t\t"
                       + prefix + " >>> body: Alpha\n\t\tkept();\n\t\t"
                       + prefix + " <<< body: Alpha\n}\n";

        var merged = F4SERegionMerge.Merge(Generated("Alpha"), previous);

        merged.Should().Contain("kept();");
    }

    [Fact]
    public void A_first_emit_with_no_previous_file_is_unchanged()
    {
        var generated = Generated("Alpha");
        F4SERegionMerge.Merge(generated, null).Should().Be(generated.Replace("\r\n", "\n"));
        F4SERegionMerge.Merge(generated, "").Should().Be(generated.Replace("\r\n", "\n"));
    }

    [Fact]
    public void A_hand_written_body_survives_regeneration()
    {
        var previous = Generated("Alpha", "\t\treturn DoTheRealThing();");
        var merged = F4SERegionMerge.Merge(Generated("Alpha"), previous);

        merged.Should().Contain("return DoTheRealThing();");
        merged.Should().NotContain("STUB;", "the stub is replaced by the preserved body");
    }

    [Fact]
    public void Everything_outside_a_region_is_machine_owned_and_overwritten()
    {
        var prefix = "/" + "/";
        var previous = "void Alpha(int oldSignature)\n{\n\t\t"
                       + F4SERegionMerge.Begin("Alpha") + "\n\t\tkept();\n\t\t"
                       + F4SERegionMerge.End("Alpha") + "\n}\n" + prefix + " a stale trailing comment\n";

        var merged = F4SERegionMerge.Merge(Generated("Alpha"), previous);

        merged.Should().Contain("kept();");
        merged.Should().NotContain("a stale trailing comment");
        merged.Should().NotContain("oldSignature");
    }

    [Fact]
    public void A_changed_signature_keeps_the_body_and_gains_a_banner()
    {
        var previous = "void Alpha(StaticFunctionTag * base, int extra)\n{\n\t\t"
                       + F4SERegionMerge.Begin("Alpha") + "\n\t\tkept();\n\t\t"
                       + F4SERegionMerge.End("Alpha") + "\n}\n";

        var merged = F4SERegionMerge.Merge(Generated("Alpha"), previous);

        merged.Should().Contain("kept();", "a changed signature must never cost the author their code");
        merged.Should().Contain(F4SERegionMerge.SignatureChangedBanner.Trim());
    }

    [Fact]
    public void An_unchanged_signature_gets_no_banner()
    {
        var previous = Generated("Alpha", "\t\tkept();");
        var merged = F4SERegionMerge.Merge(Generated("Alpha"), previous);

        merged.Should().Contain("kept();");
        merged.Should().NotContain("SIGNATURE CHANGED");
    }

    [Fact]
    public void Several_regions_are_matched_by_name_not_by_order()
    {
        var previous = Generated("Alpha", "\t\talphaBody();") + "\n" + Generated("Beta", "\t\tbetaBody();");
        var regenerated = Generated("Beta") + "\n" + Generated("Alpha");

        var merged = F4SERegionMerge.Merge(regenerated, previous);

        merged.Should().Contain("alphaBody();").And.Contain("betaBody();");
        merged.IndexOf("betaBody();", System.StringComparison.Ordinal)
            .Should().BeLessThan(merged.IndexOf("alphaBody();", System.StringComparison.Ordinal),
                "each body follows its own function, wherever that function now sits");
    }

    [Fact]
    public void A_body_whose_function_is_gone_is_reported_rather_than_silently_dropped()
    {
        var previous = Generated("Alpha", "\t\tkept();") + "\n" + Generated("Removed", "\t\torphan();");

        F4SERegionMerge.Orphaned(Generated("Alpha"), previous)
            .Should().ContainSingle().Which.Should().Be("Removed");
    }

    [Fact]
    public void A_new_function_added_since_the_last_emit_keeps_its_stub()
    {
        var previous = Generated("Alpha", "\t\tkept();");
        var merged = F4SERegionMerge.Merge(Generated("Alpha") + "\n" + Generated("Fresh"), previous);

        merged.Should().Contain("kept();");
        merged.Should().Contain("STUB;", "the newly added function has no preserved body yet");
    }

    [Fact]
    public void Reading_a_file_recovers_each_region_with_its_signature_line()
    {
        var regions = F4SERegionMerge.Read(Generated("Alpha", "\t\tbody();"));

        var region = regions.Should().ContainKey("Alpha").WhoseValue;
        region.Body.Trim().Should().Be("body();");
        region.SignatureLine.Should().Be("void Alpha(StaticFunctionTag * base)");
    }

    [Fact]
    public void An_end_marker_with_no_beginning_is_ignored_rather_than_paired_wrongly()
    {

        var damaged = "\t\t" + F4SERegionMerge.End("Alpha") + "\n\t\tloose();\n";

        F4SERegionMerge.Read(damaged).Should().BeEmpty();
    }

    [Fact]
    public void Windows_line_endings_in_the_previous_file_do_not_corrupt_the_merge()
    {
        var previous = Generated("Alpha", "\t\tkept();").Replace("\n", "\r\n");
        var merged = F4SERegionMerge.Merge(Generated("Alpha"), previous);

        merged.Should().Contain("kept();");
        merged.Should().NotContain("\r", "output is normalised to one line ending");
    }

    [Fact]
    public void The_emitter_preserves_a_body_when_handed_the_previous_file()
    {

        var plugin = new PluginBinding
        {
            Name = "Preserve",
            Modules = new[]
            {
                new ModuleBinding
                {
                    Name = "Util",
                    ScriptName = "PreserveUtil",
                    Natives = new[]
                    {
                        new NativeBinding
                        {
                            FunctionName = "Answer",
                            PapyrusClass = "PreserveUtil",
                            CppBaseType = F4SETypeMap.StaticFunctionTag,
                            ReturnType = new PapyrusTypeText("int"),
                            CppFunctionName = "Answer",
                        },
                    },
                },
            },
        };

        var emitter = new F4SEEmitter();
        const string path = "src/PapyrusPreserveUtil.cpp";

        var first = emitter.Emit(plugin);
        var edited = first.File(path)!.Text.Replace(
            "return SInt32();", "return 42;");

        var second = emitter.Emit(plugin, new F4SEEmitOptions
        {
            Existing = new Dictionary<string, string> { [path] = edited },
        });

        second.File(path)!.Text.Should().Contain("return 42;");
        second.OrphanedBodies.Should().BeEmpty();
    }

    [Fact]
    public void The_emitter_reports_an_orphaned_body_when_a_native_is_removed()
    {
        var withBoth = new PluginBinding
        {
            Name = "Orphan",
            Modules = new[]
            {
                new ModuleBinding
                {
                    Name = "Util",
                    ScriptName = "OrphanUtil",
                    Natives = new[] { Native("Kept"), Native("Dropped") },
                },
            },
        };
        var withOne = withBoth with
        {
            Modules = new[] { withBoth.Modules[0] with { Natives = new[] { Native("Kept") } } },
        };

        var emitter = new F4SEEmitter();
        const string path = "src/PapyrusOrphanUtil.cpp";
        var previous = emitter.Emit(withBoth).File(path)!.Text;

        var result = emitter.Emit(withOne, new F4SEEmitOptions
        {
            Existing = new Dictionary<string, string> { [path] = previous },
        });

        result.OrphanedBodies.Should().ContainSingle().Which.Should().Contain("Dropped");

        static NativeBinding Native(string name) => new()
        {
            FunctionName = name,
            PapyrusClass = "OrphanUtil",
            CppBaseType = F4SETypeMap.StaticFunctionTag,
            CppFunctionName = name,
        };
    }
}
