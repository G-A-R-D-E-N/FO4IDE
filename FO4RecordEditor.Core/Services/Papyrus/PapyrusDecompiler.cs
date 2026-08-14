using System.Text;

namespace FO4RecordEditor.Services.Papyrus;

public static class PapyrusDecompiler
{
    public static string Decompile(string pexPath, bool assembly)
    {
        var pex = PexFile.ReadFile(pexPath);
        return assembly ? Disassemble(pex) : ToSource(pex);
    }

    private static string ToSource(PexFile pex)
    {
        var sb = new StringBuilder();

        var flagNames = new Dictionary<int, string>();
        foreach (var uf in pex.UserFlags) flagNames[uf.Index] = uf.Name;
        string Flags(uint mask, params string[] omit)
        {
            var names = new List<string>();
            for (int b = 0; b < 32; b++)
                if ((mask & (1u << b)) != 0 && flagNames.TryGetValue(b, out var n) && !omit.Contains(n, StringComparer.OrdinalIgnoreCase))
                    names.Add(Cap(n));
            return names.Count == 0 ? "" : " " + string.Join(" ", names);
        }

        foreach (var obj in pex.Objects)
        {
            sb.Append($"ScriptName {obj.Name}");
            if (!string.IsNullOrEmpty(obj.ParentClassName)) sb.Append($" extends {obj.ParentClassName}");
            if (obj.Const) sb.Append(" Const");
            sb.Append(Flags(obj.UserFlags));
            sb.AppendLine();
            if (!string.IsNullOrEmpty(obj.DocString)) sb.AppendLine($"{{ {obj.DocString} }}");
            sb.AppendLine();

            foreach (var st in obj.Structs)
            {
                sb.AppendLine($"Struct {st.Name}");
                foreach (var m in st.Members)
                {
                    sb.Append($"\t{m.Type} {m.Name}");
                    if (m.DefaultValue is { } dv && !dv.IsNoneType) sb.Append($" = {dv}");
                    if (m.Const) sb.Append(" Const");
                    sb.Append(Flags(m.UserFlags));
                    sb.AppendLine();
                    if (!string.IsNullOrEmpty(m.DocString)) sb.AppendLine($"\t{{ {m.DocString} }}");
                }
                sb.AppendLine("EndStruct");
                sb.AppendLine();
            }

            foreach (var p in obj.Properties)
            {
                if (p.IsAutoVar)
                {
                    var backing = obj.Variables.FirstOrDefault(v => v.Name.Equals(p.AutoVarName, StringComparison.OrdinalIgnoreCase));
                    sb.Append($"{p.Type} Property {p.Name}");
                    if (backing?.DefaultValue is { } dv && !dv.IsNoneType) sb.Append($" = {dv}");
                    sb.Append(p.CanWrite ? " Auto" : " AutoReadOnly");
                    if (backing?.Const == true) sb.Append(" Const");
                    sb.Append(Flags(p.UserFlags));
                    sb.AppendLine();
                    if (!string.IsNullOrEmpty(p.DocString)) sb.AppendLine($"{{ {p.DocString} }}");
                }
                else
                {
                    sb.AppendLine($"{p.Type} Property {p.Name}{Flags(p.UserFlags)}");
                    if (!string.IsNullOrEmpty(p.DocString)) sb.AppendLine($"{{ {p.DocString} }}");
                    if (p.ReadHandler != null) EmitFunction(sb, obj, p.ReadHandler, flagNames, "\t", nameOverride: $"{p.Type} Function Get");
                    if (p.WriteHandler != null) EmitFunction(sb, obj, p.WriteHandler, flagNames, "\t", nameOverride: $"Function Set");
                    sb.AppendLine("EndProperty");
                }
                sb.AppendLine();
            }

            bool anyVar = false;
            foreach (var v in obj.Variables)
            {
                if (v.Name.StartsWith("::")) continue;
                sb.Append($"{v.Type} {v.Name}");
                if (v.DefaultValue is { } dv && !dv.IsNoneType) sb.Append($" = {dv}");
                if (v.Const) sb.Append(" Const");
                sb.Append(Flags(v.UserFlags));
                sb.AppendLine();
                anyVar = true;
            }
            if (anyVar) sb.AppendLine();

            foreach (var state in obj.States)
            {
                bool isDefault = string.IsNullOrEmpty(state.Name);
                if (!isDefault)
                {
                    bool isAuto = state.Name.Equals(obj.AutoStateName, StringComparison.OrdinalIgnoreCase);
                    sb.AppendLine($"{(isAuto ? "Auto " : "")}State {state.Name}");
                    sb.AppendLine();
                }
                foreach (var fn in state.Functions)
                {
                    EmitFunction(sb, obj, fn, flagNames, isDefault ? "" : "\t");
                    sb.AppendLine();
                }
                if (!isDefault) sb.AppendLine($"EndState").AppendLine();
            }
        }
        return sb.ToString().TrimEnd() + "\n";
    }

    private static void EmitFunction(StringBuilder sb, PexObject obj, PexFunction fn,
        Dictionary<int, string> flagNames, string indent, string? nameOverride = null)
    {
        string FlagsStr(uint mask)
        {
            var names = new List<string>();
            for (int b = 0; b < 32; b++)
                if ((mask & (1u << b)) != 0 && flagNames.TryGetValue(b, out var n)) names.Add(Cap(n));
            return names.Count == 0 ? "" : " " + string.Join(" ", names);
        }

        bool isRemote = nameOverride == null && fn.Name.StartsWith("::remote_", StringComparison.OrdinalIgnoreCase);
        bool isEvent = nameOverride == null
            && fn.ReturnType.Equals("None", StringComparison.OrdinalIgnoreCase)
            && (isRemote || fn.Name.StartsWith("On", StringComparison.OrdinalIgnoreCase));

        var ps = string.Join(", ", fn.Params.Select(p => $"{p.Type} {p.Name}"));
        string header;
        if (nameOverride != null)
            header = $"{nameOverride}({ps})";
        else if (isRemote)
        {

            var rest = fn.Name.Substring("::remote_".Length);
            string type = fn.Params.Count > 0 ? fn.Params[0].Type : "";
            string ev = rest;

            if (type.Length > 0 && rest.StartsWith(type + "_", StringComparison.OrdinalIgnoreCase))
            {
                ev = rest.Substring(type.Length + 1);
            }
            else
            {

                int idx = rest.IndexOf("_On", StringComparison.OrdinalIgnoreCase);
                type = idx > 0 ? rest.Substring(0, idx) : "";
                ev = idx > 0 ? rest.Substring(idx + 1) : rest;
            }

            header = type.Length > 0 ? $"Event {type}.{ev}({ps})" : $"Event {ev}({ps})";
        }
        else if (isEvent)
            header = $"Event {fn.Name}({ps})";
        else
        {
            string ret = fn.ReturnType.Equals("None", StringComparison.OrdinalIgnoreCase) ? "" : fn.ReturnType + " ";
            header = $"{ret}Function {fn.Name}({ps})";
        }
        if (fn.IsGlobal) header += " Global";
        if (fn.IsNative) header += " Native";
        header += FlagsStr(fn.UserFlags);
        sb.AppendLine(indent + header);
        if (!string.IsNullOrEmpty(fn.DocString)) sb.AppendLine($"{indent}{{ {fn.DocString} }}");

        if (fn.IsNative) return;

        string body;
        try
        {
            body = DecompileBody(obj, fn, indent + "\t", inlineTemps: true);

            if (body.Contains("::")) body = DecompileBody(obj, fn, indent + "\t", inlineTemps: false);
        }
        catch (Exception ex) { body = $"{indent}\t; [decompile failed: {ex.Message}; assembly follows]\n" + AsmBody(fn, indent + "\t"); }
        sb.Append(body);

        string end = nameOverride == null && isEvent ? "EndEvent" : "EndFunction";
        sb.AppendLine(indent + end);
    }

    private enum JKind { JmpF, JmpT, Jmp }
    private sealed class JInfo { public JKind Kind; public int Target; public string Cond = ""; public string CondVar = ""; }

    private static string DecompileBody(PexObject obj, PexFunction fn, string indent, bool inlineTemps)
    {

        var types = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in fn.Params) types[p.Name] = p.Type;
        foreach (var l in fn.Locals) types[l.Name] = l.Type;
        foreach (var v in obj.Variables) types[v.Name] = v.Type;
        foreach (var pr in obj.Properties) types[pr.Name] = pr.Type;

        var backing = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pr in obj.Properties)
            if (pr.IsAutoVar && !string.IsNullOrEmpty(pr.AutoVarName)) backing[pr.AutoVarName] = pr.Name;

        var temps = new Dictionary<string, string>();
        var instrs = fn.Instructions;
        var stmt = new string?[instrs.Count];
        var jumps = new JInfo?[instrs.Count];
        var firstWrite = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        var tempDefinition = new Dictionary<string, int>();
        var tempExpression = new Dictionary<int, string>();
        var tempWasRead = new HashSet<int>();

        string TypeOf(string name) => types.TryGetValue(name, out var t) ? t : "";

        bool IsTemp(string n) => n.StartsWith("::temp", StringComparison.OrdinalIgnoreCase);
        string ResolveName(string n) => backing.TryGetValue(n, out var pn) ? pn : n;

        string San(string n) => n.StartsWith("::") ? "_" + n.Substring(2) : n;

        string Op(PexValue v)
        {
            if (v.Type != PexValueType.Identifier) return v.ToString();
            var n = v.Str;
            if (n.Equals("::NoneVar", StringComparison.OrdinalIgnoreCase)) return "None";
            if (inlineTemps && IsTemp(n) && temps.TryGetValue(n, out var e))
            {
                if (tempDefinition.TryGetValue(n, out var definition)) tempWasRead.Add(definition);
                return e;
            }
            return San(ResolveName(n));
        }
        string A(PexInstruction ins, int i) => Op(ins.Args[i]);
        string Dest(PexInstruction ins, int i) => ins.Args[i].Str;

        void Put(int idx, string dest, string expr)
        {
            if (!firstWrite.ContainsKey(dest)) firstWrite[dest] = idx;
            if (inlineTemps && IsTemp(dest))
            {
                temps[dest] = expr;
                tempDefinition[dest] = idx;
                tempExpression[idx] = expr;
            }
            else if (dest.Equals("::NoneVar", StringComparison.OrdinalIgnoreCase)) stmt[idx] = expr;
            else stmt[idx] = $"{San(ResolveName(dest))} = {expr}";
        }

        for (int i = 0; i < instrs.Count; i++)
        {
            var ins = instrs[i];
            switch (ins.Mnemonic)
            {
                case "nop": break;
                case "iadd": case "fadd": Put(i, Dest(ins,0), $"({A(ins,1)} + {A(ins,2)})"); break;
                case "isub": case "fsub": Put(i, Dest(ins,0), $"({A(ins,1)} - {A(ins,2)})"); break;
                case "imul": case "fmul": Put(i, Dest(ins,0), $"({A(ins,1)} * {A(ins,2)})"); break;
                case "idiv": case "fdiv": Put(i, Dest(ins,0), $"({A(ins,1)} / {A(ins,2)})"); break;
                case "imod": Put(i, Dest(ins,0), $"({A(ins,1)} % {A(ins,2)})"); break;
                case "ineg": case "fneg": Put(i, Dest(ins,0), $"(-{A(ins,1)})"); break;
                case "not": Put(i, Dest(ins,0), $"(!{A(ins,1)})"); break;
                case "assign": Put(i, Dest(ins,0), A(ins,1)); break;
                case "cast":
                {

                    string src = A(ins, 1);
                    if (src == "None") { Put(i, Dest(ins, 0), "None"); break; }
                    string dt = TypeOf(Dest(ins,0));

                    if (dt.Equals("ScriptObject", StringComparison.OrdinalIgnoreCase)) { Put(i, Dest(ins, 0), src); break; }

                    if (ins.Args[1].Type == PexValueType.Identifier
                        && TypeOf(ins.Args[1].Str).Equals(dt, StringComparison.OrdinalIgnoreCase))
                    {
                        Put(i, Dest(ins, 0), src);
                        break;
                    }
                    Put(i, Dest(ins,0), dt.Length > 0 ? $"({src} as {dt})" : src);
                    break;
                }
                case "cmp_eq": Put(i, Dest(ins,0), $"({A(ins,1)} == {A(ins,2)})"); break;
                case "cmp_lt": Put(i, Dest(ins,0), $"({A(ins,1)} < {A(ins,2)})"); break;
                case "cmp_lte": Put(i, Dest(ins,0), $"({A(ins,1)} <= {A(ins,2)})"); break;
                case "cmp_gt": Put(i, Dest(ins,0), $"({A(ins,1)} > {A(ins,2)})"); break;
                case "cmp_gte": Put(i, Dest(ins,0), $"({A(ins,1)} >= {A(ins,2)})"); break;
                case "strcat": Put(i, Dest(ins,0), $"({A(ins,1)} + {A(ins,2)})"); break;
                case "is": Put(i, Dest(ins,0), $"({A(ins,1)} is {ins.Args[2].Str})"); break;
                case "propget": Put(i, Dest(ins,2), $"{SelfDot(A(ins,1))}{ins.Args[0].Str}"); break;
                case "propset": stmt[i] = $"{SelfDot(A(ins,1))}{ins.Args[0].Str} = {A(ins,2)}"; break;
                case "array_create": Put(i, Dest(ins,0), $"new {ElemType(TypeOf(Dest(ins,0)))}[{A(ins,1)}]"); break;
                case "array_length": Put(i, Dest(ins,0), $"{A(ins,1)}.Length"); break;
                case "array_getelement": Put(i, Dest(ins,0), $"{A(ins,1)}[{A(ins,2)}]"); break;
                case "array_setelement": stmt[i] = $"{A(ins,0)}[{A(ins,1)}] = {A(ins,2)}"; break;
                case "array_findelement": Put(i, Dest(ins,1), $"{A(ins,0)}.Find({A(ins,2)}, {A(ins,3)})"); break;
                case "array_rfindelement": Put(i, Dest(ins,1), $"{A(ins,0)}.RFind({A(ins,2)}, {A(ins,3)})"); break;
                case "array_findstruct": Put(i, Dest(ins,1), $"{A(ins,0)}.FindStruct(\"{ins.Args[2].Str}\", {A(ins,3)}, {A(ins,4)})"); break;
                case "array_rfindstruct": Put(i, Dest(ins,1), $"{A(ins,0)}.RFindStruct(\"{ins.Args[2].Str}\", {A(ins,3)}, {A(ins,4)})"); break;
                case "array_add": stmt[i] = $"{A(ins,0)}.Add({A(ins,1)}, {A(ins,2)})"; break;
                case "array_insert": stmt[i] = $"{A(ins,0)}.Insert({A(ins,1)}, {A(ins,2)})"; break;
                case "array_removelast": stmt[i] = $"{A(ins,0)}.RemoveLast()"; break;
                case "array_remove": stmt[i] = $"{A(ins,0)}.Remove({A(ins,1)}, {A(ins,2)})"; break;
                case "array_clear": stmt[i] = $"{A(ins,0)}.Clear()"; break;
                case "struct_create": Put(i, Dest(ins,0), $"new {TypeOf(Dest(ins,0))}"); break;
                case "struct_get": Put(i, Dest(ins,0), $"{A(ins,1)}.{ins.Args[2].Str}"); break;
                case "struct_set": stmt[i] = $"{A(ins,0)}.{ins.Args[1].Str} = {A(ins,2)}"; break;
                case "callmethod":
                {
                    string method = ins.Args[0].Str, self = A(ins,1), dest = Dest(ins,2);
                    string call = $"{SelfDot(self)}{method}({Args(ins, Op)})";
                    Put(i, dest, call);
                    break;
                }
                case "callstatic":
                {
                    string type = ins.Args[0].Str, method = ins.Args[1].Str, dest = Dest(ins,2);
                    Put(i, dest, $"{type}.{method}({Args(ins, Op)})");
                    break;
                }
                case "callparent":
                {
                    string method = ins.Args[0].Str, dest = Dest(ins,1);
                    Put(i, dest, $"Parent.{method}({Args(ins, Op)})");
                    break;
                }
                case "return":
                {
                    var rv = ins.Args[0];
                    bool none = rv.IsNoneType || (rv.Type == PexValueType.Identifier && rv.Str.Equals("::NoneVar", StringComparison.OrdinalIgnoreCase));
                    stmt[i] = none ? "Return" : $"Return {Op(rv)}";
                    break;
                }
                case "jmp": jumps[i] = new JInfo { Kind = JKind.Jmp, Target = i + ins.Args[0].Int }; break;
                case "jmpt": jumps[i] = new JInfo { Kind = JKind.JmpT, Target = i + ins.Args[1].Int, Cond = A(ins,0), CondVar = ins.Args[0].Str }; break;
                case "jmpf": jumps[i] = new JInfo { Kind = JKind.JmpF, Target = i + ins.Args[1].Int, Cond = A(ins,0), CondVar = ins.Args[0].Str }; break;
                default: stmt[i] = $"; [unhandled {ins.Mnemonic}]"; break;
            }
        }

        for (bool folding = true; folding;)
        {
            folding = false;
            for (int i = 0; i < instrs.Count; i++)
            {
                var first = jumps[i];
                if (first == null || first.CondVar.Length == 0 || first.Target <= i) continue;
                if (first.Target >= instrs.Count) continue;

                var second = jumps[first.Target];
                if (second == null || !second.CondVar.Equals(first.CondVar, StringComparison.OrdinalIgnoreCase)) continue;

                bool clear = true;
                for (int k = i + 1; k < first.Target && clear; k++) clear = stmt[k] == null && jumps[k] == null;
                if (!clear) continue;

                second.Cond = first.Kind == JKind.JmpT
                    ? $"({first.Cond} || {second.Cond})"
                    : $"({first.Cond} && {second.Cond})";
                jumps[i] = null;
                folding = true;
            }
        }

        foreach (var (idx, expression) in tempExpression)
        {
            if (tempWasRead.Contains(idx)) continue;
            if (instrs[idx].Mnemonic is not ("callmethod" or "callstatic" or "callparent")) continue;
            stmt[idx] = expression;
        }

        var declaredAt = new Dictionary<int, string>();
        var zeroedByDeclaration = new HashSet<int>();
        var hoisted = new List<PexTypedName>();

        foreach (var l in fn.Locals)
        {
            if (l.Name.Equals("::NoneVar", StringComparison.OrdinalIgnoreCase)) continue;
            if (l.Type.Equals("None", StringComparison.OrdinalIgnoreCase)) continue;
            if (inlineTemps && IsTemp(l.Name)) continue;

            if (firstWrite.TryGetValue(l.Name, out var at)
                && instrs[at].Mnemonic == "assign"
                && IsZeroFor(l.Type, instrs[at].Args[1]))
            {

                zeroedByDeclaration.Add(at);
                hoisted.Add(l);
                continue;
            }

            if (firstWrite.TryGetValue(l.Name, out at)
                && stmt[at] != null
                && stmt[at]!.StartsWith(San(l.Name) + " = ", StringComparison.Ordinal)
                && RunsUnconditionally(at))
            {
                declaredAt[at] = l.Type;
                continue;
            }

            hoisted.Add(l);
        }

        var sb = new StringBuilder();

        foreach (var l in hoisted) sb.AppendLine($"{indent}{l.Type} {San(l.Name)}");

        Structure(0, instrs.Count, indent);
        return sb.ToString();

        bool RunsUnconditionally(int idx)
        {
            for (int k = 0; k < instrs.Count; k++)
            {
                var j = jumps[k];
                if (j == null) continue;
                if (k < idx && j.Target > idx) return false;
                if (k > idx && j.Target <= idx) return false;
            }
            return true;
        }

        void Structure(int start, int end, string ind)
        {
            int i = start;
            while (i < end)
            {
                var j = jumps[i];
                if (j == null)
                {
                    if (stmt[i] != null && !zeroedByDeclaration.Contains(i))
                    {
                        sb.AppendLine(declaredAt.TryGetValue(i, out var declared)
                            ? $"{ind}{declared} {stmt[i]}"
                            : ind + stmt[i]);
                    }
                    i++; continue;
                }
                if (j.Kind == JKind.JmpF && j.Target > i && j.Target <= end)
                {
                    int target = j.Target, lastIdx = target - 1;
                    var lastJ = (lastIdx > i) ? jumps[lastIdx] : null;
                    if (lastJ is { Kind: JKind.Jmp } && lastJ.Target <= i)
                    {
                        sb.AppendLine($"{ind}While ({j.Cond})");
                        Structure(i + 1, lastIdx, ind + "\t");
                        sb.AppendLine($"{ind}EndWhile");
                        i = target; continue;
                    }
                    if (lastJ is { Kind: JKind.Jmp } && lastJ.Target > lastIdx)
                    {
                        int elseEnd = Math.Min(lastJ.Target, end);
                        if (elseEnd <= target)
                        {

                            sb.AppendLine($"{ind}If ({j.Cond})");
                            Structure(i + 1, lastIdx, ind + "\t");
                            sb.AppendLine($"{ind}EndIf");
                            i = target; continue;
                        }
                        sb.AppendLine($"{ind}If ({j.Cond})");
                        Structure(i + 1, lastIdx, ind + "\t");
                        sb.AppendLine($"{ind}Else");
                        Structure(target, elseEnd, ind + "\t");
                        sb.AppendLine($"{ind}EndIf");
                        i = elseEnd; continue;
                    }
                    sb.AppendLine($"{ind}If ({j.Cond})");
                    Structure(i + 1, target, ind + "\t");
                    sb.AppendLine($"{ind}EndIf");
                    i = target; continue;
                }
                if (j.Kind == JKind.JmpT && j.Target > i && j.Target <= end)
                {
                    sb.AppendLine($"{ind}If (!({j.Cond}))");
                    Structure(i + 1, j.Target, ind + "\t");
                    sb.AppendLine($"{ind}EndIf");
                    i = j.Target; continue;
                }

                i++;
            }
        }
    }

    private static bool IsZeroFor(string type, PexValue value) => type.ToLowerInvariant() switch
    {
        "bool" => value.Type == PexValueType.Bool && !value.Bool,
        "int" => value.Type == PexValueType.Integer && value.Int == 0,
        "float" => value.Type == PexValueType.Float && value.Float == 0f,
        "string" => value.Type == PexValueType.String && value.Str.Length == 0,

        _ => value.IsNoneType,
    };

    private static string Args(PexInstruction ins, Func<PexValue, string> op) =>
        string.Join(", ", ins.VarArgs.Select(op));

    private static string SelfDot(string self) =>
        self.Equals("self", StringComparison.OrdinalIgnoreCase) ? "" : self + ".";

    private static string ElemType(string arrayType) =>
        arrayType.EndsWith("[]") ? arrayType[..^2] : arrayType;

    private static string Disassemble(PexFile pex)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"; Disassembly of {pex.SourceFileName}");
        sb.AppendLine($"; compiled by {pex.UserName}@{pex.ComputerName}, FO4 PEX v{pex.MajorVersion}.{pex.MinorVersion}");
        sb.AppendLine();
        foreach (var obj in pex.Objects)
        {
            sb.AppendLine($".object {obj.Name} extends {obj.ParentClassName}");
            foreach (var st in obj.States)
            {
                sb.AppendLine($"  .state \"{st.Name}\"");
                foreach (var fn in st.Functions) DumpFn(sb, fn, "    ");
            }
            foreach (var p in obj.Properties)
            {
                if (p.ReadHandler != null) { sb.AppendLine($"  .propertyGet {p.Name}"); DumpFn(sb, p.ReadHandler, "    "); }
                if (p.WriteHandler != null) { sb.AppendLine($"  .propertySet {p.Name}"); DumpFn(sb, p.WriteHandler, "    "); }
            }
            sb.AppendLine(".endObject");
        }
        return sb.ToString();
    }

    private static void DumpFn(StringBuilder sb, PexFunction fn, string indent)
    {
        sb.AppendLine($"{indent}.function {fn.Name} -> {fn.ReturnType}{(fn.IsGlobal ? " global" : "")}{(fn.IsNative ? " native" : "")}");
        if (fn.Params.Count > 0) sb.AppendLine($"{indent}  .params {string.Join(", ", fn.Params.Select(p => $"{p.Type} {p.Name}"))}");
        if (fn.Locals.Count > 0) sb.AppendLine($"{indent}  .locals {string.Join(", ", fn.Locals.Select(l => $"{l.Type} {l.Name}"))}");
        sb.Append(AsmBody(fn, indent + "  "));
        sb.AppendLine($"{indent}.endFunction");
    }

    private static string AsmBody(PexFunction fn, string indent)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < fn.Instructions.Count; i++)
        {
            var ins = fn.Instructions[i];
            var args = string.Join(" ", ins.Args.Select(a => a.ToString()));
            string line = ins.Line > 0 ? $"L{ins.Line}" : "";
            sb.AppendLine($"{indent}[{i,3}] {line,-6} {ins.Mnemonic} {args}".TrimEnd());
        }
        return sb.ToString();
    }

    private static string Cap(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
