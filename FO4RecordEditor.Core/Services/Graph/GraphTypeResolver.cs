using System;
using System.Collections.Generic;
using System.Linq;
using FO4RecordEditor.Services.Papyrus;

namespace FO4RecordEditor.Services.Graph;

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

        if (!_bound.ContainsKey(variable)) _bound[variable] = (typeName, isArray);
    }

    public bool IsBound(string variable) => _bound.ContainsKey(variable);
}

public sealed class GraphTypeResolver
{
    private readonly PapyrusScriptIndex _index;
    private readonly string _selfType;
    private readonly string? _selfExtends;
    private readonly Dictionary<(string, string), bool> _inherits = new();

    public GraphTypeResolver(PapyrusScriptIndex index, string selfType, string? selfExtends = null)
    {
        _index = index ?? throw new ArgumentNullException(nameof(index));
        _selfType = selfType ?? "";
        _selfExtends = selfExtends;
    }

    public bool SourcesComplete { get; private set; } = true;

    public bool InheritsFrom(string child, string ancestor)
    {
        if (string.Equals(child, ancestor, StringComparison.OrdinalIgnoreCase)) return true;

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

    public enum WireVerdict
    {

        Implicit,

        NeedsCast,

        Incompatible,

        Unknown,
    }

    public WireVerdict Judge(
        string fromType, bool fromArray, string toType, bool toArray, PapyrusScript? relativeTo)
    {

        var from = Resolve(fromType, fromArray, relativeTo);
        var to = Resolve(toType, toArray, relativeTo);
        if (from == null || to == null) return WireVerdict.Unknown;

        PapyrusConversions.InheritsFrom inherits = InheritsFrom;

        if (PapyrusConversions.IsImplicit(from, to, inherits)) return WireVerdict.Implicit;
        if (PapyrusConversions.IsExplicit(from, to, inherits)) return WireVerdict.NeedsCast;
        return WireVerdict.Incompatible;
    }

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
