using System.Collections.Generic;

namespace FO4RecordEditor.Services.Papyrus;

public enum PapyrusBindingKind
{

    Local,
    Parameter,


    ScriptVariable,
    Property,
    Function,
    Event,
    CustomEvent,


    Struct,


    StructMember,


    Script,


    ArrayMember,


    SelfKeyword,


    ParentKeyword,
}


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





    public PapyrusType Type { get; }


    public PapyrusDeclaration? Declaration { get; }


    public PapyrusScript? Owner { get; }

    public override string ToString() => $"{Kind} {Name} : {Type}";
}











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












    public bool BaseChainComplete { get; }

    public PapyrusBinding? BindingFor(PapyrusNode node) =>
        node != null && _bindings.TryGetValue(node, out var b) ? b : null;

    public PapyrusType TypeOf(PapyrusNode node) =>
        node != null && _types.TryGetValue(node, out var t) ? t : PapyrusType.Error;


    public IReadOnlyDictionary<PapyrusNode, PapyrusBinding> Bindings => _bindings;
}
