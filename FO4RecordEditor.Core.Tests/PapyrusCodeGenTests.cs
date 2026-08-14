using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using FO4RecordEditor.Services.Papyrus;

namespace FO4RecordEditor.Core.Tests;















public class PapyrusCodeGenTests : IDisposable
{
    private readonly string _root;

    public PapyrusCodeGenTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "fo4re-codegen-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);



        Write("ScriptObject", "Scriptname ScriptObject Native Hidden\n");
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

    private PapyrusCompileResult Compile(string scriptName)
    {
        var index = new PapyrusScriptIndex();
        index.AddRoot(_root);
        return new PapyrusCompiler(index).CompileFile(
            Path.Combine(_root, scriptName.Replace(':', Path.DirectorySeparatorChar) + ".psc"));
    }

    private PexObject CompileObject(string scriptName)
    {
        var result = Compile(scriptName);
        result.Success.Should().BeTrue(
            "compilation should succeed; diagnostics were: "
            + string.Join(" | ", result.Diagnostics.Select(d => d.ToString())));
        return result.Pex!.Objects.Single();
    }

    private static PexFunction Function(PexObject obj, string name, string state = "") =>
        obj.States.Single(s => s.Name == state).Functions
            .Single(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase));


    private static string[] Listing(PexFunction fn) =>
        fn.Instructions.Select(i => i.Mnemonic + " " + string.Join(" ", i.Args.Select(Operand))).ToArray();

    private static string Operand(PexValue v) => v.Type switch
    {
        PexValueType.Identifier => v.Str.StartsWith("::temp", StringComparison.Ordinal) ? "T" : v.Str,
        PexValueType.String => "\"" + v.Str + "\"",
        PexValueType.Integer => v.Int.ToString(),
        PexValueType.Float => v.Float.ToString("0.0###"),
        PexValueType.Bool => v.Bool ? "true" : "false",
        _ => "None",
    };



    [Fact]
    public void A_script_that_extends_nothing_still_names_ScriptObject_as_its_parent()
    {
        Write("Bare", "Scriptname Bare\n");
        CompileObject("Bare").ParentClassName.Should().Be("ScriptObject");
    }

    [Fact]
    public void ScriptObject_itself_has_no_parent()
    {
        CompileObject("ScriptObject").ParentClassName.Should().BeEmpty();
    }

    [Fact]
    public void Header_carries_the_docstring_const_flag_and_user_flags()
    {
        Write("Meta", "Scriptname Meta Const Hidden\n{What it is}\n");
        var obj = CompileObject("Meta");

        obj.DocString.Should().Be("What it is");
        obj.Const.Should().BeTrue();
        obj.UserFlags.Should().Be(1u, "Hidden is bit 0 in Institute_Papyrus_Flags.flg");
    }

    [Fact]
    public void An_auto_property_gets_a_backing_variable_and_the_read_write_autovar_flags()
    {
        Write("Props", "Scriptname Props\nint Property Count = 3 Auto Mandatory\n");
        var obj = CompileObject("Props");

        var property = obj.Properties.Single();
        property.Flags.Should().Be(0x07);
        property.AutoVarName.Should().Be("::Count_var");
        property.UserFlags.Should().Be(32u, "Mandatory is bit 5");

        var backing = obj.Variables.Single();
        backing.Name.Should().Be("::Count_var");
        backing.Type.Should().Be("Int");
        backing.DefaultValue!.Int.Should().Be(3);
    }





    [Fact]
    public void An_auto_read_only_property_becomes_a_getter_returning_the_constant()
    {
        Write("Consts", "Scriptname Consts\nint Property Mask = 0x00200000 AutoReadOnly\n");
        var obj = CompileObject("Consts");

        obj.Variables.Should().BeEmpty();
        var property = obj.Properties.Single();
        property.Flags.Should().Be(0x01);
        property.AutoVarName.Should().BeEmpty();
        Listing(property.ReadHandler!).Should().Equal("return 2097152");
    }

    [Fact]
    public void A_full_property_emits_its_handlers_and_no_backing_variable()
    {
        Write("Full", """
            Scriptname Full
            int mCount
            int Property Count
                int Function Get()
                    return mCount
                EndFunction
                Function Set(int aiValue)
                    mCount = aiValue
                EndFunction
            EndProperty
            """);
        var obj = CompileObject("Full");

        var property = obj.Properties.Single();
        property.Flags.Should().Be(0x03);
        property.IsAutoVar.Should().BeFalse();
        obj.Variables.Select(v => v.Name).Should().Equal("mCount");
        Listing(property.ReadHandler!).Should().Equal("return mCount");
        Listing(property.WriteHandler!).Should().Equal("assign mCount aiValue");
    }

    [Fact]
    public void A_struct_keeps_its_member_order_in_the_debug_struct_order_table()
    {
        Write("WithStruct", """
            Scriptname WithStruct
            Struct Point
                float fX = 0.0
                float fY = 1.5
            EndStruct
            """);
        var result = Compile("WithStruct");
        result.Success.Should().BeTrue();

        result.Pex!.StructOrders.Single().MemberNames.Should().Equal("fX", "fY");
        var members = result.Pex.Objects.Single().Structs.Single().Members;
        members.Select(m => m.Type).Should().AllBe("Float");
        members[1].DefaultValue!.Float.Should().Be(1.5f);
    }

    [Fact]
    public void Ungrouped_properties_are_listed_under_an_unnamed_property_group()
    {
        Write("Grouped", "Scriptname Grouped\nint Property A Auto\nint Property B Auto\n");
        var result = Compile("Grouped");

        var group = result.Pex!.PropertyGroups.Single();
        group.GroupName.Should().BeEmpty();
        group.PropertyNames.Should().Equal("A", "B");
    }

    [Fact]
    public void A_state_override_lands_in_its_own_state_and_the_auto_state_is_named()
    {
        Write("States", """
            Scriptname States
            Function Poke()
            EndFunction
            Auto State Idle
                Function Poke()
                EndFunction
            EndState
            """);
        var obj = CompileObject("States");

        obj.AutoStateName.Should().Be("Idle");
        obj.States.Select(s => s.Name).Should().Equal("", "Idle");
        obj.States.Single(s => s.Name == "Idle").Functions.Single().Name.Should().Be("Poke");
    }







    [Fact]
    public void A_lone_if_emits_a_trailing_jump_to_the_end()
    {
        Write("Flow", """
            Scriptname Flow
            Function Go(bool abFlag)
                if abFlag
                    Go(false)
                endIf
            EndFunction
            """);
        Listing(Function(CompileObject("Flow"), "Go")).Should().Equal(
            "jmpf abFlag 3",
            "callmethod Go self ::nonevar false",
            "jmp 1");
    }


    [Fact]
    public void An_if_else_jumps_over_the_else_and_the_else_falls_through()
    {
        Write("Flow2", """
            Scriptname Flow2
            Function Go(bool abFlag)
                if abFlag
                    Go(false)
                else
                    Go(true)
                endIf
            EndFunction
            """);
        Listing(Function(CompileObject("Flow2"), "Go")).Should().Equal(
            "jmpf abFlag 3",
            "callmethod Go self ::nonevar false",
            "jmp 2",
            "callmethod Go self ::nonevar true");
    }

    [Fact]
    public void A_while_loop_jumps_back_to_its_condition()
    {
        Write("Loop", """
            Scriptname Loop
            Function Go()
                int i = 0
                while i < 3
                    i += 1
                endWhile
            EndFunction
            """);
        Listing(Function(CompileObject("Loop"), "Go")).Should().Equal(
            "assign i 0",
            "cmp_lt T i 3",
            "jmpf T 3",
            "iadd i i 1",
            "jmp -3");
    }





    [Fact]
    public void And_short_circuits_through_one_shared_slot()
    {
        Write("Logic", """
            Scriptname Logic
            Function Go(bool abLeft, bool abRight)
                if abLeft && abRight
                    Go(false, false)
                endIf
            EndFunction
            """);
        Listing(Function(CompileObject("Logic"), "Go")).Should().Equal(
            "cast T abLeft",
            "jmpf T 2",
            "cast T abRight",
            "jmpf T 3",
            "callmethod Go self ::nonevar false false",
            "jmp 1");
    }

    [Fact]
    public void Or_short_circuits_on_true()
    {
        Write("Logic2", """
            Scriptname Logic2
            Function Go(bool abLeft, bool abRight)
                if abLeft || abRight
                    Go(false, false)
                endIf
            EndFunction
            """);
        Listing(Function(CompileObject("Logic2"), "Go"))[1].Should().Be("jmpt T 2");
    }




    [Fact]
    public void Inequality_is_equality_followed_by_not()
    {
        Write("Cmp", """
            Scriptname Cmp
            Function Go(int aiLeft)
                bool b = aiLeft != 2
            EndFunction
            """);
        Listing(Function(CompileObject("Cmp"), "Go")).Should().Equal(
            "cmp_eq b aiLeft 2",
            "not b b");
    }


    [Fact]
    public void A_mixed_comparison_promotes_the_int_side_to_float()
    {
        Write("Promote", """
            Scriptname Promote
            Function Go(float afValue, int aiOther)
                bool b = afValue > aiOther
            EndFunction
            """);
        Listing(Function(CompileObject("Promote"), "Go")).Should().Equal(
            "cast T aiOther",
            "cmp_gt b afValue T");
    }


    [Fact]
    public void An_int_literal_in_a_float_slot_is_folded()
    {
        Write("Fold", """
            Scriptname Fold
            Function Go()
                float f = 5
            EndFunction
            """);
        Listing(Function(CompileObject("Fold"), "Go")).Should().Equal("assign f 5.0");
    }

    [Fact]
    public void String_concatenation_uses_strcat_and_casts_the_other_side()
    {
        Write("Cat", """
            Scriptname Cat
            Function Go(int aiCount)
                string s = "n=" + aiCount
            EndFunction
            """);
        Listing(Function(CompileObject("Cat"), "Go")).Should().Equal(
            "cast T aiCount",
            "strcat s \"n=\" T");
    }

    [Fact]
    public void A_local_with_no_initialiser_is_still_zeroed()
    {
        Write("Zero", """
            Scriptname Zero
            Function Go()
                int i
                string s
            EndFunction
            """);
        Listing(Function(CompileObject("Zero"), "Go")).Should().Equal(
            "assign i 0",
            "assign s \"\"");
    }

    [Fact]
    public void Compound_assignment_reads_modifies_and_writes_in_place()
    {
        Write("Compound", """
            Scriptname Compound
            Function Go()
                float f = 1.0
                f *= 2.0
            EndFunction
            """);
        Listing(Function(CompileObject("Compound"), "Go")).Should().Equal(
            "assign f 1.0",
            "fmul f f 2.0");
    }

    [Fact]
    public void New_array_and_element_access_use_the_array_opcodes()
    {
        Write("Arrays", """
            Scriptname Arrays
            Function Go()
                int[] xs = new int[4]
                xs[0] = 7
                int n = xs.Length
                xs.Add(9)
                int at = xs.Find(9)
            EndFunction
            """);
        Listing(Function(CompileObject("Arrays"), "Go")).Should().Equal(
            "array_create xs 4",
            "array_setelement xs 0 7",
            "array_length n xs",
            "array_add xs 9 1",
            "array_findelement xs at 9 0");
    }

    [Fact]
    public void Struct_members_are_read_and_written_through_the_struct_opcodes()
    {
        Write("Structs", """
            Scriptname Structs
            Struct Point
                float fX = 0.0
            EndStruct
            Function Go()
                Point p = new Point
                p.fX = 2.0
                float got = p.fX
            EndFunction
            """);
        Listing(Function(CompileObject("Structs"), "Go")).Should().Equal(
            "struct_create p",
            "assign T 2.0",
            "struct_set p fX T",
            "struct_get got p fX");
    }

    [Fact]
    public void The_is_operator_names_its_type_as_an_identifier()
    {
        Write("Other", "Scriptname Other\n");
        Write("Check", """
            Scriptname Check
            Function Go(ScriptObject akThing)
                bool b = akThing is Other
            EndFunction
            """);
        Listing(Function(CompileObject("Check"), "Go")).Should().Equal("is b akThing Other");
    }







    [Fact]
    public void Optional_arguments_are_filled_in_from_the_declaration()
    {
        Write("Defaults", """
            Scriptname Defaults
            Function Target(int aiFirst, bool abSecond = true, string asThird = "x")
            EndFunction
            Function Go()
                Target(1)
            EndFunction
            """);
        Listing(Function(CompileObject("Defaults"), "Go")).Should().Equal(
            "callmethod Target self ::nonevar 1 true \"x\"");
    }

    [Fact]
    public void A_named_argument_is_placed_by_name()
    {
        Write("Named", """
            Scriptname Named
            Function Target(int aiFirst = 1, int aiSecond = 2)
            EndFunction
            Function Go()
                Target(aiSecond = 9)
            EndFunction
            """);
        Listing(Function(CompileObject("Named"), "Go")).Should().Equal(
            "callmethod Target self ::nonevar 1 9");
    }


    [Fact]
    public void A_bare_call_to_an_own_global_is_a_static_call_on_this_script()
    {
        Write("Globals", """
            Scriptname Globals
            int Function Helper() Global
                return 1
            EndFunction
            int Function Go() Global
                return Helper()
            EndFunction
            """);
        Listing(Function(CompileObject("Globals"), "Go")).Should().Equal(
            "callstatic Globals Helper T",
            "return T");
    }

    [Fact]
    public void A_qualified_global_call_names_the_receiving_script()
    {
        Write("Util", "Scriptname Util\nint Function Read() Global\n    return 2\nEndFunction\n");
        Write("Caller", """
            Scriptname Caller
            Function Go()
                int n = Util.Read()
            EndFunction
            """);
        Listing(Function(CompileObject("Caller"), "Go")).Should().Equal("callstatic Util Read n");
    }

    [Fact]
    public void A_parent_call_uses_callparent()
    {
        Write("Base", "Scriptname Base\nFunction Poke()\nEndFunction\n");
        Write("Derived", """
            Scriptname Derived extends Base
            Function Poke()
                Parent.Poke()
            EndFunction
            """);
        Listing(Function(CompileObject("Derived"), "Poke")).Should().Equal(
            "callparent Poke ::nonevar");
    }





    [Fact]
    public void A_method_calls_receiver_is_evaluated_before_its_arguments()
    {
        Write("Order", """
            Scriptname Order
            Order Function First() Global
                return None
            EndFunction
            int Function Second() Global
                return 1
            EndFunction
            Function Take(int aiValue)
            EndFunction
            Function Go() Global
                First().Take(Second())
            EndFunction
            """);
        Listing(Function(CompileObject("Order"), "Go")).Should().Equal(
            "callstatic Order First T",
            "callstatic Order Second T",
            "callmethod Take T ::nonevar T");
    }





    [Fact]
    public void An_own_auto_property_is_read_and_written_through_its_backing_variable()
    {
        Write("Self", """
            Scriptname Self
            int Property Count Auto
            Function Go()
                Count = Count + 1
            EndFunction
            """);
        Listing(Function(CompileObject("Self"), "Go")).Should().Equal(
            "iadd ::Count_var ::Count_var 1");
    }

    [Fact]
    public void Another_objects_property_goes_through_propget_and_propset()
    {
        Write("Holder", "Scriptname Holder\nint Property Count Auto\n");
        Write("User", """
            Scriptname User
            Function Go(Holder akHolder)
                akHolder.Count = 5
                int n = akHolder.Count
            EndFunction
            """);





        Listing(Function(CompileObject("User"), "Go")).Should().Equal(
            "assign T 5",
            "propset Count akHolder T",
            "propget Count akHolder n");
    }


    [Fact]
    public void A_remote_event_handler_is_named_after_its_type_and_event()
    {
        Write("Sender", "Scriptname Sender\n");
        Write("Listener", """
            Scriptname Listener
            Event Sender.OnSomething(Sender akSender)
            EndEvent
            """);
        var obj = CompileObject("Listener");
        obj.States.Single().Functions.Single().Name.Should().Be("::remote_Sender_OnSomething");
    }

    [Fact]
    public void A_void_call_written_as_a_statement_discards_into_the_none_local()
    {
        Write("Void", """
            Scriptname Void
            int Function Answer()
                return 1
            EndFunction
            Function Nothing()
            EndFunction
            Function Go()
                Nothing()
                Answer()
            EndFunction
            """);
        var fn = Function(CompileObject("Void"), "Go");
        Listing(fn).Should().Equal(
            "callmethod Nothing self ::nonevar",
            "callmethod Answer self T");
        fn.Locals.Should().Contain(l => l.Name == "::nonevar" && l.Type == "None");
    }







    [Fact]
    public void A_call_into_a_script_that_is_not_on_the_roots_is_refused_rather_than_guessed()
    {
        Write("Blind", """
            Scriptname Blind extends MissingBase
            Function Go()
                SomeInheritedThing()
            EndFunction
            """);
        var result = Compile("Blind");

        result.Success.Should().BeFalse();
        result.SourcesComplete.Should().BeFalse();
        result.Pex.Should().BeNull();
    }







    [Theory]
    [InlineData("xs.Add()")]
    [InlineData("xs.Insert(1)")]
    [InlineData("xs.Remove()")]
    [InlineData("xs.Clear(1)")]
    [InlineData("xs.RemoveLast(1)")]
    public void An_array_builtin_with_the_wrong_argument_count_is_refused_not_thrown(string call)
    {
        Write("BadArray", $"""
            Scriptname BadArray
            Function Go()
                int[] xs = new int[2]
                {call}
            EndFunction
            """);

        var result = Compile("BadArray");

        result.Success.Should().BeFalse();
        result.Errors.Should().Contain(d => d.Code == PapyrusDiagnosticCodes.ArgumentCount);
    }

    [Theory]
    [InlineData("xs.Find(1)")]
    [InlineData("xs.Find(1, 2)")]
    [InlineData("xs.Add(1)")]
    [InlineData("xs.Add(1, 3)")]
    [InlineData("xs.Insert(1, 0)")]
    [InlineData("xs.Remove(0)")]
    [InlineData("xs.RemoveLast()")]
    [InlineData("xs.Clear()")]
    public void An_array_builtin_with_a_legal_argument_count_still_compiles(string call)
    {
        Write("GoodArray", $"""
            Scriptname GoodArray
            Function Go()
                int[] xs = new int[2]
                {call}
            EndFunction
            """);

        Compile("GoodArray").Success.Should().BeTrue();
    }





    [Fact]
    public void An_array_builtin_with_no_opcode_is_refused_by_name()
    {
        Write("NoOpcode", """
            Scriptname NoOpcode
            Struct Pair
                int iKey = 0
            EndStruct
            Function Go()
                Pair[] xs = new Pair[2]
                Pair[] hits = xs.GetMatchingStructs("iKey", 1)
            EndFunction
            """);

        var result = Compile("NoOpcode");

        result.Success.Should().BeFalse();
        result.Errors.Should().Contain(d => d.Message.Contains("GetMatchingStructs"));
    }

    [Fact]
    public void A_non_literal_variable_initialiser_is_refused()
    {
        Write("BadInit", "Scriptname BadInit\nint Property Count = 1 + 1 Auto\n");
        var result = Compile("BadInit");

        result.Success.Should().BeFalse();
        result.Errors.Should().Contain(
            d => d.Code == PapyrusDiagnosticCodes.NonConstantInitializer);
    }







    [Fact]
    public void A_generated_file_writes_and_reads_back_identically()
    {
        Write("RoundTrip", """
            Scriptname RoundTrip
            {A script with most of the shapes in it}
            Struct Pair
                int iKey = 0
                string sValue = ""
            EndStruct
            int Property Count = 1 Auto Hidden
            int Property Mask = 8 AutoReadOnly
            string mName
            Event OnInit()
                Count += 1
                Pair p = new Pair
                p.iKey = Count
                if Count > 2 && mName != ""
                    mName = "big"
                else
                    mName = "small"
                endIf
                int i = 0
                while i < Count
                    i += 1
                endWhile
            EndEvent
            """);
        var result = Compile("RoundTrip");
        result.Success.Should().BeTrue(
            string.Join(" | ", result.Diagnostics.Select(d => d.ToString())));

        var bytes = result.Pex!.ToBytes();
        var reread = PexFile.FromBytes(bytes);
        reread.ToBytes().Should().Equal(bytes);

        reread.Objects.Single().Name.Should().Be("RoundTrip");
        reread.HasDebugInfo.Should().BeTrue();
        reread.DebugFunctions.Should().Contain(d => d.FunctionName == "OnInit");
    }


    [Fact]
    public void Debug_line_numbers_are_recorded_one_per_instruction()
    {
        Write("Lines", """
            Scriptname Lines
            Function Go()
                int a = 1
                int b = 2
            EndFunction
            """);
        var result = Compile("Lines");
        var debug = result.Pex!.DebugFunctions.Single(d => d.FunctionName == "Go");

        debug.LineNumbers.Should().Equal(3, 4);
    }






    [Fact]
    public void A_debug_only_call_is_dropped_only_when_asked_for()
    {
        Write("Logger", "Scriptname Logger Native DebugOnly Hidden\nFunction Say(string asText) Global\nEndFunction\n");
        Write("Talker", """
            Scriptname Talker
            Function Go()
                Logger.Say("hello")
            EndFunction
            """);

        var index = new PapyrusScriptIndex();
        index.AddRoot(_root);
        var path = Path.Combine(_root, "Talker.psc");

        var kept = new PapyrusCompiler(index).CompileFile(path);
        Listing(Function(kept.Pex!.Objects.Single(), "Go")).Should().Equal(
            "callstatic Logger Say ::nonevar \"hello\"");

        var stripped = new PapyrusCompiler(index).CompileFile(
            path, new PapyrusCompileOptions { EmitDebugOnlyCode = false });
        Listing(Function(stripped.Pex!.Objects.Single(), "Go")).Should().BeEmpty();
    }
}








public class PapyrusCompileServiceTests : IDisposable
{
    private readonly string _root;
    private readonly string _out;

    public PapyrusCompileServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "fo4re-compilesvc-" + Guid.NewGuid().ToString("N"));
        _out = Path.Combine(_root, "out");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private string Write(string scriptName, string source)
    {
        var path = Path.Combine(_root, scriptName.Replace(':', Path.DirectorySeparatorChar) + ".psc");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, source);
        return path;
    }

    [Fact]
    public void A_script_compiles_to_a_pex_that_reads_back()
    {
        var file = Write("Simple", """
            Scriptname Simple
            int Property Count = 2 Auto
            Function Bump()
                Count += 1
            EndFunction
            """);

        var result = PapyrusAnalysisService.Compile(file, _out);
        result.Should().StartWith("RESULT: 1 succeeded, 0 failed");

        var written = Path.Combine(_out, "Simple.pex");
        File.Exists(written).Should().BeTrue();

        var pex = PexFile.ReadFile(written);
        pex.Objects.Single().Name.Should().Be("Simple");
        pex.Objects.Single().States.Single().Functions.Single().Name.Should().Be("Bump");
    }


    [Fact]
    public void A_namespaced_script_is_written_into_namespace_folders()
    {
        var file = Write("MyNS:Inner", "Scriptname MyNS:Inner\n");

        PapyrusAnalysisService.Compile(file, _out).Should().StartWith("RESULT: 1 succeeded");
        File.Exists(Path.Combine(_out, "MyNS", "Inner.pex")).Should().BeTrue();
    }





    [Fact]
    public void A_missing_import_root_is_reported_as_a_missing_import_root()
    {
        var file = Write("Needy", """
            Scriptname Needy extends SomethingNotHere
            Function Go()
                Inherited()
            EndFunction
            """);

        var result = PapyrusAnalysisService.Compile(file, _out);

        result.Should().StartWith("RESULT: 0 succeeded, 1 failed");
        result.Should().Contain("could not see every script they refer to");
        result.Should().Contain("imports");
        Directory.Exists(_out).Should().BeFalse("nothing should be written for a file that did not compile");
    }

    [Fact]
    public void A_folder_compiles_every_script_under_it()
    {
        Write("One", "Scriptname One\n");
        Write("Two", "Scriptname Two\n");

        PapyrusAnalysisService.Compile(_root, _out).Should().StartWith("RESULT: 2 succeeded, 0 failed");
        File.Exists(Path.Combine(_out, "One.pex")).Should().BeTrue();
        File.Exists(Path.Combine(_out, "Two.pex")).Should().BeTrue();
    }







    [Fact]
    public void A_folder_compile_sees_scripts_under_a_dotted_or_hidden_directory()
    {
        Write("Visible", "Scriptname Visible\n");

        var hidden = Path.Combine(_root, ".worktree");
        Directory.CreateDirectory(hidden);
        if (OperatingSystem.IsWindows())
            File.SetAttributes(hidden, File.GetAttributes(hidden) | FileAttributes.Hidden);
        File.WriteAllText(Path.Combine(hidden, "Tucked.psc"), "Scriptname Tucked\n");

        PapyrusAnalysisService.Compile(_root, _out).Should().StartWith("RESULT: 2 succeeded, 0 failed");
        File.Exists(Path.Combine(_out, "Tucked.pex")).Should().BeTrue();


        PapyrusAnalysisService.Check(_root, semantic: false).Should().Contain("of 2 file(s)");
    }





    [Fact]
    public void A_script_under_a_Source_User_tree_resolves_its_siblings_without_extra_roots()
    {
        var user = Path.Combine(_root, "Scripts", "Source", "User");
        Directory.CreateDirectory(Path.Combine(user, "Alpha"));
        Directory.CreateDirectory(Path.Combine(user, "Beta"));
        File.WriteAllText(Path.Combine(user, "Beta", "Helper.psc"),
            "Scriptname Beta:Helper\nint Function Answer() Global\n    return 42\nEndFunction\n");
        var caller = Path.Combine(user, "Alpha", "Caller.psc");
        File.WriteAllText(caller,
            "Scriptname Alpha:Caller\nFunction Go()\n    int n = Beta:Helper.Answer()\nEndFunction\n");

        PapyrusAnalysisService.Compile(caller, _out).Should().StartWith("RESULT: 1 succeeded");
        File.Exists(Path.Combine(_out, "Alpha", "Caller.pex")).Should().BeTrue();
    }


    [Fact]
    public void Natural_roots_are_normalised_so_a_trailing_separator_does_not_duplicate_one()
    {
        var withSeparator = _root + Path.DirectorySeparatorChar;

        PapyrusAnalysisService.NaturalRootsFor(withSeparator)
            .Should().Equal(PapyrusAnalysisService.NaturalRootsFor(_root));
    }

    [Fact]
    public void An_assembly_listing_is_rejected_with_the_fix_rather_than_a_parse_error()
    {
        var pas = Path.Combine(_root, "Thing.pas");
        File.WriteAllText(pas, "; not source\n");

        PapyrusAnalysisService.Compile(pas, _out).Should().Contain("Compile the .psc instead");
    }





    [Fact]
    public void Release_strips_debug_only_calls()
    {
        Write("Logger", "Scriptname Logger Native DebugOnly Hidden\nFunction Say(string asText) Global\nEndFunction\n");
        var file = Write("Talker", """
            Scriptname Talker
            Function Go()
                Logger.Say("hello")
            EndFunction
            """);

        PapyrusAnalysisService.Compile(file, _out, release: false).Should().StartWith("RESULT: 1 succeeded");
        Instructions(Path.Combine(_out, "Talker.pex")).Should().Be(1);

        var releaseOut = Path.Combine(_root, "release");
        var result = PapyrusAnalysisService.Compile(file, releaseOut, release: true);
        result.Should().Contain("DebugOnly and BetaOnly calls are not compiled");
        Instructions(Path.Combine(releaseOut, "Talker.pex")).Should().Be(0);

        static int Instructions(string pex) =>
            PexFile.ReadFile(pex).Objects.Single().States.Single()
                .Functions.Single(f => f.Name == "Go").Instructions.Count;
    }
}


public class PapyrusUserFlagTableTests
{
    [Fact]
    public void The_built_in_table_matches_the_bits_every_real_pex_carries()
    {
        var table = PapyrusUserFlagTable.Fallout4Default();

        table.Flags.Select(f => (f.Name, f.Index)).Should().Equal(
            ("hidden", (byte)0),
            ("conditional", (byte)1),
            ("default", (byte)2),
            ("collapsedonref", (byte)3),
            ("collapsedonbase", (byte)4),
            ("mandatory", (byte)5));
    }

    [Fact]
    public void A_composite_flag_expands_to_its_children_and_owns_no_bit()
    {
        var table = PapyrusUserFlagTable.Parse("""
            Flag CollapsedOnRef 3 { Group }
            Flag CollapsedOnBase 4 { Group }
            Flag Collapsed CollapsedOnRef & CollapsedOnBase
            """);

        table.MaskFor("Collapsed").Should().Be((1u << 3) | (1u << 4));
        table.Flags.Should().HaveCount(2, "a composite appears only as the flags it is made of");
    }

    [Fact]
    public void Comments_and_the_allowed_kinds_list_are_ignored()
    {
        var table = PapyrusUserFlagTable.Parse("""
            /* header
               comment */
            // Flag NotReal 9
            Flag Mandatory 5
            {
                Property
            }
            """);

        table.MaskFor("Mandatory").Should().Be(32u);
        table.Knows("NotReal").Should().BeFalse();
    }

    [Fact]
    public void An_unknown_name_contributes_nothing_so_language_keywords_pass_through_harmlessly()
    {
        var table = PapyrusUserFlagTable.Fallout4Default();
        table.MaskFor(new[] { "auto", "const", "hidden" }).Should().Be(1u);
    }
}
