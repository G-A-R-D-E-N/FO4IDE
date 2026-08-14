using System.Linq;
using FluentAssertions;
using FO4RecordEditor.Services.Graph.F4SE;

namespace FO4RecordEditor.Core.Tests;










public class F4SETypeMapTests
{
    private static readonly F4SETypeMap Map = new(new[] { "WornItem", "Owner", "PluginInfo" });

    private static PapyrusTypeText P(string name, bool array = false) => new(name, array);



    [Theory]
    [InlineData("bool", "bool")]
    [InlineData("TESForm*", "TESForm*")]
    [InlineData("TESObjectMISC *", "TESObjectMISC*")]
    [InlineData("  BSFixedString  ", "BSFixedString")]
    [InlineData("VMArray<TESForm*>", "VMArray<TESForm*>")]
    [InlineData("VMArray< BGSKeyword* >", "VMArray<BGSKeyword*>")]
    [InlineData("BGSMod::Attachment::Mod*", "BGSMod::Attachment::Mod*")]
    [InlineData("VMArray<BGSMod::Attachment::Mod*>", "VMArray<BGSMod::Attachment::Mod*>")]
    public void A_signature_type_parses_and_formats_back(string text, string expected)
    {
        CppTypeRef.TryParse(text, out var type).Should().BeTrue();
        type!.Format().Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("TESForm**")]
    [InlineData("VMArray<")]
    public void A_type_the_marshaller_cannot_carry_is_refused(string text)
    {
        CppTypeRef.TryParse(text, out _).Should().BeFalse();
    }



    [Theory]
    [InlineData("bool", "bool")]
    [InlineData("float", "float")]
    [InlineData("string", "BSFixedString")]
    [InlineData("var", "VMVariable")]
    [InlineData("None", "void")]
    [InlineData("Form", "TESForm*")]
    [InlineData("ObjectReference", "TESObjectREFR*")]
    [InlineData("Actor", "Actor*")]
    [InlineData("ActorBase", "TESNPC*")]
    [InlineData("ActorValue", "ActorValueInfo*")]
    [InlineData("Keyword", "BGSKeyword*")]
    [InlineData("FormList", "BGSListForm*")]
    [InlineData("GlobalVariable", "TESGlobal*")]
    [InlineData("ObjectMod", "BGSMod::Attachment::Mod*")]
    [InlineData("MatSwap", "BGSMaterialSwap*")]
    [InlineData("MiscObject", "TESObjectMISC*")]
    [InlineData("Spell", "SpellItem*")]
    [InlineData("Enchantment", "EnchantmentItem*")]
    [InlineData("LeveledItem", "TESLevItem*")]
    [InlineData("WaterType", "TESWaterForm*")]
    [InlineData("ScriptObject", "VMObject")]
    public void A_papyrus_type_maps_to_the_cpp_type_f4se_uses(string papyrus, string expected)
    {
        Map.TryToCpp(P(papyrus), unsigned: false, out var cpp, out var refusal)
            .Should().BeTrue(refusal);
        cpp!.Format().Should().Be(expected);
    }

    [Fact]
    public void Int_picks_its_signedness_from_the_caller()
    {
        Map.TryToCpp(P("int"), unsigned: false, out var signed, out _).Should().BeTrue();
        signed!.Format().Should().Be("SInt32");

        Map.TryToCpp(P("int"), unsigned: true, out var unsigned, out _).Should().BeTrue();
        unsigned!.Format().Should().Be("UInt32");
    }

    [Fact]
    public void An_array_maps_through_its_element_type()
    {
        Map.TryToCpp(P("Form", array: true), unsigned: false, out var forms, out _).Should().BeTrue();
        forms!.Format().Should().Be("VMArray<TESForm*>");

        Map.TryToCpp(P("string", array: true), unsigned: false, out var strings, out _).Should().BeTrue();
        strings!.Format().Should().Be("VMArray<BSFixedString>");

        Map.TryToCpp(P("var", array: true), unsigned: false, out var vars, out _).Should().BeTrue();
        vars!.Format().Should().Be("VMArray<VMVariable>");
    }

    [Fact]
    public void A_declared_struct_marshals_by_value_under_its_own_name()
    {
        Map.TryToCpp(P("WornItem"), unsigned: false, out var worn, out var refusal)
            .Should().BeTrue(refusal);
        worn!.Format().Should().Be("WornItem");

        Map.TryToCpp(P("PluginInfo", array: true), unsigned: false, out var plugins, out _).Should().BeTrue();
        plugins!.Format().Should().Be("VMArray<PluginInfo>");
    }

    [Fact]
    public void An_undeclared_type_is_refused_rather_than_invented()
    {
        Map.TryToCpp(P("SomeModsCustomScript"), unsigned: false, out var cpp, out var refusal)
            .Should().BeFalse();
        cpp.Should().BeNull();
        refusal.Should().Contain("not a marshallable type");
    }

    [Fact]
    public void None_has_no_array_form()
    {
        Map.TryToCpp(P("None", array: true), unsigned: false, out _, out var refusal).Should().BeFalse();
        refusal.Should().Contain("None");
    }



    [Theory]
    [InlineData("bool", "bool")]
    [InlineData("float", "float")]
    [InlineData("BSFixedString", "string")]
    [InlineData("VMVariable", "Var")]
    [InlineData("void", "None")]
    [InlineData("SInt32", "int")]
    [InlineData("UInt32", "int")]
    [InlineData("int", "int")]
    [InlineData("TESForm*", "Form")]
    [InlineData("TESObjectREFR*", "ObjectReference")]
    [InlineData("VMRefOrInventoryObj", "ObjectReference")]
    [InlineData("VMObject", "ScriptObject")]
    [InlineData("BGSMod::Attachment::Mod*", "ObjectMod")]
    [InlineData("TESObjectMISC *", "MiscObject")]
    [InlineData("VMArray<TESForm*>", "Form[]")]
    [InlineData("VMArray<BSFixedString>", "string[]")]
    [InlineData("VMArray<VMVariable>", "Var[]")]
    public void A_cpp_type_maps_back_to_the_papyrus_type_it_carries(string cpp, string expected)
    {
        CppTypeRef.TryParse(cpp, out var parsed).Should().BeTrue();
        Map.TryToPapyrus(parsed, out var papyrus, out var refusal).Should().BeTrue(refusal);
        papyrus.ToString().Should().Be(expected);
    }

    [Fact]
    public void The_receiver_tag_is_not_a_value_type()
    {
        Map.TryToPapyrus(new CppTypeRef(F4SETypeMap.StaticFunctionTag), out _, out var refusal)
            .Should().BeFalse();
        refusal.Should().Contain("receiver tag");
    }

    [Fact]
    public void An_unnamed_form_pointer_is_refused_rather_than_guessed_from_its_spelling()
    {


        CppTypeRef.TryParse("TESObjectTREE*", out var tree).Should().BeTrue();
        Map.TryToPapyrus(tree, out _, out var refusal).Should().BeFalse();
        refusal.Should().Contain("the form table does not name");
    }

    [Fact]
    public void A_jagged_array_has_no_papyrus_type()
    {
        var jagged = CppTypeRef.ArrayOf(CppTypeRef.ArrayOf(new CppTypeRef("TESForm", true)));
        Map.TryToPapyrus(jagged, out _, out var refusal).Should().BeFalse();
        refusal.Should().Contain("jagged");
    }



    [Theory]
    [InlineData("bool")]
    [InlineData("float")]
    [InlineData("string")]
    [InlineData("var")]
    [InlineData("None")]
    [InlineData("int")]
    [InlineData("Form")]
    [InlineData("ObjectReference")]
    [InlineData("Actor")]
    [InlineData("ObjectMod")]
    [InlineData("ScriptObject")]
    [InlineData("WornItem")]
    public void Papyrus_to_cpp_and_back_is_the_identity(string papyrus)
    {
        Map.TryToCpp(P(papyrus), unsigned: false, out var cpp, out var refusal).Should().BeTrue(refusal);
        Map.TryToPapyrus(cpp, out var back, out refusal).Should().BeTrue(refusal);
        back.Name.Should().BeEquivalentTo(papyrus);
        back.IsArray.Should().BeFalse();
    }

    [Theory]
    [InlineData("Form")]
    [InlineData("string")]
    [InlineData("var")]
    [InlineData("Keyword")]
    [InlineData("PluginInfo")]
    public void An_array_round_trips_too(string element)
    {
        Map.TryToCpp(P(element, array: true), unsigned: false, out var cpp, out var refusal)
            .Should().BeTrue(refusal);
        Map.TryToPapyrus(cpp, out var back, out refusal).Should().BeTrue(refusal);
        back.Name.Should().BeEquivalentTo(element);
        back.IsArray.Should().BeTrue();
    }

    [Fact]
    public void Both_int_spellings_collapse_to_one_papyrus_type()
    {
        foreach (var spelling in new[] { "SInt32", "UInt32", "int" })
        {
            Map.TryToPapyrus(new CppTypeRef(spelling), out var papyrus, out _).Should().BeTrue();
            papyrus.Name.Should().Be("int");
        }
    }

    [Fact]
    public void Every_form_table_row_maps_both_ways()
    {
        foreach (var (papyrus, cpp) in F4SETypeMap.FormTypes)
        {
            Map.TryToCpp(P(papyrus), unsigned: false, out var forward, out var refusal)
                .Should().BeTrue($"{papyrus} should map forward: {refusal}");
            forward!.Name.Should().Be(cpp);

            Map.TryToPapyrus(forward, out var back, out refusal)
                .Should().BeTrue($"{cpp} should map back: {refusal}");
            back.Name.Should().BeEquivalentTo(papyrus);
        }
    }



    [Fact]
    public void The_template_instantiation_matches_the_shipped_registration_shape()
    {

        F4SETypeMap.TemplateInstantiation(
            latent: false,
            arity: 2,
            cppBaseType: "Actor",
            returnType: new CppTypeRef("WornItem"),
            parameters: new[] { new CppTypeRef("UInt32"), new CppTypeRef("bool") })
            .Should().Be("NativeFunction2<Actor, WornItem, UInt32, bool>");
    }

    [Fact]
    public void A_global_instantiates_against_the_receiver_tag()
    {
        F4SETypeMap.TemplateInstantiation(
            latent: false,
            arity: 0,
            cppBaseType: F4SETypeMap.StaticFunctionTag,
            returnType: new CppTypeRef("TESObjectREFR", true),
            parameters: System.Array.Empty<CppTypeRef>())
            .Should().Be("NativeFunction0<StaticFunctionTag, TESObjectREFR*>");
    }

    [Fact]
    public void A_latent_function_instantiates_the_latent_template()
    {
        F4SETypeMap.TemplateInstantiation(
            latent: true,
            arity: 3,
            cppBaseType: F4SETypeMap.StaticFunctionTag,
            returnType: new CppTypeRef("bool"),
            parameters: new[]
            {
                new CppTypeRef("BSFixedString"), new CppTypeRef("BSFixedString"), new CppTypeRef("VMVariable"),
            })
            .Should().Be("LatentNativeFunction3<StaticFunctionTag, bool, BSFixedString, BSFixedString, VMVariable>");
    }

    [Fact]
    public void The_struct_header_is_included_only_when_a_struct_is_used()
    {
        Map.RequiredIncludesFor(new[] { new CppTypeRef("bool") })
            .Should().NotContain("f4se/PapyrusStruct.h");

        Map.RequiredIncludesFor(new[] { new CppTypeRef("WornItem") })
            .Should().Contain("f4se/PapyrusStruct.h");

        Map.RequiredIncludesFor(new[] { CppTypeRef.ArrayOf(new CppTypeRef("PluginInfo")) })
            .Should().Contain("f4se/PapyrusStruct.h");
    }

    [Fact]
    public void Game_headers_come_in_only_when_a_form_pointer_is_used()
    {
        Map.RequiredIncludesFor(new[] { new CppTypeRef("float") })
            .Should().NotContain("f4se/GameForms.h");

        Map.RequiredIncludesFor(new[] { new CppTypeRef("TESForm", true) })
            .Should().Contain("f4se/GameForms.h");
    }

    [Fact]
    public void The_core_includes_are_always_present_and_come_first()
    {
        var includes = Map.RequiredIncludesFor(new[] { new CppTypeRef("TESForm", true) });
        includes.Take(F4SETypeMap.CoreIncludes.Count).Should().Equal(F4SETypeMap.CoreIncludes);
    }
}
