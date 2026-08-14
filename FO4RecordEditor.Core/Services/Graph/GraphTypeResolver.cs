using System;
using System.Collections.Generic;
using System.Linq;
using FO4RecordEditor.Services.Papyrus;

namespace FO4RecordEditor.Services.Graph;

/// <summary>The type variables solved for one node.</summary>
public sealed class GenericBinding
{
    private readonly Dictionary<string, (string TypeName, bool IsArray)> _bound = new(StringComparer.Ordinal);

    public bool TryGet(string variable, out string typeName, out bool isArray)
    {
        if (_bound.TryGetValue(variable, out var found))
        {
            typeName = found.TypeName;
            isArray = found.IsArray;
            return true;
        }
        typeName = "";
        isArray = false;
        return false;
    }

    public void Bind(string variable, string typeName, bool isArray)
    {
        // First concrete binding wins. One pass is enough because every generic in Papyrus is
        // reachable from a single array pin, so there is nothing to unify against.
        if (!_bound.ContainsKey(variable)) _bound[variable] = (typeName, isArray);
    }

    public bool IsBound(string variable) => _bound.ContainsKey(variable);
}

/// <summary>
/// What a pin's type is, and whether a wire between two of them is legal.
/// </summary>
/// <remarks>
/// The conversion rules are not reimplemented here. <c>PapyrusConversions</c> is the one cast table
/// in the codebase and the resolver and type checker already share it, so the graph shares it too;
/// a second copy would drift and then disagree with the compiler the graph feeds.
/// </remarks>
public sealed class GraphTypeResolver
{
    private readonly PapyrusScriptIndex _index;
    private readonly string _selfType;
    private readonly string? _selfExtends;
    private readonly Dictionary<(string, string), bool> _inherits = new();

    /// <param name="selfExtends">
    /// What the graph's own script extends. Needed because that script does not exist on the import
    /// roots yet, so its chain cannot be looked up the way every other script's can.
    /// </param>
    public GraphTypeResolver(PapyrusScriptIndex index, string selfType, string? selfExtends = null)
    {
        _index = index ?? throw new ArgumentNullException(nameof(index));
        _selfType = selfType ?? "";
        _selfExtends = selfExtends;
    }

    /// <summary>False once any type name failed to resolve, mirroring the compiler's own flag.</summary>
    public bool SourcesComplete { get; private set; } = true;

    /// <summary>
    /// Whether one script type descends from another.
    /// </summary>
    /// <remarks>
    /// Memoized because it is asked once per data wire and the underlying chain walk re-resolves the
    /// script each time.
    /// </remarks>
    public bool InheritsFrom(string child, string ancestor)
    {
        if (string.Equals(child, ancestor, StringComparison.OrdinalIgnoreCase)) return true;

        // The graph's own script is not on the roots, so its chain starts from what it extends.
        // Without this, every call inherited from the parent would look like a call on a foreign
        // type and demand a receiver.
        if (string.Equals(child, _selfType, StringComparison.OrdinalIgnoreCase))
        {
            return !string.IsNullOrEmpty(_selfExtends)
                   && InheritsFrom(_selfExtends, ancestor);
        }

        var key = (child.ToLowerInvariant(), ancestor.ToLowerInvariant());
        if (_inherits.TryGetValue(key, out var known)) return known;

        var script = _index.Resolve(child);
        bool result = false;
        if (script == null)
        {
            SourcesComplete = false;
        }
        else
        {
            result = _index.BaseChain(script)
                .Any(s => string.Equals(s.Name, ancestor, StringComparison.OrdinalIgnoreCase));
        }

        _inherits[key] = result;
        return result;
    }

    /// <summary>The concrete type of a pin, once generics are solved.</summary>
    public (string TypeName, bool IsArray) TypeOf(PinTypeExpr? type, GenericBinding generics)
    {
        if (type == null) return ("", false);

        switch (type.Form)
        {
            case PinTypeForm.Concrete:
                return (type.TypeName, type.IsArray);

            case PinTypeForm.SelfType:
                return (_selfType, false);

            case PinTypeForm.Any:
                return ("var", false);

            case PinTypeForm.Generic:
                return generics.TryGet(type.Variable, out var name, out var array)
                    ? (name, array)
                    : ("", false);

            case PinTypeForm.ArrayOfGeneric:
                return generics.TryGet(type.Variable, out var element, out _)
                    ? (element, true)
                    : ("", false);

            case PinTypeForm.ElementOfGeneric:
                return generics.TryGet(type.Variable, out var whole, out _)
                    ? (whole, false)
                    : ("", false);

            default:
                return ("", false);
        }
    }

    /// <summary>Turns a written type into the resolved form the conversion table wants.</summary>
    public PapyrusType? Resolve(string typeName, bool isArray, PapyrusScript? relativeTo)
    {
        if (string.IsNullOrEmpty(typeName)) return null;

        var element = typeName.ToLowerInvariant() switch
        {
            "bool" => PapyrusType.Bool,
            "int" => PapyrusType.Int,
            "float" => PapyrusType.Float,
            "string" => PapyrusType.String,
            "var" => PapyrusType.Var,
            "none" => PapyrusType.None,
            _ => relativeTo == null ? null : PapyrusResolver.ResolveTypeName(_index, typeName, relativeTo),
        };

        if (element == null)
        {
            SourcesComplete = false;
            return null;
        }

        return isArray ? PapyrusType.ArrayOf(element) : element;
    }

    /// <summary>How a wire between two typed pins is judged.</summary>
    public enum WireVerdict
    {
        /// <summary>Legal with no conversion, or with one the compiler inserts itself.</summary>
        Implicit,

        /// <summary>Legal only through an explicit cast, which the graph requires a node for.</summary>
        NeedsCast,

        /// <summary>Not legal at all.</summary>
        Incompatible,

        /// <summary>One side did not resolve, so no judgement is possible.</summary>
        Unknown,
    }

    /// <summary>
    /// Judges a data wire.
    /// </summary>
    /// <remarks>
    /// An explicit conversion is refused rather than inserted silently. A downcast can yield None at
    /// run time and a float to int conversion loses information, so the author owns that decision by
    /// placing a Cast node. Inserting one for them would hide a real risk inside generated source
    /// they never see.
    /// </remarks>
    public WireVerdict Judge(
        string fromType, bool fromArray, string toType, bool toArray, PapyrusScript? relativeTo)
    {
        // A var pin accepts and produces anything the compiler will accept, so leave the judgement
        // to the conversion table rather than short circuiting on the name.
        var from = Resolve(fromType, fromArray, relativeTo);
        var to = Resolve(toType, toArray, relativeTo);
        if (from == null || to == null) return WireVerdict.Unknown;

        PapyrusConversions.InheritsFrom inherits = InheritsFrom;

        if (PapyrusConversions.IsImplicit(from, to, inherits)) return WireVerdict.Implicit;
        if (PapyrusConversions.IsExplicit(from, to, inherits)) return WireVerdict.NeedsCast;
        return WireVerdict.Incompatible;
    }

    /// <summary>
    /// Solves a node's type variables from what its pins are wired to.
    /// </summary>
    /// <remarks>
    /// One pass over the connected pins in a fixed order, first concrete binding wins. Deterministic,
    /// so the same graph always produces the same types and therefore the same source.
    /// </remarks>
    public GenericBinding SolveGenerics(
        GraphNode node,
        NodeDefinition definition,
        GraphDocument document,
        Func<PinRef, (string TypeName, bool IsArray)?> typeOfSource)
    {
        var generics = new GenericBinding();

        foreach (var pin in definition.PinsFor(node, document)
                     .Where(p => p.Kind == PinKind.Data && p.Type != null)
                     .OrderBy(p => p.Direction == PinDirection.In ? 0 : 1)
                     .ThenBy(p => p.Id, StringComparer.Ordinal))
        {
            var form = pin.Type!.Form;
            if (form is not (PinTypeForm.Generic or PinTypeForm.ArrayOfGeneric or PinTypeForm.ElementOfGeneric))
                continue;
            if (generics.IsBound(pin.Type.Variable)) continue;

            var source = typeOfSource(new PinRef(node.Id, pin.Id));
            if (source == null) continue;

            var (typeName, isArray) = source.Value;
            if (string.IsNullOrEmpty(typeName)) continue;

            switch (form)
            {
                case PinTypeForm.Generic:
                    generics.Bind(pin.Type.Variable, typeName, isArray);
                    break;

                case PinTypeForm.ArrayOfGeneric when isArray:
                    // The variable names the element type, so an array pin binds the element.
                    generics.Bind(pin.Type.Variable, typeName, false);
                    break;

                case PinTypeForm.ElementOfGeneric when !isArray:
                    generics.Bind(pin.Type.Variable, typeName, false);
                    break;
            }
        }

        return generics;
    }
}
