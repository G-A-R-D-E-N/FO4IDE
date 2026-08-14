using System;

namespace FO4RecordEditor.Services.Papyrus;

public enum PapyrusTypeKind
{

    None,
    Bool,
    Int,
    Float,
    String,
    Var,

    Object,

    Struct,

    Array,

    Error,
}

public sealed class PapyrusType : IEquatable<PapyrusType>
{
    public static readonly PapyrusType None = new(PapyrusTypeKind.None, "None");
    public static readonly PapyrusType Bool = new(PapyrusTypeKind.Bool, "bool");
    public static readonly PapyrusType Int = new(PapyrusTypeKind.Int, "int");
    public static readonly PapyrusType Float = new(PapyrusTypeKind.Float, "float");
    public static readonly PapyrusType String = new(PapyrusTypeKind.String, "string");
    public static readonly PapyrusType Var = new(PapyrusTypeKind.Var, "var");
    public static readonly PapyrusType Error = new(PapyrusTypeKind.Error, "<unknown>");

    private PapyrusType(PapyrusTypeKind kind, string name, PapyrusType? element = null)
    {
        Kind = kind;
        Name = name;
        ElementType = element;
    }

    public PapyrusTypeKind Kind { get; }

    public string Name { get; }

    public PapyrusType? ElementType { get; }

    public bool IsArray => Kind == PapyrusTypeKind.Array;

    public bool IsReference =>
        Kind is PapyrusTypeKind.Object or PapyrusTypeKind.Struct or PapyrusTypeKind.Array;

    public static PapyrusType Object(string scriptName) =>
        new(PapyrusTypeKind.Object, scriptName);

    public static PapyrusType StructOf(string owningScript, string structName) =>
        new(PapyrusTypeKind.Struct, owningScript + ":" + structName);

    public static PapyrusType ArrayOf(PapyrusType element) =>
        new(PapyrusTypeKind.Array, element.Name, element);

    public static PapyrusType? Primitive(string name) => name.ToLowerInvariant() switch
    {
        "bool" => Bool,
        "int" => Int,
        "float" => Float,
        "string" => String,
        "var" => Var,
        "none" => None,
        _ => null,
    };

    public override string ToString() => IsArray ? ElementType!.ToString() + "[]" : Name;

    public bool Equals(PapyrusType? other) =>
        other is not null
        && Kind == other.Kind
        && string.Equals(Name, other.Name, StringComparison.OrdinalIgnoreCase)
        && Equals(ElementType, other.ElementType);

    public override bool Equals(object? obj) => Equals(obj as PapyrusType);

    public override int GetHashCode() =>
        HashCode.Combine(Kind, Name.ToLowerInvariant(), ElementType);
}

public static class PapyrusConversions
{

    public delegate bool InheritsFrom(string child, string ancestor);

    private static bool AlwaysFalse(string a, string b) => false;

    public static bool IsImplicit(PapyrusType from, PapyrusType to, InheritsFrom? inherits = null)
    {
        inherits ??= AlwaysFalse;
        if (from.Kind == PapyrusTypeKind.Error || to.Kind == PapyrusTypeKind.Error) return true;
        if (from.Equals(to)) return true;

        switch (to.Kind)
        {

            case PapyrusTypeKind.Bool:
            case PapyrusTypeKind.String:
                return true;

            case PapyrusTypeKind.Float:
                return from.Kind == PapyrusTypeKind.Int;

            case PapyrusTypeKind.Int:
                return false;

            case PapyrusTypeKind.Var:
                return !from.IsArray;

            case PapyrusTypeKind.Object:
                if (from.Kind == PapyrusTypeKind.None) return true;
                return from.Kind == PapyrusTypeKind.Object && inherits(from.Name, to.Name);

            case PapyrusTypeKind.Struct:
            case PapyrusTypeKind.Array:
                return from.Kind == PapyrusTypeKind.None;

            case PapyrusTypeKind.None:
                return from.Kind == PapyrusTypeKind.None;

            default:
                return false;
        }
    }

    public static bool IsExplicit(PapyrusType from, PapyrusType to, InheritsFrom? inherits = null)
    {
        inherits ??= AlwaysFalse;
        if (IsImplicit(from, to, inherits)) return true;
        if (from.Kind == PapyrusTypeKind.Var || to.Kind == PapyrusTypeKind.Var) return !from.IsArray || to.IsArray;

        switch (to.Kind)
        {

            case PapyrusTypeKind.Int:
            case PapyrusTypeKind.Float:
                return from.Kind is PapyrusTypeKind.Int or PapyrusTypeKind.Float
                    or PapyrusTypeKind.String or PapyrusTypeKind.Bool;

            case PapyrusTypeKind.Object:
                return from.Kind == PapyrusTypeKind.Object && inherits(to.Name, from.Name);

            case PapyrusTypeKind.Array:
                return from.IsArray && IsExplicit(from.ElementType!, to.ElementType!, inherits);

            case PapyrusTypeKind.Struct:
                return false;

            default:
                return false;
        }
    }
}
