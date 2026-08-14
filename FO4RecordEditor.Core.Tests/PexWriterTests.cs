using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using FO4RecordEditor.Services.Papyrus;

namespace FO4RecordEditor.Core.Tests;

/// <summary>
/// The .pex writer, checked against the reader it inverts.
/// </summary>
/// <remarks>
/// The bar here is byte-identical round tripping, not "it parses back". A writer that loses a field
/// quietly still round trips through a model comparison if the reader drops the same field, so the
/// bytes are the only check that closes that loop. <see cref="PexCorpusTests"/> applies the same bar
/// to real compiler output; this file covers the shapes a corpus might not contain and the failures
/// a corpus cannot produce.
/// </remarks>
public class PexWriterTests
{
    // A file exercising every construct the format has: struct with a defaulted member, object
    // variable, auto property, full property with both handlers, a state with a function carrying
    // locals and a vararg call, every PexValue type, debug info, property groups, struct orders and
    // user flags.
    private static PexFile Sample()
    {
        var strings = new[]
        {
            "", "MyScript", "ScriptObject", "MyScript.psc", "docs", "Point", "X", "Y", "Int",
            "MyVar", "Bool", "MyProp", "Float", "::MyProp_var", "String", "GetName", "None",
            "Run", "self", "arg", "temp", "Log", "Hello", "Stats", "Auto", "State1", "value",
        };

        var pex = new PexFile
        {
            MajorVersion = 3,
            MinorVersion = 9,
            GameId = 2,
            CompilationTime = 1_700_000_000L,
            SourceFileName = "MyScript.psc",
            UserName = "tester",
            ComputerName = "box",
            HasDebugInfo = true,
            ModificationTime = 1_700_000_001L,
        };
        pex.StringTable.AddRange(strings);
        pex.UserFlags.Add(new PexUserFlag { Name = "Auto", Index = 5 });

        var obj = new PexObject
        {
            Name = "MyScript",
            ParentClassName = "ScriptObject",
            DocString = "docs",
            Const = false,
            UserFlags = 0x20,
            AutoStateName = "State1",
        };

        obj.Structs.Add(new PexStruct
        {
            Name = "Point",
            Members =
            {
                new PexStructMember { Name = "X", Type = "Int", DocString = "docs", DefaultValue = new PexValue { Type = PexValueType.Integer, Int = -7 } },
                new PexStructMember { Name = "Y", Type = "Float", DocString = "", Const = true, DefaultValue = new PexValue { Type = PexValueType.Float, Float = 1.5f } },
            },
        });

        obj.Variables.Add(new PexVariable
        {
            Name = "MyVar",
            Type = "Bool",
            UserFlags = 1,
            DefaultValue = new PexValue { Type = PexValueType.Bool, Bool = true },
        });

        obj.Properties.Add(new PexProperty
        {
            Name = "MyProp", Type = "Float", DocString = "docs", Flags = 0x07, AutoVarName = "::MyProp_var",
        });

        var getter = new PexFunction { ReturnType = "String", DocString = "docs" };
        getter.Instructions.Add(new PexInstruction
        {
            OpCode = 26, Mnemonic = "return", FixedArgCount = 1,
            Args = { new PexValue { Type = PexValueType.String, Str = "Hello" } },
        });
        var setter = new PexFunction { ReturnType = "None", DocString = "" };
        setter.Params.Add(new PexTypedName { Name = "value", Type = "String" });
        setter.Instructions.Add(new PexInstruction { OpCode = 0, Mnemonic = "nop", FixedArgCount = 0 });
        obj.Properties.Add(new PexProperty
        {
            Name = "GetName", Type = "String", DocString = "", Flags = 0x03,
            ReadHandler = getter, WriteHandler = setter,
        });

        var run = new PexFunction { Name = "Run", ReturnType = "None", DocString = "docs", UserFlags = 2, IsGlobal = true };
        run.Params.Add(new PexTypedName { Name = "arg", Type = "Int" });
        run.Locals.Add(new PexTypedName { Name = "temp", Type = "String" });
        // callmethod: 3 fixed operands then a vararg list. Two varargs here, one of them None.
        run.Instructions.Add(new PexInstruction
        {
            OpCode = 23, Mnemonic = "callmethod", FixedArgCount = 3, HasVarArgs = true,
            Args =
            {
                new PexValue { Type = PexValueType.Identifier, Str = "Log" },
                new PexValue { Type = PexValueType.Identifier, Str = "self" },
                new PexValue { Type = PexValueType.Identifier, Str = "temp" },
                new PexValue { Type = PexValueType.String, Str = "Hello" },
                new PexValue { Type = PexValueType.None },
            },
        });
        obj.States.Add(new PexState { Name = "State1", Functions = { run } });
        obj.States.Add(new PexState { Name = "", Functions = { } });
        pex.Objects.Add(obj);

        pex.DebugFunctions.Add(new PexDebugFunction
        {
            ObjectName = "MyScript", StateName = "State1", FunctionName = "Run",
            FunctionType = 0, LineNumbers = { 12 },
        });
        pex.PropertyGroups.Add(new PexPropertyGroup
        {
            ObjectName = "MyScript", GroupName = "Stats", DocString = "docs",
            UserFlags = 3, PropertyNames = { "MyProp", "GetName" },
        });
        pex.StructOrders.Add(new PexStructOrder
        {
            ObjectName = "MyScript", OrderName = "Point", MemberNames = { "X", "Y" },
        });
        return pex;
    }

    [Fact]
    public void A_written_file_reads_back_with_the_same_bytes()
    {
        var first = Sample().ToBytes();
        var second = PexFile.FromBytes(first).ToBytes();

        second.Should().Equal(first, "read(write(x)) must reproduce write(x) exactly");
    }

    [Fact]
    public void A_written_file_reads_back_with_the_same_content()
    {
        var back = PexFile.FromBytes(Sample().ToBytes());

        back.MajorVersion.Should().Be(3);
        back.MinorVersion.Should().Be(9);
        back.GameId.Should().Be(2);
        back.CompilationTime.Should().Be(1_700_000_000L);
        back.SourceFileName.Should().Be("MyScript.psc");
        back.UserName.Should().Be("tester");
        back.ComputerName.Should().Be("box");
        back.HasDebugInfo.Should().BeTrue();
        back.ModificationTime.Should().Be(1_700_000_001L);
        back.StringTable.Should().Equal(Sample().StringTable);
        back.UserFlags.Single().Name.Should().Be("Auto");
        back.UserFlags.Single().Index.Should().Be(5);

        var obj = back.Objects.Single();
        obj.Name.Should().Be("MyScript");
        obj.ParentClassName.Should().Be("ScriptObject");
        obj.UserFlags.Should().Be(0x20u);
        obj.AutoStateName.Should().Be("State1");

        var point = obj.Structs.Single();
        point.Members.Should().HaveCount(2);
        point.Members[0].DefaultValue!.Int.Should().Be(-7);
        point.Members[1].DefaultValue!.Float.Should().Be(1.5f);
        point.Members[1].Const.Should().BeTrue();

        obj.Variables.Single().DefaultValue!.Bool.Should().BeTrue();

        obj.Properties[0].IsAutoVar.Should().BeTrue();
        obj.Properties[0].AutoVarName.Should().Be("::MyProp_var");
        obj.Properties[1].ReadHandler!.Instructions.Single().Args[0].Str.Should().Be("Hello");
        obj.Properties[1].WriteHandler!.Params.Single().Name.Should().Be("value");

        var run = obj.States.Single(s => s.Name == "State1").Functions.Single();
        run.Name.Should().Be("Run");
        run.IsGlobal.Should().BeTrue();
        run.Params.Single().Type.Should().Be("Int");
        run.Locals.Single().Name.Should().Be("temp");

        var call = run.Instructions.Single();
        call.Mnemonic.Should().Be("callmethod");
        call.Args.Should().HaveCount(5, "three fixed operands and two varargs");
        call.VarArgs.Should().HaveCount(2);
        call.VarArgs.Last().IsNoneType.Should().BeTrue();
        call.Line.Should().Be(12, "the debug table supplies it");

        obj.States.Should().Contain(s => s.Name == "" && s.Functions.Count == 0);
        back.PropertyGroups.Single().PropertyNames.Should().Equal("MyProp", "GetName");
        back.StructOrders.Single().MemberNames.Should().Equal("X", "Y");
    }

    // The reason the writer keeps the table verbatim instead of rebuilding it: rebuilding would
    // renumber every index, and nothing about the format requires the new numbering to match.
    [Fact]
    public void The_string_table_is_written_in_its_original_order()
    {
        var pex = Sample();
        var back = PexFile.FromBytes(pex.ToBytes());

        back.StringTable.Should().Equal(pex.StringTable);
    }

    // Sixteen of the 1,496 loose .pex on the development machine carry four bytes past the last
    // object. Dropping them would silently produce a shorter file than the one that was read.
    [Fact]
    public void Bytes_past_the_last_object_survive_the_round_trip()
    {
        var pex = Sample();
        pex.TrailingBytes = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };

        var back = PexFile.FromBytes(pex.ToBytes());

        back.TrailingBytes.Should().Equal(pex.TrailingBytes);
        back.ToBytes().Should().Equal(pex.ToBytes());
    }

    [Fact]
    public void A_file_with_no_debug_info_round_trips()
    {
        var pex = Sample();
        pex.HasDebugInfo = false;
        pex.DebugFunctions.Clear();
        pex.PropertyGroups.Clear();
        pex.StructOrders.Clear();

        var bytes = pex.ToBytes();
        var back = PexFile.FromBytes(bytes);

        back.HasDebugInfo.Should().BeFalse();
        back.Objects.Single().States.Single(s => s.Name == "State1").Functions.Single()
            .Instructions.Single().Line.Should().Be(0, "there is no debug table to supply one");
        back.ToBytes().Should().Equal(bytes);
    }

    // The object header carries a byte count, and getting it wrong is the failure most likely to
    // produce a file that still reads back through our own reader (which had ignored the field)
    // while the game rejects it. Real files use two conventions; a fresh object gets the majority
    // one, and the other has to survive a round trip because 16 real files depend on it.
    [Theory]
    [InlineData(true, 4)]
    [InlineData(false, 0)]
    public void The_object_size_field_is_written_in_the_convention_the_object_carries(
        bool includesItself, int expectedOverhead)
    {
        var pex = Sample();
        pex.Objects[0].SizeIncludesItself = includesItself;
        var bytes = pex.ToBytes();

        ReadObjectSizeField(bytes, out var size, out var bodyBytes);

        size.Should().Be((uint)(bodyBytes + expectedOverhead));
        PexFile.FromBytes(bytes).Objects[0].SizeIncludesItself.Should().Be(includesItself,
            "the reader has to recover the convention or the writer cannot reproduce it");
    }

    [Fact]
    public void A_freshly_built_object_uses_the_majority_convention()
    {
        new PexObject().SizeIncludesItself.Should().BeTrue(
            "1,480 of the 1,496 real .pex measured count the size field itself");
    }

    private static void ReadObjectSizeField(byte[] bytes, out uint size, out long bodyBytes)
    {

        // Walk the header to the single object's size field.
        using var ms = new MemoryStream(bytes);
        using var r = new BinaryReader(ms, System.Text.Encoding.Latin1);
        r.ReadUInt32();                                     // magic
        r.ReadByte(); r.ReadByte(); r.ReadUInt16();         // version, gameId
        r.ReadInt64();                                      // compilation time
        SkipStr(r); SkipStr(r); SkipStr(r);
        int strCount = r.ReadUInt16();
        for (int i = 0; i < strCount; i++) SkipStr(r);
        r.ReadByte();                                       // hasDebugInfo
        r.ReadInt64();                                      // modification time
        int fnCount = r.ReadUInt16();
        for (int i = 0; i < fnCount; i++)
        {
            r.ReadUInt16(); r.ReadUInt16(); r.ReadUInt16(); r.ReadByte();
            int lines = r.ReadUInt16();
            r.ReadBytes(lines * 2);
        }
        int pgCount = r.ReadUInt16();
        for (int i = 0; i < pgCount; i++)
        {
            r.ReadUInt16(); r.ReadUInt16(); r.ReadUInt16(); r.ReadUInt32();
            r.ReadBytes(r.ReadUInt16() * 2);
        }
        int soCount = r.ReadUInt16();
        for (int i = 0; i < soCount; i++)
        {
            r.ReadUInt16(); r.ReadUInt16();
            r.ReadBytes(r.ReadUInt16() * 2);
        }
        int ufCount = r.ReadUInt16();
        for (int i = 0; i < ufCount; i++) { r.ReadUInt16(); r.ReadByte(); }
        r.ReadUInt16().Should().Be(1, "the sample has one object");
        r.ReadUInt16();                                     // object name index
        size = r.ReadUInt32();
        bodyBytes = bytes.Length - ms.Position;
    }

    private static void SkipStr(BinaryReader r) => r.ReadBytes(r.ReadUInt16());

    // ---- refusals, rather than corrupt output ----

    [Fact]
    public void A_string_missing_from_the_table_is_refused_rather_than_written_wrong()
    {
        var pex = Sample();
        pex.Objects[0].Variables[0].Type = "NotInTheTable";

        var write = () => pex.ToBytes();

        write.Should().Throw<InvalidDataException>().WithMessage("*NotInTheTable*string table*");
    }

    [Fact]
    public void A_property_flagged_readable_with_no_handler_is_refused()
    {
        var pex = Sample();
        pex.Objects[0].Properties[1].ReadHandler = null;

        var write = () => pex.ToBytes();

        write.Should().Throw<InvalidDataException>().WithMessage("*GetName*readable*");
    }

    [Fact]
    public void An_instruction_with_the_wrong_operand_count_is_refused()
    {
        var pex = Sample();
        var ret = new PexInstruction { OpCode = 26, Mnemonic = "return", FixedArgCount = 1 };
        pex.Objects[0].Properties[1].ReadHandler!.Instructions[0] = ret;   // return with no operand

        var write = () => pex.ToBytes();

        write.Should().Throw<InvalidDataException>().WithMessage("*return takes*1 operand*");
    }

    [Fact]
    public void An_unknown_opcode_is_refused()
    {
        var pex = Sample();
        pex.Objects[0].Properties[1].ReadHandler!.Instructions[0].OpCode = 0x7F;

        var write = () => pex.ToBytes();

        write.Should().Throw<InvalidDataException>().WithMessage("*0x7F*");
    }
}
