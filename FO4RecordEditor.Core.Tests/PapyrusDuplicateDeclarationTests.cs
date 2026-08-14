using System.Linq;
using FluentAssertions;
using FO4RecordEditor.Services.Papyrus;

namespace FO4RecordEditor.Core.Tests;















public class PapyrusDuplicateDeclarationTests
{
    private static PapyrusCompileResult Compile(string text)
    {
        var compiler = new PapyrusCompiler(PapyrusCompiler.IndexFor(new[] { TestRoots.BaseStubs }));
        return compiler.Compile(PapyrusParser.Parse(text, "Fixture.psc"), sourceFileName: "Fixture.psc");
    }

    private static string Describe(PapyrusCompileResult result) =>
        string.Join(" | ", result.Diagnostics.Select(d => $"{d.Code} {d.Severity} {d.Message}"));

    private static PapyrusDiagnostic Refused(string text, string expectedName)
    {
        var result = Compile(text);

        result.Success.Should().BeFalse("a duplicate declaration cannot be emitted");
        result.Pex.Should().BeNull("nothing should be written for a script that was refused");

        var match = result.Diagnostics.FirstOrDefault(
            d => d.Code == PapyrusDiagnosticCodes.DuplicateDeclaration);
        match.Should().NotBeNull(Describe(result));
        match!.Message.Should().Contain(expectedName);
        return match;
    }

    private static void Accepted(string text)
    {
        var result = Compile(text);

        result.Diagnostics.Should().NotContain(
            d => d.Code == PapyrusDiagnosticCodes.DuplicateDeclaration, Describe(result));
        result.Success.Should().BeTrue(Describe(result));
    }

    [Fact]
    public void Two_events_of_the_same_name_are_refused()
    {
        Refused("""
            Scriptname Fixture extends ObjectReference
            Event OnLoad()
                Disable()
            EndEvent
            Event OnLoad()
                Enable()
            EndEvent
            """, "OnLoad");
    }

    [Fact]
    public void Two_functions_of_the_same_name_are_refused_even_with_different_parameters()
    {

        Refused("""
            Scriptname Fixture extends ObjectReference
            Function Go()
            EndFunction
            Function Go(int a)
            EndFunction
            """, "Go");
    }

    [Fact]
    public void A_function_and_an_event_of_the_same_name_collide()
    {

        Refused("""
            Scriptname Fixture extends ObjectReference
            Function OnLoad()
            EndFunction
            Event OnLoad()
            EndEvent
            """, "OnLoad");
    }

    [Theory]
    [InlineData("int Counter\nint Counter", "Counter", "variable")]
    [InlineData("int Property Amount Auto\nint Property Amount Auto", "Amount", "property")]
    public void Two_script_level_declarations_of_the_same_name_are_refused(
        string body, string name, string kind)
    {
        var diagnostic = Refused($"Scriptname Fixture extends ObjectReference\n{body}", name);
        diagnostic.Message.Should().Contain(kind);
    }

    [Fact]
    public void Two_structs_of_the_same_name_are_refused()
    {
        Refused("""
            Scriptname Fixture extends ObjectReference
            Struct Pair
                int a
            EndStruct
            Struct Pair
                int b
            EndStruct
            """, "Pair");
    }

    [Fact]
    public void Two_states_of_the_same_name_are_refused()
    {
        Refused("""
            Scriptname Fixture extends ObjectReference
            State Busy
            EndState
            State Busy
            EndState
            """, "Busy");
    }

    [Fact]
    public void Two_events_of_the_same_name_inside_one_state_are_refused()
    {
        var diagnostic = Refused("""
            Scriptname Fixture extends ObjectReference
            State Busy
                Event OnLoad()
                    Disable()
                EndEvent
                Event OnLoad()
                    Enable()
                EndEvent
            EndState
            """, "OnLoad");

        diagnostic.Message.Should().Contain("Busy", "the message should name the state at fault");
    }

    [Fact]
    public void The_same_event_in_two_different_states_is_accepted()
    {
        Accepted("""
            Scriptname Fixture extends ObjectReference
            Auto State Idle
                Event OnLoad()
                    Disable()
                EndEvent
            EndState
            State Busy
                Event OnLoad()
                    Enable()
                EndEvent
            EndState
            """);
    }

    [Fact]
    public void The_same_event_in_the_empty_state_and_a_named_state_is_accepted()
    {
        Accepted("""
            Scriptname Fixture extends ObjectReference
            Event OnLoad()
                Disable()
            EndEvent
            State Busy
                Event OnLoad()
                    Enable()
                EndEvent
            EndState
            """);
    }

    [Fact]
    public void A_local_override_beside_a_remote_handler_of_the_same_event_is_accepted()
    {


        Accepted("""
            Scriptname Fixture extends ObjectReference
            Event OnLoad()
                Disable()
            EndEvent
            Event ObjectReference.OnLoad(ObjectReference akSender)
                Enable()
            EndEvent
            """);
    }

    [Fact]
    public void Two_remote_handlers_for_the_same_event_are_refused()
    {

        Refused("""
            Scriptname Fixture extends ObjectReference
            Event ObjectReference.OnLoad(ObjectReference akSender)
                Disable()
            EndEvent
            Event ObjectReference.OnLoad(ObjectReference akSender)
                Enable()
            EndEvent
            """, "ObjectReference.OnLoad");
    }

    [Fact]
    public void Remote_handlers_of_the_same_event_on_different_types_are_accepted()
    {
        Accepted("""
            Scriptname Fixture extends ObjectReference
            Event ObjectReference.OnLoad(ObjectReference akSender)
                Disable()
            EndEvent
            Event Actor.OnLoad(Actor akSender)
                Enable()
            EndEvent
            """);
    }

    [Fact]
    public void The_diagnostic_points_at_the_later_declaration()
    {

        var diagnostic = Refused("""
            Scriptname Fixture extends ObjectReference
            Function Go()
            EndFunction
            Function Go()
            EndFunction
            """, "Go");

        diagnostic.Span.Line.Should().Be(4);
        diagnostic.Message.Should().Contain("line 2", "and name where the first one was");
    }

    [Fact]
    public void A_clean_script_raises_nothing()
    {
        Accepted("""
            Scriptname Fixture extends ObjectReference
            int Counter
            int Property Amount Auto
            Struct Pair
                int a
            EndStruct
            Function Go()
                Disable()
            EndFunction
            Event OnLoad()
                Enable()
            EndEvent
            State Busy
                Event OnLoad()
                    Disable()
                EndEvent
            EndState
            """);
    }
}
