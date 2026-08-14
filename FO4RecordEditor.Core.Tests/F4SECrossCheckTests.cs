using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using FO4RecordEditor.Services.Graph.F4SE;

namespace FO4RecordEditor.Core.Tests;

/// <summary>
/// Comparing recovered C++ registrations against Papyrus declarations of the same functions.
/// </summary>
public class F4SECrossCheckTests
{
    private static NativeBinding Binding(
        string cls, string name, string ret = "None", string? cppBase = null, params string[] parameters) =>
        new()
        {
            FunctionName = name,
            PapyrusClass = cls,
            CppBaseType = cppBase ?? "TESForm",
            ReturnType = Type(ret),
            Parameters = parameters.Select((p, i) => new NativeParameter($"a{i}", Type(p))).ToList(),
            CppFunctionName = "papyrusX::" + name,
        };

    private static OracleNative Native(
        string cls, string name, string ret = "None", bool global = false, params string[] parameters) =>
        new(cls, name, Type(ret), parameters.Select(Type).ToList(), global);

    private static PapyrusTypeText Type(string written) =>
        written.EndsWith("[]", StringComparison.Ordinal)
            ? new PapyrusTypeText(written[..^2], true)
            : new PapyrusTypeText(written);

    private static OracleNative[] OracleOf(params OracleNative[] natives) => natives;

    [Fact]
    public void Matching_signatures_agree()
    {
        var result = F4SECrossCheck.Compare(
            new[] { Binding("Actor", "IsProtected", "bool") },
            OracleOf(Native("Actor", "IsProtected", "bool")));

        result.Agrees.Should().BeTrue();
        result.Matched.Should().Be(1);
    }

    [Fact]
    public void An_owner_qualified_struct_name_matches_the_bare_cpp_typedef()
    {
        // The shipped ObjectReference.ApplyMaterialSwap returns MatSwap:RemapData[] against a C++
        // VMArray<RemapData>. Both spellings are correct, and treating that as a mismatch would
        // report a defect that is not there.
        var result = F4SECrossCheck.Compare(
            new[] { Binding("ObjectReference", "ApplyMaterialSwap", "RemapData[]") },
            OracleOf(Native("ObjectReference", "ApplyMaterialSwap", "MatSwap:RemapData[]")));

        result.Mismatches.Should().BeEmpty();
    }

    [Fact]
    public void Two_different_qualified_struct_names_still_disagree()
    {
        var result = F4SECrossCheck.Compare(
            new[] { Binding("X", "F", "A:Thing") },
            OracleOf(Native("X", "F", "B:Thing")));

        result.Mismatches.Should().ContainSingle().Which.What.Should().Be("return");
    }

    [Fact]
    public void A_differing_arity_is_reported_and_stops_further_comparison()
    {
        var result = F4SECrossCheck.Compare(
            new[] { Binding("Actor", "F", "None", null, "int") },
            OracleOf(Native("Actor", "F")));

        result.Mismatches.Should().ContainSingle().Which.What.Should().Be("arity");
    }

    [Fact]
    public void A_differing_parameter_type_is_reported_by_position()
    {
        var result = F4SECrossCheck.Compare(
            new[] { Binding("Actor", "F", "None", null, "int", "float") },
            OracleOf(Native("Actor", "F", "None", false, "int", "bool")));

        result.Mismatches.Should().ContainSingle().Which.What.Should().Be("parameter 1");
    }

    [Fact]
    public void A_differing_global_flag_is_reported()
    {
        var result = F4SECrossCheck.Compare(
            new[] { Binding("Game", "GetPlayer", "Actor", F4SETypeMap.StaticFunctionTag) },
            OracleOf(Native("Game", "GetPlayer", "Actor", global: false)));

        result.Mismatches.Should().ContainSingle().Which.What.Should().Be("global");
    }

    [Fact]
    public void A_registration_with_no_declaration_is_reported_as_cpp_only()
    {
        // F4SE.TestInventoryFunc is a real instance of this: registered in PapyrusF4SE.cpp and
        // declared in no .psc at all.
        var result = F4SECrossCheck.Compare(
            new[] { Binding("F4SE", "TestInventoryFunc", "None", F4SETypeMap.StaticFunctionTag, "ObjectReference") },
            OracleOf(Native("F4SE", "GetVersion", "int", global: true)));

        result.CppOnly.Should().ContainSingle().Which.FunctionName.Should().Be("TestInventoryFunc");
        result.PscOnly.Should().ContainSingle().Which.Name.Should().Be("GetVersion");
        result.Agrees.Should().BeFalse();
    }

    [Fact]
    public void Latency_and_no_wait_are_counted_but_never_compared()
    {
        // Neither can be expressed in a .psc, so a latent registration against a plain declaration
        // has to agree. Claiming this check covers them would be overstating it.
        var latent = Binding("UI", "Set", "bool", F4SETypeMap.StaticFunctionTag, "string")
            with { IsLatent = true, NoWait = true };

        // The real UI.psc declares this native global, matching the StaticFunctionTag receiver.
        var result = F4SECrossCheck.Compare(
            new[] { latent },
            OracleOf(Native("UI", "Set", "bool", global: true, "string")));

        result.Mismatches.Should().BeEmpty();
        result.LatentOnlyInCpp.Should().Be(1);
        result.NoWaitOnlyInCpp.Should().Be(1);
    }

    [Fact]
    public void Class_and_function_names_match_case_insensitively()
    {
        var result = F4SECrossCheck.Compare(
            new[] { Binding("actor", "isprotected", "bool") },
            OracleOf(Native("Actor", "IsProtected", "bool")));

        result.Matched.Should().Be(1);
        result.Agrees.Should().BeTrue();
    }
}
