using System.Collections.Generic;

namespace FO4RecordEditor.Services.Papyrus;

public enum PapyrusBindingKind
{
    /// <summary>A variable defined inside a function body.</summary>
    Local,
    Parameter,

    /// <summary>A variable declared at script scope, on this script or an ancestor.</summary>
    ScriptVariable,
    Property,
    Function,
    Event,
    CustomEvent,

    /// <summary>A struct type name.</summary>
    Struct,

    /// <summary>A member of a struct value.</summary>
    StructMember,

    /// <summary>A script name used as a type or as the receiver of a global call.</summary>
    Script,

    /// <summary>One of the array built-ins: <c>Length</c>, <c>Find</c>, <c>Add</c> and the rest.</summary>
    ArrayMember,

    /// <summary>The <c>Self</c> pseudo-variable.</summary>
    SelfKeyword,

    /// <summary>The <c>Parent</c> pseudo-variable.</summary>
    ParentKeyword,
}

/// <summary>What a name in the source turned out to refer to.</summary>
public sealed class PapyrusBinding
{
    public PapyrusBinding(
        PapyrusBindingKind kind,
        string name,
        PapyrusType type,
        PapyrusDeclaration? declaration = null,
        PapyrusScript? owner = null)
    {
        Kind = kind;
        Name = name;
        Type = type;
        Declaration = declaration;
        Owner = owner;
    }

    public PapyrusBindingKind Kind { get; }

    public string Name { get; }

    /// <summary>
    /// The type of the value this name denotes. For a function or event it is the return type, so a
    /// call expression can take its type straight from the callee's binding.
    /// </summary>
    public PapyrusType Type { get; }

    /// <summary>The declaration, when there is a source one. Null for built-ins like array members.</summary>
    public PapyrusDeclaration? Declaration { get; }

    /// <summary>The script the declaration was found on, which is not always the script being resolved.</summary>
    public PapyrusScript? Owner { get; }

    public override string ToString() => $"{Kind} {Name} : {Type}";
}

/// <summary>
/// The result of resolving one script: what every name in it refers to, and what every expression's
/// type is.
/// </summary>
/// <remarks>
/// Both maps are keyed by node identity, so a consumer holding an AST node can ask about it directly.
/// A node that could not be resolved is simply absent rather than mapped to a null binding, and
/// <see cref="TypeOf"/> answers <see cref="PapyrusType.Error"/> for anything it does not know --
/// which downstream code must treat as "do not report", not as a distinct type.
/// </remarks>
public sealed class PapyrusResolution
{
    internal PapyrusResolution(
        PapyrusScript script,
        Dictionary<PapyrusNode, PapyrusBinding> bindings,
        Dictionary<PapyrusNode, PapyrusType> types,
        IReadOnlyList<PapyrusDiagnostic> diagnostics,
        bool baseChainComplete)
    {
        Script = script;
        _bindings = bindings;
        _types = types;
        Diagnostics = diagnostics;
        BaseChainComplete = baseChainComplete;
    }

    private readonly Dictionary<PapyrusNode, PapyrusBinding> _bindings;
    private readonly Dictionary<PapyrusNode, PapyrusType> _types;

    public PapyrusScript Script { get; }

    public IReadOnlyList<PapyrusDiagnostic> Diagnostics { get; }

    /// <summary>
    /// False when a script named by <c>Extends</c>, or a type this script mentions, was not on the
    /// index's roots.
    /// </summary>
    /// <remarks>
    /// This is the difference between "this name does not exist" and "this name might exist in a file
    /// I was not given". Resolving a script whose base class is missing would otherwise report every
    /// inherited member as undefined, so name-not-found diagnostics are suppressed when this is
    /// false. A caller wanting to know whether the roots were adequate should read this rather than
    /// infer it from an empty diagnostic list.
    /// </remarks>
    public bool BaseChainComplete { get; }

    public PapyrusBinding? BindingFor(PapyrusNode node) =>
        node != null && _bindings.TryGetValue(node, out var b) ? b : null;

    public PapyrusType TypeOf(PapyrusNode node) =>
        node != null && _types.TryGetValue(node, out var t) ? t : PapyrusType.Error;

    /// <summary>Every name that was bound, for tests and for whole-file consumers.</summary>
    public IReadOnlyDictionary<PapyrusNode, PapyrusBinding> Bindings => _bindings;
}
