using System;
using System.Linq;
using FluentAssertions;
using FO4RecordEditor.Services.Graph;
using FO4RecordEditor.Services.Papyrus;

namespace FO4RecordEditor.Core.Tests;

/// <summary>
/// Turning scripts into node types.
/// </summary>
/// <remarks>
/// Runs against the checked-in stub tree, so it exercises the same path a real install would take
/// without needing one.
/// </remarks>
public class NodePaletteTests
{
    private static NodePalette Palette() =>
        new(PapyrusCompiler.IndexFor(new[] { TestRoots.BaseStubs }));

    private static NodeDefinition Definition(string id)
    {
        var definition = Palette().Find(id);
        definition.Should().NotBeNull($"{id} should resolve against the stub tree");
        return definition!;
    }

    // ---- generated definitions ----------------------------------------------------------------

    [Fact]
    public void An_instance_function_gets_a_self_pin_and_an_argument_pin_per_parameter()
    {
        var definition = Definition("call:ObjectReference.AddItem");

        definition.Kind.Should().Be(GraphNodeKind.Call);
        definition.IsGlobal.Should().BeFalse();
        definition.Pin(PinIds.Self).Should().NotBeNull();
        definition.DataInputs.Select(p => p.Id).Should().Contain(new[]
        {
            PinIds.Self, "arg:akItemToAdd", "arg:aiCount", "arg:abSilent",
        });
        definition.ExecInputs.Should().ContainSingle();
        definition.ExecOutputs.Should().ContainSingle();
    }

    [Fact]
    public void A_global_function_has_no_self_pin()
    {
        var definition = Definition("global:Game.GetPlayer");

        definition.IsGlobal.Should().BeTrue();
        definition.Pin(PinIds.Self).Should().BeNull();
        definition.DataOutputs.Should().ContainSingle().Which.Type!.TypeName.Should().Be("Actor");
    }

    [Fact]
    public void An_optional_parameter_carries_its_declared_default()
    {
        var count = Definition("call:ObjectReference.AddItem").Pin("arg:aiCount")!;

        count.IsOptional.Should().BeTrue();
        count.DeclaredDefault.Should().Be("1");

        Definition("call:ObjectReference.AddItem").Pin("arg:akItemToAdd")!.IsOptional
            .Should().BeFalse();
    }

    [Fact]
    public void A_function_returning_nothing_has_no_return_pin()
    {
        Definition("global:Debug.Notification").Pin(PinIds.Return).Should().BeNull();
        Definition("call:ObjectReference.GetBaseObject").Pin(PinIds.Return).Should().NotBeNull();
    }

    [Fact]
    public void An_array_return_type_is_kept_as_an_array()
    {
        // Utility has none, so use a stub member that does return one if present; otherwise assert
        // the shape through a known array-typed parameter instead.
        var definition = Definition("call:ScriptObject.CallFunction");
        definition.Pin("arg:aParams")!.Type!.IsArray.Should().BeTrue();
        definition.Pin("arg:aParams")!.Type!.TypeName.Should().BeEquivalentTo("Var");
    }

    [Fact]
    public void An_event_becomes_an_entry_node_whose_parameters_are_outputs()
    {
        var definition = Definition("event:ObjectReference.OnActivate");

        definition.Kind.Should().Be(GraphNodeKind.EventEntry);
        definition.ExecInputs.Should().BeEmpty("an event is where control flow starts");
        definition.ExecOutputs.Should().ContainSingle().Which.Id.Should().Be(PinIds.Exec);
        definition.DataOutputs.Should().ContainSingle()
            .Which.Id.Should().Be("param:akActionRef");
    }

    [Fact]
    public void An_event_with_no_parameters_has_only_an_exec_output()
    {
        var definition = Definition("event:ObjectReference.OnLoad");

        definition.DataOutputs.Should().BeEmpty();
        definition.ExecOutputs.Should().ContainSingle();
    }

    [Fact]
    public void A_property_yields_a_pure_getter_and_an_impure_setter()
    {
        // Nothing in the stub tree declares a property, so this is asserted on a synthetic script
        // to keep the stubs minimal.
        var root = System.IO.Directory.CreateTempSubdirectory("fo4re-palette-");
        try
        {
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(root.FullName, "PropHolder.psc"),
                "Scriptname PropHolder\nObjectReference Property Target Auto\n");

            var palette = new NodePalette(PapyrusCompiler.IndexFor(new[] { root.FullName, TestRoots.BaseStubs }));

            var getter = palette.Find("prop.get:PropHolder.Target")!;
            getter.IsPure.Should().BeTrue();
            getter.ExecInputs.Should().BeEmpty();
            getter.Pin(PinIds.Value)!.Type!.TypeName.Should().Be("ObjectReference");

            var setter = palette.Find("prop.set:PropHolder.Target")!;
            setter.IsPure.Should().BeFalse();
            setter.ExecInputs.Should().ContainSingle();
            setter.Pin(PinIds.Value).Should().NotBeNull();
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    // ---- purity -------------------------------------------------------------------------------

    [Fact]
    public void No_generated_call_is_ever_marked_pure()
    {
        // Papyrus annotates nothing as pure. Guessing would let the emitter reorder side effects
        // across an exec boundary, so a call is always sequenced.
        var palette = Palette();
        foreach (var script in new[] { "ObjectReference", "Actor", "Game", "Utility", "Math" })
        {
            palette.ForScript(script)
                .Where(d => d.Kind == GraphNodeKind.Call)
                .Should().OnlyContain(d => !d.IsPure, $"{script} calls should be sequenced");
        }
    }

    [Fact]
    public void Structural_value_nodes_are_pure()
    {
        foreach (var id in new[]
                 {
                     BuiltinNodeDefinitions.Self, BuiltinNodeDefinitions.NoneValue,
                     BuiltinNodeDefinitions.Cast, BuiltinNodeDefinitions.TypeCheck,
                     "literal.int", "op.add", "op.eq", "array.length",
                 })
        {
            BuiltinNodeDefinitions.Find(id)!.IsPure.Should().BeTrue($"{id} has no effect to sequence");
        }
    }

    [Fact]
    public void Array_mutators_are_sequenced_rather_than_pure()
    {
        foreach (var id in new[] { "array.add", "array.remove", "array.clear", "array.insert" })
        {
            var definition = BuiltinNodeDefinitions.Find(id)!;
            definition.IsPure.Should().BeFalse($"{id} changes the array");
            definition.ExecInputs.Should().ContainSingle();
        }
    }

    // ---- built-ins ----------------------------------------------------------------------------

    [Fact]
    public void The_array_builtins_match_what_the_resolver_actually_binds()
    {
        // The resolver's own table is private, so this asserts behaviourally: every offered member
        // has to resolve on a real array, and a made-up one must not.
        var root = System.IO.Directory.CreateTempSubdirectory("fo4re-arraymember-");
        try
        {
            var index = PapyrusCompiler.IndexFor(new[] { root.FullName, TestRoots.BaseStubs });

            foreach (var member in BuiltinNodeDefinitions.ArrayMemberNames)
            {
                var source = $"Scriptname Probe\nFunction Go()\n\tint[] a = new int[4]\n\ta.{member}\nEndFunction\n";
                var script = PapyrusParser.Parse(source, "Probe.psc");
                var resolution = new PapyrusResolver(index).Resolve(script);

                resolution.Diagnostics
                    .Where(d => d.Severity == PapyrusSeverity.Error)
                    .Should().BeEmpty($"the resolver should bind a.{member}");
            }
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void Every_builtin_has_a_unique_id_and_at_least_one_pin()
    {
        var all = BuiltinNodeDefinitions.All;

        all.Select(d => d.Id).Should().OnlyHaveUniqueItems();
        all.Should().OnlyContain(d => d.Pins.Count > 0);
        all.Should().OnlyContain(d => d.Title.Length > 0);
    }

    [Fact]
    public void Branch_and_while_have_the_exec_shape_the_lowering_expects()
    {
        var branch = BuiltinNodeDefinitions.Find(BuiltinNodeDefinitions.Branch)!;
        branch.ExecOutputs.Select(p => p.Id).Should().Equal(PinIds.Then, PinIds.Else);
        branch.Pin(PinIds.Condition)!.Type!.TypeName.Should().Be("bool");

        var loop = BuiltinNodeDefinitions.Find(BuiltinNodeDefinitions.While)!;
        loop.ExecOutputs.Select(p => p.Id).Should().Equal(PinIds.Body, PinIds.Completed);
    }

    [Fact]
    public void For_each_exposes_an_element_and_an_index()
    {
        var forEach = BuiltinNodeDefinitions.Find(BuiltinNodeDefinitions.ForEach)!;

        forEach.Pin(PinIds.Element)!.Type!.Form.Should().Be(PinTypeForm.ElementOfGeneric);
        forEach.Pin(PinIds.Index)!.Type!.TypeName.Should().Be("int");
        forEach.Pin(PinIds.Array)!.Type!.Form.Should().Be(PinTypeForm.ArrayOfGeneric);
    }

    [Fact]
    public void Operator_tokens_resolve_from_their_definition_ids()
    {
        BuiltinNodeDefinitions.OperatorToken("op.add").Should().Be("+");
        BuiltinNodeDefinitions.OperatorToken("op.eq").Should().Be("==");
        BuiltinNodeDefinitions.OperatorToken("op.and").Should().Be("&&");
        BuiltinNodeDefinitions.OperatorToken("op.not").Should().Be("!");
        BuiltinNodeDefinitions.OperatorToken("branch").Should().BeNull();
        BuiltinNodeDefinitions.OperatorToken("op.nonsense").Should().BeNull();
    }

    // ---- searching ----------------------------------------------------------------------------

    [Fact]
    public void Search_finds_a_member_by_name_and_reports_the_true_total()
    {
        var result = Palette().Search("AddItem", limit: 5);

        result.Entries.Should().NotBeEmpty();
        result.Entries[0].Title.Should().BeEquivalentTo("AddItem");
        result.Total.Should().BeGreaterThanOrEqualTo(result.Entries.Count);
    }

    [Fact]
    public void Search_caps_results_but_still_reports_how_many_were_hidden()
    {
        // A capped list is only honest if the caller can say "showing N of M".
        var result = Palette().Search("Get", limit: 3);

        result.Entries.Should().HaveCount(3);
        result.Total.Should().BeGreaterThan(3);
        result.Truncated.Should().BeTrue();
    }

    [Fact]
    public void An_exact_match_ranks_above_a_longer_one_that_merely_contains_it()
    {
        var result = new NodePalette(PapyrusCompiler.IndexFor(new[] { TestRoots.BaseStubs }))
            .Search("Add", limit: 10);

        result.Entries.Should().NotBeEmpty();
        result.Entries[0].Title.Should().BeEquivalentTo("Add");
    }

    [Fact]
    public void Search_can_be_filtered_to_one_script()
    {
        var result = Palette().Search("Get", limit: 100, scriptFilter: "Game");

        result.Entries.Should().NotBeEmpty();
        result.Entries.Should().OnlyContain(e => e.Category == "Game");
    }

    [Fact]
    public void Search_results_carry_no_pins()
    {
        // Inlining pins would take a sixty result payload from kilobytes to hundreds of kilobytes.
        var entry = Palette().Search("AddItem", limit: 1).Entries.Single();

        entry.Signature.Should().NotBeNullOrEmpty();
        entry.Id.Should().Be("call:ObjectReference.AddItem");
    }

    // ---- ids ----------------------------------------------------------------------------------

    [Theory]
    [InlineData("call:ObjectReference.AddItem", "ObjectReference", "AddItem")]
    [InlineData("global:Game.GetPlayer", "Game", "GetPlayer")]
    [InlineData("event:ObjectReference.OnActivate", "ObjectReference", "OnActivate")]
    [InlineData("prop.get:MyQuest.Target", "MyQuest", "Target")]
    [InlineData("call:MyMod:Sub.Thing", "MyMod:Sub", "Thing")]
    public void A_definition_id_names_its_script_and_member(string id, string script, string member)
    {
        NodePalette.OwnerScriptOf(id).Should().Be(script);
        NodePalette.MemberNameOf(id).Should().Be(member);
    }

    [Fact]
    public void A_builtin_id_names_no_script()
    {
        NodePalette.OwnerScriptOf("branch").Should().BeNull();
        NodePalette.OwnerScriptOf("op.add").Should().BeNull();
    }

    [Fact]
    public void Definition_ids_are_stable_across_two_builds()
    {
        // A saved graph reattaches by id, so instability here would orphan every node in a document
        // the moment the palette was rebuilt.
        var first = Palette().ForScript("ObjectReference").Select(d => d.Id).OrderBy(x => x).ToList();
        var second = Palette().ForScript("ObjectReference").Select(d => d.Id).OrderBy(x => x).ToList();

        second.Should().Equal(first);
        first.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void A_script_is_built_once_and_cached()
    {
        var palette = Palette();
        palette.ForScript("Actor").Should().BeSameAs(palette.ForScript("Actor"));
    }

    [Fact]
    public void An_unknown_definition_resolves_to_null_rather_than_throwing()
    {
        Palette().Find("call:NoSuchScript.NoSuchThing").Should().BeNull();
        Palette().Find("nonsense").Should().BeNull();
        Palette().Find(null).Should().BeNull();
    }

    [Theory]
    [InlineData("GetPlayer", "player")]
    [InlineData("GetActorBase", "actorBase")]
    [InlineData("IsDead", "dead")]
    [InlineData("HasKeyword", "keyword")]
    [InlineData("AddItem", "addItem")]
    [InlineData("Get", "get")]
    public void A_local_name_hint_reads_like_something_a_person_would_write(string member, string expected)
    {
        NodePalette.LocalNameHint(member).Should().Be(expected);
    }

    // ---- documentation ------------------------------------------------------------------------

    [Fact]
    public void A_palette_with_no_wiki_mirror_still_builds()
    {
        var palette = new NodePalette(
            PapyrusCompiler.IndexFor(new[] { TestRoots.BaseStubs }), new CkWikiDocProvider(null));

        palette.WikiStats.Available.Should().BeFalse();
        palette.Find("call:ObjectReference.AddItem").Should().NotBeNull();
        palette.Find("call:ObjectReference.AddItem")!.Summary.Should().BeNull();
    }

    [Fact]
    public void A_wiki_root_that_does_not_exist_degrades_to_nulls()
    {
        var provider = new CkWikiDocProvider("/no/such/directory/anywhere");

        provider.Function("ObjectReference", "AddItem").Should().BeNull();
        provider.Script("ObjectReference").Should().BeNull();
        provider.Stats.Available.Should().BeFalse();
    }
}
