using System.Linq;
using FluentAssertions;
using FO4RecordEditor.Services.Papyrus;

namespace FO4RecordEditor.Core.Tests;

public class PapyrusParserTests
{
    private static PapyrusScript Parse(string source) => PapyrusParser.Parse(source);

    private static PapyrusScript ParseClean(string source)
    {
        var script = PapyrusParser.Parse(source);
        script.Diagnostics.Where(d => d.Severity == PapyrusSeverity.Error)
            .Should().BeEmpty("the source is valid Papyrus");
        return script;
    }

    // -----------------------------------------------------------------------------------------
    // Header
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void Header_captures_name_parent_and_flags()
    {
        var script = ParseClean("ScriptName HiddenScript Extends ParentScript Hidden\n");
        script.Name.Should().Be("HiddenScript");
        script.Extends.Should().Be("ParentScript");
        script.Flags.Should().Equal("hidden");
    }

    [Fact]
    public void Header_accepts_a_namespaced_script_name()
    {
        ParseClean("ScriptName Traps:Triggers:TripWire\n").Name.Should().Be("Traps:Triggers:TripWire");
    }

    [Fact]
    public void Header_doc_comment_is_attached_to_the_script()
    {
        var script = ParseClean("ScriptName MyCoolScript\n{Docs here\nand here}\n");
        script.Documentation.Should().Be("Docs here\nand here");
    }

    [Fact]
    public void Missing_header_is_reported()
    {
        Parse("Function Foo()\nEndFunction\n").Diagnostics
            .Should().Contain(d => d.Code == PapyrusDiagnosticCodes.ExpectedScriptName);
    }

    // -----------------------------------------------------------------------------------------
    // Script members
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void Imports_are_collected()
    {
        var script = ParseClean("ScriptName S\nimport Utility\nimport MyNamespace:MyScript\n");
        script.Imports.Select(i => i.Name).Should().Equal("Utility", "MyNamespace:MyScript");
    }

    [Fact]
    public void Script_variable_with_initializer_and_flags()
    {
        var script = ParseClean("ScriptName S\nint myValue = 0 Conditional\n");
        var v = script.Variables.Should().ContainSingle().Subject;
        v.Name.Should().Be("myValue");
        v.Type.Name.Should().Be("int");
        v.Initializer.Should().BeOfType<PapyrusLiteralExpression>();
        // "Conditional" is a user flag from the CK's flags file, not a language keyword, and must
        // still parse on a machine that has no Creation Kit installed.
        v.Flags.Should().Equal("conditional");
    }

    [Fact]
    public void Array_type_is_recognised()
    {
        var script = ParseClean("ScriptName S\nint[] myArray\n");
        var v = script.Variables.Single();
        v.Type.IsArray.Should().BeTrue();
        v.Type.ToString().Should().Be("int[]");
    }

    [Fact]
    public void Auto_property_with_initializer()
    {
        var script = ParseClean("ScriptName S\nfloat Property MyProperty = 1.0 Auto\n");
        var p = script.Properties.Should().ContainSingle().Subject;
        p.Kind.Should().Be(PapyrusPropertyKind.Auto);
        p.Type.Name.Should().Be("float");
        p.Initializer.Should().BeOfType<PapyrusLiteralExpression>();
    }

    [Fact]
    public void Auto_read_only_property()
    {
        var script = ParseClean("ScriptName S\nstring Property Hello = \"Hello world!\" AutoReadOnly\n");
        script.Properties.Single().Kind.Should().Be(PapyrusPropertyKind.AutoReadOnly);
    }

    [Fact]
    public void Full_property_collects_its_get_and_set()
    {
        var script = ParseClean(@"ScriptName S
int Property ValueProperty
  Function Set(int newValue)
    myValue = newValue
  EndFunction
  int Function Get()
    return myValue
  EndFunction
EndProperty
");
        var p = script.Properties.Single();
        p.Kind.Should().Be(PapyrusPropertyKind.Full);
        p.Setter.Should().NotBeNull();
        p.Getter.Should().NotBeNull();
        p.Getter!.ReturnType!.Name.Should().Be("int");
    }

    [Fact]
    public void Full_property_without_an_accessor_is_reported()
    {
        Parse("ScriptName S\nint Property P\nEndProperty\n").Diagnostics
            .Should().Contain(d => d.Code == PapyrusDiagnosticCodes.PropertyNeedsAccessor);
    }

    [Fact]
    public void Group_properties_are_listed_on_both_the_group_and_the_script()
    {
        var script = ParseClean(@"ScriptName S
Group MyGroup CollapsedOnRef
{A group}
  int Property FirstProperty auto
  float Property SecondProperty auto
EndGroup
");
        var group = script.Groups.Should().ContainSingle().Subject;
        group.Flags.Should().Equal("collapsedonref");
        group.Documentation.Should().Be("A group");
        group.Properties.Should().HaveCount(2);
        script.Properties.Should().HaveCount(2);
        script.Properties.Should().OnlyContain(p => p.GroupName == "MyGroup");
    }

    [Fact]
    public void Struct_members_keep_their_doc_strings()
    {
        var script = ParseClean(@"ScriptName S
struct QuestStage
  Quest QuestToSet
  {The quest whose stage is to be set}
  int StageToSet
  {The stage to set on the quest}
endStruct
");
        var s = script.Structs.Should().ContainSingle().Subject;
        s.Members.Select(m => m.Name).Should().Equal("QuestToSet", "StageToSet");
        s.Members[0].Documentation.Should().Be("The quest whose stage is to be set");
    }

    [Fact]
    public void Empty_struct_is_reported()
    {
        Parse("ScriptName S\nstruct Empty\nendStruct\n").Diagnostics
            .Should().Contain(d => d.Code == PapyrusDiagnosticCodes.StructNeedsMember);
    }

    [Fact]
    public void Custom_event_definition()
    {
        ParseClean("ScriptName S\nCustomEvent MyCustomEvent\n")
            .CustomEvents.Single().Name.Should().Be("MyCustomEvent");
    }

    // -----------------------------------------------------------------------------------------
    // Functions and events
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void Function_header_with_return_type_parameters_and_global_flag()
    {
        var script = ParseClean(@"ScriptName S
int Function AddTwo(int a, int b) global
  return a + b
endFunction
");
        var fn = script.Functions.Should().ContainSingle().Subject;
        fn.Name.Should().Be("AddTwo");
        fn.ReturnType!.Name.Should().Be("int");
        fn.IsGlobal.Should().BeTrue();
        fn.Parameters.Select(p => p.Name).Should().Equal("a", "b");
        fn.Body.Should().ContainSingle().Which.Should().BeOfType<PapyrusReturnStatement>();
        fn.Signature.Should().Be("int Function AddTwo(int a, int b) global");
    }

    [Fact]
    public void Parameter_default_makes_it_optional()
    {
        var fn = ParseClean("ScriptName S\nFunction IncrementValue(int howMuch = 1)\nendFunction\n")
            .Functions.Single();
        fn.Parameters.Single().DefaultValue.Should().BeOfType<PapyrusLiteralExpression>();
    }

    [Fact]
    public void Native_function_has_no_body_and_no_terminator()
    {
        var script = ParseClean("ScriptName S Native\nint Function GetThing() native global\nFunction Other()\nEndFunction\n");
        script.Functions.Should().HaveCount(2);
        script.Functions[0].IsNative.Should().BeTrue();
        script.Functions[0].Body.Should().BeEmpty();
    }

    [Fact]
    public void Function_doc_comment_is_attached()
    {
        var fn = ParseClean("ScriptName S\nFunction Foo()\n{What Foo does}\nEndFunction\n").Functions.Single();
        fn.Documentation.Should().Be("What Foo does");
    }

    [Fact]
    public void Plain_event()
    {
        var e = ParseClean("ScriptName S\nEvent OnActivate(ObjectReference akActivator)\nendEvent\n")
            .Events.Single();
        e.Name.Should().Be("OnActivate");
        e.RemoteObjectType.Should().BeNull();
        e.Parameters.Single().Type.Name.Should().Be("ObjectReference");
    }

    [Fact]
    public void Remote_event_records_the_object_type()
    {
        var e = ParseClean(@"ScriptName S
Event ObjectReference.OnActivate(ObjectReference akSender, ObjectReference akActivator)
endEvent
").Events.Single();
        e.RemoteObjectType.Should().Be("ObjectReference");
        e.Name.Should().Be("OnActivate");
        e.Signature.Should().StartWith("Event ObjectReference.OnActivate(");
    }

    [Fact]
    public void Custom_event_handler_takes_a_var_array()
    {
        var e = ParseClean(@"ScriptName S
Event MyQuestScript.MyCustomEvent(MyQuestScript akSender, Var[] akArgs)
endEvent
").Events.Single();
        e.Parameters[1].Type.IsArray.Should().BeTrue();
        e.Parameters[1].Type.Name.Should().Be("var");
    }

    [Fact]
    public void Special_parameter_types_parse()
    {
        var fn = ParseClean("ScriptName S\nFunction Send(Form akSender, ScriptEventName asEvent)\nEndFunction\n")
            .Functions.Single();
        fn.Parameters[1].Type.Name.Should().Be("scripteventname");
    }

    // -----------------------------------------------------------------------------------------
    // States
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void Auto_state_with_an_override()
    {
        var script = ParseClean(@"ScriptName S
Auto State MyState
  int Function MyFunction()
    Return 2
  EndFunction
  Event OnActivate(ObjectReference a)
  EndEvent
EndState
");
        var state = script.States.Should().ContainSingle().Subject;
        state.IsAuto.Should().BeTrue();
        state.Name.Should().Be("MyState");
        state.Functions.Should().ContainSingle();
        state.Events.Should().ContainSingle();
        state.Functions[0].StateName.Should().Be("MyState");
        // A state override must not leak into the empty state's function list.
        script.Functions.Should().BeEmpty();
    }

    // -----------------------------------------------------------------------------------------
    // Statements
    // -----------------------------------------------------------------------------------------

    private static List<PapyrusStatement> BodyOf(string statements) =>
        ParseClean("ScriptName S\nFunction F()\n" + statements + "\nEndFunction\n").Functions.Single().Body;

    [Fact]
    public void Define_statement_with_initializer()
    {
        var define = BodyOf("float seconds = CurrentTimeInMinutes() * 60.0").Single()
            .Should().BeOfType<PapyrusDefineStatement>().Subject;
        define.Name.Should().Be("seconds");
        define.Initializer.Should().BeOfType<PapyrusBinaryExpression>();
    }

    [Fact]
    public void Local_may_carry_a_const_flag_even_though_the_published_grammar_omits_it()
    {
        // Real shipping scripts do this and the Creation Kit compiler accepts them.
        var define = BodyOf("string filename = \"S7System\" const").Single()
            .Should().BeOfType<PapyrusDefineStatement>().Subject;
        define.Flags.Should().Equal("const");
    }

    [Fact]
    public void Assignment_is_not_mistaken_for_a_definition()
    {
        BodyOf("x = 5").Single().Should().BeOfType<PapyrusAssignStatement>();
    }

    [Fact]
    public void Indexed_assignment_is_not_mistaken_for_an_array_definition()
    {
        // "myArray[0] = 1" against "int[] myArray": the difference is whether the brackets are
        // empty, and getting it wrong turns every array write into a bogus syntax error.
        var assign = BodyOf("myArray[0] = 1").Single()
            .Should().BeOfType<PapyrusAssignStatement>().Subject;
        assign.Target.Should().BeOfType<PapyrusIndexExpression>();
    }

    [Fact]
    public void Array_definition_with_empty_brackets_is_a_definition()
    {
        var define = BodyOf("int[] myArray = new int[10]").Single()
            .Should().BeOfType<PapyrusDefineStatement>().Subject;
        define.Type.IsArray.Should().BeTrue();
        define.Initializer.Should().BeOfType<PapyrusNewArrayExpression>();
    }

    [Fact]
    public void Compound_assignment_operators()
    {
        var assign = BodyOf("MyObject.MyProperty += CoolFunction() * 10").Single()
            .Should().BeOfType<PapyrusAssignStatement>().Subject;
        assign.Operator.Should().Be(PapyrusTokenKind.PlusAssign);
        assign.Target.Should().BeOfType<PapyrusMemberExpression>();
    }

    [Fact]
    public void Bare_call_is_an_expression_statement()
    {
        BodyOf("PlayAnimation(\"CoolStuff\")").Single()
            .Should().BeOfType<PapyrusExpressionStatement>()
            .Which.Expression.Should().BeOfType<PapyrusCallExpression>();
    }

    [Fact]
    public void Return_with_and_without_a_value()
    {
        BodyOf("Return 5").Single().Should().BeOfType<PapyrusReturnStatement>()
            .Which.Value.Should().NotBeNull();
        BodyOf("Return").Single().Should().BeOfType<PapyrusReturnStatement>()
            .Which.Value.Should().BeNull();
    }

    [Fact]
    public void If_elseif_else()
    {
        var statement = BodyOf(@"if value > 10
  x = 1
elseif value < 10
  x = -1
else
  x = 0
endIf").Single().Should().BeOfType<PapyrusIfStatement>().Subject;

        statement.Branches.Should().HaveCount(2);
        statement.Branches[0].Body.Should().ContainSingle();
        statement.ElseBody.Should().ContainSingle();
    }

    [Fact]
    public void While_loop()
    {
        var loop = BodyOf("while x < 10\n  DoCoolStuff()\n  x += 1\nendWhile").Single()
            .Should().BeOfType<PapyrusWhileStatement>().Subject;
        loop.Body.Should().HaveCount(2);
        loop.Condition.Should().BeOfType<PapyrusBinaryExpression>();
    }

    // -----------------------------------------------------------------------------------------
    // Expressions
    // -----------------------------------------------------------------------------------------

    private static PapyrusExpression ExprOf(string expression) =>
        BodyOf("x = " + expression).OfType<PapyrusAssignStatement>().Single().Value;

    [Fact]
    public void Multiplication_binds_tighter_than_addition()
    {
        // 2 + 4 * 10 is 42, not 60: the tree must be Add(2, Mul(4, 10)).
        var root = ExprOf("2 + 4 * 10").Should().BeOfType<PapyrusBinaryExpression>().Subject;
        root.Operator.Should().Be(PapyrusTokenKind.Plus);
        root.Right.Should().BeOfType<PapyrusBinaryExpression>()
            .Which.Operator.Should().Be(PapyrusTokenKind.Star);
    }

    [Fact]
    public void Parentheses_override_precedence_without_adding_a_node()
    {
        var root = ExprOf("(2 + 4) * 10").Should().BeOfType<PapyrusBinaryExpression>().Subject;
        root.Operator.Should().Be(PapyrusTokenKind.Star);
        root.Left.Should().BeOfType<PapyrusBinaryExpression>()
            .Which.Operator.Should().Be(PapyrusTokenKind.Plus);
    }

    [Fact]
    public void Comparison_binds_looser_than_arithmetic_and_tighter_than_and()
    {
        var root = ExprOf("a + 1 > b && c").Should().BeOfType<PapyrusBinaryExpression>().Subject;
        root.Operator.Should().Be(PapyrusTokenKind.And);
        root.Left.Should().BeOfType<PapyrusBinaryExpression>()
            .Which.Operator.Should().Be(PapyrusTokenKind.Greater);
    }

    [Fact]
    public void And_binds_tighter_than_or()
    {
        var root = ExprOf("a || b && c").Should().BeOfType<PapyrusBinaryExpression>().Subject;
        root.Operator.Should().Be(PapyrusTokenKind.Or);
        root.Right.Should().BeOfType<PapyrusBinaryExpression>()
            .Which.Operator.Should().Be(PapyrusTokenKind.And);
    }

    [Fact]
    public void Unary_minus_and_not()
    {
        ExprOf("-a").Should().BeOfType<PapyrusUnaryExpression>()
            .Which.Operator.Should().Be(PapyrusTokenKind.Minus);
        ExprOf("!a").Should().BeOfType<PapyrusUnaryExpression>()
            .Which.Operator.Should().Be(PapyrusTokenKind.Not);
    }

    [Fact]
    public void Cast_and_type_check()
    {
        ExprOf("a as float").Should().BeOfType<PapyrusCastExpression>()
            .Which.Type.Name.Should().Be("float");
        ExprOf("myObject is ObjectReference").Should().BeOfType<PapyrusTypeCheckExpression>()
            .Which.Type.Name.Should().Be("ObjectReference");
    }

    [Fact]
    public void Dot_chain_with_call_and_index()
    {
        // (MyVariable as MyObject).MyFunction()[0]
        var index = ExprOf("(MyVariable as MyObject).MyFunction()[0]")
            .Should().BeOfType<PapyrusIndexExpression>().Subject;
        var call = index.Target.Should().BeOfType<PapyrusCallExpression>().Subject;
        call.FunctionName.Should().Be("MyFunction");
        call.Callee.Should().BeOfType<PapyrusMemberExpression>()
            .Which.Target.Should().BeOfType<PapyrusCastExpression>();
    }

    [Fact]
    public void Array_length_is_a_member_access()
    {
        ExprOf("myArray.Length").Should().BeOfType<PapyrusMemberExpression>()
            .Which.Name.Should().Be("Length");
    }

    [Fact]
    public void Named_argument_is_distinguished_from_a_comparison()
    {
        var call = ExprOf("MyFunction(howMuch = 1, other == 2)")
            .Should().BeOfType<PapyrusCallExpression>().Subject;
        call.Arguments[0].Name.Should().Be("howMuch");
        call.Arguments[1].Name.Should().BeNull();
        call.Arguments[1].Value.Should().BeOfType<PapyrusBinaryExpression>();
    }

    [Fact]
    public void New_struct_versus_new_array()
    {
        ExprOf("new Point").Should().BeOfType<PapyrusNewStructExpression>();
        ExprOf("new MyScript[5 * count]").Should().BeOfType<PapyrusNewArrayExpression>()
            .Which.Size.Should().BeOfType<PapyrusBinaryExpression>();
    }

    [Fact]
    public void Namespaced_script_is_one_atom_not_a_member_chain()
    {
        var call = ExprOf("MyNamespace:MyScript.MyGlobal()")
            .Should().BeOfType<PapyrusCallExpression>().Subject;
        var member = call.Callee.Should().BeOfType<PapyrusMemberExpression>().Subject;
        member.Target.Should().BeOfType<PapyrusIdentifierExpression>()
            .Which.Name.Should().Be("MyNamespace:MyScript");
    }

    [Fact]
    public void None_true_and_false_are_literals()
    {
        ExprOf("none").Should().BeOfType<PapyrusLiteralExpression>()
            .Which.Kind.Should().Be(PapyrusLiteralKind.None);
        ExprOf("true").Should().BeOfType<PapyrusLiteralExpression>()
            .Which.Kind.Should().Be(PapyrusLiteralKind.Bool);
    }

    // -----------------------------------------------------------------------------------------
    // Recovery
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void A_broken_line_does_not_cost_the_declarations_after_it()
    {
        var script = PapyrusParser.Parse(@"ScriptName S
int Property Good1 auto
int Property % auto
int Property Good2 auto
");
        script.HasErrors.Should().BeTrue();
        script.Properties.Select(p => p.Name).Should().Contain(new[] { "Good1", "Good2" });
    }

    [Fact]
    public void A_broken_statement_does_not_cost_the_rest_of_the_function()
    {
        var script = PapyrusParser.Parse(@"ScriptName S
Function F()
  x = 1
  y = *
  z = 3
EndFunction
Function G()
EndFunction
");
        script.HasErrors.Should().BeTrue();
        script.Functions.Select(f => f.Name).Should().Equal("F", "G");
        script.Functions[0].Body.Should().HaveCountGreaterThanOrEqualTo(2);
    }

    [Fact]
    public void Unterminated_function_is_reported_and_does_not_hang()
    {
        var script = PapyrusParser.Parse("ScriptName S\nFunction F()\n  x = 1\n");
        script.Diagnostics.Should().Contain(d => d.Code == PapyrusDiagnosticCodes.UnterminatedBlock);
    }

    [Fact]
    public void Diagnostics_are_capped_rather_than_unbounded()
    {
        var junk = "ScriptName S\n" + string.Concat(Enumerable.Repeat("%\n", 500));
        PapyrusParser.Parse(junk).Diagnostics.Count.Should().BeLessThanOrEqualTo(201);
    }

    [Fact]
    public void Empty_input_produces_a_script_and_a_single_complaint()
    {
        var script = PapyrusParser.Parse(string.Empty);
        script.Should().NotBeNull();
        script.Diagnostics.Should().ContainSingle()
            .Which.Code.Should().Be(PapyrusDiagnosticCodes.ExpectedScriptName);
    }

    [Fact]
    public void Diagnostics_carry_the_file_path_when_one_is_given()
    {
        var script = PapyrusParser.Parse("nonsense\n", "/tmp/Foo.psc");
        script.FilePath.Should().Be("/tmp/Foo.psc");
        script.Diagnostics.Should().OnlyContain(d => d.File == "/tmp/Foo.psc");
    }
}
