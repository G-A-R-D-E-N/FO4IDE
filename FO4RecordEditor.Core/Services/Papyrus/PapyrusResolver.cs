using System;
using System.Collections.Generic;
using System.Linq;

namespace FO4RecordEditor.Services.Papyrus;

/// <summary>
/// Binds every name in a script to what it declares, and gives every expression a type.
/// </summary>
/// <remarks>
/// Issue #78 phase 2's semantic half. The front end could say where a name is written; this says
/// what it means. Nothing here enforces anything -- assignability, arity and argument types are the
/// type checker's job, and this deliberately computes the facts that checker will need without
/// pre-judging them. The one thing it does report is a name that resolves to nothing, because that
/// is a resolution failure rather than a type error.
/// <para>
/// The rules are the Creation Kit's, transcribed rather than recalled. From the Variable Reference:
/// a script variable need not be declared before use but a function local must; a variable declared
/// inside an <c>if</c> or <c>while</c> is local to that block; and a script variable and a function
/// local may not share a name, though two functions may each have their own. From the Function
/// Reference: <c>Self</c> is the instance the function runs on and a <c>global</c> function has
/// none, and <c>Parent</c> exists only to reach the parent script's version of a function.
/// </para>
/// <para>
/// <b>Missing sources are not errors.</b> If a script extends something the index cannot find, every
/// inherited member would look undefined, so unresolved-name reporting switches off for that script
/// and <see cref="PapyrusResolution.BaseChainComplete"/> says why. Over a real corpus this is the
/// difference between a useful diagnostic and thousands of false ones.
/// </para>
/// </remarks>
public sealed class PapyrusResolver
{
    private readonly PapyrusScriptIndex _index;

    public PapyrusResolver(PapyrusScriptIndex index) =>
        _index = index ?? throw new ArgumentNullException(nameof(index));

    // ---- per-resolution state -------------------------------------------------------------------

    private sealed class Context
    {
        public PapyrusScript Script = null!;
        public IReadOnlyList<PapyrusScript> Chain = Array.Empty<PapyrusScript>();
        public List<PapyrusScript> Imports = new();
        public Dictionary<PapyrusNode, PapyrusBinding> Bindings = new();
        public Dictionary<PapyrusNode, PapyrusType> Types = new();
        public List<PapyrusDiagnostic> Diagnostics = new();
        public bool BaseChainComplete = true;

        /// <summary>Innermost scope last. Each frame is one block, or one function's parameters.</summary>
        public List<Dictionary<string, PapyrusBinding>> Scopes = new();

        /// <summary>Null inside a global function, which has no Self.</summary>
        public PapyrusType? SelfType;
    }

    public PapyrusResolution Resolve(PapyrusScript script)
    {
        if (script == null) throw new ArgumentNullException(nameof(script));

        var ctx = new Context { Script = script };
        ctx.Chain = _index.BaseChain(script);

        // A chain that stops short of its declared parent means sources are missing, not that the
        // parent does not exist.
        if (!string.IsNullOrEmpty(script.Extends) && ctx.Chain.Count < 2) ctx.BaseChainComplete = false;
        var last = ctx.Chain[^1];
        if (!string.IsNullOrEmpty(last.Extends)) ctx.BaseChainComplete = false;

        foreach (var import in script.Imports)
        {
            var imported = _index.Resolve(import.Name);
            if (imported == null) ctx.BaseChainComplete = false;
            else ctx.Imports.Add(imported);
        }

        ResolveDeclarations(ctx);
        ResolveBodies(ctx);

        return new PapyrusResolution(script, ctx.Bindings, ctx.Types, ctx.Diagnostics, ctx.BaseChainComplete);
    }

    // ---- declarations ---------------------------------------------------------------------------

    private void ResolveDeclarations(Context ctx)
    {
        foreach (var s in ctx.Script.Structs)
        {
            foreach (var m in s.Members) TypeOfRef(ctx, m.Type);
        }
        foreach (var v in ctx.Script.Variables) TypeOfRef(ctx, v.Type);
        foreach (var p in ctx.Script.Properties) TypeOfRef(ctx, p.Type);
    }

    private void ResolveBodies(Context ctx)
    {
        foreach (var fn in ctx.Script.Functions) ResolveCallable(ctx, fn, fn.IsGlobal);
        foreach (var ev in ctx.Script.Events) ResolveCallable(ctx, ev, isGlobal: false);

        foreach (var state in ctx.Script.States)
        {
            foreach (var fn in state.Functions) ResolveCallable(ctx, fn, fn.IsGlobal);
            foreach (var ev in state.Events) ResolveCallable(ctx, ev, isGlobal: false);
        }

        foreach (var prop in ctx.Script.Properties)
        {
            if (prop.Getter != null) ResolveCallable(ctx, prop.Getter, isGlobal: false);
            if (prop.Setter != null) ResolveCallable(ctx, prop.Setter, isGlobal: false);
            if (prop.Initializer != null) ResolveExpression(ctx, prop.Initializer);
        }
    }

    private void ResolveCallable(Context ctx, PapyrusCallableDecl callable, bool isGlobal)
    {
        ctx.SelfType = isGlobal ? null : PapyrusType.Object(ctx.Script.Name);
        ctx.Scopes.Clear();
        ctx.Scopes.Add(new Dictionary<string, PapyrusBinding>(StringComparer.OrdinalIgnoreCase));

        foreach (var p in callable.Parameters)
        {
            var type = TypeOfRef(ctx, p.Type);
            ctx.Scopes[^1][p.Name] = new PapyrusBinding(PapyrusBindingKind.Parameter, p.Name, type, p, ctx.Script);
            if (p.DefaultValue != null) ResolveExpression(ctx, p.DefaultValue);
        }

        if (callable is PapyrusFunctionDecl fn && fn.ReturnType != null) TypeOfRef(ctx, fn.ReturnType);

        ResolveBlock(ctx, callable.Body);
        ctx.Scopes.Clear();
        ctx.SelfType = null;
    }

    private void ResolveBlock(Context ctx, IEnumerable<PapyrusStatement> body)
    {
        ctx.Scopes.Add(new Dictionary<string, PapyrusBinding>(StringComparer.OrdinalIgnoreCase));
        foreach (var stmt in body) ResolveStatement(ctx, stmt);
        ctx.Scopes.RemoveAt(ctx.Scopes.Count - 1);
    }

    private void ResolveStatement(Context ctx, PapyrusStatement stmt)
    {
        switch (stmt)
        {
            case PapyrusDefineStatement def:
            {
                // The initialiser is resolved first, so `int x = x` sees the outer x rather than
                // binding to the variable being declared.
                if (def.Initializer != null) ResolveExpression(ctx, def.Initializer);
                var type = TypeOfRef(ctx, def.Type);
                var binding = new PapyrusBinding(PapyrusBindingKind.Local, def.Name, type, null, ctx.Script);
                ctx.Scopes[^1][def.Name] = binding;
                ctx.Bindings[def] = binding;
                break;
            }

            case PapyrusAssignStatement assign:
                ResolveExpression(ctx, assign.Target);
                ResolveExpression(ctx, assign.Value);
                break;

            case PapyrusExpressionStatement expr:
                ResolveExpression(ctx, expr.Expression);
                break;

            case PapyrusReturnStatement ret:
                if (ret.Value != null) ResolveExpression(ctx, ret.Value);
                break;

            case PapyrusIfStatement iff:
                foreach (var branch in iff.Branches)
                {
                    ResolveExpression(ctx, branch.Condition);
                    ResolveBlock(ctx, branch.Body);
                }
                if (iff.ElseBody != null) ResolveBlock(ctx, iff.ElseBody);
                break;

            case PapyrusWhileStatement wh:
                ResolveExpression(ctx, wh.Condition);
                ResolveBlock(ctx, wh.Body);
                break;
        }
    }

    // ---- expressions ----------------------------------------------------------------------------

    /// <summary>
    /// The left of a dot. Identical to <see cref="ResolveExpression"/> except for a bare name that
    /// resolves to nothing.
    /// </summary>
    /// <remarks>
    /// <c>SomeScript.SomeGlobal()</c> is the only shape in which an unknown bare name legitimately
    /// refers to a file rather than to a variable, so a name that fails here means the roots are
    /// probably incomplete rather than that the author made a typo. Reporting it would fire on every
    /// script that calls into a framework the caller did not include -- which is what it did before
    /// this existed, on a real script calling a real framework that was simply under a different
    /// root. A bare name used as a value still reports, because there is nothing else it could be.
    /// </remarks>
    private PapyrusType ResolveMemberTarget(Context ctx, PapyrusExpression target)
    {
        if (target is not PapyrusIdentifierExpression id) return ResolveExpression(ctx, target);

        var binding = LookupName(ctx, id.Name);
        if (binding == null)
        {
            ctx.BaseChainComplete = false;
            ctx.Types[id] = PapyrusType.Error;
            return PapyrusType.Error;
        }

        ctx.Bindings[id] = binding;
        ctx.Types[id] = binding.Type;
        return binding.Type;
    }

    /// <summary>The callee of a call. A bare name here is a function, never a local.</summary>
    private PapyrusType ResolveCallee(Context ctx, PapyrusExpression callee)
    {
        if (callee is not PapyrusIdentifierExpression id) return ResolveExpression(ctx, callee);

        var binding = LookupName(ctx, id.Name, inCallPosition: true);
        if (binding == null)
        {
            ReportUnresolved(ctx, id.Name, id.Span);
            ctx.Types[id] = PapyrusType.Error;
            return PapyrusType.Error;
        }

        ctx.Bindings[id] = binding;
        ctx.Types[id] = binding.Type;
        return binding.Type;
    }

    private PapyrusType ResolveExpression(Context ctx, PapyrusExpression expr)
    {
        var type = ComputeType(ctx, expr);
        ctx.Types[expr] = type;
        return type;
    }

    private PapyrusType ComputeType(Context ctx, PapyrusExpression expr)
    {
        switch (expr)
        {
            case PapyrusLiteralExpression lit:
                return lit.Kind switch
                {
                    PapyrusLiteralKind.Int => PapyrusType.Int,
                    PapyrusLiteralKind.Float => PapyrusType.Float,
                    PapyrusLiteralKind.String => PapyrusType.String,
                    PapyrusLiteralKind.Bool => PapyrusType.Bool,
                    _ => PapyrusType.None,
                };

            case PapyrusIdentifierExpression id:
            {
                var binding = LookupName(ctx, id.Name);
                if (binding == null)
                {
                    ReportUnresolved(ctx, id.Name, id.Span);
                    return PapyrusType.Error;
                }
                ctx.Bindings[id] = binding;
                return binding.Type;
            }

            case PapyrusMemberExpression member:
            {
                var targetType = ResolveMemberTarget(ctx, member.Target);
                var binding = LookupMember(ctx, member.Target, targetType, member.Name);
                if (binding == null)
                {
                    ReportUnknownMember(ctx, member.Name, targetType, member.NameSpan);
                    return PapyrusType.Error;
                }
                ctx.Bindings[member] = binding;
                return binding.Type;
            }

            case PapyrusIndexExpression index:
            {
                var targetType = ResolveExpression(ctx, index.Target);
                ResolveExpression(ctx, index.Index);
                return targetType.IsArray ? targetType.ElementType! : PapyrusType.Error;
            }

            case PapyrusCallExpression call:
            {
                foreach (var arg in call.Arguments) ResolveExpression(ctx, arg.Value);
                var calleeType = ResolveCallee(ctx, call.Callee);
                var binding = ctx.Bindings.TryGetValue(call.Callee, out var b) ? b : null;
                if (binding != null) ctx.Bindings[call] = binding;
                return calleeType;
            }

            case PapyrusUnaryExpression unary:
            {
                var operand = ResolveExpression(ctx, unary.Operand);
                return unary.Operator == PapyrusTokenKind.Not ? PapyrusType.Bool : operand;
            }

            case PapyrusBinaryExpression bin:
            {
                var left = ResolveExpression(ctx, bin.Left);
                var right = ResolveExpression(ctx, bin.Right);
                return BinaryResult(bin.Operator, left, right);
            }

            case PapyrusCastExpression cast:
                ResolveExpression(ctx, cast.Operand);
                return TypeOfRef(ctx, cast.Type);

            case PapyrusTypeCheckExpression check:
                ResolveExpression(ctx, check.Operand);
                TypeOfRef(ctx, check.Type);
                return PapyrusType.Bool;

            case PapyrusNewArrayExpression newArray:
                ResolveExpression(ctx, newArray.Size);
                return PapyrusType.ArrayOf(TypeOfRef(ctx, newArray.ElementType));

            case PapyrusNewStructExpression newStruct:
                return TypeOfRef(ctx, newStruct.Type);

            default:
                return PapyrusType.Error;
        }
    }

    /// <summary>
    /// The type an operator yields. Comparisons and the logical operators give bool; arithmetic
    /// promotes to float when either side is a float, which is the only direction the implicit rule
    /// allows.
    /// </summary>
    private static PapyrusType BinaryResult(PapyrusTokenKind op, PapyrusType left, PapyrusType right)
    {
        switch (op)
        {
            case PapyrusTokenKind.Equal:
            case PapyrusTokenKind.NotEqual:
            case PapyrusTokenKind.Less:
            case PapyrusTokenKind.LessEqual:
            case PapyrusTokenKind.Greater:
            case PapyrusTokenKind.GreaterEqual:
            case PapyrusTokenKind.And:
            case PapyrusTokenKind.Or:
                return PapyrusType.Bool;
        }

        if (left.Kind == PapyrusTypeKind.String || right.Kind == PapyrusTypeKind.String)
            return op == PapyrusTokenKind.Plus ? PapyrusType.String : PapyrusType.Error;
        if (left.Kind == PapyrusTypeKind.Float || right.Kind == PapyrusTypeKind.Float) return PapyrusType.Float;
        if (left.Kind == PapyrusTypeKind.Int && right.Kind == PapyrusTypeKind.Int) return PapyrusType.Int;
        return left.Kind == PapyrusTypeKind.Error ? right : left;
    }

    // ---- name lookup ----------------------------------------------------------------------------

    /// <summary>A bare identifier, resolved outward: locals, then Self's members, then wider.</summary>
    /// <param name="inCallPosition">
    /// True for the name in <c>Foo(...)</c>. Papyrus has no function values, so a name being called
    /// cannot be a local or a parameter however well it matches one -- and since the language is
    /// case-insensitive, it can match one by accident. Vanilla's
    /// <c>Inst307_ZoneQuestRespawnScript</c> does exactly this: it calls <c>RespawnCollection(...)</c>
    /// from a function whose parameter is named <c>respawnCollection</c>, and binding the call to the
    /// parameter gives the call that parameter's type instead of the function's return type.
    /// </param>
    private PapyrusBinding? LookupName(Context ctx, string name, bool inCallPosition = false)
    {
        if (!inCallPosition)
        {
            for (int i = ctx.Scopes.Count - 1; i >= 0; i--)
            {
                if (ctx.Scopes[i].TryGetValue(name, out var local)) return local;
            }
        }

        // "Self refers to the instance of the script that the function is running on", and a global
        // function has none. That rules out instance members inside one -- but NOT the script's own
        // global functions, which stay callable unqualified. Vanilla leans on this heavily: Debug,
        // Game and Utility are entirely global functions calling each other by bare name, and
        // treating "no Self" as "no members at all" reports every one of those as undefined.
        if (ctx.SelfType != null)
        {
            if (string.Equals(name, "self", StringComparison.OrdinalIgnoreCase))
                return new PapyrusBinding(PapyrusBindingKind.SelfKeyword, "Self", ctx.SelfType, ctx.Script, ctx.Script);

            if (string.Equals(name, "parent", StringComparison.OrdinalIgnoreCase) && ctx.Chain.Count > 1)
            {
                var parent = ctx.Chain[1];
                return new PapyrusBinding(
                    PapyrusBindingKind.ParentKeyword, "Parent", PapyrusType.Object(parent.Name), parent, parent);
            }
        }

        var member = LookupOnChain(ctx, ctx.Chain, name, globalsOnly: ctx.SelfType == null);
        if (member != null) return member;

        // An imported script contributes its global functions unqualified.
        foreach (var import in ctx.Imports)
        {
            var hit = LookupOnChain(ctx, _index.BaseChain(import), name, globalsOnly: true);
            if (hit != null) return hit;
        }

        // Finally the name may be a script, which is how a global call and a cast target are written.
        var script = _index.Resolve(name);
        if (script != null)
            return new PapyrusBinding(PapyrusBindingKind.Script, script.Name, PapyrusType.Object(script.Name), script, script);

        return null;
    }

    private PapyrusBinding? LookupOnChain(
        Context ctx, IReadOnlyList<PapyrusScript> chain, string name, bool globalsOnly = false)
    {
        foreach (var level in chain)
        {
            var decl = PapyrusScriptIndex.FindMemberOn(level, name);
            if (decl == null) continue;
            if (globalsOnly && decl is not PapyrusFunctionDecl { IsGlobal: true }) continue;
            return BindingFor(ctx, decl, level);
        }
        return null;
    }

    /// <summary><c>target.name</c>, dispatched on what the target turned out to be.</summary>
    private PapyrusBinding? LookupMember(Context ctx, PapyrusExpression target, PapyrusType targetType, string name)
    {
        // A script name on the left is a static receiver: Game.GetPlayer(), not an instance.
        if (ctx.Bindings.TryGetValue(target, out var targetBinding)
            && targetBinding.Kind == PapyrusBindingKind.Script
            && targetBinding.Owner != null)
        {
            return LookupOnChain(ctx, _index.BaseChain(targetBinding.Owner), name);
        }

        switch (targetType.Kind)
        {
            case PapyrusTypeKind.Array:
                return ArrayMember(targetType, name);

            case PapyrusTypeKind.Object:
            {
                var script = _index.Resolve(targetType.Name);
                if (script == null) { ctx.BaseChainComplete = false; return null; }
                return LookupOnChain(ctx, _index.BaseChain(script), name);
            }

            case PapyrusTypeKind.Struct:
            {
                var (ownerName, structName) = SplitStructName(targetType.Name);
                var owner = _index.Resolve(ownerName);
                var decl = owner?.Structs.FirstOrDefault(
                    s => string.Equals(s.Name, structName, StringComparison.OrdinalIgnoreCase));
                if (decl == null) { ctx.BaseChainComplete = false; return null; }

                var field = decl.Members.FirstOrDefault(
                    m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));
                if (field == null) return null;
                return new PapyrusBinding(
                    PapyrusBindingKind.StructMember, field.Name, TypeOfRef(ctx, field.Type), field, owner);
            }

            // A var holds whatever it holds; its members are not knowable until runtime, so this
            // reports nothing rather than guessing.
            case PapyrusTypeKind.Var:
            case PapyrusTypeKind.Error:
            default:
                return null;
        }
    }

    private PapyrusBinding BindingFor(Context ctx, PapyrusDeclaration decl, PapyrusScript owner) => decl switch
    {
        PapyrusPropertyDecl p =>
            new PapyrusBinding(PapyrusBindingKind.Property, p.Name, TypeOfRef(ctx, p.Type, owner), p, owner),
        PapyrusVariableDecl v =>
            new PapyrusBinding(PapyrusBindingKind.ScriptVariable, v.Name, TypeOfRef(ctx, v.Type, owner), v, owner),
        PapyrusFunctionDecl f =>
            new PapyrusBinding(
                PapyrusBindingKind.Function, f.Name,
                f.ReturnType == null ? PapyrusType.None : TypeOfRef(ctx, f.ReturnType, owner), f, owner),
        PapyrusEventDecl e =>
            new PapyrusBinding(PapyrusBindingKind.Event, e.Name, PapyrusType.None, e, owner),
        PapyrusStructDecl s =>
            new PapyrusBinding(
                PapyrusBindingKind.Struct, s.Name, PapyrusType.StructOf(owner.Name, s.Name), s, owner),
        PapyrusCustomEventDecl c =>
            new PapyrusBinding(PapyrusBindingKind.CustomEvent, c.Name, PapyrusType.String, c, owner),
        _ => new PapyrusBinding(PapyrusBindingKind.ScriptVariable, decl.Name, PapyrusType.Error, decl, owner),
    };

    // ---- array built-ins ------------------------------------------------------------------------

    /// <summary>
    /// The members every array has. Signatures are from the Creation Kit's per-function pages, and
    /// they line up exactly with the array opcodes the .pex format defines, which is a useful
    /// independent confirmation that the list is complete.
    /// </summary>
    private static PapyrusBinding? ArrayMember(PapyrusType arrayType, string name)
    {
        var element = arrayType.ElementType!;
        var kind = PapyrusBindingKind.ArrayMember;

        return name.ToLowerInvariant() switch
        {
            "length" => new PapyrusBinding(kind, "Length", PapyrusType.Int),
            "find" => new PapyrusBinding(kind, "Find", PapyrusType.Int),
            "rfind" => new PapyrusBinding(kind, "RFind", PapyrusType.Int),
            "findstruct" => new PapyrusBinding(kind, "FindStruct", PapyrusType.Int),
            "rfindstruct" => new PapyrusBinding(kind, "RFindStruct", PapyrusType.Int),
            "add" => new PapyrusBinding(kind, "Add", PapyrusType.None),
            "insert" => new PapyrusBinding(kind, "Insert", PapyrusType.None),
            "remove" => new PapyrusBinding(kind, "Remove", PapyrusType.None),
            "removelast" => new PapyrusBinding(kind, "RemoveLast", PapyrusType.None),
            "clear" => new PapyrusBinding(kind, "Clear", PapyrusType.None),
            // GetMatchingStructs is documented on the Arrays page and yields an array of the element.
            "getmatchingstructs" => new PapyrusBinding(kind, "GetMatchingStructs", PapyrusType.ArrayOf(element)),
            _ => null,
        };
    }

    // ---- types ----------------------------------------------------------------------------------

    /// <summary>Turns a written type into a resolved one, reporting a name that names nothing.</summary>
    private PapyrusType TypeOfRef(Context ctx, PapyrusTypeRef reference, PapyrusScript? relativeTo = null)
    {
        var resolved = ResolveTypeName(ctx, reference.Name, relativeTo ?? ctx.Script);
        if (resolved == null)
        {
            ctx.BaseChainComplete = false;
            resolved = PapyrusType.Error;
        }
        var final = reference.IsArray ? PapyrusType.ArrayOf(resolved) : resolved;
        ctx.Types[reference] = final;
        return final;
    }

    private PapyrusType? ResolveTypeName(Context ctx, string name, PapyrusScript relativeTo) =>
        ResolveTypeName(_index, name, relativeTo);

    /// <summary>
    /// A written type name, relative to the script that wrote it. Null when nothing declares it.
    /// </summary>
    /// <remarks>
    /// Public and static because the code generator needs the same answer about a script it is not
    /// resolving: a call's argument has to be cast to the parameter type, and that parameter is
    /// declared in the callee's file, whose nodes are absent from this script's
    /// <see cref="PapyrusResolution"/>. Sharing the one implementation is what keeps the back end
    /// from re-deriving a rule -- struct names in particular -- and getting it subtly different.
    /// </remarks>
    public static PapyrusType? ResolveTypeName(PapyrusScriptIndex index, string name, PapyrusScript relativeTo)
    {
        var primitive = PapyrusType.Primitive(name);
        if (primitive != null) return primitive;

        // A struct is written bare inside its own script, and Script:Struct from outside.
        foreach (var level in index.BaseChain(relativeTo))
        {
            var local = level.Structs.FirstOrDefault(
                s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
            if (local != null) return PapyrusType.StructOf(level.Name, local.Name);
        }

        int split = name.LastIndexOf(':');
        if (split > 0)
        {
            var ownerName = name[..split];
            var structName = name[(split + 1)..];
            var owner = index.Resolve(ownerName);
            if (owner != null
                && owner.Structs.Any(s => string.Equals(s.Name, structName, StringComparison.OrdinalIgnoreCase)))
            {
                return PapyrusType.StructOf(owner.Name, structName);
            }
        }

        var script = index.Resolve(name);
        if (script != null) return PapyrusType.Object(script.Name);

        // A name the index cannot see is not necessarily wrong: the roots may simply be incomplete.
        // Object is the honest guess, and BaseChainComplete records the doubt.
        return null;
    }

    private static (string owner, string name) SplitStructName(string qualified)
    {
        int split = qualified.LastIndexOf(':');
        return split <= 0 ? (string.Empty, qualified) : (qualified[..split], qualified[(split + 1)..]);
    }

    // ---- diagnostics ----------------------------------------------------------------------------

    private static void ReportUnresolved(Context ctx, string name, PapyrusSpan span)
    {
        if (!ctx.BaseChainComplete) return;
        ctx.Diagnostics.Add(new PapyrusDiagnostic(
            PapyrusDiagnosticCodes.UnresolvedName, PapyrusSeverity.Error,
            $"'{name}' is not defined.", span, ctx.Script.FilePath));
    }

    private static void ReportUnknownMember(Context ctx, string name, PapyrusType targetType, PapyrusSpan span)
    {
        if (!ctx.BaseChainComplete) return;
        if (targetType.Kind is PapyrusTypeKind.Error or PapyrusTypeKind.Var) return;
        ctx.Diagnostics.Add(new PapyrusDiagnostic(
            PapyrusDiagnosticCodes.UnknownMember, PapyrusSeverity.Error,
            $"'{targetType}' has no member named '{name}'.", span, ctx.Script.FilePath));
    }
}
