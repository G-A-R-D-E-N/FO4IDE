using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using FO4RecordEditor.Services.Graph;
using FO4RecordEditor.Services.Graph.F4SE;
using FO4RecordEditor.Services.Papyrus;

namespace FO4RecordEditor.Core.Tests;










public class F4SEEmitterTests
{
    private static PapyrusTypeText T(string written) =>
        written.EndsWith("[]", StringComparison.Ordinal)
            ? new PapyrusTypeText(written[..^2], true)
            : new PapyrusTypeText(written);

    private static NativeParameter Param(string name, string type, string? def = null, bool unsigned = false) =>
        new(name, T(type), def, unsigned);


    private static PluginBinding Sample(F4SETarget target = F4SETarget.Og_1_10_163) => new()
    {
        Name = "SamplePlugin",
        Author = "Tester",
        VersionMajor = 1,
        VersionMinor = 2,
        VersionPatch = 3,
        Target = target,
        Modules = new[]
        {
            new ModuleBinding
            {
                Name = "Actor",
                ScriptName = "SampleActor",
                Extends = "Actor",
                Structs = new[]
                {
                    new StructBinding
                    {
                        Name = "SampleWornItem",
                        OwnerScript = "SampleActor",
                        Members = new[]
                        {
                            new StructMemberBinding("item", T("Form")),
                            new StructMemberBinding("slotIndex", T("int"), Unsigned: true),
                        },
                    },
                },
                Natives = new[]
                {
                    new NativeBinding
                    {
                        FunctionName = "GetSampleWornItem",
                        PapyrusClass = "SampleActor",
                        CppBaseType = "Actor",
                        ReturnType = T("SampleWornItem"),
                        Parameters = new[] { Param("aiSlot", "int", unsigned: true), Param("abFirst", "bool", "false") },
                        CppFunctionName = "GetSampleWornItem",
                    },
                    new NativeBinding
                    {
                        FunctionName = "GetTargets",
                        PapyrusClass = "SampleActor",
                        CppBaseType = "Actor",
                        ReturnType = T("ObjectReference[]"),
                        NoWait = true,
                        CppFunctionName = "GetTargets",
                    },
                },
            },
            new ModuleBinding
            {
                Name = "Util",
                ScriptName = "SampleUtil",
                Natives = new[]
                {
                    new NativeBinding
                    {
                        FunctionName = "Describe",
                        PapyrusClass = "SampleUtil",
                        CppBaseType = F4SETypeMap.StaticFunctionTag,
                        ReturnType = T("string"),
                        Parameters = new[] { Param("akForm", "Form") },
                        CppFunctionName = "Describe",
                    },
                    new NativeBinding
                    {
                        FunctionName = "WaitFor",
                        PapyrusClass = "SampleUtil",
                        CppBaseType = F4SETypeMap.StaticFunctionTag,
                        ReturnType = T("bool"),
                        Parameters = new[] { Param("afSeconds", "float") },
                        IsLatent = true,
                        CppFunctionName = "WaitFor",
                    },
                },
            },
        },
        Messages = new[] { new MessageSubscription("kMessage_GameDataReady", "OnGameDataReady") },
    };

    private static F4SEEmitResult EmitSample(F4SETarget target = F4SETarget.Og_1_10_163)
    {
        var result = new F4SEEmitter().Emit(Sample(target));
        result.Errors.Should().BeEmpty(string.Join(" | ", result.Diagnostics));
        return result;
    }

    private static string TextOf(F4SEEmitResult result, string path)
    {
        var file = result.File(path);
        file.Should().NotBeNull($"{path} should be emitted; got {string.Join(", ", result.Files.Select(f => f.RelativePath))}");
        return file!.Text;
    }



    [Fact]
    public void Every_expected_file_is_emitted()
    {
        var result = EmitSample();

        result.Files.Select(f => f.RelativePath).Should().BeEquivalentTo(new[]
        {
            "src/PapyrusSamplePluginActor.h",
            "src/PapyrusSamplePluginActor.cpp",
            "Data/Scripts/Source/User/SampleActor.psc",
            "src/PapyrusSamplePluginUtil.h",
            "src/PapyrusSamplePluginUtil.cpp",
            "Data/Scripts/Source/User/SampleUtil.psc",
            "src/SamplePluginRegistrations.h",
            "src/SamplePluginRegistrations.cpp",
            "src/main.cpp",
            "CMakeLists.txt",
            "README.md",
        });
    }

    [Fact]
    public void The_emitter_writes_nothing_to_disk()
    {

        var before = Environment.CurrentDirectory;
        EmitSample();
        Environment.CurrentDirectory.Should().Be(before);
        EmitSample().Files.Should().OnlyContain(f => f.Text.Length > 0);
    }

    [Fact]
    public void A_registration_matches_the_shape_the_runtime_expects()
    {
        var source = TextOf(EmitSample(), "src/PapyrusSamplePluginActor.cpp");

        source.Should().Contain(
            "new NativeFunction2<Actor, SampleWornItem, UInt32, bool>(\"GetSampleWornItem\", \"SampleActor\", "
            + "papyrusSamplePluginActor::GetSampleWornItem, vm)");
    }

    [Fact]
    public void A_global_registers_against_the_receiver_tag_and_takes_it_as_the_first_parameter()
    {
        var source = TextOf(EmitSample(), "src/PapyrusSamplePluginUtil.cpp");

        source.Should().Contain("new NativeFunction1<StaticFunctionTag, BSFixedString, TESForm*>(\"Describe\"");
        source.Should().Contain("BSFixedString Describe(StaticFunctionTag * base, TESForm* akForm)");
    }

    [Fact]
    public void A_latent_binding_uses_the_latent_template()
    {
        TextOf(EmitSample(), "src/PapyrusSamplePluginUtil.cpp")
            .Should().Contain("new LatentNativeFunction1<StaticFunctionTag, bool, float>(\"WaitFor\"");
    }

    [Fact]
    public void No_wait_is_emitted_as_a_separate_flag_statement_after_the_registrations()
    {
        var source = TextOf(EmitSample(), "src/PapyrusSamplePluginActor.cpp");

        source.Should().Contain(
            "vm->SetFunctionFlags(\"SampleActor\", \"GetTargets\", IFunction::kFunctionFlag_NoWait);");
        source.IndexOf("SetFunctionFlags", StringComparison.Ordinal)
            .Should().BeGreaterThan(source.LastIndexOf("RegisterFunction", StringComparison.Ordinal),
                "flags are applied after every registration, as the shipped source does");
    }

    [Fact]
    public void A_struct_is_declared_in_the_cpp_and_externed_in_the_header()
    {
        var result = EmitSample();

        TextOf(result, "src/PapyrusSamplePluginActor.cpp")
            .Should().Contain("DECLARE_STRUCT(SampleWornItem, \"SampleActor\")");
        TextOf(result, "src/PapyrusSamplePluginActor.h")
            .Should().Contain("DECLARE_EXTERN_STRUCT(SampleWornItem)");
    }

    [Fact]
    public void A_stub_body_is_guarded_so_it_cannot_ship_unimplemented()
    {
        var source = TextOf(EmitSample(), "src/PapyrusSamplePluginUtil.cpp");

        source.Should().Contain($"#ifndef {F4SECppEmitter.AllowStubsMacro}");
        source.Should().Contain("static_assert(false,");
        source.Should().Contain($"{F4SECppEmitter.UnimplementedMacro}(\"SampleUtil.Describe\");");
        source.Should().Contain("return BSFixedString();");
    }



    [Fact]
    public void The_emitted_script_declares_the_natives_with_their_defaults()
    {
        var script = TextOf(EmitSample(), "Data/Scripts/Source/User/SampleActor.psc");

        script.Should().Contain("Scriptname SampleActor extends Actor Native Hidden");
        script.Should().Contain(
            "SampleWornItem Function GetSampleWornItem(int aiSlot, bool abFirst = false) native");
        script.Should().Contain("ObjectReference[] Function GetTargets() native");
        script.Should().NotContain("latent", "no .psc can express latency");
        script.Should().NotContain("NoWait", "no .psc can express the NoWait flag");
    }

    [Fact]
    public void A_globals_only_module_extends_nothing_and_declares_its_functions_global()
    {
        var script = TextOf(EmitSample(), "Data/Scripts/Source/User/SampleUtil.psc");

        script.Should().Contain("Scriptname SampleUtil Native Hidden");
        script.Should().NotContain("extends");
        script.Should().Contain("string Function Describe(Form akForm) native global");
    }

    [Fact]
    public void A_struct_owned_by_another_script_is_written_owner_qualified()
    {


        var plugin = Sample() with
        {
            Modules = Sample().Modules.Select(m => m.Name != "Util" ? m : m with
            {
                Natives = m.Natives.Append(new NativeBinding
                {
                    FunctionName = "FirstWorn",
                    PapyrusClass = "SampleUtil",
                    CppBaseType = F4SETypeMap.StaticFunctionTag,
                    ReturnType = T("SampleWornItem"),
                    CppFunctionName = "FirstWorn",
                }).ToList(),
            }).ToList(),
        };

        var result = new F4SEEmitter().Emit(plugin);
        result.Errors.Should().BeEmpty();

        TextOf(result, "Data/Scripts/Source/User/SampleUtil.psc")
            .Should().Contain("SampleActor:SampleWornItem Function FirstWorn() native global");


        TextOf(result, "Data/Scripts/Source/User/SampleActor.psc")
            .Should().Contain("SampleWornItem Function GetSampleWornItem");
    }

    [Fact]
    public void The_emitted_scripts_compile_to_pex_with_the_built_in_compiler()
    {


        var result = EmitSample();
        var directory = System.IO.Directory.CreateTempSubdirectory("fo4re-f4se-emit-");
        try
        {
            foreach (var file in result.Files.Where(f => f.RelativePath.EndsWith(".psc", StringComparison.OrdinalIgnoreCase)))
            {
                System.IO.File.WriteAllText(
                    System.IO.Path.Combine(directory.FullName, System.IO.Path.GetFileName(file.RelativePath)),
                    file.Text);
            }

            var index = PapyrusCompiler.IndexFor(new[] { directory.FullName, TestRoots.BaseStubs });
            var compiler = new PapyrusCompiler(index);

            foreach (var path in System.IO.Directory.GetFiles(directory.FullName, "*.psc"))
            {
                var compiled = compiler.CompileFile(path);
                compiled.Success.Should().BeTrue(
                    $"{System.IO.Path.GetFileName(path)} should compile; diagnostics: "
                    + string.Join(" | ", compiled.Diagnostics.Select(d => $"{d.Code} {d.Message}")));
            }
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }



    [Fact]
    public void The_original_target_exports_the_query_entry_point_and_checks_the_runtime()
    {
        var main = TextOf(EmitSample(F4SETarget.Og_1_10_163), "src/main.cpp");

        main.Should().Contain("bool F4SEPlugin_Query(const F4SEInterface * f4se, PluginInfo * info)");
        main.Should().Contain("f4se->runtimeVersion != CURRENT_RELEASE_RUNTIME");
        main.Should().NotContain("F4SEPluginVersionData");
    }

    [Fact]
    public void The_next_generation_target_exports_the_version_data_instead()
    {


        var main = TextOf(EmitSample(F4SETarget.Ng_0_7_8), "src/main.cpp");

        main.Should().Contain("__declspec(dllexport) F4SEPluginVersionData F4SEPlugin_Version");
        main.Should().NotContain("F4SEPlugin_Query");
    }

    [Fact]
    public void Both_targets_register_the_papyrus_functions_and_refuse_when_that_fails()
    {
        foreach (var target in new[] { F4SETarget.Og_1_10_163, F4SETarget.Ng_0_7_8 })
        {
            var main = TextOf(EmitSample(target), "src/main.cpp");
            main.Should().Contain("bool F4SEPlugin_Load(const F4SEInterface * f4se)");
            main.Should().Contain("g_papyrus->Register(RegisterAllFuncs)");
            main.Should().Contain("return false;");
        }
    }

    [Fact]
    public void A_declared_message_subscription_becomes_a_dispatch_case()
    {
        var main = TextOf(EmitSample(), "src/main.cpp");

        main.Should().Contain("case F4SEMessagingInterface::kMessage_GameDataReady:");
        main.Should().Contain("OnGameDataReady(message);");
        main.Should().Contain("g_messaging->RegisterListener(g_pluginHandle, \"F4SE\", OnF4SEMessage);");
    }

    [Fact]
    public void The_aggregator_calls_every_module_so_the_shell_never_changes()
    {
        var registrations = TextOf(EmitSample(), "src/SamplePluginRegistrations.cpp");

        registrations.Should().Contain("papyrusSamplePluginActor::RegisterFuncs(vm);");
        registrations.Should().Contain("papyrusSamplePluginUtil::RegisterFuncs(vm);");
        registrations.Should().Contain("return true;");
    }

    [Fact]
    public void The_build_file_lists_every_emitted_translation_unit()
    {
        var cmake = TextOf(EmitSample(), "CMakeLists.txt");

        cmake.Should().Contain("src/main.cpp");
        cmake.Should().Contain("src/PapyrusSamplePluginActor.cpp");
        cmake.Should().Contain("src/PapyrusSamplePluginUtil.cpp");
        cmake.Should().Contain("src/SamplePluginRegistrations.cpp");
        cmake.Should().Contain("find_package(f4se REQUIRED CONFIG)");
    }



    [Fact]
    public void Emitted_cpp_read_back_agrees_with_the_emitted_papyrus()
    {

        var plugin = Sample();
        var result = new F4SEEmitter().Emit(plugin);
        var crossCheck = F4SEEmitter.RoundTrip(plugin, result);

        crossCheck.Mismatches.Should().BeEmpty(string.Join(" | ", crossCheck.Mismatches));
        crossCheck.CppOnly.Should().BeEmpty();
        crossCheck.PscOnly.Should().BeEmpty();
        crossCheck.Matched.Should().Be(plugin.AllNatives.Count());
    }

    [Fact]
    public void Reading_back_recovers_latency_and_flags_that_the_papyrus_cannot_carry()
    {
        var plugin = Sample();
        var result = new F4SEEmitter().Emit(plugin);

        var recovered = result.Files
            .Where(f => f.RelativePath.EndsWith(".cpp", StringComparison.OrdinalIgnoreCase))
            .SelectMany(f => new F4SERegistrationExtractor()
                .Extract(f.Text, f.RelativePath, plugin.AllStructs.Select(s => s.Name)).Natives)
            .ToList();

        recovered.Should().ContainSingle(n => n.IsLatent).Which.FunctionName.Should().Be("WaitFor");
        recovered.Should().ContainSingle(n => n.NoWait).Which.FunctionName.Should().Be("GetTargets");
    }



    [Fact]
    public void Emitted_cpp_has_balanced_braces()
    {
        foreach (var file in EmitSample().Files.Where(f => f.RelativePath.EndsWith(".cpp") || f.RelativePath.EndsWith(".h")))
        {
            var text = F4SECppScanner.BlankComments(file.Text);
            text.Count(c => c == '{').Should().Be(text.Count(c => c == '}'), $"{file.RelativePath} braces");
            text.Count(c => c == '(').Should().Be(text.Count(c => c == ')'), $"{file.RelativePath} parentheses");
        }
    }

    [Fact]
    public void Every_include_is_one_the_type_map_knows_about()
    {
        var allowed = F4SETypeMap.CoreIncludes
            .Concat(F4SETypeMap.GameIncludes)
            .Concat(new[] { "f4se/PapyrusStruct.h", "f4se/PluginAPI.h", "f4se_common/f4se_version.h" })
            .ToHashSet(StringComparer.Ordinal);

        foreach (var file in EmitSample().Files.Where(f => f.RelativePath.EndsWith(".cpp") || f.RelativePath.EndsWith(".h")))
        {
            foreach (System.Text.RegularExpressions.Match match in
                     System.Text.RegularExpressions.Regex.Matches(file.Text, @"#include\s+""(?<path>[^""]+)"""))
            {
                var path = match.Groups["path"].Value;
                if (path.StartsWith("f4se", StringComparison.Ordinal))
                    allowed.Should().Contain(path, $"{file.RelativePath} includes {path}");
            }
        }
    }

    [Fact]
    public void Every_registration_names_a_function_the_same_file_defines()
    {
        foreach (var file in EmitSample().Files.Where(f => f.RelativePath.EndsWith(".cpp")))
        {
            var recovered = new F4SERegistrationExtractor()
                .Extract(file.Text, file.RelativePath, new[] { "SampleWornItem" }).Natives;

            foreach (var native in recovered)
            {
                var bare = native.CppFunctionName.Split(':').Last();
                file.Text.Should().Contain($" {bare}(", $"{file.RelativePath} should define {bare}");
            }
        }
    }

    [Fact]
    public void Every_body_marker_is_balanced_and_unique()
    {
        foreach (var file in EmitSample().Files.Where(f => f.RelativePath.EndsWith(".cpp")))
        {
            var begins = file.Text.Split(F4SERegionMerge.BeginPrefix).Length - 1;
            var ends = file.Text.Split(F4SERegionMerge.EndPrefix).Length - 1;
            begins.Should().Be(ends, $"{file.RelativePath} body markers");

            var regions = F4SERegionMerge.Read(file.Text);
            regions.Count.Should().Be(begins, $"{file.RelativePath} region names should be unique");
        }
    }



    [Fact]
    public void A_duplicate_registration_is_refused_rather_than_emitted()
    {
        var duplicated = Sample() with
        {
            Modules = new[]
            {
                Sample().Modules[0] with
                {
                    Natives = Sample().Modules[0].Natives
                        .Concat(new[] { Sample().Modules[0].Natives[0] }).ToList(),
                },
            },
        };

        var result = new F4SEEmitter().Emit(duplicated);

        result.Files.Should().BeEmpty();
        result.Errors.Should().ContainSingle()
            .Which.Code.Should().Be(GraphDiagnosticCodes.DuplicateNativeBinding);
    }

    [Fact]
    public void An_unmappable_parameter_type_is_refused()
    {
        var broken = Sample() with
        {
            Modules = new[]
            {
                new ModuleBinding
                {
                    Name = "Broken",
                    ScriptName = "SampleBroken",
                    Natives = new[]
                    {
                        new NativeBinding
                        {
                            FunctionName = "Odd",
                            PapyrusClass = "SampleBroken",
                            CppBaseType = F4SETypeMap.StaticFunctionTag,
                            Parameters = new[] { Param("akThing", "SomeModsScript") },
                            CppFunctionName = "Odd",
                        },
                    },
                },
            },
        };

        var result = new F4SEEmitter().Emit(broken);

        result.Files.Should().BeEmpty();
        result.Errors.Should().ContainSingle().Which.Code.Should().Be(GraphDiagnosticCodes.UnmappedNativeType);
    }

    [Fact]
    public void An_arity_beyond_the_template_family_is_refused()
    {
        var wide = Sample() with
        {
            Modules = new[]
            {
                new ModuleBinding
                {
                    Name = "Wide",
                    ScriptName = "SampleWide",
                    Natives = new[]
                    {
                        new NativeBinding
                        {
                            FunctionName = "TooMany",
                            PapyrusClass = "SampleWide",
                            CppBaseType = F4SETypeMap.StaticFunctionTag,
                            Parameters = Enumerable.Range(0, F4SEBindingValidator.MaximumArity + 1)
                                .Select(i => Param($"a{i}", "int")).ToList(),
                            CppFunctionName = "TooMany",
                        },
                    },
                },
            },
        };

        new F4SEEmitter().Emit(wide).Errors
            .Should().ContainSingle().Which.Code.Should().Be(GraphDiagnosticCodes.NativeArityUnsupported);
    }

    [Fact]
    public void A_required_parameter_after_an_optional_one_is_refused()
    {
        var awkward = Sample() with
        {
            Modules = new[]
            {
                new ModuleBinding
                {
                    Name = "Awkward",
                    ScriptName = "SampleAwkward",
                    Natives = new[]
                    {
                        new NativeBinding
                        {
                            FunctionName = "Mixed",
                            PapyrusClass = "SampleAwkward",
                            CppBaseType = F4SETypeMap.StaticFunctionTag,
                            Parameters = new[] { Param("a", "int", "0"), Param("b", "int") },
                            CppFunctionName = "Mixed",
                        },
                    },
                },
            },
        };

        new F4SEEmitter().Emit(awkward).Errors
            .Should().ContainSingle().Which.Code.Should().Be(GraphDiagnosticCodes.ArgumentCount);
    }

    [Fact]
    public void A_struct_owned_by_no_emitted_script_is_refused()
    {
        var orphaned = Sample() with
        {
            Modules = new[]
            {
                new ModuleBinding
                {
                    Name = "Orphan",
                    ScriptName = "SampleOrphan",
                    Structs = new[]
                    {
                        new StructBinding { Name = "Thing", OwnerScript = "NotEmittedAnywhere" },
                    },
                },
            },
        };

        new F4SEEmitter().Emit(orphaned).Errors
            .Should().ContainSingle().Which.Code.Should().Be(GraphDiagnosticCodes.StructOwnerMismatch);
    }
}
