using System;

namespace FO4RecordEditor.Services.Papyrus;

public enum PapyrusTypeKind
{
    /// <summary>The type of a function that returns nothing, and of the <c>None</c> literal.</summary>
    None,
    Bool,
    Int,
    Float,
    String,
    Var,

    /// <summary>A script type. <see cref="PapyrusType.Name"/> is the script name.</summary>
    Object,

    /// <summary>A struct. <see cref="PapyrusType.Name"/> is the struct name, qualified by its owner.</summary>
    Struct,

    /// <summary>An array. <see cref="PapyrusType.ElementType"/> is what it holds.</summary>
    Array,

    /// <summary>Unknown, because something upstream failed. Never reported against.</summary>
    Error,
}

/// <summary>
/// A resolved Papyrus type, as opposed to <see cref="PapyrusTypeRef"/>, which is one as written.
/// </summary>
/// <remarks>
/// Arrays hold one level only -- Papyrus has no array of arrays -- which is why the parser's type
/// reference carries a bool rather than a nesting depth.
/// <para>
/// Object identity is the script name, compared case-insensitively like everything else in the
/// language. Assignability between two object types needs the inheritance chain, which this type does
/// not know; see <see cref="PapyrusConversions"/>, which takes it as a callback.
/// </para>
/// </remarks>
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

    /// <summary>The type's name as it would be written, without any <c>[]</c>.</summary>
    public string Name { get; }

    /// <summary>Element type for <see cref="PapyrusTypeKind.Array"/>, else null.</summary>
    public PapyrusType? ElementType { get; }

    public bool IsArray => Kind == PapyrusTypeKind.Array;

    /// <summary>Object, struct and array types hold a reference; the rest hold a value.</summary>
    public bool IsReference =>
        Kind is PapyrusTypeKind.Object or PapyrusTypeKind.Struct or PapyrusTypeKind.Array;

    public static PapyrusType Object(string scriptName) =>
        new(PapyrusTypeKind.Object, scriptName);

    /// <summary>A struct, named <c>Owner:Struct</c> so two scripts' same-named structs stay distinct.</summary>
    public static PapyrusType StructOf(string owningScript, string structName) =>
        new(PapyrusTypeKind.Struct, owningScript + ":" + structName);

    public static PapyrusType ArrayOf(PapyrusType element) =>
        new(PapyrusTypeKind.Array, element.Name, element);

    /// <summary>The named primitive, or null when the name is not one.</summary>
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

/// <summary>
/// What may be converted to what, per the Creation Kit's Cast Reference.
/// </summary>
/// <remarks>
/// The rules are transcribed from that page rather than recalled, because its "Compiler auto-cast
/// from" line per target type is the whole of the implicit rule and it is not symmetric or
/// guessable. In its own words:
/// <list type="bullet">
/// <item><c>bool</c> auto-casts from <b>anything</b>.</item>
/// <item><c>string</c> auto-casts from <b>anything</b>.</item>
/// <item><c>float</c> auto-casts from <b>int</b>, and only int.</item>
/// <item><c>int</c> auto-casts from <b>nothing</b>. Float to int needs an explicit cast; it truncates.</item>
/// <item>An object auto-casts from a <b>child object</b> only. Parent to child needs <c>as</c> and may
/// yield None at runtime.</item>
/// <item><c>var</c> auto-casts from <b>everything but arrays</b>.</item>
/// <item>Arrays and structs auto-cast from <b>nothing</b>. Arrays cast to other arrays explicitly
/// when their elements would cast; nothing at all casts to a struct.</item>
/// </list>
/// </remarks>
public static class PapyrusConversions
{
    /// <summary>Answers whether <c>child</c> is <c>ancestor</c> or descends from it.</summary>
    public delegate bool InheritsFrom(string child, string ancestor);

    private static bool AlwaysFalse(string a, string b) => false;

    /// <summary>Whether <paramref name="from"/> may be used where <paramref name="to"/> is wanted, with no <c>as</c>.</summary>
    public static bool IsImplicit(PapyrusType from, PapyrusType to, InheritsFrom? inherits = null)
    {
        inherits ??= AlwaysFalse;
        if (from.Kind == PapyrusTypeKind.Error || to.Kind == PapyrusTypeKind.Error) return true;
        if (from.Equals(to)) return true;

        switch (to.Kind)
        {
            // "Compiler auto-cast from: Anything."
            case PapyrusTypeKind.Bool:
            case PapyrusTypeKind.String:
                return true;

            // "Compiler auto-cast from: Int."
            case PapyrusTypeKind.Float:
                return from.Kind == PapyrusTypeKind.Int;

            // "Compiler auto-cast from: Nothing."
            case PapyrusTypeKind.Int:
                return false;

            // "Compiler auto-cast from: Everything but arrays."
            case PapyrusTypeKind.Var:
                return !from.IsArray;

            // "Compiler auto-cast from: Child object." None is assignable to any object.
            case PapyrusTypeKind.Object:
                if (from.Kind == PapyrusTypeKind.None) return true;
                return from.Kind == PapyrusTypeKind.Object && inherits(from.Name, to.Name);

            // "Compiler auto-cast from: Nothing" for both, but None still initialises a reference.
            case PapyrusTypeKind.Struct:
            case PapyrusTypeKind.Array:
                return from.Kind == PapyrusTypeKind.None;

            case PapyrusTypeKind.None:
                return from.Kind == PapyrusTypeKind.None;

            default:
                return false;
        }
    }

    /// <summary>Whether <c>from as to</c> is a cast the compiler accepts.</summary>
    public static bool IsExplicit(PapyrusType from, PapyrusType to, InheritsFrom? inherits = null)
    {
        inherits ??= AlwaysFalse;
        if (IsImplicit(from, to, inherits)) return true;
        if (from.Kind == PapyrusTypeKind.Var || to.Kind == PapyrusTypeKind.Var) return !from.IsArray || to.IsArray;

        switch (to.Kind)
        {
            // "Floats, strings, and vars can be cast to integers", and likewise to floats.
            //
            // Bool is missing from that sentence and belongs in it. `false as int` and
            // `bStartAtTop as float` are in eleven vanilla scripts that the Creation Kit compiled --
            // LightbulbOnOffScript, SimpleElevatorMasterScript and MQ206Script among them -- so the
            // published list is incomplete rather than the code being wrong. This is the same shape
            // as the two grammar gaps the parser found (a local carrying `const`, a float literal
            // carrying `f`): the reference under-describes what the compiler accepts.
            case PapyrusTypeKind.Int:
            case PapyrusTypeKind.Float:
                return from.Kind is PapyrusTypeKind.Int or PapyrusTypeKind.Float
                    or PapyrusTypeKind.String or PapyrusTypeKind.Bool;

            // Downcast: legal to write, may be None at runtime.
            case PapyrusTypeKind.Object:
                return from.Kind == PapyrusTypeKind.Object && inherits(to.Name, from.Name);

            // "Arrays can cast to other arrays ... only if their elements could be cast."
            case PapyrusTypeKind.Array:
                return from.IsArray && IsExplicit(from.ElementType!, to.ElementType!, inherits);

            // "Nothing can be cast to a struct."
            case PapyrusTypeKind.Struct:
                return false;

            default:
                return false;
        }
    }
}
