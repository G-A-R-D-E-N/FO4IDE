using System.Collections.Generic;

namespace FO4RecordEditor.Services.Papyrus;

// A .pex refers to every string it uses by u16 index into one table, so a file assembled in memory
// has to gain that table before it can be written -- PexWriter deliberately throws rather than
// inventing an index for a string it cannot find, because a silently-appended entry would shift no
// index but would let a genuinely inconsistent model through.
//
// The reader keeps the table it read verbatim so a round trip reproduces indices exactly; this is
// the other direction, for a file that was generated rather than read. Order is arbitrary as far as
// the format is concerned -- only completeness matters -- so it is first-use in a fixed walk, which
// makes it deterministic and diffable.

public sealed partial class PexFile
{
    /// <summary>Rebuilds <see cref="StringTable"/> from every string the model refers to.</summary>
    public void RebuildStringTable()
    {
        var seen = new HashSet<string>(System.StringComparer.Ordinal);
        var table = new List<string>();

        void Add(string? s)
        {
            s ??= "";
            if (seen.Add(s)) table.Add(s);
        }

        void AddValue(PexValue? v)
        {
            if (v == null) return;
            if (v.Type is PexValueType.Identifier or PexValueType.String) Add(v.Str);
        }

        void AddFunction(PexFunction fn)
        {
            Add(fn.ReturnType);
            Add(fn.DocString);
            foreach (var p in fn.Params) { Add(p.Name); Add(p.Type); }
            foreach (var l in fn.Locals) { Add(l.Name); Add(l.Type); }
            foreach (var i in fn.Instructions) foreach (var a in i.Args) AddValue(a);
        }

        foreach (var uf in UserFlags) Add(uf.Name);

        foreach (var df in DebugFunctions) { Add(df.ObjectName); Add(df.StateName); Add(df.FunctionName); }
        foreach (var pg in PropertyGroups)
        {
            Add(pg.ObjectName); Add(pg.GroupName); Add(pg.DocString);
            foreach (var n in pg.PropertyNames) Add(n);
        }
        foreach (var so in StructOrders)
        {
            Add(so.ObjectName); Add(so.OrderName);
            foreach (var n in so.MemberNames) Add(n);
        }

        foreach (var obj in Objects)
        {
            Add(obj.Name);
            Add(obj.ParentClassName);
            Add(obj.DocString);
            Add(obj.AutoStateName);

            foreach (var st in obj.Structs)
            {
                Add(st.Name);
                foreach (var m in st.Members)
                {
                    Add(m.Name); Add(m.Type); Add(m.DocString); AddValue(m.DefaultValue);
                }
            }
            foreach (var v in obj.Variables) { Add(v.Name); Add(v.Type); AddValue(v.DefaultValue); }
            foreach (var p in obj.Properties)
            {
                Add(p.Name); Add(p.Type); Add(p.DocString);
                if (p.IsAutoVar) Add(p.AutoVarName);
                if (p.ReadHandler != null) AddFunction(p.ReadHandler);
                if (p.WriteHandler != null) AddFunction(p.WriteHandler);
            }
            foreach (var st in obj.States)
            {
                Add(st.Name);
                foreach (var fn in st.Functions) { Add(fn.Name); AddFunction(fn); }
            }
        }

        StringTable = table;
    }
}
