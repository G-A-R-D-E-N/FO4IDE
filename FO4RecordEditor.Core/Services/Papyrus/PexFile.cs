using System.IO;
using System.Text;

namespace FO4RecordEditor.Services.Papyrus;

public enum PexValueType : byte { None = 0, Identifier = 1, String = 2, Integer = 3, Float = 4, Bool = 5 }

public sealed class PexValue
{
    public PexValueType Type;
    public string Str = "";
    public int Int;
    public float Float;
    public bool Bool;

    public bool IsNoneType => Type == PexValueType.None;
    public override string ToString() => Type switch
    {
        PexValueType.None => "None",
        PexValueType.Identifier => Str,
        PexValueType.String => "\"" + Str.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"",
        PexValueType.Integer => Int.ToString(),
        PexValueType.Float => Float.ToString("0.0###############", System.Globalization.CultureInfo.InvariantCulture),
        PexValueType.Bool => Bool ? "true" : "false",
        _ => "?",
    };
}

public sealed class PexInstruction
{
    public byte OpCode;
    public string Mnemonic = "";
    public int FixedArgCount;
    public bool HasVarArgs;
    public List<PexValue> Args = new();
    public int Line;

    public IEnumerable<PexValue> VarArgs => Args.Skip(FixedArgCount);
}

public sealed class PexTypedName { public string Name = ""; public string Type = ""; }

public sealed class PexFunction
{
    public string Name = "";
    public string ReturnType = "None";
    public string DocString = "";
    public uint UserFlags;
    public bool IsGlobal;
    public bool IsNative;
    public List<PexTypedName> Params = new();
    public List<PexTypedName> Locals = new();
    public List<PexInstruction> Instructions = new();
}

public sealed class PexState { public string Name = ""; public List<PexFunction> Functions = new(); }

public sealed class PexVariable
{
    public string Name = "";
    public string Type = "";
    public uint UserFlags;
    public PexValue? DefaultValue;
    public bool Const;
}

public sealed class PexProperty
{
    public string Name = "";
    public string Type = "";
    public string DocString = "";
    public uint UserFlags;
    public byte Flags;
    public bool IsAutoVar => (Flags & 0x04) != 0;
    public bool CanRead => (Flags & 0x01) != 0;
    public bool CanWrite => (Flags & 0x02) != 0;
    public string AutoVarName = "";
    public PexFunction? ReadHandler;
    public PexFunction? WriteHandler;
}

public sealed class PexStructMember
{
    public string Name = "";
    public string Type = "";
    public uint UserFlags;
    public PexValue? DefaultValue;
    public bool Const;
    public string DocString = "";
}

public sealed class PexStruct { public string Name = ""; public List<PexStructMember> Members = new(); }

public sealed class PexObject
{
    public string Name = "";
    public string ParentClassName = "";
    public string DocString = "";
    public bool Const;
    public uint UserFlags;
    public string AutoStateName = "";

    public bool SizeIncludesItself = true;

    public List<PexStruct> Structs = new();
    public List<PexVariable> Variables = new();
    public List<PexProperty> Properties = new();
    public List<PexState> States = new();
}

public sealed class PexUserFlag { public string Name = ""; public byte Index; }

public sealed class PexDebugFunction
{
    public string ObjectName = "", StateName = "", FunctionName = "";
    public byte FunctionType;
    public List<ushort> LineNumbers = new();
}

public sealed class PexPropertyGroup
{
    public string ObjectName = "", GroupName = "", DocString = "";
    public uint UserFlags;
    public List<string> PropertyNames = new();
}

public sealed class PexStructOrder
{
    public string ObjectName = "", OrderName = "";
    public List<string> MemberNames = new();
}

public sealed partial class PexFile
{

    public byte[] TrailingBytes = Array.Empty<byte>();

    public byte MajorVersion, MinorVersion;
    public ushort GameId;
    public long CompilationTime;
    public string SourceFileName = "", UserName = "", ComputerName = "";
    public List<string> StringTable = new();
    public bool HasDebugInfo;
    public long ModificationTime;
    public List<PexDebugFunction> DebugFunctions = new();
    public List<PexPropertyGroup> PropertyGroups = new();
    public List<PexStructOrder> StructOrders = new();
    public List<PexUserFlag> UserFlags = new();
    public List<PexObject> Objects = new();

    public static readonly (string name, int args, bool varargs)[] OpCodes =
    {
        ("nop", 0, false), ("iadd", 3, false), ("fadd", 3, false), ("isub", 3, false), ("fsub", 3, false),
        ("imul", 3, false), ("fmul", 3, false), ("idiv", 3, false), ("fdiv", 3, false), ("imod", 3, false),
        ("not", 2, false), ("ineg", 2, false), ("fneg", 2, false), ("assign", 2, false), ("cast", 2, false),
        ("cmp_eq", 3, false), ("cmp_lt", 3, false), ("cmp_lte", 3, false), ("cmp_gt", 3, false), ("cmp_gte", 3, false),
        ("jmp", 1, false), ("jmpt", 2, false), ("jmpf", 2, false),
        ("callmethod", 3, true), ("callparent", 2, true), ("callstatic", 3, true),
        ("return", 1, false), ("strcat", 3, false), ("propget", 3, false), ("propset", 3, false),
        ("array_create", 2, false), ("array_length", 2, false), ("array_getelement", 3, false), ("array_setelement", 3, false),
        ("array_findelement", 4, false), ("array_rfindelement", 4, false),

        ("is", 3, false), ("struct_create", 1, false), ("struct_get", 3, false), ("struct_set", 3, false),
        ("array_findstruct", 5, false), ("array_rfindstruct", 5, false),
        ("array_add", 3, false), ("array_insert", 3, false), ("array_removelast", 1, false),
        ("array_remove", 3, false), ("array_clear", 1, false),
    };

    public static PexFile ReadFile(string path)
    {
        using var fs = File.OpenRead(path);
        using var r = new BinaryReader(fs, Encoding.Latin1, leaveOpen: false);
        return Read(r);
    }

    public static PexFile Read(Stream stream)
    {
        using var r = new BinaryReader(stream, Encoding.Latin1, leaveOpen: true);
        return Read(r);
    }

    public static PexFile FromBytes(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes, writable: false);
        return Read(ms);
    }

    private static PexFile Read(BinaryReader r)
    {
        uint magic = r.ReadUInt32();
        if (magic == 0xDEC057FAu)
            throw new InvalidDataException("This is a big-endian (Skyrim LE) .pex; this decompiler targets Fallout 4 (little-endian).");
        if (magic != 0xFA57C0DEu)
            throw new InvalidDataException($"Not a .pex file (bad magic 0x{magic:X8}).");

        var pex = new PexFile
        {
            MajorVersion = r.ReadByte(),
            MinorVersion = r.ReadByte(),
            GameId = r.ReadUInt16(),
            CompilationTime = r.ReadInt64(),
        };

        if (pex.GameId != 2)
            throw new InvalidDataException(
                $"This .pex is for game id {pex.GameId} at format {pex.MajorVersion}.{pex.MinorVersion}; " +
                "Fallout 4 is game id 2 at format 3.9. It was built by another game's Papyrus compiler.");

        pex.SourceFileName = ReadStr(r);
        pex.UserName = ReadStr(r);
        pex.ComputerName = ReadStr(r);

        int strCount = r.ReadUInt16();
        for (int i = 0; i < strCount; i++) pex.StringTable.Add(ReadStr(r));
        string S(ushort idx) => idx < pex.StringTable.Count ? pex.StringTable[idx] : $"<str#{idx}>";

        pex.HasDebugInfo = r.ReadByte() != 0;
        if (pex.HasDebugInfo)
        {
            pex.ModificationTime = r.ReadInt64();
            int fnCount = r.ReadUInt16();
            for (int i = 0; i < fnCount; i++)
            {
                var df = new PexDebugFunction
                {
                    ObjectName = S(r.ReadUInt16()),
                    StateName = S(r.ReadUInt16()),
                    FunctionName = S(r.ReadUInt16()),
                    FunctionType = r.ReadByte(),
                };
                int lineCount = r.ReadUInt16();
                for (int l = 0; l < lineCount; l++) df.LineNumbers.Add(r.ReadUInt16());
                pex.DebugFunctions.Add(df);
            }

            int pgCount = r.ReadUInt16();
            for (int i = 0; i < pgCount; i++)
            {
                var pg = new PexPropertyGroup
                {
                    ObjectName = S(r.ReadUInt16()),
                    GroupName = S(r.ReadUInt16()),
                    DocString = S(r.ReadUInt16()),
                    UserFlags = r.ReadUInt32(),
                };
                int n = r.ReadUInt16();
                for (int k = 0; k < n; k++) pg.PropertyNames.Add(S(r.ReadUInt16()));
                pex.PropertyGroups.Add(pg);
            }
            int soCount = r.ReadUInt16();
            for (int i = 0; i < soCount; i++)
            {
                var so = new PexStructOrder { ObjectName = S(r.ReadUInt16()), OrderName = S(r.ReadUInt16()) };
                int n = r.ReadUInt16();
                for (int k = 0; k < n; k++) so.MemberNames.Add(S(r.ReadUInt16()));
                pex.StructOrders.Add(so);
            }
        }

        int ufCount = r.ReadUInt16();
        for (int i = 0; i < ufCount; i++)
            pex.UserFlags.Add(new PexUserFlag { Name = S(r.ReadUInt16()), Index = r.ReadByte() });

        int objCount = r.ReadUInt16();
        for (int i = 0; i < objCount; i++)
        {
            var obj = new PexObject { Name = S(r.ReadUInt16()) };
            uint declaredSize = r.ReadUInt32();
            long bodyStart = r.BaseStream.CanSeek ? r.BaseStream.Position : -1;
            obj.ParentClassName = S(r.ReadUInt16());
            obj.DocString = S(r.ReadUInt16());
            obj.Const = r.ReadByte() != 0;
            obj.UserFlags = r.ReadUInt32();
            obj.AutoStateName = S(r.ReadUInt16());

            int structCount = r.ReadUInt16();
            for (int s = 0; s < structCount; s++)
            {
                var st = new PexStruct { Name = S(r.ReadUInt16()) };
                int memberCount = r.ReadUInt16();
                for (int m = 0; m < memberCount; m++)
                {
                    var mem = new PexStructMember
                    {
                        Name = S(r.ReadUInt16()),
                        Type = S(r.ReadUInt16()),
                        UserFlags = r.ReadUInt32(),
                        DefaultValue = ReadValue(r, S),
                        Const = r.ReadByte() != 0,
                        DocString = S(r.ReadUInt16()),
                    };
                    st.Members.Add(mem);
                }
                obj.Structs.Add(st);
            }

            int varCount = r.ReadUInt16();
            for (int v = 0; v < varCount; v++)
            {
                obj.Variables.Add(new PexVariable
                {
                    Name = S(r.ReadUInt16()),
                    Type = S(r.ReadUInt16()),
                    UserFlags = r.ReadUInt32(),
                    DefaultValue = ReadValue(r, S),
                    Const = r.ReadByte() != 0,
                });
            }

            int propCount = r.ReadUInt16();
            for (int p = 0; p < propCount; p++)
            {
                var prop = new PexProperty
                {
                    Name = S(r.ReadUInt16()),
                    Type = S(r.ReadUInt16()),
                    DocString = S(r.ReadUInt16()),
                    UserFlags = r.ReadUInt32(),
                    Flags = r.ReadByte(),
                };
                if (prop.IsAutoVar)
                    prop.AutoVarName = S(r.ReadUInt16());
                else
                {
                    if (prop.CanRead) prop.ReadHandler = ReadFunction(r, S);
                    if (prop.CanWrite) prop.WriteHandler = ReadFunction(r, S);
                }
                obj.Properties.Add(prop);
            }

            int stateCount = r.ReadUInt16();
            for (int st = 0; st < stateCount; st++)
            {
                var state = new PexState { Name = S(r.ReadUInt16()) };
                int fnCount = r.ReadUInt16();
                for (int f = 0; f < fnCount; f++)
                {
                    string fnName = S(r.ReadUInt16());
                    var fn = ReadFunction(r, S);
                    fn.Name = fnName;
                    state.Functions.Add(fn);
                }
                obj.States.Add(state);
            }

            if (bodyStart >= 0)
            {
                long body = r.BaseStream.Position - bodyStart;
                if (declaredSize == body) obj.SizeIncludesItself = false;
            }

            pex.Objects.Add(obj);
        }

        var rest = r.BaseStream;
        if (rest.CanSeek && rest.Position < rest.Length)
            pex.TrailingBytes = r.ReadBytes((int)(rest.Length - rest.Position));

        AttachLineNumbers(pex);
        return pex;
    }

    private static PexFunction ReadFunction(BinaryReader r, Func<ushort, string> S)
    {
        var fn = new PexFunction
        {
            ReturnType = S(r.ReadUInt16()),
            DocString = S(r.ReadUInt16()),
            UserFlags = r.ReadUInt32(),
        };
        byte flags = r.ReadByte();
        fn.IsGlobal = (flags & 0x01) != 0;
        fn.IsNative = (flags & 0x02) != 0;

        int paramCount = r.ReadUInt16();
        for (int i = 0; i < paramCount; i++)
            fn.Params.Add(new PexTypedName { Name = S(r.ReadUInt16()), Type = S(r.ReadUInt16()) });
        int localCount = r.ReadUInt16();
        for (int i = 0; i < localCount; i++)
            fn.Locals.Add(new PexTypedName { Name = S(r.ReadUInt16()), Type = S(r.ReadUInt16()) });

        int instrCount = r.ReadUInt16();
        for (int i = 0; i < instrCount; i++)
        {
            byte op = r.ReadByte();
            if (op >= OpCodes.Length)
                throw new InvalidDataException($"Unknown/unsupported opcode 0x{op:X2} (FO4 supports 0x00-0x2E).");
            var meta = OpCodes[op];
            var instr = new PexInstruction { OpCode = op, Mnemonic = meta.name, FixedArgCount = meta.args, HasVarArgs = meta.varargs };
            for (int a = 0; a < meta.args; a++) instr.Args.Add(ReadValue(r, S));
            if (meta.varargs)
            {
                var countVal = ReadValue(r, S);
                int n = countVal.Type == PexValueType.Integer ? countVal.Int : 0;
                for (int a = 0; a < n; a++) instr.Args.Add(ReadValue(r, S));
            }
            fn.Instructions.Add(instr);
        }
        return fn;
    }

    private static PexValue ReadValue(BinaryReader r, Func<ushort, string> S)
    {
        var v = new PexValue { Type = (PexValueType)r.ReadByte() };
        switch (v.Type)
        {
            case PexValueType.None: break;
            case PexValueType.Identifier: v.Str = S(r.ReadUInt16()); break;
            case PexValueType.String: v.Str = S(r.ReadUInt16()); break;
            case PexValueType.Integer: v.Int = r.ReadInt32(); break;
            case PexValueType.Float: v.Float = r.ReadSingle(); break;
            case PexValueType.Bool: v.Bool = r.ReadByte() != 0; break;
            default: throw new InvalidDataException($"Invalid value type tag {(byte)v.Type}.");
        }
        return v;
    }

    private static string ReadStr(BinaryReader r)
    {
        int len = r.ReadUInt16();
        var bytes = r.ReadBytes(len);
        return Encoding.Latin1.GetString(bytes);
    }

    private static void AttachLineNumbers(PexFile pex)
    {
        if (!pex.HasDebugInfo) return;
        foreach (var df in pex.DebugFunctions)
        {
            var fn = FindFunction(pex, df.ObjectName, df.StateName, df.FunctionName, df.FunctionType);
            if (fn == null) continue;
            for (int i = 0; i < fn.Instructions.Count && i < df.LineNumbers.Count; i++)
                fn.Instructions[i].Line = df.LineNumbers[i];
        }
    }

    private static PexFunction? FindFunction(PexFile pex, string objName, string stateName, string fnName, byte fnType)
    {
        var obj = pex.Objects.FirstOrDefault(o => o.Name.Equals(objName, StringComparison.OrdinalIgnoreCase));
        if (obj == null) return null;
        if (fnType == 1 || fnType == 2)
        {
            var prop = obj.Properties.FirstOrDefault(p => p.Name.Equals(fnName, StringComparison.OrdinalIgnoreCase));
            return fnType == 1 ? prop?.ReadHandler : prop?.WriteHandler;
        }
        var state = obj.States.FirstOrDefault(s => s.Name.Equals(stateName, StringComparison.OrdinalIgnoreCase));
        return state?.Functions.FirstOrDefault(f => f.Name.Equals(fnName, StringComparison.OrdinalIgnoreCase));
    }
}
