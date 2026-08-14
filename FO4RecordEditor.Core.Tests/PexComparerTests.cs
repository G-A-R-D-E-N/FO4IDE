using FluentAssertions;
using FO4RecordEditor.Services.Papyrus;

namespace FO4RecordEditor.Core.Tests;

/// <summary>
/// The shared <c>.pex</c> comparison contract: what it treats as a difference, and what it
/// deliberately does not.
/// </summary>
/// <remarks>
/// <see cref="PapyrusDifferentialTests"/> measures the code generator against the Creation Kit with
/// this, and the graph roundtrip oracles measure generated source against hand-written source with
/// it. Both readings only mean something if the ruler itself is pinned, which is what this file
/// does. The normalisation cases matter most: each one is a difference the comparer is being asked
/// to ignore, and an over-eager normaliser would quietly turn a real regression into a pass.
/// </remarks>
public class PexComparerTests
{
    private static PexValue Id(string name) => new() { Type = PexValueType.Identifier, Str = name };
    private static PexValue Int(int value) => new() { Type = PexValueType.Integer, Int = value };
    private static PexValue Flt(float value) => new() { Type = PexValueType.Float, Float = value };
    private static PexValue Str(string value) => new() { Type = PexValueType.String, Str = value };

    private static PexInstruction Op(string mnemonic, params PexValue[] args) =>
        new() { Mnemonic = mnemonic, FixedArgCount = args.Length, Args = new(args) };

    /// <summary>One object with one empty-state function holding the given instructions.</summary>
    private static PexFile File(string name, params PexInstruction[] instructions)
    {
        var fn = new PexFunction { Name = name, ReturnType = "None" };
        fn.Instructions.AddRange(instructions);
        var obj = new PexObject { Name = "MyScript", ParentClassName = "ScriptObject" };
        obj.States.Add(new PexState { Name = "", Functions = { fn } });
        var pex = new PexFile();
        pex.Objects.Add(obj);
        return pex;
    }

    // ---- agreement ---------------------------------------------------------------------------

    [Fact]
    public void Two_identical_objects_report_no_difference()
    {
        var a = File("Run", Op("callmethod", Id("Foo"), Id("self"), Id("::NoneVar")));
        var b = File("Run", Op("callmethod", Id("Foo"), Id("self"), Id("::NoneVar")));

        PexComparer.FirstDifference(a, b).Should().BeNull();
    }

    // ---- what is normalised away -------------------------------------------------------------

    [Fact]
    public void Temporary_numbering_is_not_a_difference()
    {
        var a = File("Run", Op("assign", Id("::temp3"), Int(1)));
        var b = File("Run", Op("assign", Id("::temp17"), Int(1)));

        PexComparer.FirstDifference(a, b).Should().BeNull(
            "the Creation Kit's temp counter is object wide and leaves gaps, so which number a "
            + "temporary got is not a function of the source");
    }

    [Fact]
    public void Mangled_local_numbering_is_not_a_difference()
    {
        var a = File("Run", Op("assign", Id("::mangled_a_1"), Int(1)));
        var b = File("Run", Op("assign", Id("::mangled_a_9"), Int(1)));

        PexComparer.FirstDifference(a, b).Should().BeNull();
    }

    [Fact]
    public void Identifier_case_is_not_a_difference()
    {
        var a = File("Run", Op("callstatic", Id("Debug"), Id("Notification")));
        var b = File("Run", Op("callstatic", Id("debug"), Id("notification")));

        PexComparer.FirstDifference(a, b).Should().BeNull("Papyrus identifiers are case insensitive");
    }

    [Fact]
    public void Float_formatting_below_the_compared_precision_is_not_a_difference()
    {
        // Guard against this quietly becoming a tautology: the two constants have to be genuinely
        // distinct as float32, or the test proves nothing about the normaliser.
        const float coarse = 1.5f, fine = 1.5000001f;
        fine.Should().NotBe(coarse);

        var a = File("Run", Op("assign", Id("x"), Flt(coarse)));
        var b = File("Run", Op("assign", Id("x"), Flt(fine)));

        PexComparer.FirstDifference(a, b).Should().BeNull();
    }

    [Fact]
    public void Function_declaration_order_is_not_a_difference()
    {
        var first = new PexFunction { Name = "Alpha", ReturnType = "None" };
        var second = new PexFunction { Name = "Beta", ReturnType = "None" };

        var a = new PexFile();
        a.Objects.Add(new PexObject
        {
            ParentClassName = "ScriptObject",
            States = { new PexState { Name = "", Functions = { first, second } } },
        });

        var b = new PexFile();
        b.Objects.Add(new PexObject
        {
            ParentClassName = "ScriptObject",
            States = { new PexState { Name = "", Functions = { second, first } } },
        });

        PexComparer.FirstDifference(a, b).Should().BeNull(
            "the Creation Kit writes members in a hash order that differs between compiles, so "
            + "functions are matched by name rather than position");
    }

    // ---- what is a difference ----------------------------------------------------------------

    [Fact]
    public void A_different_mnemonic_is_a_difference()
    {
        var a = File("Run", Op("cmp_eq", Id("r"), Id("x"), Id("y")));
        var b = File("Run", Op("cmp_lt", Id("r"), Id("x"), Id("y")));

        PexComparer.FirstDifference(a, b).Should().Contain("cmp_eq").And.Contain("cmp_lt");
    }

    [Fact]
    public void Swapped_operands_are_a_difference()
    {
        var a = File("Run", Op("cmp_lt", Id("r"), Id("x"), Id("y")));
        var b = File("Run", Op("cmp_lt", Id("r"), Id("y"), Id("x")));

        PexComparer.FirstDifference(a, b).Should().NotBeNull(
            "operand role and order carry the meaning of the instruction");
    }

    [Fact]
    public void A_different_jump_offset_is_a_difference()
    {
        var a = File("Run", Op("jmpf", Id("cond"), Int(3)));
        var b = File("Run", Op("jmpf", Id("cond"), Int(4)));

        PexComparer.FirstDifference(a, b).Should().NotBeNull(
            "jump offsets are integer operands and are compared exactly");
    }

    [Fact]
    public void A_different_string_literal_is_a_difference()
    {
        var a = File("Run", Op("callstatic", Id("Debug"), Id("Trace"), Str("opened")));
        var b = File("Run", Op("callstatic", Id("Debug"), Id("Trace"), Str("closed")));

        PexComparer.FirstDifference(a, b).Should().NotBeNull();
    }

    [Fact]
    public void A_longer_instruction_sequence_is_a_difference()
    {
        // The shared prefix has to agree, otherwise the comparer reports that difference first and
        // never reaches the length check.
        var a = File("Run", Op("assign", Id("x"), Int(1)));
        var b = File("Run", Op("assign", Id("x"), Int(1)), Op("return", Id("::NoneVar")));

        PexComparer.FirstDifference(a, b).Should().Contain("instructions vs");
    }

    [Fact]
    public void A_missing_function_is_a_difference()
    {
        var a = File("Run");
        var b = File("Walk");

        PexComparer.FirstDifference(a, b).Should().Contain("missing function Run");
    }

    [Fact]
    public void An_extra_function_is_a_difference()
    {
        var a = File("Run");
        var b = File("Run");
        b.Objects[0].States[0].Functions.Add(new PexFunction { Name = "Extra", ReturnType = "None" });

        PexComparer.FirstDifference(a, b).Should().Contain("extra function Extra");
    }

    [Fact]
    public void A_different_parent_class_is_a_difference()
    {
        var a = File("Run");
        var b = File("Run");
        b.Objects[0].ParentClassName = "ObjectReference";

        PexComparer.FirstDifference(a, b).Should().Contain("parent");
    }

    [Fact]
    public void A_different_return_type_is_a_difference()
    {
        var a = File("Run");
        var b = File("Run");
        b.Objects[0].States[0].Functions[0].ReturnType = "Bool";

        PexComparer.FirstDifference(a, b).Should().Contain("returns");
    }

    [Fact]
    public void Property_flags_and_user_flags_are_differences()
    {
        var a = File("Run");
        a.Objects[0].Properties.Add(new PexProperty { Name = "Target", Type = "Actor", Flags = 0x07 });

        var flagged = File("Run");
        flagged.Objects[0].Properties.Add(new PexProperty { Name = "Target", Type = "Actor", Flags = 0x01 });
        PexComparer.FirstDifference(a, flagged).Should().Contain("flags");

        var userFlagged = File("Run");
        userFlagged.Objects[0].Properties.Add(
            new PexProperty { Name = "Target", Type = "Actor", Flags = 0x07, UserFlags = 2 });
        PexComparer.FirstDifference(a, userFlagged).Should().Contain("user flags");
    }

    [Fact]
    public void A_missing_state_is_a_difference()
    {
        var a = File("Run");
        a.Objects[0].States.Add(new PexState { Name = "Busy" });
        var b = File("Run");

        PexComparer.FirstDifference(a, b).Should().Contain("missing state 'Busy'");
    }

    // ---- reporting ---------------------------------------------------------------------------

    [Fact]
    public void The_reported_difference_names_both_sides_with_the_given_labels()
    {
        var a = File("Run", Op("cmp_eq", Id("r")));
        var b = File("Run", Op("cmp_lt", Id("r")));

        PexComparer.FirstDifference(a, b, "graph", "reference")
            .Should().Contain("graph:").And.Contain("reference:");
    }

    [Fact]
    public void Functions_are_keyed_by_state_with_the_empty_state_using_bare_names()
    {
        var pex = File("Run");
        pex.Objects[0].States.Add(new PexState
        {
            Name = "Busy",
            Functions = { new PexFunction { Name = "Run", ReturnType = "None" } },
        });

        PexComparer.FunctionsOf(pex.Objects[0]).Keys
            .Should().BeEquivalentTo("Run", "Busy.Run");
    }
}
