using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using FO4RecordEditor.Services.Papyrus;

namespace FO4RecordEditor.Core.Tests;










public class PapyrusTypeCheckerTests : IDisposable
{
    private readonly string _root;

    public PapyrusTypeCheckerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "fo4re-typecheck-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private void Write(string scriptName, string source)
    {
        var path = Path.Combine(_root, scriptName.Replace(':', Path.DirectorySeparatorChar) + ".psc");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, source);
    }

    private IReadOnlyList<PapyrusDiagnostic> Check(string scriptName)
    {
        var index = new PapyrusScriptIndex();
        index.AddRoot(_root);
        var script = index.Resolve(scriptName);
        script.Should().NotBeNull();
        script!.HasErrors.Should().BeFalse("the source under test has to parse before it can be checked");

        var resolution = new PapyrusResolver(index).Resolve(script);
        return new PapyrusTypeChecker(index).Check(resolution);
    }


    private void WriteWithHierarchy(string body, string members = "")
    {
        Write("Form", "ScriptName Form");
        Write("ObjectReference", "ScriptName ObjectReference extends Form");
        Write("Actor", "ScriptName Actor extends ObjectReference");
        Write("A", $@"
ScriptName A
{members}
Function Go()
{body}
EndFunction");
    }



    [Fact]
    public void Assigning_a_string_to_an_int_is_rejected()
    {
        WriteWithHierarchy(@"
    int x = 0
    string s = """"
    x = s");

        Check("A").Should().ContainSingle()
            .Which.Code.Should().Be(PapyrusDiagnosticCodes.TypeMismatch);
    }

    // "Cast to Int -- Compiler auto-cast from: Nothing."
    [Fact]
    public void Assigning_a_float_to_an_int_is_rejected_because_it_would_truncate()
    {
        WriteWithHierarchy(@"
    int x = 0
    float f = 1.5
    x = f");

        Check("A").Should().ContainSingle()
            .Which.Message.Should().Contain("float").And.Contain("int");
    }

    // "Cast to Float -- Compiler auto-cast from: Int."
    [Fact]
    public void Assigning_an_int_to_a_float_is_fine()
    {
        WriteWithHierarchy(@"
    float f = 0.0
    int x = 1
    f = x");

        Check("A").Should().BeEmpty();
    }

    // "Cast to Object -- Compiler auto-cast from: Child object."
    [Fact]
    public void Assigning_a_child_object_to_a_parent_is_fine_but_not_the_reverse()
    {
        WriteWithHierarchy(@"
    Actor a = None
    Form f = None
    f = a");
        Check("A").Should().BeEmpty("an Actor is a Form");

        WriteWithHierarchy(@"
    Actor a = None
    Form f = None
    a = f");
        Check("A").Should().ContainSingle("not every Form is an Actor");
    }

    [Fact]
    public void None_may_be_assigned_to_an_object()
    {
        WriteWithHierarchy(@"
    Actor a = None
    a = None");

        Check("A").Should().BeEmpty();
    }

    // ---- returns --------------------------------------------------------------------------------

    [Fact]
    public void Returning_the_wrong_type_is_rejected()
    {
        Write("A", @"
ScriptName A
int Function Go()
    return ""nope""
EndFunction");

        Check("A").Should().ContainSingle()
            .Which.Code.Should().Be(PapyrusDiagnosticCodes.TypeMismatch);
    }

    [Fact]
    public void Returning_a_value_from_a_function_that_returns_nothing_is_rejected()
    {
        Write("A", @"
ScriptName A
Function Go()
    return 1
EndFunction");

        Check("A").Should().ContainSingle()
            .Which.Message.Should().Contain("returns nothing");
    }

    [Fact]
    public void A_bare_return_is_always_fine()
    {
        Write("A", @"
ScriptName A
Function Go()
    return
EndFunction");

        Check("A").Should().BeEmpty();
    }

    // ---- calls ----------------------------------------------------------------------------------

    private const string Callee = @"
Function Takes(int a, string b, float c = 1.0)
EndFunction";

    [Fact]
    public void Too_many_arguments_are_rejected()
    {
        WriteWithHierarchy("    Takes(1, \"x\", 2.0, 3)", Callee);

        Check("A").Should().ContainSingle()
            .Which.Code.Should().Be(PapyrusDiagnosticCodes.ArgumentCount);
    }

    [Fact]
    public void Too_few_arguments_are_rejected()
    {
        WriteWithHierarchy("    Takes(1)", Callee);

        Check("A").Should().ContainSingle()
            .Which.Code.Should().Be(PapyrusDiagnosticCodes.ArgumentCount);
    }

    // "If a parameter is optional, it does not have to be passed."
    [Fact]
    public void Omitting_an_optional_argument_is_fine()
    {
        WriteWithHierarchy("    Takes(1, \"x\")", Callee);

        Check("A").Should().BeEmpty();
    }

    // "You may specify parameters out of order by prefixing the expression with the identifier."
    [Fact]
    public void A_named_argument_may_skip_an_earlier_optional_one()
    {
        WriteWithHierarchy("    Takes(1, \"x\", c = 2.0)", Callee);

        Check("A").Should().BeEmpty();
    }

    [Fact]
    public void A_named_argument_that_matches_no_parameter_is_rejected()
    {
        WriteWithHierarchy("    Takes(1, \"x\", nope = 2.0)", Callee);

        Check("A").Should().ContainSingle()
            .Which.Code.Should().Be(PapyrusDiagnosticCodes.UnknownArgumentName);
    }

    [Fact]
    public void An_argument_of_the_wrong_type_is_rejected()
    {
        WriteWithHierarchy(@"
    float f = 1.5
    Takes(f, ""x"")", Callee);

        Check("A").Should().ContainSingle()
            .Which.Message.Should().Contain("pass to 'a'");
    }

    // Array members are real, but they are not declared in any .psc, so there is no signature to
    // check against and the checker must not invent one.
    [Fact]
    public void Array_builtin_arguments_are_left_unchecked_rather_than_wrongly_checked()
    {
        WriteWithHierarchy(@"
    int[] items = new int[4]
    items.Add(1, 2)");

        Check("A").Should().BeEmpty();
    }

    // ---- casts ----------------------------------------------------------------------------------

    // Eleven vanilla scripts do this and the Creation Kit compiled every one, even though the Cast
    // Reference's "Floats, strings, and vars can be cast to integers" omits bool.
    [Theory]
    [InlineData("int")]
    [InlineData("float")]
    public void A_bool_casts_to_a_number_even_though_the_reference_omits_it(string target)
    {
        WriteWithHierarchy($@"
    bool flag = true
    {target} n = flag as {target}");

        Check("A").Should().BeEmpty();
    }

    // "Cast to Struct -- Nothing can be cast to a struct."
    [Fact]
    public void Casting_to_a_struct_is_rejected()
    {
        Write("A", @"
ScriptName A
Struct Point
    int X
EndStruct
Function Go()
    int n = 1
    Point p = n as Point
EndFunction");

        Check("A").Should().ContainSingle()
            .Which.Code.Should().Be(PapyrusDiagnosticCodes.InvalidCast);
    }

    [Fact]
    public void A_downcast_between_related_objects_is_allowed()
    {
        WriteWithHierarchy(@"
    Form f = None
    Actor a = f as Actor");

        Check("A").Should().BeEmpty();
    }

    // ---- declarations ---------------------------------------------------------------------------

    // "If the identifier matches a function in the parent script, then the return type and
    // parameters much match the parent script's version."
    [Fact]
    public void An_override_with_a_different_parameter_count_is_rejected()
    {
        Write("Base", @"
ScriptName Base
Function Go(int a)
EndFunction");
        Write("Child", @"
ScriptName Child extends Base
Function Go(int a, int b)
EndFunction");

        var index = new PapyrusScriptIndex();
        index.AddRoot(_root);
        var script = index.Resolve("Child")!;
        var diagnostics = new PapyrusTypeChecker(index).Check(new PapyrusResolver(index).Resolve(script));

        diagnostics.Should().ContainSingle()
            .Which.Code.Should().Be(PapyrusDiagnosticCodes.OverrideMismatch);
    }

    [Fact]
    public void A_matching_override_is_fine()
    {
        Write("Base", @"
ScriptName Base
Function Go(int a)
EndFunction");
        Write("Child", @"
ScriptName Child extends Base
Function Go(int a)
EndFunction");

        Check("Child").Should().BeEmpty();
    }

    // "If a parameter has a default value, every parameter after it must also have a default value."
    [Fact]
    public void A_required_parameter_after_an_optional_one_is_rejected()
    {
        Write("A", @"
ScriptName A
Function Go(int a = 1, int b)
EndFunction");

        Check("A").Should().ContainSingle()
            .Which.Code.Should().Be(PapyrusDiagnosticCodes.ParameterOrder);
    }

    // ---- staying quiet --------------------------------------------------------------------------

    // The rule that makes the whole thing usable: judge nothing when the sources were incomplete.
    [Fact]
    public void Nothing_is_reported_when_the_sources_are_incomplete()
    {
        Write("Child", @"
ScriptName Child extends NotOnDisk
int Function Go()
    return ""this would be wrong if we could see the parent""
EndFunction");

        Check("Child").Should().BeEmpty();
    }

    // Papyrus is case-insensitive, so a call can accidentally match a local. It is still a call.
    [Fact]
    public void A_call_does_not_bind_to_a_same_named_local()
    {
        Write("A", @"
ScriptName A
int Function Value()
    return 1
EndFunction
Function Go(string value)
    int n = Value()
EndFunction");

        Check("A").Should().BeEmpty("Value() is the function, not the string parameter");
    }

    [Fact]
    public void A_var_accepts_anything_and_is_accepted_anywhere()
    {
        WriteWithHierarchy(@"
    var slot = 1
    slot = ""text""
    int n = 0
    n = slot as int");

        Check("A").Should().BeEmpty();
    }
}
