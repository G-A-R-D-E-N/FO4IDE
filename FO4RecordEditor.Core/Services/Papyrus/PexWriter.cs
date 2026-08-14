using System.IO;
using System.Text;

namespace FO4RecordEditor.Services.Papyrus;

public sealed partial class PexFile
{
    public void WriteFile(string path)
    {
        using var fs = File.Create(path);
        Write(fs);
    }

    public byte[] ToBytes()
    {
        using var ms = new MemoryStream();
        Write(ms);
        return ms.ToArray();
    }

    public void Write(Stream stream)
    {
        using var w = new BinaryWriter(stream, Encoding.Latin1, leaveOpen: true);

        var index = new Dictionary<string, ushort>(StringTable.Count, StringComparer.Ordinal);
        for (int i = 0; i < StringTable.Count; i++)
        {
            if (i > ushort.MaxValue)
                throw new InvalidDataException($"String table has {StringTable.Count} entries; the format indexes it with a u16.");
            if (!index.ContainsKey(StringTable[i])) index[StringTable[i]] = (ushort)i;
        }

        ushort Idx(string s)
        {
            if (index.TryGetValue(s ?? "", out var i)) return i;
            throw new InvalidDataException(
                $"\"{s}\" is not in the string table, so it has no index to write. " +
                "Every string a .pex refers to must be present in PexFile.StringTable.");
        }

        w.Write(0xFA57C0DEu);
        w.Write(MajorVersion);
        w.Write(MinorVersion);
        w.Write(GameId);
        w.Write(CompilationTime);
        WriteStr(w, SourceFileName);
        WriteStr(w, UserName);
        WriteStr(w, ComputerName);

        WriteCount(w, StringTable.Count, "string table");
        foreach (var s in StringTable) WriteStr(w, s);

        w.Write((byte)(HasDebugInfo ? 1 : 0));
        if (HasDebugInfo)
        {
            w.Write(ModificationTime);
            WriteCount(w, DebugFunctions.Count, "debug functions");
            foreach (var df in DebugFunctions)
            {
                w.Write(Idx(df.ObjectName));
                w.Write(Idx(df.StateName));
                w.Write(Idx(df.FunctionName));
                w.Write(df.FunctionType);
                WriteCount(w, df.LineNumbers.Count, "debug line numbers");
                foreach (var line in df.LineNumbers) w.Write(line);
            }

            WriteCount(w, PropertyGroups.Count, "property groups");
            foreach (var pg in PropertyGroups)
            {
                w.Write(Idx(pg.ObjectName));
                w.Write(Idx(pg.GroupName));
                w.Write(Idx(pg.DocString));
                w.Write(pg.UserFlags);
                WriteCount(w, pg.PropertyNames.Count, "property group members");
                foreach (var n in pg.PropertyNames) w.Write(Idx(n));
            }

            WriteCount(w, StructOrders.Count, "struct orders");
            foreach (var so in StructOrders)
            {
                w.Write(Idx(so.ObjectName));
                w.Write(Idx(so.OrderName));
                WriteCount(w, so.MemberNames.Count, "struct order members");
                foreach (var n in so.MemberNames) w.Write(Idx(n));
            }
        }

        WriteCount(w, UserFlags.Count, "user flags");
        foreach (var uf in UserFlags)
        {
            w.Write(Idx(uf.Name));
            w.Write(uf.Index);
        }

        WriteCount(w, Objects.Count, "objects");
        foreach (var obj in Objects)
        {
            w.Write(Idx(obj.Name));

            var body = new MemoryStream();
            using (var bw = new BinaryWriter(body, Encoding.Latin1, leaveOpen: true))
                WriteObjectBody(bw, obj, Idx);

            w.Write((uint)(body.Length + (obj.SizeIncludesItself ? sizeof(uint) : 0)));
            body.Position = 0;
            body.CopyTo(w.BaseStream);
        }

        if (TrailingBytes.Length > 0) w.Write(TrailingBytes);
        w.Flush();
    }

    private static void WriteObjectBody(BinaryWriter w, PexObject obj, Func<string, ushort> Idx)
    {
        w.Write(Idx(obj.ParentClassName));
        w.Write(Idx(obj.DocString));
        w.Write((byte)(obj.Const ? 1 : 0));
        w.Write(obj.UserFlags);
        w.Write(Idx(obj.AutoStateName));

        WriteCount(w, obj.Structs.Count, "structs");
        foreach (var st in obj.Structs)
        {
            w.Write(Idx(st.Name));
            WriteCount(w, st.Members.Count, "struct members");
            foreach (var m in st.Members)
            {
                w.Write(Idx(m.Name));
                w.Write(Idx(m.Type));
                w.Write(m.UserFlags);
                WriteValue(w, m.DefaultValue, Idx);
                w.Write((byte)(m.Const ? 1 : 0));
                w.Write(Idx(m.DocString));
            }
        }

        WriteCount(w, obj.Variables.Count, "variables");
        foreach (var v in obj.Variables)
        {
            w.Write(Idx(v.Name));
            w.Write(Idx(v.Type));
            w.Write(v.UserFlags);
            WriteValue(w, v.DefaultValue, Idx);
            w.Write((byte)(v.Const ? 1 : 0));
        }

        WriteCount(w, obj.Properties.Count, "properties");
        foreach (var p in obj.Properties)
        {
            w.Write(Idx(p.Name));
            w.Write(Idx(p.Type));
            w.Write(Idx(p.DocString));
            w.Write(p.UserFlags);
            w.Write(p.Flags);
            if (p.IsAutoVar)
            {
                w.Write(Idx(p.AutoVarName));
            }
            else
            {
                if (p.CanRead) WriteFunction(w, Required(p.ReadHandler, p.Name, "readable"), Idx);
                if (p.CanWrite) WriteFunction(w, Required(p.WriteHandler, p.Name, "writable"), Idx);
            }
        }

        WriteCount(w, obj.States.Count, "states");
        foreach (var st in obj.States)
        {
            w.Write(Idx(st.Name));
            WriteCount(w, st.Functions.Count, "state functions");
            foreach (var fn in st.Functions)
            {
                w.Write(Idx(fn.Name));
                WriteFunction(w, fn, Idx);
            }
        }
    }

    private static PexFunction Required(PexFunction? fn, string propName, string what) =>
        fn ?? throw new InvalidDataException(
            $"Property \"{propName}\" is flagged {what} but carries no handler to write.");

    private static void WriteFunction(BinaryWriter w, PexFunction fn, Func<string, ushort> Idx)
    {
        w.Write(Idx(fn.ReturnType));
        w.Write(Idx(fn.DocString));
        w.Write(fn.UserFlags);
        w.Write((byte)((fn.IsGlobal ? 0x01 : 0) | (fn.IsNative ? 0x02 : 0)));

        WriteCount(w, fn.Params.Count, "parameters");
        foreach (var p in fn.Params) { w.Write(Idx(p.Name)); w.Write(Idx(p.Type)); }
        WriteCount(w, fn.Locals.Count, "locals");
        foreach (var l in fn.Locals) { w.Write(Idx(l.Name)); w.Write(Idx(l.Type)); }

        WriteCount(w, fn.Instructions.Count, "instructions");
        foreach (var instr in fn.Instructions)
        {
            if (instr.OpCode >= OpCodes.Length)
                throw new InvalidDataException($"Unknown opcode 0x{instr.OpCode:X2} (FO4 supports 0x00-0x2E).");
            var meta = OpCodes[instr.OpCode];
            if (instr.Args.Count < meta.args)
                throw new InvalidDataException(
                    $"{meta.name} takes {meta.args} operands, got {instr.Args.Count}.");
            if (!meta.varargs && instr.Args.Count != meta.args)
                throw new InvalidDataException(
                    $"{meta.name} takes exactly {meta.args} operands, got {instr.Args.Count}.");

            w.Write(instr.OpCode);
            for (int a = 0; a < meta.args; a++) WriteValue(w, instr.Args[a], Idx);
            if (meta.varargs)
            {

                WriteValue(w, new PexValue { Type = PexValueType.Integer, Int = instr.Args.Count - meta.args }, Idx);
                for (int a = meta.args; a < instr.Args.Count; a++) WriteValue(w, instr.Args[a], Idx);
            }
        }
    }

    private static void WriteValue(BinaryWriter w, PexValue? v, Func<string, ushort> Idx)
    {
        v ??= new PexValue { Type = PexValueType.None };
        w.Write((byte)v.Type);
        switch (v.Type)
        {
            case PexValueType.None: break;
            case PexValueType.Identifier:
            case PexValueType.String: w.Write(Idx(v.Str)); break;
            case PexValueType.Integer: w.Write(v.Int); break;
            case PexValueType.Float: w.Write(v.Float); break;
            case PexValueType.Bool: w.Write((byte)(v.Bool ? 1 : 0)); break;
            default: throw new InvalidDataException($"Invalid value type tag {(byte)v.Type}.");
        }
    }

    private static void WriteCount(BinaryWriter w, int count, string what)
    {
        if (count > ushort.MaxValue)
            throw new InvalidDataException($"{count} {what}; the format counts them with a u16.");
        w.Write((ushort)count);
    }

    private static void WriteStr(BinaryWriter w, string? s)
    {
        var bytes = Encoding.Latin1.GetBytes(s ?? "");
        if (bytes.Length > ushort.MaxValue)
            throw new InvalidDataException($"String of {bytes.Length} bytes; the format length-prefixes with a u16.");
        w.Write((ushort)bytes.Length);
        w.Write(bytes);
    }
}
