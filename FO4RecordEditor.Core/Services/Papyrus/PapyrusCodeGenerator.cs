using System;
using System.Collections.Generic;
using System.Linq;

namespace FO4RecordEditor.Services.Papyrus;

/// <summary>Knobs a caller may set on an emitted <c>.pex</c>; everything has a working default.</summary>
public sealed class PapyrusCodeGenOptions
{
    /// <summary>Recorded in the header. The Creation Kit writes the absolute path it compiled.</summary>
    public string SourceFileName { get; set; } = "";

    public string UserName { get; set; } = "";

    public string ComputerName { get; set; } = "";

    /// <summary>Unix seconds. Left at zero it makes output reproducible, which is worth more here.</summary>
    public long CompilationTime { get; set; }

    public long ModificationTime { get; set; }

    /// <summary>
    /// Line numbers, property groups and struct member order. The Creation Kit emits these by
    /// default and the game does not need them; a stack trace does.
    /// </summary>
    public bool EmitDebugInfo { get; set; } = true;

    /// <summary>Null uses <see cref="PapyrusUserFlagTable.Fallout4Default"/>.</summary>
    public PapyrusUserFlagTable? UserFlags { get; set; }

    /// <summary>Whether calls into <c>DebugOnly</c> code are emitted at all.</summary>
    /// <remarks>
    /// <c>DebugOnly</c> and <c>BetaOnly</c> are language flags, and a call into something carrying
    /// one is not compiled to a no-op -- it is not compiled at all. Bethesda's own
    /// <c>ObjectReference.pex</c> proves it: the source has sixteen <c>debug.</c> calls and the
    /// compiled object has zero <c>callstatic debug</c>, because <c>Debug</c> itself is declared
    /// <c>Scriptname Debug Native DebugOnly Hidden</c>.
    /// <para>
    /// Default true, which is to say do not strip, because three of the four compiler builds in the
    /// corpus kept them and silently deleting an author's logging is the worse failure. Only a call
    /// used as a whole statement can be dropped; one whose value is used has no safe removal, so
    /// that is refused rather than approximated.
    /// </para>
    /// </remarks>
    public bool EmitDebugOnlyCode { get; set; } = true;

    /// <summary>The <c>BetaOnly</c> half of <see cref="EmitDebugOnlyCode"/>.</summary>
    public bool EmitBetaOnlyCode { get; set; } = true;
}

/// <summary>
/// Turns a resolved, type-checked script into a <c>.pex</c>.
/// </summary>
/// <remarks>
/// The last piece of issue #78 phase 2. Everything above it -- lexer, parser, script index,
/// resolver, type checker -- says what the source means; <see cref="PexWriter"/> below it turns an
/// object model into bytes that round trip a real compiler's output byte for byte. This is the
/// middle: a resolved AST to <see cref="PexInstruction"/>s.
/// <para>
/// <b>Every instruction shape here was read off real Creation Kit output</b>, disassembled from the
/// (.psc, .pex) pairs on the development machine, rather than taken from a specification. The
/// non-obvious ones, with the file they came from:
/// </para>
/// <list type="bullet">
/// <item><b>An if/elseif chain ends every branch with an unconditional jump to the end, including
/// the last branch when there is no else -- but never after the else body.</b> So a lone
/// <c>If</c> compiles to <c>jmpf</c>, body, <c>jmp 1</c>. Confirmed in PulowskShelterScript's
/// OnLoad and OnActivate, which nest all three shapes.</item>
/// <item><b><c>&amp;&amp;</c> and <c>||</c> short-circuit through one shared bool slot</b>: evaluate
/// the left into it, <c>jmpf</c> (or <c>jmpt</c>) past the right, evaluate the right into the same
/// slot. MetroCurrency:Manager line 71 and line 84.</item>
/// <item><b>Implicit conversions are materialised.</b> Passing an int where a float is wanted, or a
/// child object where a parent is wanted, emits an explicit <c>cast</c> into a typed temporary; the
/// runtime does not convert for you. <c>None</c> is the exception and is passed raw.</item>
/// <item><b>Optional arguments are filled in at the call site.</b>
/// <c>akPlayer.PlaceAtMe(GraveMarker)</c> emits four more operands from the declaration's defaults.
/// This is why an unresolvable callee is refused rather than guessed at: the arity is not derivable
/// from the call.</item>
/// <item><b>An auto property is read and written through its backing variable inside its own
/// script</b> (<c>::Name_var</c>), and through <c>propget</c>/<c>propset</c> from anywhere else.</item>
/// <item><b>A remote or custom event handler compiles to a function named
/// <c>::remote_&lt;Type&gt;_&lt;Event&gt;</c></b>, which is also how the decompiler recognises one.</item>
/// </list>
/// <para>
/// <b>What is deliberately not reproduced.</b> The Creation Kit's own output is not a function of
/// the source alone: struct members, object variables, properties and functions are written in a
/// hash order that varies between files, temporaries are numbered from an object-wide counter with
/// gaps, and two compiler builds in the corpus disagree about whether
/// <c>int x = f()</c> writes the call's result straight into <c>x</c>. Emission here is source
/// order and the numbering is deterministic. Byte-identity with the CK is therefore not the bar and
/// cannot be; see <c>PapyrusDifferentialTests</c> for what is compared instead.
/// </para>
/// <para>
/// <b>Silence beats a false positive, restated for a back end: emit nothing you cannot justify.</b>
/// The resolver and checker report nothing when sources are incomplete, which is what makes their
/// numbers mean anything. The equivalent here is to refuse. A call whose declaration is not on the
/// roots has unknown arity once defaults exist, so it produces a diagnostic and no file, rather
/// than an instruction that is the right shape and the wrong length.
/// </para>
/// </remarks>
public sealed class PapyrusCodeGenerator
{
    private readonly PapyrusScriptIndex _index;

    public PapyrusCodeGenerator(PapyrusScriptIndex index) =>
        _index = index ?? throw new ArgumentNullException(nameof(index));

    // ---- opcodes ---------------------------------------------------------------------------

    private static readonly Dictionary<string, byte> OpByName = BuildOpTable();

    private static Dictionary<string, byte> BuildOpTable()
    {
        var map = new Dictionary<string, byte>(StringComparer.Ordinal);
        for (int i = 0; i < PexFile.OpCodes.Length; i++) map[PexFile.OpCodes[i].name] = (byte)i;
        return map;
    }

    // ---- per-generation state --------------------------------------------------------------

    private PapyrusScript _script = null!;
    private PapyrusResolution _resolution = null!;
    private PapyrusUserFlagTable _userFlags = null!;
    private List<PapyrusDiagnostic> _diagnostics = null!;
    private List<PexDebugFunction> _debug = null!;

    /// <summary>
    /// Object-wide temporary counter. The Creation Kit numbers temporaries across a whole object in
    /// source order of the functions rather than per function, and reuses a freed number, so this is
    /// object state and not function state.
    /// </summary>
    private int _tempCounter;

    /// <summary>Auto and auto-read-only properties declared on this script, by name.</summary>
    private Dictionary<string, PapyrusPropertyDecl> _ownAutoProperties = null!;

    private bool _emitDebugOnly = true;
    private bool _emitBetaOnly = true;

    /// <summary>The flag that excludes this call target from the build, or null when it is included.</summary>
    private string? ExcludedBy(PapyrusBinding binding)
    {
        bool Flagged(string flag) =>
            binding.Declaration?.HasFlag(flag) == true || binding.Owner?.HasFlag(flag) == true;

        if (!_emitDebugOnly && Flagged("debugonly")) return "DebugOnly";
        if (!_emitBetaOnly && Flagged("betaonly")) return "BetaOnly";
        return null;
    }

    public PexFile? Generate(
        PapyrusScript script,
        PapyrusResolution resolution,
        PapyrusCodeGenOptions? options,
        out IReadOnlyList<PapyrusDiagnostic> diagnostics)
    {
        if (script == null) throw new ArgumentNullException(nameof(script));
        if (resolution == null) throw new ArgumentNullException(nameof(resolution));

        options ??= new PapyrusCodeGenOptions();
        _script = script;
        _resolution = resolution;
        _userFlags = options.UserFlags ?? PapyrusUserFlagTable.Fallout4Default();
        _diagnostics = new List<PapyrusDiagnostic>();
        _debug = new List<PexDebugFunction>();
        _emitDebugOnly = options.EmitDebugOnlyCode;
        _emitBetaOnly = options.EmitBetaOnlyCode;
        _tempCounter = 0;
        _ownAutoProperties = script.Properties
            .Where(p => p.Kind == PapyrusPropertyKind.Auto)
            .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var pex = new PexFile
        {
            MajorVersion = 3,
            MinorVersion = 9,
            GameId = 2,
            CompilationTime = options.CompilationTime,
            ModificationTime = options.ModificationTime,
            SourceFileName = options.SourceFileName,
            UserName = options.UserName,
            ComputerName = options.ComputerName,
            HasDebugInfo = options.EmitDebugInfo,
        };
        pex.UserFlags.AddRange(_userFlags.Flags);

        var obj = BuildObject();
        pex.Objects.Add(obj);

        if (options.EmitDebugInfo)
        {
            pex.DebugFunctions.AddRange(_debug);
            pex.PropertyGroups.AddRange(BuildPropertyGroups());
            pex.StructOrders.AddRange(BuildStructOrders());
        }

        pex.RebuildStringTable();
        diagnostics = _diagnostics;
        return _diagnostics.Any(d => d.Severity == PapyrusSeverity.Error) ? null : pex;
    }

    // ---- object ----------------------------------------------------------------------------

    private PexObject BuildObject()
    {
        var obj = new PexObject
        {
            Name = _script.Name,
            ParentClassName = ParentClassName(),
            DocString = _script.Documentation ?? "",
            Const = _script.HasFlag("const"),
            UserFlags = _userFlags.MaskFor(_script.Flags),
            AutoStateName = _script.States.FirstOrDefault(s => s.IsAuto)?.Name ?? "",
        };

        foreach (var st in _script.Structs)
        {
            var pexStruct = new PexStruct { Name = st.Name };
            foreach (var m in st.Members)
            {
                pexStruct.Members.Add(new PexStructMember
                {
                    Name = m.Name,
                    Type = PexTypeName(TypeOf(m.Type), m.Type),
                    UserFlags = _userFlags.MaskFor(m.Flags),
                    DefaultValue = ConstantOrDefault(m.Initializer, TypeOf(m.Type)),
                    Const = m.HasFlag("const"),
                    DocString = m.Documentation ?? "",
                });
            }
            obj.Structs.Add(pexStruct);
        }

        // An auto property's storage is a hidden variable the compiler generates. It is emitted
        // before the author's own variables purely so the order is stated rather than incidental;
        // the Creation Kit's is a hash order that differs file to file.
        foreach (var p in _script.Properties)
        {
            if (p.Kind != PapyrusPropertyKind.Auto) continue;
            obj.Variables.Add(new PexVariable
            {
                Name = BackingVarName(p.Name),
                Type = PexTypeName(TypeOf(p.Type), p.Type),
                // Conditional describes the storage, not the accessor, so it rides on the backing
                // variable. See ConditionalMask.
                UserFlags = _userFlags.MaskFor(p.Flags) & ConditionalMask,
                DefaultValue = ConstantOrDefault(p.Initializer, TypeOf(p.Type)),
                Const = p.HasFlag("const"),
            });
        }

        foreach (var v in _script.Variables)
        {
            obj.Variables.Add(new PexVariable
            {
                Name = v.Name,
                Type = PexTypeName(TypeOf(v.Type), v.Type),
                UserFlags = _userFlags.MaskFor(v.Flags),
                DefaultValue = ConstantOrDefault(v.Initializer, TypeOf(v.Type)),
                Const = v.HasFlag("const"),
            });
        }

        foreach (var p in _script.Properties) obj.Properties.Add(BuildProperty(p));

        // The empty state carries this script's own top-level functions and events; a named state
        // carries only its overrides.
        var emptyState = new PexState { Name = "" };
        foreach (var fn in _script.Functions) emptyState.Functions.Add(BuildFunction(fn, "", fn.Name, 0));
        foreach (var ev in _script.Events) emptyState.Functions.Add(BuildEvent(ev, ""));
        obj.States.Add(emptyState);

        foreach (var state in _script.States)
        {
            var pexState = new PexState { Name = state.Name };
            foreach (var fn in state.Functions) pexState.Functions.Add(BuildFunction(fn, state.Name, fn.Name, 0));
            foreach (var ev in state.Events) pexState.Functions.Add(BuildEvent(ev, state.Name));
            obj.States.Add(pexState);
        }

        return obj;
    }

    /// <summary>
    /// The bit for <c>Conditional</c>, which a property never carries.
    /// </summary>
    /// <remarks>
    /// Counted over the whole vanilla corpus. Of every user flag bit set on a property in 7,875
    /// shipped objects, only two ever appear: bit 0 (<c>Hidden</c>) 381 times and bit 5
    /// (<c>Mandatory</c>) 2,440 times. The conditional bit appears on properties **zero** times, and
    /// on variables 846 times, which is where it belongs: <c>Conditional</c> is what lets a
    /// condition function read the storage, so it describes the variable rather than the accessor.
    /// An auto property's backing variable carries it, and the property does not.
    /// </remarks>
    private uint ConditionalMask => _userFlags.MaskFor("conditional");

    private PexProperty BuildProperty(PapyrusPropertyDecl p)
    {
        var type = PexTypeName(TypeOf(p.Type), p.Type);
        var prop = new PexProperty
        {
            Name = p.Name,
            Type = type,
            DocString = p.Documentation ?? "",
            UserFlags = _userFlags.MaskFor(p.Flags) & ~ConditionalMask,
        };

        switch (p.Kind)
        {
            // 0x01 readable, 0x02 writable, 0x04 backed by a generated variable.
            case PapyrusPropertyKind.Auto:
                prop.Flags = 0x07;
                prop.AutoVarName = BackingVarName(p.Name);
                break;

            // An AutoReadOnly is a constant, not storage. Real output gives it no backing variable
            // and no autovar flag: it is a read-only property whose generated Get returns the
            // literal. Confirmed across all 32 of Form.psc's kSlotMask properties, each of which
            // compiles to flags 0x01 and a one-instruction getter.
            case PapyrusPropertyKind.AutoReadOnly:
                prop.Flags = 0x01;
                prop.ReadHandler = ConstantGetter(p, type);
                break;

            default:
                prop.Flags = (byte)((p.Getter != null ? 0x01 : 0) | (p.Setter != null ? 0x02 : 0));
                if (p.Getter != null) prop.ReadHandler = BuildFunction(p.Getter, "", p.Name, 1);
                if (p.Setter != null) prop.WriteHandler = BuildFunction(p.Setter, "", p.Name, 2);
                break;
        }

        return prop;
    }

    /// <summary>The generated body of an <c>AutoReadOnly</c>: one instruction returning the literal.</summary>
    private PexFunction ConstantGetter(PapyrusPropertyDecl property, string pexType)
    {
        var value = ConstantOrDefault(property.Initializer, TypeOf(property.Type));
        var fn = new PexFunction { Name = "", ReturnType = pexType };
        var ret = new PexInstruction
        {
            OpCode = OpByName["return"],
            Mnemonic = "return",
            FixedArgCount = 1,
            Line = property.Span.Line,
        };
        ret.Args.Add(value);
        fn.Instructions.Add(ret);

        _debug.Add(new PexDebugFunction
        {
            ObjectName = _script.Name,
            StateName = "",
            FunctionName = property.Name,
            FunctionType = 1,
            LineNumbers = { (ushort)Math.Clamp(property.Span.Line, 0, ushort.MaxValue) },
        });
        return fn;
    }

    private static string BackingVarName(string propertyName) => "::" + propertyName + "_var";

    /// <summary>
    /// The parent written into the object, which is <c>ScriptObject</c> for a script that extends
    /// nothing.
    /// </summary>
    /// <remarks>
    /// Not in the language reference, which describes <c>Extends</c> as optional and stops there.
    /// Every script in the corpus that omits it -- <c>F4SE</c>, <c>Form</c>, <c>Hydra:Vectors2</c>,
    /// 59 in all -- still carries <c>ScriptObject</c> here. <c>ScriptObject</c> itself is the one
    /// object with an empty parent, because it is the root.
    /// </remarks>
    private string ParentClassName()
    {
        if (!string.IsNullOrEmpty(_script.Extends))
            return _index.Resolve(_script.Extends!)?.Name ?? _script.Extends!;

        return _script.Name.Equals("ScriptObject", StringComparison.OrdinalIgnoreCase) ? "" : "ScriptObject";
    }

    private IEnumerable<PexPropertyGroup> BuildPropertyGroups()
    {
        // Real output carries one entry per declared Group plus one with an empty name listing the
        // ungrouped properties, in declaration order. Verified against MetroCurrency:Manager and
        // Permadeath:GraveManager, both of which declare no groups and still emit the empty one.
        var ungrouped = _script.Properties.Where(p => p.GroupName == null).Select(p => p.Name).ToList();
        if (ungrouped.Count > 0)
        {
            var group = new PexPropertyGroup { ObjectName = _script.Name, GroupName = "", DocString = "" };
            group.PropertyNames.AddRange(ungrouped);
            yield return group;
        }

        foreach (var g in _script.Groups)
        {
            var group = new PexPropertyGroup
            {
                ObjectName = _script.Name,
                GroupName = g.Name,
                DocString = g.Documentation ?? "",
                UserFlags = _userFlags.MaskFor(g.Flags),
            };
            group.PropertyNames.AddRange(g.Properties.Select(p => p.Name));
            yield return group;
        }
    }

    private IEnumerable<PexStructOrder> BuildStructOrders()
    {
        foreach (var st in _script.Structs)
        {
            var order = new PexStructOrder { ObjectName = _script.Name, OrderName = st.Name };
            order.MemberNames.AddRange(st.Members.Select(m => m.Name));
            yield return order;
        }
    }

    // ---- functions -------------------------------------------------------------------------

    private PexFunction BuildFunction(PapyrusFunctionDecl decl, string stateName, string debugName, byte debugType)
    {
        var returnType = decl.ReturnType == null
            ? PapyrusType.None
            : TypeOf(decl.ReturnType);

        var fn = new PexFunction
        {
            // A property handler carries no name of its own; it is found through the property.
            Name = debugType == 0 ? decl.Name : "",
            ReturnType = decl.ReturnType == null ? "None" : PexTypeName(returnType, decl.ReturnType),
            DocString = decl.Documentation ?? "",
            UserFlags = _userFlags.MaskFor(decl.Flags),
            IsGlobal = decl.IsGlobal,
            IsNative = decl.IsNative,
        };
        EmitBody(fn, decl, returnType, stateName, debugName, debugType);
        return fn;
    }

    private PexFunction BuildEvent(PapyrusEventDecl decl, string stateName)
    {
        // Event ObjectReference.OnActivate(...) compiles to a function whose name encodes the
        // remote type, which is also how PapyrusDecompiler recognises one on the way back.
        var name = decl.RemoteObjectType == null
            ? decl.Name
            : "::remote_" + decl.RemoteObjectType + "_" + decl.Name;

        var fn = new PexFunction
        {
            Name = name,
            ReturnType = "None",
            DocString = decl.Documentation ?? "",
            UserFlags = _userFlags.MaskFor(decl.Flags),
            IsGlobal = false,
            IsNative = decl.IsNative,
        };
        EmitBody(fn, decl, PapyrusType.None, stateName, name, 0);
        return fn;
    }

    private void EmitBody(
        PexFunction fn,
        PapyrusCallableDecl decl,
        PapyrusType returnType,
        string stateName,
        string debugName,
        byte debugType)
    {
        foreach (var p in decl.Parameters)
        {
            fn.Params.Add(new PexTypedName { Name = p.Name, Type = PexTypeName(TypeOf(p.Type), p.Type) });
        }

        if (decl.IsNative) return;

        var emitter = new BodyEmitter(this, fn, decl, returnType);
        emitter.Run();

        _debug.Add(new PexDebugFunction
        {
            ObjectName = _script.Name,
            StateName = stateName,
            FunctionName = debugName,
            FunctionType = debugType,
            LineNumbers = fn.Instructions.Select(i => (ushort)Math.Clamp(i.Line, 0, ushort.MaxValue)).ToList(),
        });
    }

    // ---- shared helpers --------------------------------------------------------------------

    private PapyrusType TypeOf(PapyrusNode node) => _resolution.TypeOf(node);

    /// <summary>
    /// A written type from a script other than the one being compiled.
    /// </summary>
    /// <remarks>
    /// A <see cref="PapyrusResolution"/> only knows the nodes of its own script, so a callee's
    /// parameter types -- which is what an argument has to be cast to -- are not in it. Reading them
    /// as <c>Error</c> is what made the first differential run skip every widening object cast the
    /// Creation Kit emits, so this resolves them against the script that declares them instead.
    /// </remarks>
    /// <summary>
    /// The operand a custom event name compiles to: the declaring script, lowercased, an underscore,
    /// and the name exactly as the call site spelled it.
    /// </summary>
    /// <remarks>
    /// Measured over the vanilla corpus rather than reasoned about. All 232 custom event operands
    /// are qualified, none is bare, and all 232 name a script that really does declare that event.
    /// <para>
    /// Two details only the corpus could settle. The qualifier is the script that <em>declares</em>
    /// the event, not the target's concrete type: were it the concrete type, an inherited event
    /// would produce a prefix that declares nothing, and there is no such case in 232. And the event
    /// half keeps the <em>call site's</em> spelling, not the declaration's: BirdSpawnerScript
    /// declares <c>customEvent flee</c>, BirdCritterScript calls it with <c>"Flee"</c>, and the
    /// shipped operand is <c>birdspawnerscript_Flee</c>. Three of the 232 turn on that difference,
    /// so a rule read off a sample would have had it backwards.
    /// </para>
    /// <para>
    /// Null when this is not a literal, or when no ancestor declares the name. Neither is this
    /// method's business to report: the checker owns that, and a silent wrong qualification would be
    /// worse than leaving the string alone.
    /// </para>
    /// </remarks>
    internal PexValue? QualifyCustomEvent(
        PapyrusExpression argument, PapyrusExpression?[] slots, int index, string? receiverScript)
    {
        if (argument is not PapyrusLiteralExpression { Kind: PapyrusLiteralKind.String } literal) return null;

        // The target is the nearest preceding object argument, which is how RegisterForCustomEvent
        // names the sender. With none, the call is about the caller itself.
        string? target = receiverScript;
        for (int i = index - 1; i >= 0; i--)
        {
            if (slots[i] == null) continue;
            var type = TypeOf(slots[i]!);
            if (type.Kind == PapyrusTypeKind.Object) { target = type.Name; break; }
        }
        if (target == null) return null;

        var declarer = DeclaringScriptOf(target, literal.Text);
        return declarer == null
            ? null
            : new PexValue
            {
                Type = PexValueType.String,
                Str = declarer.ToLowerInvariant() + "_" + literal.Text,
            };
    }

    /// <summary>Walks up from a script to whichever ancestor declares this custom event.</summary>
    private string? DeclaringScriptOf(string scriptName, string eventName)
    {
        var script = _index.Resolve(scriptName);
        if (script == null) return null;

        // BaseChain is the script and its ancestors in order, and it already guards a cycle in
        // Extends, so the nearest declaration wins the same way a member lookup does.
        foreach (var ancestor in _index.BaseChain(script))
        {
            if (ancestor.CustomEvents.Any(e => e.Name.Equals(eventName, StringComparison.OrdinalIgnoreCase)))
                return ancestor.Name;
        }
        return null;
    }

    internal PapyrusType ForeignType(PapyrusTypeRef reference, PapyrusScript? owner)
    {
        var known = _resolution.TypeOf(reference);
        if (known.Kind != PapyrusTypeKind.Error) return known;
        if (owner == null) return PapyrusType.Error;

        var resolved = PapyrusResolver.ResolveTypeName(_index, reference.Name, owner);
        if (resolved == null) return PapyrusType.Error;
        return reference.IsArray ? PapyrusType.ArrayOf(resolved) : resolved;
    }

    /// <summary>
    /// Records a refusal, once per site.
    /// </summary>
    /// <remarks>
    /// The same written type is emitted more than once -- a property's type appears on the property
    /// and again on its backing variable -- so an unresolvable one would otherwise be reported twice
    /// for the same source position. A caller reading a list where every line repeats stops reading
    /// it, and the point of refusing is that the message gets read.
    /// </remarks>
    private void Report(string code, string message, PapyrusSpan span)
    {
        foreach (var existing in _diagnostics)
        {
            if (existing.Code == code && existing.Span.Equals(span) && existing.Message == message) return;
        }
        _diagnostics.Add(new PapyrusDiagnostic(code, PapyrusSeverity.Error, message, span, _script.FilePath));
    }

    /// <summary>The name this type is written under in a <c>.pex</c>.</summary>
    /// <remarks>
    /// Primitives are canonicalised to the spelling real output uses (<c>Bool</c>, <c>Int</c>,
    /// <c>Float</c>, <c>String</c>, <c>Var</c>, <c>None</c>); a script type is its declared name; a
    /// struct is <c>owner#struct</c>, which is the one place the format does not simply reuse source
    /// spelling. Case is not canonicalised beyond that because real output does not agree with
    /// itself about it -- the same compiler writes <c>ObjectReference</c> in one slot and
    /// <c>objectreference</c> in another -- and Papyrus is case-insensitive throughout.
    /// </remarks>
    internal string PexTypeName(PapyrusType type, PapyrusNode? at = null)
    {
        switch (type.Kind)
        {
            case PapyrusTypeKind.None: return "None";
            case PapyrusTypeKind.Bool: return "Bool";
            case PapyrusTypeKind.Int: return "Int";
            case PapyrusTypeKind.Float: return "Float";
            case PapyrusTypeKind.String: return "String";
            case PapyrusTypeKind.Var: return "Var";
            case PapyrusTypeKind.Object: return type.Name;
            case PapyrusTypeKind.Array: return PexTypeName(type.ElementType!, at) + "[]";

            case PapyrusTypeKind.Struct:
            {
                int split = type.Name.LastIndexOf(':');
                return split <= 0
                    ? type.Name
                    : type.Name[..split] + "#" + type.Name[(split + 1)..];
            }

            default:
                Report(
                    PapyrusDiagnosticCodes.CannotEmit,
                    "The type of this is not known, so no instruction can name it. This is a resolver "
                    + "gap or a missing import root, not a syntax error.",
                    at?.Span ?? default);
                return "None";
        }
    }

    /// <summary>A declaration's initialiser as a constant, which is the only form the format stores.</summary>
    private PexValue ConstantOrDefault(PapyrusExpression? initializer, PapyrusType declared)
    {
        if (initializer == null) return ZeroValue(declared);
        var value = TryConstant(initializer, declared);
        if (value != null) return value;

        Report(
            PapyrusDiagnosticCodes.NonConstantInitializer,
            "A variable, property or struct member may only be initialised with a literal; the "
            + "format stores one value, not an expression.",
            initializer.Span);
        return ZeroValue(declared);
    }

    /// <summary>The value an uninitialised slot of this type holds.</summary>
    internal static PexValue ZeroValue(PapyrusType type) => type.Kind switch
    {
        PapyrusTypeKind.Bool => new PexValue { Type = PexValueType.Bool, Bool = false },
        PapyrusTypeKind.Int => new PexValue { Type = PexValueType.Integer, Int = 0 },
        PapyrusTypeKind.Float => new PexValue { Type = PexValueType.Float, Float = 0f },
        PapyrusTypeKind.String => new PexValue { Type = PexValueType.String, Str = "" },
        _ => new PexValue { Type = PexValueType.None },
    };

    /// <summary>
    /// A literal expression as a <see cref="PexValue"/> of the wanted type, or null when it is not
    /// a literal.
    /// </summary>
    /// <remarks>
    /// A leading minus is lexed as an operator rather than as part of the number -- otherwise
    /// <c>a-1</c> could never parse as subtraction -- so a negative constant arrives here as a unary
    /// expression and is folded back.
    /// </remarks>
    internal static PexValue? TryConstant(PapyrusExpression expression, PapyrusType? want = null)
    {
        if (expression is PapyrusUnaryExpression { Operator: PapyrusTokenKind.Minus } neg)
        {
            var inner = TryConstant(neg.Operand, want);
            if (inner == null) return null;
            return inner.Type switch
            {
                PexValueType.Integer => new PexValue { Type = PexValueType.Integer, Int = -inner.Int },
                PexValueType.Float => new PexValue { Type = PexValueType.Float, Float = -inner.Float },
                _ => null,
            };
        }

        if (expression is not PapyrusLiteralExpression literal) return null;

        switch (literal.Kind)
        {
            case PapyrusLiteralKind.String:
                return new PexValue { Type = PexValueType.String, Str = literal.Text };

            case PapyrusLiteralKind.Bool:
                return new PexValue
                {
                    Type = PexValueType.Bool,
                    Bool = literal.Text.Equals("true", StringComparison.OrdinalIgnoreCase),
                };

            case PapyrusLiteralKind.None:
                return new PexValue { Type = PexValueType.None };

            case PapyrusLiteralKind.Int:
            {
                if (!TryParseInt(literal.Text, out int i)) return null;
                // An int literal in a float slot is stored as a float rather than converted at
                // load: `float Property f = 1` has to write 1.0.
                if (want?.Kind == PapyrusTypeKind.Float) return new PexValue { Type = PexValueType.Float, Float = i };
                if (want?.Kind == PapyrusTypeKind.Bool) return new PexValue { Type = PexValueType.Bool, Bool = i != 0 };
                return new PexValue { Type = PexValueType.Integer, Int = i };
            }

            case PapyrusLiteralKind.Float:
            {
                var text = literal.Text;
                // A float literal may carry an `f` suffix; the published Literals production does
                // not mention it and the wiki's own Statement Reference example uses it.
                if (text.EndsWith("f", StringComparison.OrdinalIgnoreCase)) text = text[..^1];
                if (!float.TryParse(text, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float f)) return null;
                return want?.Kind == PapyrusTypeKind.Bool
                    ? new PexValue { Type = PexValueType.Bool, Bool = f != 0f }
                    : new PexValue { Type = PexValueType.Float, Float = f };
            }

            default:
                return null;
        }
    }

    private static bool TryParseInt(string text, out int value)
    {
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return int.TryParse(text[2..], System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out value);
        }
        return int.TryParse(text, System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out value);
    }

    // =========================================================================================
    // Function body emission
    // =========================================================================================

    private sealed class BodyEmitter
    {
        private readonly PapyrusCodeGenerator _gen;
        private readonly PexFunction _fn;
        private readonly PapyrusCallableDecl _decl;
        private readonly PapyrusType _returnType;

        /// <summary>Source name to emitted local name, innermost scope last.</summary>
        private readonly List<Dictionary<string, Local>> _scopes = new();

        /// <summary>Freed temporaries, keyed by emitted type name, newest first.</summary>
        private readonly Dictionary<string, Stack<string>> _freeTemps = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Temporaries handed out and not yet released, in allocation order.</summary>
        private readonly List<(string name, string type)> _liveTemps = new();

        private readonly HashSet<string> _usedLocalNames = new(StringComparer.OrdinalIgnoreCase);

        private string? _noneVar;
        private int _line;
        private int _mangleCounter;

        private readonly record struct Local(string Name, PapyrusType Type);

        public BodyEmitter(PapyrusCodeGenerator gen, PexFunction fn, PapyrusCallableDecl decl, PapyrusType returnType)
        {
            _gen = gen;
            _fn = fn;
            _decl = decl;
            _returnType = returnType;
        }

        public void Run()
        {
            _scopes.Add(new Dictionary<string, Local>(StringComparer.OrdinalIgnoreCase));
            foreach (var p in _decl.Parameters)
            {
                _usedLocalNames.Add(p.Name);
                _scopes[^1][p.Name] = new Local(p.Name, _gen.TypeOf(p.Type));
            }

            EmitBlock(_decl.Body);
            _scopes.Clear();
        }

        // ---- instruction plumbing ----------------------------------------------------------

        private int Add(string mnemonic, params PexValue[] args)
        {
            if (!OpByName.TryGetValue(mnemonic, out var op))
                throw new InvalidOperationException($"No opcode named '{mnemonic}'.");
            var meta = PexFile.OpCodes[op];
            var instruction = new PexInstruction
            {
                OpCode = op,
                Mnemonic = meta.name,
                FixedArgCount = meta.args,
                HasVarArgs = meta.varargs,
                Line = _line,
            };
            instruction.Args.AddRange(args);
            _fn.Instructions.Add(instruction);
            return _fn.Instructions.Count - 1;
        }

        private int Here => _fn.Instructions.Count;

        /// <summary>Fills in a jump's operand, which is an offset relative to the jump itself.</summary>
        private void PatchJump(int jumpIndex, int target)
        {
            var instruction = _fn.Instructions[jumpIndex];
            var operand = instruction.Args[^1];
            operand.Type = PexValueType.Integer;
            operand.Int = target - jumpIndex;
        }

        private static PexValue Id(string name) => new() { Type = PexValueType.Identifier, Str = name };

        private static PexValue Int(int value) => new() { Type = PexValueType.Integer, Int = value };

        private static PexValue NoneValue() => new() { Type = PexValueType.None };

        // ---- locals and temporaries --------------------------------------------------------

        private string DeclareLocal(string sourceName, PapyrusType type)
        {
            // Papyrus scopes a variable to the block it was defined in, so two sibling blocks may
            // each declare the same name while the .pex has one flat local list. The Creation Kit
            // mangles the later one; the name only has to be unique and unwritable in source, which
            // "::" guarantees.
            var emitted = sourceName;
            if (!_usedLocalNames.Add(emitted))
            {
                emitted = "::mangled_" + sourceName + "_" + _mangleCounter++;
                _usedLocalNames.Add(emitted);
            }

            _fn.Locals.Add(new PexTypedName { Name = emitted, Type = _gen.PexTypeName(type) });
            _scopes[^1][sourceName] = new Local(emitted, type);
            return emitted;
        }

        private Local? FindLocal(string name)
        {
            for (int i = _scopes.Count - 1; i >= 0; i--)
            {
                if (_scopes[i].TryGetValue(name, out var local)) return local;
            }
            return null;
        }

        private string NoneVar()
        {
            if (_noneVar != null) return _noneVar;
            _noneVar = "::nonevar";
            _usedLocalNames.Add(_noneVar);
            _fn.Locals.Add(new PexTypedName { Name = _noneVar, Type = "None" });
            return _noneVar;
        }

        private string AllocTemp(PapyrusType type)
        {
            var typeName = _gen.PexTypeName(type);
            if (_freeTemps.TryGetValue(typeName, out var pool) && pool.Count > 0)
            {
                var reused = pool.Pop();
                _liveTemps.Add((reused, typeName));
                return reused;
            }

            var name = "::temp" + _gen._tempCounter++;
            _fn.Locals.Add(new PexTypedName { Name = name, Type = typeName });
            _liveTemps.Add((name, typeName));
            return name;
        }

        /// <summary>
        /// Returns every temporary allocated since <paramref name="mark"/> to the free pool.
        /// </summary>
        /// <remarks>
        /// Taken at the start of a statement and released at its end, so a compound statement holds
        /// its condition's temporaries for the whole of its body. That is what real output does:
        /// PulowskShelterScript's OnLoad reuses <c>::temp2</c> for the condition of the next
        /// statement but allocates a fresh Int for the elseif nested inside the previous one.
        /// </remarks>
        private void ReleaseTemps(int mark)
        {
            for (int i = _liveTemps.Count - 1; i >= mark; i--)
            {
                var (name, type) = _liveTemps[i];
                if (!_freeTemps.TryGetValue(type, out var pool)) _freeTemps[type] = pool = new Stack<string>();
                pool.Push(name);
            }
            if (mark < _liveTemps.Count) _liveTemps.RemoveRange(mark, _liveTemps.Count - mark);
        }

        // ---- statements --------------------------------------------------------------------

        private void EmitBlock(IEnumerable<PapyrusStatement> body)
        {
            _scopes.Add(new Dictionary<string, Local>(StringComparer.OrdinalIgnoreCase));
            foreach (var statement in body) EmitStatement(statement);
            _scopes.RemoveAt(_scopes.Count - 1);
        }

        private void EmitStatement(PapyrusStatement statement)
        {
            int mark = _liveTemps.Count;
            _line = statement.Span.Line;

            switch (statement)
            {
                case PapyrusDefineStatement define:
                {
                    var type = _gen.TypeOf(define.Type);
                    var name = DeclareLocal(define.Name, type);
                    // A local with no initialiser is still zeroed explicitly. `int payment` on its
                    // own line in ObjectReference.SellItem compiles to `assign payment 0`, so the
                    // slot is not assumed to arrive clean.
                    if (define.Initializer != null) EmitInto(define.Initializer, name, type);
                    else Add("assign", Id(name), ZeroValue(type));
                    break;
                }

                case PapyrusAssignStatement assign:
                    EmitAssign(assign);
                    break;

                case PapyrusExpressionStatement expression:
                    EmitDiscarded(expression.Expression);
                    break;

                case PapyrusReturnStatement ret:
                    if (ret.Value == null || _returnType.Kind == PapyrusTypeKind.None) Add("return", NoneValue());
                    else Add("return", ValueAs(ret.Value, _returnType));
                    break;

                case PapyrusIfStatement iff:
                    EmitIf(iff);
                    break;

                case PapyrusWhileStatement wh:
                    EmitWhile(wh);
                    break;
            }

            ReleaseTemps(mark);
        }

        private void EmitIf(PapyrusIfStatement iff)
        {
            var toEnd = new List<int>();

            foreach (var branch in iff.Branches)
            {
                _line = branch.Condition.Span.Line;
                var condition = Value(branch.Condition);
                int jumpPastBranch = Add("jmpf", condition, Int(0));

                EmitBlock(branch.Body);

                // Every branch closes with a jump to the end of the whole chain, including the last
                // one when there is no else -- real output emits `jmp 1` there rather than nothing.
                toEnd.Add(Add("jmp", Int(0)));
                PatchJump(jumpPastBranch, Here);
            }

            // The else body carries no trailing jump: it already falls through to the end.
            if (iff.ElseBody != null) EmitBlock(iff.ElseBody);

            foreach (var jump in toEnd) PatchJump(jump, Here);
        }

        private void EmitWhile(PapyrusWhileStatement wh)
        {
            int top = Here;
            _line = wh.Condition.Span.Line;
            var condition = Value(wh.Condition);
            int exit = Add("jmpf", condition, Int(0));

            EmitBlock(wh.Body);
            PatchJump(Add("jmp", Int(0)), top);
            PatchJump(exit, Here);
        }

        private void EmitAssign(PapyrusAssignStatement assign)
        {
            var targetType = _gen.TypeOf(assign.Target);

            // `x += 1` is `x = x + 1`; the format has no read-modify-write instruction.
            PapyrusExpression value = assign.Value;
            var op = CompoundOperator(assign.Operator);

            switch (assign.Target)
            {
                case PapyrusIdentifierExpression id:
                {
                    var slot = StorageFor(id);
                    if (slot == null) return;
                    if (slot.Value.IsProperty)
                    {
                        if (op == null)
                        {
                            var (stored, receiver) = StoreOrder(
                                () => Materialise(ValueAs(value, targetType), targetType),
                                () => slot.Value.Receiver!);
                            Add("propset", Id(slot.Value.Name), receiver, stored);
                            return;
                        }

                        var current = PropertyGet(slot.Value, id);
                        var result = BinaryInto(
                            null, targetType, op.Value, current, ValueAs(value, targetType), assign.Span);
                        Add("propset", Id(slot.Value.Name), slot.Value.Receiver!, Materialise(result, targetType));
                        return;
                    }

                    if (op == null) { EmitInto(value, slot.Value.Name, targetType); return; }
                    BinaryInto(slot.Value.Name, targetType, op.Value, Id(slot.Value.Name),
                        ValueAs(value, targetType), assign.Span);
                    return;
                }

                case PapyrusMemberExpression member:
                {
                    var binding = _gen._resolution.BindingFor(member);
                    if (binding == null) { CannotEmit(member); return; }

                    if (binding.Kind == PapyrusBindingKind.StructMember)
                    {
                        if (op == null)
                        {
                            var (stored, target) = StoreOrder(
                                () => Materialise(ValueAs(value, targetType), targetType),
                                () => Value(member.Target));
                            Add("struct_set", target, Id(member.Name), stored);
                            return;
                        }

                        var structValue = Value(member.Target);
                        var newValue = StructCompound(
                            structValue, member.Name, targetType, op.Value, value, assign.Span);
                        Add("struct_set", structValue, Id(member.Name), Materialise(newValue, targetType));
                        return;
                    }

                    if (binding.Kind == PapyrusBindingKind.Property)
                    {
                        if (op == null)
                        {
                            var (stored, target) = StoreOrder(
                                () => Materialise(ValueAs(value, targetType), targetType),
                                () => Value(member.Target));
                            Add("propset", Id(member.Name), target, stored);
                            return;
                        }

                        var receiver = Value(member.Target);
                        var current = AllocTemp(targetType);
                        Add("propget", Id(member.Name), receiver, Id(current));
                        var newValue = BinaryInto(null, targetType, op.Value, Id(current),
                            ValueAs(value, targetType), assign.Span);
                        Add("propset", Id(member.Name), receiver, Materialise(newValue, targetType));
                        return;
                    }

                    CannotEmit(member);
                    return;
                }

                case PapyrusIndexExpression index:
                {
                    if (op == null)
                    {
                        // The array and its subscript are both part of the target, so they follow
                        // the value and are themselves read left to right.
                        var (stored, target) = StoreOrder(
                            () => ValueAs(value, targetType),
                            () => (Array: Value(index.Target), Subscript: ValueAs(index.Index, PapyrusType.Int)));
                        Add("array_setelement", target.Array, target.Subscript, stored);
                        return;
                    }

                    var array = Value(index.Target);
                    var subscript = ValueAs(index.Index, PapyrusType.Int);
                    var current = AllocTemp(targetType);
                    Add("array_getelement", Id(current), array, subscript);
                    var newValue = BinaryInto(null, targetType, op.Value, Id(current),
                        ValueAs(value, targetType), assign.Span);
                    Add("array_setelement", array, subscript, newValue);
                    return;
                }

                default:
                    CannotEmit(assign.Target);
                    return;
            }
        }

        /// <summary>
        /// Emits a simple assignment's operands in the order the Creation Kit emits them: the value
        /// first, then the target it is stored through.
        /// </summary>
        /// <remarks>
        /// Reads and writes go opposite ways, and both were measured over the vanilla corpus by
        /// tracing every temporary operand back to the instruction that defined it.
        /// <para>
        /// Everything that reads is left to right, with no exception in more than 3,200 cases: 2,652
        /// for a method call's receiver and arguments, 99 for a static call's arguments, and the rest
        /// spread over every binary operator and <c>array_getelement</c>.
        /// </para>
        /// <para>
        /// Every form that writes is value first, with no exception in 490 cases: 447 <c>propset</c>,
        /// 22 <c>array_setelement</c> and 21 <c>struct_set</c>. That is why this is one helper rather
        /// than three patches. Only simple assignment goes through it: a compound assignment has to
        /// read the target before it can compute the value, so it cannot be value first and is left
        /// alone.
        /// </para>
        /// <para>
        /// What the corpus does <em>not</em> establish is whether this ordering is observable. For
        /// calls it plainly is, because 495 of those 2,652 have a call on both sides, so the order
        /// the game runs them in is fixed and visible. For assignment there is no such case: not one
        /// shipped script assigns a call's result through a receiver that is also a call, so nothing
        /// in the corpus can separate a behavioural rule from a shape convention here. Matching the
        /// Creation Kit is right either way, and with no such case shipped, no vanilla script can
        /// change behaviour from it.
        /// </para>
        /// </remarks>
        private static (PexValue Value, TTarget Target) StoreOrder<TTarget>(
            Func<PexValue> value, Func<TTarget> target)
        {
            var stored = value();
            return (stored, target());
        }

        /// <summary>Copies a value into a temporary, which is what <c>struct_set</c> and
        /// <c>propset</c> are given.</summary>
        /// <remarks>
        /// Every <c>struct_set</c> in the corpus takes a temporary as its value operand, even where
        /// the source assigns a parameter straight across: <c>kVector.fX = afX</c> compiles to
        /// <c>assign ::temp1 afX</c> then <c>struct_set kVector fX ::temp1</c>. Following that costs
        /// one instruction and keeps the emitted shape the one the game has been fed for a decade.
        /// </remarks>
        private PexValue Materialise(PexValue value, PapyrusType type)
        {
            if (value.Type == PexValueType.Identifier && value.Str.StartsWith("::temp", StringComparison.Ordinal))
                return value;
            var temp = AllocTemp(type);
            Add("assign", Id(temp), value);
            return Id(temp);
        }

        private PexValue StructCompound(
            PexValue structValue, string member, PapyrusType type, PapyrusTokenKind op,
            PapyrusExpression value, PapyrusSpan span)
        {
            var current = AllocTemp(type);
            Add("struct_get", Id(current), structValue, Id(member));
            return BinaryInto(null, type, op, Id(current), ValueAs(value, type), span);
        }

        private static PapyrusTokenKind? CompoundOperator(PapyrusTokenKind assignOperator) => assignOperator switch
        {
            PapyrusTokenKind.PlusAssign => PapyrusTokenKind.Plus,
            PapyrusTokenKind.MinusAssign => PapyrusTokenKind.Minus,
            PapyrusTokenKind.StarAssign => PapyrusTokenKind.Star,
            PapyrusTokenKind.SlashAssign => PapyrusTokenKind.Slash,
            PapyrusTokenKind.PercentAssign => PapyrusTokenKind.Percent,
            _ => null,
        };

        /// <summary>An expression evaluated for its effect, with the result thrown away.</summary>
        /// <remarks>
        /// A call that returns nothing writes to <c>::nonevar</c>; one whose result is simply
        /// ignored still needs a real destination of the right type, so it gets a temporary. Both
        /// shapes are in real output side by side in Permadeath:GraveManager's OnInit.
        /// </remarks>
        private void EmitDiscarded(PapyrusExpression expression)
        {
            if (expression is not PapyrusCallExpression call) { Value(expression); return; }

            // A whole-statement call into excluded code is simply not compiled.
            var callBinding = _gen._resolution.BindingFor(call) ?? _gen._resolution.BindingFor(call.Callee);
            if (callBinding != null && _gen.ExcludedBy(callBinding) != null) return;

            var type = _gen.TypeOf(call);
            if (type.Kind == PapyrusTypeKind.None) { EmitCall(call, NoneVar()); return; }
            EmitCall(call, AllocTemp(type));
        }

        // ---- expressions -------------------------------------------------------------------

        /// <summary>Emits <paramref name="expression"/> and answers where its value ended up.</summary>
        private PexValue Value(PapyrusExpression expression)
        {
            var direct = TryDirectValue(expression);
            if (direct != null) return direct;

            var type = _gen.TypeOf(expression);
            if (type.Kind == PapyrusTypeKind.Error) { CannotEmit(expression); return NoneValue(); }

            var temp = AllocTemp(type);
            EmitComputation(expression, temp, type);
            return Id(temp);
        }

        /// <summary>Emits <paramref name="expression"/> converted to <paramref name="want"/>.</summary>
        private PexValue ValueAs(PapyrusExpression expression, PapyrusType want)
        {
            var natural = _gen.TypeOf(expression);
            if (natural.Equals(want) || NeedsNoCast(natural, want)) return Value(expression);

            // An int literal in a float slot is written as a float literal rather than cast at run
            // time: `Utility.Wait(5)` in ObjectReference.MoveToWhenUnloaded compiles to the operand
            // 5.0 and no cast instruction. Only literals fold -- a computed int still casts.
            if (natural.Kind == PapyrusTypeKind.Int && want.Kind == PapyrusTypeKind.Float)
            {
                var folded = TryConstant(expression, want);
                if (folded is { Type: PexValueType.Float }) return folded;
            }

            // The same holds for a bool slot, and measured over the vanilla corpus it is close to
            // absolute: of 28,074 cast instructions, 28,073 take an identifier and exactly one
            // takes a literal. `GetStageDone(100) == 1` compares against the operand true, with no
            // cast. Int and float fold on "not zero"; string is left to cast, because what the
            // runtime makes of a string in a bool slot is not written down anywhere checked.
            if (want.Kind == PapyrusTypeKind.Bool
                && natural.Kind is PapyrusTypeKind.Int or PapyrusTypeKind.Float)
            {
                var folded = TryConstant(expression, want);
                if (folded is { Type: PexValueType.Bool }) return folded;
            }

            var source = Value(expression);
            var temp = AllocTemp(want);
            Add("cast", Id(temp), source);
            return Id(temp);
        }

        /// <summary>
        /// True when a value of <paramref name="from"/> may be used as <paramref name="to"/> with no
        /// <c>cast</c> instruction.
        /// </summary>
        /// <remarks>
        /// Almost nothing qualifies: real output materialises even a widening object conversion.
        /// <c>None</c> is the one exception -- it is a valid value of any reference type, and the
        /// decompiler notes the same thing from the other direction, that casting <c>None</c> is
        /// illegal to write in source.
        /// </remarks>
        private static bool NeedsNoCast(PapyrusType from, PapyrusType to) =>
            from.Kind == PapyrusTypeKind.None && to.IsReference
            || from.Kind == PapyrusTypeKind.Error
            || to.Kind == PapyrusTypeKind.Error;

        /// <summary>Emits <paramref name="expression"/> so its value lands in an existing slot.</summary>
        private void EmitInto(PapyrusExpression expression, string destination, PapyrusType destinationType)
        {
            var natural = _gen.TypeOf(expression);

            if (natural.Equals(destinationType))
            {
                var direct = TryDirectValue(expression);
                if (direct != null) { Add("assign", Id(destination), direct); return; }
                EmitComputation(expression, destination, destinationType);
                return;
            }

            if (NeedsNoCast(natural, destinationType))
            {
                Add("assign", Id(destination), Value(expression));
                return;
            }

            // Same literal fold as ValueAs: `float intensity = 0` is `assign intensity 0.0`, not a
            // cast (ObjectReference.rampRumble).
            if (natural.Kind == PapyrusTypeKind.Int && destinationType.Kind == PapyrusTypeKind.Float)
            {
                var folded = TryConstant(expression, destinationType);
                if (folded is { Type: PexValueType.Float }) { Add("assign", Id(destination), folded); return; }
            }

            // A conversion writes straight into the destination, which is why
            // `GraveData grave = Json.Deserialize(...) as GraveData` is two instructions and not
            // three in real output.
            Add("cast", Id(destination), Value(expression));
        }

        /// <summary>A literal or a name, which need no instruction at all.</summary>
        private PexValue? TryDirectValue(PapyrusExpression expression)
        {
            if (expression is PapyrusLiteralExpression)
            {
                return TryConstant(expression, _gen.TypeOf(expression));
            }

            if (expression is PapyrusIdentifierExpression id)
            {
                var storage = StorageFor(id);
                if (storage == null) return null;
                if (storage.Value.IsProperty) return null;
                return Id(storage.Value.Name);
            }

            return null;
        }

        private readonly record struct Storage(string Name, bool IsProperty, PexValue? Receiver);

        /// <summary>Where a bare name lives: a local, a script variable, or behind a property.</summary>
        private Storage? StorageFor(PapyrusIdentifierExpression id)
        {
            var binding = _gen._resolution.BindingFor(id);
            if (binding == null) { CannotEmit(id); return null; }

            switch (binding.Kind)
            {
                case PapyrusBindingKind.Local:
                case PapyrusBindingKind.Parameter:
                {
                    var local = FindLocal(id.Name);
                    if (local == null) { CannotEmit(id); return null; }
                    return new Storage(local.Value.Name, false, null);
                }

                case PapyrusBindingKind.ScriptVariable:
                    return new Storage(binding.Name, false, null);

                case PapyrusBindingKind.SelfKeyword:
                    return new Storage("self", false, null);

                case PapyrusBindingKind.Property:
                {
                    // An auto property declared on this very script is its backing variable; one
                    // inherited or reached through another object goes through propget/propset,
                    // because the backing variable is private to the script that declares it.
                    if (binding.Owner == _gen._script
                        && _gen._ownAutoProperties.ContainsKey(binding.Name))
                    {
                        return new Storage(BackingVarName(binding.Name), false, null);
                    }
                    return new Storage(binding.Name, true, Id("self"));
                }

                default:
                    CannotEmit(id);
                    return null;
            }
        }

        private PexValue PropertyGet(Storage storage, PapyrusNode at)
        {
            var temp = AllocTemp(_gen.TypeOf(at));
            Add("propget", Id(storage.Name), storage.Receiver!, Id(temp));
            return Id(temp);
        }

        /// <summary>Emits an expression that needs instructions, writing its result to a named slot.</summary>
        private void EmitComputation(PapyrusExpression expression, string destination, PapyrusType destinationType)
        {
            switch (expression)
            {
                case PapyrusLiteralExpression:
                case PapyrusIdentifierExpression:
                {
                    var direct = TryDirectValue(expression);
                    if (direct != null) { Add("assign", Id(destination), direct); return; }
                    if (expression is PapyrusIdentifierExpression id)
                    {
                        var storage = StorageFor(id);
                        if (storage is { IsProperty: true })
                        {
                            Add("propget", Id(storage.Value.Name), storage.Value.Receiver!, Id(destination));
                            return;
                        }
                    }
                    CannotEmit(expression);
                    return;
                }

                case PapyrusMemberExpression member:
                    EmitMemberRead(member, destination);
                    return;

                case PapyrusIndexExpression index:
                    Add("array_getelement", Id(destination), Value(index.Target),
                        ValueAs(index.Index, PapyrusType.Int));
                    return;

                case PapyrusCallExpression call:
                    EmitCall(call, destination);
                    return;

                case PapyrusUnaryExpression unary:
                    EmitUnary(unary, destination, destinationType);
                    return;

                case PapyrusBinaryExpression binary:
                    EmitBinary(binary, destination, destinationType);
                    return;

                case PapyrusCastExpression cast:
                {
                    var natural = _gen.TypeOf(cast.Operand);
                    var target = _gen.TypeOf(cast.Type);
                    if (natural.Equals(target) || NeedsNoCast(natural, target))
                    {
                        Add("assign", Id(destination), Value(cast.Operand));
                        return;
                    }
                    Add("cast", Id(destination), Value(cast.Operand));
                    return;
                }

                case PapyrusTypeCheckExpression check:
                    Add("is", Id(destination), Value(check.Operand),
                        Id(_gen.PexTypeName(_gen.TypeOf(check.Type), check.Type)));
                    return;

                case PapyrusNewArrayExpression newArray:
                    Add("array_create", Id(destination), ValueAs(newArray.Size, PapyrusType.Int));
                    return;

                case PapyrusNewStructExpression:
                    Add("struct_create", Id(destination));
                    return;

                default:
                    CannotEmit(expression);
                    return;
            }
        }

        private void EmitMemberRead(PapyrusMemberExpression member, string destination)
        {
            var binding = _gen._resolution.BindingFor(member);
            if (binding == null) { CannotEmit(member); return; }

            switch (binding.Kind)
            {
                case PapyrusBindingKind.ArrayMember
                    when member.Name.Equals("length", StringComparison.OrdinalIgnoreCase):
                    Add("array_length", Id(destination), Value(member.Target));
                    return;

                case PapyrusBindingKind.StructMember:
                    Add("struct_get", Id(destination), Value(member.Target), Id(member.Name));
                    return;

                case PapyrusBindingKind.Property:
                {
                    if (IsOwnBackingProperty(member, binding))
                    {
                        Add("assign", Id(destination), Id(BackingVarName(binding.Name)));
                        return;
                    }
                    Add("propget", Id(binding.Name), Value(member.Target), Id(destination));
                    return;
                }

                default:
                    CannotEmit(member);
                    return;
            }
        }

        /// <summary>True for <c>Self.SomeAutoProperty</c> on this script, which is the backing variable.</summary>
        private bool IsOwnBackingProperty(PapyrusMemberExpression member, PapyrusBinding binding)
        {
            if (binding.Owner != _gen._script || !_gen._ownAutoProperties.ContainsKey(binding.Name)) return false;
            var targetBinding = _gen._resolution.BindingFor(member.Target);
            return targetBinding?.Kind == PapyrusBindingKind.SelfKeyword;
        }

        private void EmitUnary(PapyrusUnaryExpression unary, string destination, PapyrusType destinationType)
        {
            if (unary.Operator == PapyrusTokenKind.Not)
            {
                // `not` accepts any type and yields a bool; real output does not cast its operand.
                Add("not", Id(destination), Value(unary.Operand));
                return;
            }

            var operandType = _gen.TypeOf(unary.Operand);
            var mnemonic = operandType.Kind switch
            {
                PapyrusTypeKind.Float => "fneg",
                PapyrusTypeKind.Int => "ineg",
                _ => null,
            };
            if (mnemonic == null) { CannotEmit(unary); return; }
            Add(mnemonic, Id(destination), ValueAs(unary.Operand, operandType));
        }

        private void EmitBinary(PapyrusBinaryExpression binary, string destination, PapyrusType destinationType)
        {
            if (binary.Operator is PapyrusTokenKind.And or PapyrusTokenKind.Or)
            {
                EmitShortCircuit(binary, destination);
                return;
            }

            if (IsComparison(binary.Operator))
            {
                EmitComparison(binary, destination);
                return;
            }

            var result = _gen.TypeOf(binary);
            var left = ValueAs(binary.Left, result);
            var right = ValueAs(binary.Right, result);
            BinaryInto(destination, result, binary.Operator, left, right, binary.Span);
        }

        /// <summary>
        /// Arithmetic, into <paramref name="destination"/> or a fresh temporary. Returns where the
        /// result is.
        /// </summary>
        private PexValue BinaryInto(
            string? destination, PapyrusType type, PapyrusTokenKind op,
            PexValue left, PexValue right, PapyrusSpan span)
        {
            var mnemonic = ArithmeticMnemonic(op, type);
            if (mnemonic == null)
            {
                _gen.Report(
                    PapyrusDiagnosticCodes.CannotEmit,
                    $"There is no instruction for this operator on '{type}'.",
                    span);
                return NoneValue();
            }

            var slot = destination ?? AllocTemp(type);
            Add(mnemonic, Id(slot), left, right);
            return Id(slot);
        }

        private static string? ArithmeticMnemonic(PapyrusTokenKind op, PapyrusType type)
        {
            if (type.Kind == PapyrusTypeKind.String) return op == PapyrusTokenKind.Plus ? "strcat" : null;

            bool isFloat = type.Kind == PapyrusTypeKind.Float;
            if (type.Kind != PapyrusTypeKind.Int && !isFloat) return null;

            return op switch
            {
                PapyrusTokenKind.Plus => isFloat ? "fadd" : "iadd",
                PapyrusTokenKind.Minus => isFloat ? "fsub" : "isub",
                PapyrusTokenKind.Star => isFloat ? "fmul" : "imul",
                PapyrusTokenKind.Slash => isFloat ? "fdiv" : "idiv",
                // The format has no float modulo; the language does not offer one either.
                PapyrusTokenKind.Percent => isFloat ? null : "imod",
                _ => null,
            };
        }

        private static bool IsComparison(PapyrusTokenKind op) => op
            is PapyrusTokenKind.Equal or PapyrusTokenKind.NotEqual
            or PapyrusTokenKind.Less or PapyrusTokenKind.LessEqual
            or PapyrusTokenKind.Greater or PapyrusTokenKind.GreaterEqual;

        private void EmitComparison(PapyrusBinaryExpression binary, string destination)
        {
            var common = CommonComparisonType(
                _gen.TypeOf(binary.Left), _gen.TypeOf(binary.Right));

            var left = ValueAs(binary.Left, common);
            var right = ValueAs(binary.Right, common);

            // There is no cmp_ne; inequality is equality negated in place, which is what real
            // output does (MetroCurrency:Manager line 80).
            var mnemonic = binary.Operator switch
            {
                PapyrusTokenKind.Equal or PapyrusTokenKind.NotEqual => "cmp_eq",
                PapyrusTokenKind.Less => "cmp_lt",
                PapyrusTokenKind.LessEqual => "cmp_lte",
                PapyrusTokenKind.Greater => "cmp_gt",
                _ => "cmp_gte",
            };

            Add(mnemonic, Id(destination), left, right);
            if (binary.Operator == PapyrusTokenKind.NotEqual) Add("not", Id(destination), Id(destination));
        }

        /// <summary>The type both sides of a comparison are brought to before comparing.</summary>
        /// <remarks>
        /// Real output casts the side that differs rather than comparing across types:
        /// <c>getDistance() &lt;= 55</c> casts the int to float, and
        /// <c>akActionRef == game.getPlayer()</c> casts the Actor to ObjectReference. Promoting to
        /// float rather than truncating to int is the direction the implicit cast rule allows, and
        /// getting it backwards would silently change what the comparison means.
        /// </remarks>
        private PapyrusType CommonComparisonType(PapyrusType left, PapyrusType right)
        {
            if (left.Equals(right)) return left;
            if (left.Kind == PapyrusTypeKind.Error) return right;
            if (right.Kind == PapyrusTypeKind.Error) return left;
            if (left.Kind == PapyrusTypeKind.None) return right;
            if (right.Kind == PapyrusTypeKind.None) return left;
            if (left.Kind == PapyrusTypeKind.Float || right.Kind == PapyrusTypeKind.Float) return PapyrusType.Float;
            if (left.Kind == PapyrusTypeKind.String || right.Kind == PapyrusTypeKind.String) return PapyrusType.String;
            if (left.Kind == PapyrusTypeKind.Var || right.Kind == PapyrusTypeKind.Var) return PapyrusType.Var;

            if (left.Kind == PapyrusTypeKind.Object && right.Kind == PapyrusTypeKind.Object)
            {
                return Inherits(right.Name, left.Name) ? left : Inherits(left.Name, right.Name) ? right : left;
            }

            return left;
        }

        private bool Inherits(string child, string ancestor)
        {
            var script = _gen._index.Resolve(child);
            if (script == null) return false;
            return _gen._index.BaseChain(script)
                .Any(s => string.Equals(s.Name, ancestor, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// <c>&amp;&amp;</c> and <c>||</c>, which share one bool slot and skip the right operand.
        /// </summary>
        private void EmitShortCircuit(PapyrusBinaryExpression binary, string destination)
        {
            // Each side is evaluated normally and then cast into the shared slot, even when it is
            // already a bool. Real output does that unconditionally -- MetroCurrency:Manager line 71
            // casts a `not` result that is already Bool -- and writing the operand straight into the
            // slot would be an instruction shorter but a different shape.
            Add("cast", Id(destination), Value(binary.Left));
            int skip = Add(binary.Operator == PapyrusTokenKind.And ? "jmpf" : "jmpt", Id(destination), Int(0));
            Add("cast", Id(destination), Value(binary.Right));
            PatchJump(skip, Here);
        }

        // ---- calls -------------------------------------------------------------------------

        private void EmitCall(PapyrusCallExpression call, string destination)
        {
            var binding = _gen._resolution.BindingFor(call) ?? _gen._resolution.BindingFor(call.Callee);
            if (binding == null) { CannotEmit(call); return; }

            var excluded = _gen.ExcludedBy(binding);
            if (excluded != null)
            {
                _gen.Report(
                    PapyrusDiagnosticCodes.CannotEmit,
                    $"'{binding.Name}' is {excluded} and is not part of this build, but its result is "
                    + "used here. Only a call written as a whole statement can be dropped; there is "
                    + "no value to substitute for this one.",
                    call.Span);
                return;
            }

            if (binding.Kind == PapyrusBindingKind.ArrayMember)
            {
                EmitArrayBuiltin(call, binding, destination);
                return;
            }

            if (binding.Declaration is not PapyrusCallableDecl callee)
            {
                _gen.Report(
                    PapyrusDiagnosticCodes.UnknownCallTarget,
                    $"'{binding.Name}' has no source declaration on the import roots, so its "
                    + "parameter list and defaults are unknown and the call cannot be emitted. Add "
                    + "the root that declares it.",
                    call.Span);
                return;
            }

            switch (call.Callee)
            {
                case PapyrusMemberExpression member:
                {
                    var targetBinding = _gen._resolution.BindingFor(member.Target);

                    if (targetBinding?.Kind == PapyrusBindingKind.ParentKeyword)
                    {
                        var parentArgs = BuildArguments(call, callee, binding.Owner);
                        if (parentArgs == null) return;
                        Emit("callparent", Id(binding.Name), Id(destination), parentArgs);
                        return;
                    }

                    if (targetBinding?.Kind == PapyrusBindingKind.Script)
                    {
                        var staticArgs = BuildArguments(call, callee, binding.Owner);
                        if (staticArgs == null) return;
                        Emit("callstatic", Id(targetBinding.Owner?.Name ?? targetBinding.Name),
                            Id(binding.Name), Id(destination), staticArgs);
                        return;
                    }

                    // The receiver is evaluated before the arguments.
                    // `GetPlayer().RemoveItem(GetCaps(), n)` calls GetPlayer first; building the
                    // argument list first reverses two calls whose order the author can observe.
                    var receiver = Value(member.Target);
                    var methodArgs = BuildArguments(call, callee, binding.Owner);
                    if (methodArgs == null) return;
                    Emit("callmethod", Id(binding.Name), receiver, Id(destination), methodArgs);
                    return;
                }

                default:
                {
                    var arguments = BuildArguments(call, callee, binding.Owner);
                    if (arguments == null) return;
                    // A bare name. A global is dispatched statically against the script that
                    // declares it -- including this one, which is how Game.psc's globals call each
                    // other -- and anything else is a method on Self.
                    if (callee is PapyrusFunctionDecl { IsGlobal: true })
                    {
                        Emit("callstatic", Id(binding.Owner?.Name ?? _gen._script.Name),
                            Id(binding.Name), Id(destination), arguments);
                        return;
                    }
                    Emit("callmethod", Id(binding.Name), Id("self"), Id(destination), arguments);
                    return;
                }
            }
        }

        private void Emit(string mnemonic, params object[] parts)
        {
            var args = new List<PexValue>();
            foreach (var part in parts)
            {
                if (part is PexValue value) args.Add(value);
                else if (part is List<PexValue> list) args.AddRange(list);
            }
            Add(mnemonic, args.ToArray());
        }

        /// <summary>
        /// The full operand list for a call: named arguments placed, optional ones filled in.
        /// </summary>
        /// <remarks>
        /// The format carries no notion of a default, so the call site materialises every parameter.
        /// <c>akPlayer.PlaceAtMe(GraveMarker)</c> emits five operands because
        /// <c>PlaceAtMe</c> declares five. Null means something was wrong and has been reported.
        /// </remarks>
        private List<PexValue>? BuildArguments(
            PapyrusCallExpression call, PapyrusCallableDecl callee, PapyrusScript? calleeOwner)
        {
            var slots = new PapyrusExpression?[callee.Parameters.Count];
            int positional = 0;
            bool named = false;

            foreach (var argument in call.Arguments)
            {
                if (argument.Name == null)
                {
                    if (named)
                    {
                        _gen.Report(
                            PapyrusDiagnosticCodes.ParameterOrder,
                            "A positional argument cannot follow a named one.",
                            argument.Span);
                        return null;
                    }
                    if (positional >= slots.Length)
                    {
                        _gen.Report(
                            PapyrusDiagnosticCodes.ArgumentCount,
                            $"'{callee.Name}' takes {slots.Length} argument(s).",
                            argument.Span);
                        return null;
                    }
                    slots[positional++] = argument.Value;
                    continue;
                }

                named = true;
                int at = callee.Parameters.FindIndex(
                    p => string.Equals(p.Name, argument.Name, StringComparison.OrdinalIgnoreCase));
                if (at < 0)
                {
                    _gen.Report(
                        PapyrusDiagnosticCodes.UnknownArgumentName,
                        $"'{callee.Name}' has no parameter named '{argument.Name}'.",
                        argument.NameSpan);
                    return null;
                }
                slots[at] = argument.Value;
            }

            var operands = new List<PexValue>(slots.Length);
            for (int i = 0; i < slots.Length; i++)
            {
                var parameter = callee.Parameters[i];
                var parameterType = _gen.ForeignType(parameter.Type, calleeOwner);

                if (slots[i] != null)
                {
                    // A custom event name is not the string the author wrote: it is qualified with
                    // the script that declares the event. See PapyrusCodeGenerator.QualifyCustomEvent.
                    if (parameter.Semantic == ParameterSemantic.CustomEventName
                        && _gen.QualifyCustomEvent(slots[i]!, slots, i, ReceiverScript()) is { } qualified)
                    {
                        operands.Add(qualified);
                        continue;
                    }

                    operands.Add(ValueAs(slots[i]!, parameterType));
                    continue;
                }

                if (parameter.DefaultValue == null)
                {
                    _gen.Report(
                        PapyrusDiagnosticCodes.ArgumentCount,
                        $"'{callee.Name}' has no default for '{parameter.Name}', so it must be given.",
                        call.Span);
                    return null;
                }

                var constant = TryConstant(parameter.DefaultValue, parameterType);
                if (constant == null)
                {
                    _gen.Report(
                        PapyrusDiagnosticCodes.NonConstantInitializer,
                        $"The default for '{parameter.Name}' is not a literal, so it cannot be "
                        + "materialised at the call site.",
                        call.Span);
                    return null;
                }
                operands.Add(constant);
            }

            return operands;

            // The script a bare custom event call is talking about. SendCustomEvent takes no target
            // parameter because the target is the caller, so an unqualified call means this script.
            string? ReceiverScript()
            {
                if (call.Callee is PapyrusMemberExpression member)
                {
                    var receiver = _gen.TypeOf(member.Target);
                    if (receiver.Kind == PapyrusTypeKind.Object) return receiver.Name;
                }
                return _gen._script?.Name;
            }
        }

        /// <summary>
        /// The members every array has, which have opcodes instead of declarations.
        /// </summary>
        /// <remarks>
        /// These are the one call shape with no <c>.psc</c> anywhere to read a signature from --
        /// the same gap that leaves their arguments unchecked by the type checker. The defaults are
        /// from the Creation Kit's per-function pages. <c>GetMatchingStructs</c> is documented on
        /// the Arrays page but has no opcode in the Fallout 4 instruction set, so it is refused
        /// rather than approximated.
        /// </remarks>
        /// <summary>
        /// How many arguments each array built-in takes, from the Creation Kit's per-function pages.
        /// </summary>
        /// <remarks>
        /// These have to be stated here because there is nowhere else to state them: array built-ins
        /// are declared in no <c>.psc</c>, which is exactly why the type checker leaves their
        /// arguments unchecked. That gap means bad-but-parseable source -- <c>xs.Add()</c> -- reaches
        /// the back end, and reading operand zero of an empty list is not a refusal, it is a crash.
        /// </remarks>
        private static readonly Dictionary<string, (int Min, int Max)> ArrayBuiltinArity =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["find"] = (1, 2),
                ["rfind"] = (1, 2),
                ["findstruct"] = (2, 3),
                ["rfindstruct"] = (2, 3),
                ["add"] = (1, 2),
                ["insert"] = (2, 2),
                ["remove"] = (1, 2),
                ["removelast"] = (0, 0),
                ["clear"] = (0, 0),
            };

        private void EmitArrayBuiltin(PapyrusCallExpression call, PapyrusBinding binding, string destination)
        {
            if (call.Callee is not PapyrusMemberExpression member) { CannotEmit(call); return; }

            var arguments = call.Arguments.Select(a => a.Value).ToList();
            if (!ArrayBuiltinArity.TryGetValue(binding.Name, out var arity))
            {
                _gen.Report(
                    PapyrusDiagnosticCodes.CannotEmit,
                    $"The array built-in '{binding.Name}' has no Fallout 4 opcode, so there is no "
                    + "instruction to emit for it.",
                    call.Span);
                return;
            }

            if (arguments.Count < arity.Min || arguments.Count > arity.Max)
            {
                var wanted = arity.Min == arity.Max
                    ? $"{arity.Min}"
                    : $"{arity.Min} to {arity.Max}";
                _gen.Report(
                    PapyrusDiagnosticCodes.ArgumentCount,
                    $"'{binding.Name}' on an array takes {wanted} argument(s), not {arguments.Count}. "
                    + "Array built-ins are declared in no script, so this is not caught earlier.",
                    call.Span);
                return;
            }

            var arrayType = _gen.TypeOf(member.Target);
            var element = arrayType.ElementType ?? PapyrusType.Error;
            var array = Value(member.Target);

            PexValue Argument(int at, PapyrusType type, int fallback) =>
                at < arguments.Count ? ValueAs(arguments[at], type) : Int(fallback);

            switch (binding.Name.ToLowerInvariant())
            {
                case "find":
                    Add("array_findelement", array, Id(destination),
                        ValueAs(arguments[0], element), Argument(1, PapyrusType.Int, 0));
                    return;

                case "rfind":
                    Add("array_rfindelement", array, Id(destination),
                        ValueAs(arguments[0], element), Argument(1, PapyrusType.Int, -1));
                    return;

                case "findstruct":
                    Add("array_findstruct", array, Id(destination), Value(arguments[0]),
                        Value(arguments[1]), Argument(2, PapyrusType.Int, 0));
                    return;

                case "rfindstruct":
                    Add("array_rfindstruct", array, Id(destination), Value(arguments[0]),
                        Value(arguments[1]), Argument(2, PapyrusType.Int, -1));
                    return;

                case "add":
                    Add("array_add", array, ValueAs(arguments[0], element), Argument(1, PapyrusType.Int, 1));
                    return;

                case "insert":
                    Add("array_insert", array, ValueAs(arguments[0], element), ValueAs(arguments[1], PapyrusType.Int));
                    return;

                case "remove":
                    Add("array_remove", array, ValueAs(arguments[0], PapyrusType.Int),
                        Argument(1, PapyrusType.Int, 1));
                    return;

                case "removelast":
                    Add("array_removelast", array);
                    return;

                case "clear":
                    Add("array_clear", array);
                    return;

                default:
                    // Unreachable: the arity table above is the list of built-ins with opcodes, and
                    // anything absent from it was already refused.
                    CannotEmit(call);
                    return;
            }
        }

        private void CannotEmit(PapyrusNode node) =>
            _gen.Report(
                PapyrusDiagnosticCodes.CannotEmit,
                "This did not resolve to anything the back end can name, so no instruction was "
                + "emitted for it. Nothing is guessed here; the file is refused instead.",
                node.Span);
    }
}
