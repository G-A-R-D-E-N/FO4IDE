using System.IO;
using System.Linq;
using FluentAssertions;
using FO4RecordEditor.Services.Papyrus;

namespace FO4RecordEditor.Core.Tests;














public class BaseStubTests
{
    private static PapyrusCompileResult Compile(string source)
    {
        var index = PapyrusCompiler.IndexFor(new[] { TestRoots.BaseStubs });
        var script = PapyrusParser.Parse(source, "Fixture.psc");
        return new PapyrusCompiler(index).Compile(script, sourceFileName: "Fixture.psc");
    }

    private static PapyrusCompileResult CompileOk(string source)
    {
        var result = Compile(source);
        result.Success.Should().BeTrue(
            "the stub tree should support this script; diagnostics were: "
            + string.Join(" | ", result.Diagnostics.Select(d => $"{d.Code} {d.Message}")));
        return result;
    }

    [Fact]
    public void The_stub_tree_is_copied_beside_the_test_binary()
    {
        Directory.Exists(TestRoots.BaseStubs).Should().BeTrue(
            "the project file copies Fixtures/** to the output directory");
        Directory.GetFiles(TestRoots.BaseStubs, "*.psc").Should().NotBeEmpty();
    }

    [Fact]
    public void Every_stub_parses_without_error()
    {
        foreach (var path in Directory.GetFiles(TestRoots.BaseStubs, "*.psc"))
        {
            var script = PapyrusParser.ParseFile(path);
            script.Diagnostics
                .Where(d => d.Severity == PapyrusSeverity.Error)
                .Should().BeEmpty($"{Path.GetFileName(path)} should parse cleanly");
        }
    }











    private static readonly string[] IncompleteByDesign = { "ScriptObject.psc" };

    [Fact]
    public void Every_stub_resolves_its_own_base_chain()
    {
        var index = PapyrusCompiler.IndexFor(new[] { TestRoots.BaseStubs });
        var incomplete = new System.Collections.Generic.List<string>();

        foreach (var path in Directory.GetFiles(TestRoots.BaseStubs, "*.psc"))
        {
            var name = Path.GetFileName(path);
            if (IncompleteByDesign.Contains(name, System.StringComparer.OrdinalIgnoreCase)) continue;

            var script = PapyrusParser.ParseFile(path);
            var resolution = new PapyrusResolver(index).Resolve(script);
            if (!resolution.BaseChainComplete) incomplete.Add(name);
        }

        incomplete.Should().BeEmpty(
            "every stub should name only parents and types the tree itself contains");
    }

    [Fact]
    public void The_exemption_list_is_not_hiding_a_stub_that_now_resolves()
    {

        var index = PapyrusCompiler.IndexFor(new[] { TestRoots.BaseStubs });

        foreach (var name in IncompleteByDesign)
        {
            var script = PapyrusParser.ParseFile(Path.Combine(TestRoots.BaseStubs, name));
            new PapyrusResolver(index).Resolve(script).BaseChainComplete
                .Should().BeFalse($"{name} is exempted, so it should still be the case that it needs to be");
        }
    }

    [Fact]
    public void A_script_extending_ObjectReference_compiles_against_the_stubs()
    {
        var result = CompileOk("""
            Scriptname Fixture extends ObjectReference

            Event OnActivate(ObjectReference akActionRef)
                Debug.Notification("opened")
            EndEvent
            """);

        result.Pex.Should().NotBeNull();
        result.Pex!.Objects[0].ParentClassName.Should().BeEquivalentTo("ObjectReference");
    }

    [Fact]
    public void A_script_extending_nothing_still_names_ScriptObject()
    {

        var result = CompileOk("""
            Scriptname Fixture

            int Function Twice(int a)
                return a * 2
            EndFunction
            """);

        result.Pex!.Objects[0].ParentClassName.Should().BeEquivalentTo("ScriptObject");
    }

    [Fact]
    public void Optional_parameters_and_defaults_resolve_from_the_stubs()
    {

        CompileOk("""
            Scriptname Fixture extends ObjectReference

            Function Give(Form akWhat)
                AddItem(akWhat)
                AddItem(akWhat, 3)
                AddItem(akWhat, 3, true)
            EndFunction
            """);
    }

    [Fact]
    public void A_named_argument_after_a_skipped_optional_resolves()
    {

        CompileOk("""
            Scriptname Fixture extends ObjectReference

            Function Give(Form akWhat)
                AddItem(akWhat, abSilent = true)
            EndFunction
            """);
    }

    [Fact]
    public void Global_functions_resolve_through_the_stub_tree()
    {
        CompileOk("""
            Scriptname Fixture

            Function Go()
                Actor player = Game.GetPlayer()
                Utility.Wait(1.0)
                Debug.Trace("level " + player.GetLevel())
            EndFunction
            """);
    }

    [Fact]
    public void Inheritance_across_three_levels_resolves()
    {


        CompileOk("""
            Scriptname Fixture

            int Function IdOf(Actor akWho)
                return akWho.GetFormID()
            EndFunction
            """);
    }

    [Fact]
    public void An_implicit_upcast_to_a_base_type_is_accepted()
    {
        CompileOk("""
            Scriptname Fixture extends ObjectReference

            Function Move(Actor akWho)
                MoveTo(akWho)
            EndFunction
            """);
    }

    [Fact]
    public void The_custom_and_remote_event_keyword_types_resolve()
    {



        CompileOk("""
            Scriptname Fixture extends ObjectReference

            Event OnInit()
                RegisterForRemoteEvent(Game.GetPlayer(), "OnPlayerLoadGame")
            EndEvent
            """);
    }

    [Fact]
    public void A_missing_member_is_refused_rather_than_guessed()
    {


        var result = Compile("""
            Scriptname Fixture extends ObjectReference

            Function Go()
                ThisMemberDoesNotExist(1, 2, 3)
            EndFunction
            """);

        result.Success.Should().BeFalse();
        result.Errors.Should().NotBeEmpty();
    }
}
