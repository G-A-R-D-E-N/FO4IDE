using System.Linq;
using FluentAssertions;
using FO4RecordEditor.Services.Graph.F4SE;

namespace FO4RecordEditor.Core.Tests;









public class F4SEExtractorTests
{
    private static readonly F4SERegistrationExtractor Extractor = new();



    [Fact]
    public void The_compact_style_is_recovered()
    {

        var schema = Extractor.Extract("""
            void papyrusActor::RegisterFuncs(VirtualMachine* vm)
            {
                vm->RegisterFunction(
                    new NativeFunction2<Actor, WornItem, UInt32, bool>("GetWornItem", "Actor", papyrusActor::GetWornItem, vm));
            }
            """);

        schema.Problems.Should().BeEmpty();
        var fn = schema.Natives.Should().ContainSingle().Subject;
        fn.FunctionName.Should().Be("GetWornItem");
        fn.PapyrusClass.Should().Be("Actor");
        fn.CppBaseType.Should().Be("Actor");
        fn.CppFunctionName.Should().Be("papyrusActor::GetWornItem");
        fn.Arity.Should().Be(2);
        fn.IsGlobal.Should().BeFalse();
        fn.IsLatent.Should().BeFalse();
    }

    [Fact]
    public void The_spaced_style_with_a_static_tag_is_recovered()
    {

        var schema = Extractor.Extract("""
            vm->RegisterFunction(
                new NativeFunction0 <StaticFunctionTag, TESObjectREFR*>("GetCurrentConsoleRef", "Game", papyrusGame::GetCurrentConsoleRef, vm));
            """);

        schema.Problems.Should().BeEmpty();
        var fn = schema.Natives.Should().ContainSingle().Subject;
        fn.FunctionName.Should().Be("GetCurrentConsoleRef");
        fn.IsGlobal.Should().BeTrue("StaticFunctionTag is the receiver tag");
        fn.ReturnType.ToString().Should().Be("ObjectReference");
        fn.Arity.Should().Be(0);
    }

    [Fact]
    public void A_registration_split_across_lines_is_recovered()
    {

        var schema = Extractor.Extract("""
            vm->RegisterFunction(
                new NativeFunction1<
                    TESForm,
                    bool,
                    BGSKeyword*
                >(
                    "HasKeyword",
                    "Form",
                    papyrusForm::HasKeyword,
                    vm));
            """);

        schema.Problems.Should().BeEmpty();
        schema.Natives.Should().ContainSingle().Which.FunctionName.Should().Be("HasKeyword");
    }



    [Fact]
    public void A_nested_template_argument_is_split_correctly()
    {

        var schema = Extractor.Extract("""
            vm->RegisterFunction(new NativeFunction1<Actor, VMArray<BGSMod::Attachment::Mod*>, UInt32>("GetWornItemMods", "Actor", papyrusActor::GetWornItemMods, vm));
            """);

        schema.Problems.Should().BeEmpty();
        var fn = schema.Natives.Should().ContainSingle().Subject;
        fn.ReturnType.ToString().Should().Be("ObjectMod[]");
        fn.Arity.Should().Be(1);
        fn.Parameters[0].Type.ToString().Should().Be("int");
    }

    [Fact]
    public void A_scoped_cpp_type_survives_intact()
    {
        var schema = Extractor.Extract("""
            vm->RegisterFunction(new NativeFunction0<BGSMod::Attachment::Mod, BSFixedString>("GetName", "ObjectMod", papyrusObjectMod::GetName, vm));
            """);

        schema.Natives.Should().ContainSingle().Which.CppBaseType.Should().Be("BGSMod::Attachment::Mod");
    }

    [Fact]
    public void A_pointer_with_a_space_before_the_star_is_recovered()
    {

        var schema = Extractor.Extract("""
            vm->RegisterFunction(new NativeFunction0<StaticFunctionTag, TESObjectMISC *>("Get", "Thing", papyrusThing::Get, vm));
            """);

        schema.Problems.Should().BeEmpty();
        schema.Natives.Should().ContainSingle().Which.ReturnType.ToString().Should().Be("MiscObject");
    }



    [Fact]
    public void A_latent_registration_is_marked_latent()
    {


        var schema = Extractor.Extract("""
            vm->RegisterFunction(new LatentNativeFunction3<StaticFunctionTag, bool, BSFixedString, BSFixedString, VMVariable>("Set", "UI", papyrusUI::Set, vm));
            """);

        var fn = schema.Natives.Should().ContainSingle().Subject;
        fn.IsLatent.Should().BeTrue();
        fn.Arity.Should().Be(3);
    }

    [Fact]
    public void A_no_wait_flag_lands_on_the_function_it_names()
    {

        var schema = Extractor.Extract("""
            vm->RegisterFunction(new NativeFunction0<TESObjectREFR, void>("AttachWire", "ObjectReference", papyrusObjectReference::AttachWire, vm));
            vm->RegisterFunction(new NativeFunction0<TESObjectREFR, void>("Other", "ObjectReference", papyrusObjectReference::Other, vm));
            vm->SetFunctionFlags("ObjectReference", "AttachWire", IFunction::kFunctionFlag_NoWait);
            """);

        schema.Problems.Should().BeEmpty();
        schema.Natives.Single(n => n.FunctionName == "AttachWire").NoWait.Should().BeTrue();
        schema.Natives.Single(n => n.FunctionName == "Other").NoWait.Should().BeFalse();
    }

    [Fact]
    public void A_flag_naming_an_unregistered_function_is_reported_rather_than_ignored()
    {
        var schema = Extractor.Extract("""
            vm->SetFunctionFlags("ObjectReference", "NotHere", IFunction::kFunctionFlag_NoWait);
            """);

        schema.Problems.Should().ContainSingle().Which.Should().Contain("does not register");
    }

    [Theory]
    [InlineData("""DECLARE_STRUCT(WornItem, "Actor")""", "WornItem", "Actor")]
    [InlineData("""	DECLARE_STRUCT(Owner, "InstanceData")""", "Owner", "InstanceData")]
    [InlineData("""DECLARE_STRUCT(RemapData, "MatSwap");""", "RemapData", "MatSwap")]
    public void A_struct_declaration_is_recovered_with_its_owning_script(
        string source, string name, string owner)
    {
        var declared = Extractor.Extract(source).Structs.Should().ContainSingle().Subject;
        declared.Name.Should().Be(name);
        declared.OwnerScript.Should().Be(owner);
        declared.VmTypeName.Should().Be(owner + "#" + name);
    }



    [Fact]
    public void A_line_commented_registration_is_not_recovered()
    {
        var schema = Extractor.Extract(
            "/" + "/ vm->RegisterFunction(new NativeFunction0<Actor, bool>(\"Ghost\", \"Actor\", papyrusActor::Ghost, vm));\n"
            + "vm->RegisterFunction(new NativeFunction0<Actor, bool>(\"Real\", \"Actor\", papyrusActor::Real, vm));\n");

        schema.Natives.Should().ContainSingle().Which.FunctionName.Should().Be("Real");
    }

    [Fact]
    public void A_block_commented_registration_is_not_recovered()
    {
        var schema = Extractor.Extract(
            "/" + "*\n"
            + "vm->RegisterFunction(new NativeFunction0<Actor, bool>(\"Ghost\", \"Actor\", papyrusActor::Ghost, vm));\n"
            + "*" + "/\n"
            + "vm->RegisterFunction(new NativeFunction0<Actor, bool>(\"Real\", \"Actor\", papyrusActor::Real, vm));\n");

        schema.Natives.Should().ContainSingle().Which.FunctionName.Should().Be("Real");
    }

    [Fact]
    public void A_registration_named_inside_a_string_literal_is_not_recovered()
    {
        var schema = Extractor.Extract("""
            const char * help = "new NativeFunction0<Actor, bool>(\"Fake\", \"Actor\", f, vm)";
            """);

        schema.Natives.Should().BeEmpty();
    }

    [Fact]
    public void An_arity_that_disagrees_with_its_type_name_is_refused()
    {


        var schema = Extractor.Extract("""
            vm->RegisterFunction(new NativeFunction2<Actor, bool, UInt32>("Wrong", "Actor", papyrusActor::Wrong, vm));
            """);

        schema.Natives.Should().BeEmpty();
        schema.Problems.Should().ContainSingle().Which.Should().Contain("carries 1 parameter types");
    }



    [Fact]
    public void A_recovered_binding_carries_the_line_it_came_from()
    {
        var schema = Extractor.Extract(
            "/" + "/ one\n"
            + "/" + "/ two\n"
            + "vm->RegisterFunction(new NativeFunction0<Actor, bool>(\"Third\", \"Actor\", papyrusActor::Third, vm));\n");

        schema.Natives.Should().ContainSingle().Which.SourceLine.Should().Be(3);
    }

    [Fact]
    public void Blanking_comments_preserves_every_offset()
    {
        var source = "a /" + "* two *" + "/ b\n" + "/" + "/ tail\nc";
        var blanked = F4SECppScanner.BlankComments(source);

        blanked.Length.Should().Be(source.Length, "offsets have to keep matching the input");
        blanked.Count(c => c == '\n').Should().Be(2, "line numbering has to survive");
        blanked.Should().StartWith("a ").And.Contain("b").And.EndWith("c");
        blanked.Should().NotContain("two").And.NotContain("tail");
    }

    [Fact]
    public void A_struct_the_same_file_declares_is_mapped_in_signatures()
    {


        var schema = Extractor.Extract("""
            DECLARE_STRUCT(WornItem, "Actor")
            vm->RegisterFunction(new NativeFunction2<Actor, WornItem, UInt32, bool>("GetWornItem", "Actor", papyrusActor::GetWornItem, vm));
            """);

        schema.Problems.Should().BeEmpty();
        schema.Natives.Should().ContainSingle().Which.ReturnType.ToString().Should().Be("WornItem");
    }

    [Fact]
    public void A_struct_declared_in_another_translation_unit_is_mapped()
    {
        var schema = Extractor.Extract("""
            DECLARE_EXTERN_STRUCT(Owner)
            vm->RegisterFunction(new NativeFunction1<StaticFunctionTag, float, Owner>("GetAttackDamage", "InstanceData", papyrusInstanceData::GetAttackDamage, vm));
            """);

        schema.Problems.Should().BeEmpty();
        schema.Natives.Should().ContainSingle().Which.Parameters[0].Type.ToString().Should().Be("Owner");
        schema.Structs.Should().BeEmpty("an extern declaration names no owning script");
    }

    [Fact]
    public void A_struct_name_supplied_by_the_caller_is_mapped()
    {
        var schema = Extractor.Extract(
            """
            vm->RegisterFunction(new NativeFunction0<StaticFunctionTag, VMArray<PluginInfo>>("GetInstalledPlugins", "Game", papyrusGame::GetInstalledPlugins, vm));
            """,
            knownStructs: new[] { "PluginInfo" });

        schema.Natives.Should().ContainSingle().Which.ReturnType.ToString().Should().Be("PluginInfo[]");
    }

    [Fact]
    public void An_unmapped_type_is_kept_and_marked_rather_than_dropped()
    {
        var schema = Extractor.Extract("""
            vm->RegisterFunction(new NativeFunction1<Actor, bool, TESObjectTREE*>("Odd", "Actor", papyrusActor::Odd, vm));
            """);

        var fn = schema.Natives.Should().ContainSingle().Subject;
        fn.Parameters[0].Type.Name.Should().Be(F4SERegistrationExtractor.UnknownTypeName);
    }

    [Fact]
    public void A_less_than_that_is_a_comparison_does_not_swallow_the_file()
    {
        var schema = Extractor.Extract("""
            if (a < b) { doThing(); }
            vm->RegisterFunction(new NativeFunction0<Actor, bool>("Real", "Actor", papyrusActor::Real, vm));
            """);

        schema.Natives.Should().ContainSingle().Which.FunctionName.Should().Be("Real");
    }
}
