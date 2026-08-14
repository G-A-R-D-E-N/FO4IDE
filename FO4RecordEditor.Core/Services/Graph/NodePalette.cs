using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using FO4RecordEditor.Services.Papyrus;

namespace FO4RecordEditor.Services.Graph;







public sealed record PaletteEntry(
    string Id,
    string Title,
    string Category,
    GraphNodeKind Kind,
    string Signature,
    bool IsPure)
{
    public override string ToString() => Id;
}


public sealed record PaletteSearchResult(IReadOnlyList<PaletteEntry> Entries, int Total)
{

    public bool Truncated => Entries.Count < Total;
}











public sealed class NodePalette
{
    private readonly PapyrusScriptIndex _index;
    private readonly IWikiDocProvider _docs;
    private readonly ConcurrentDictionary<string, IReadOnlyList<NodeDefinition>> _byScript = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lazy<IReadOnlyList<PaletteEntry>> _searchIndex;

    public NodePalette(PapyrusScriptIndex index, IWikiDocProvider? docs = null)
    {
        _index = index ?? throw new ArgumentNullException(nameof(index));
        _docs = docs ?? NullWikiDocProvider.Instance;
        _searchIndex = new Lazy<IReadOnlyList<PaletteEntry>>(BuildSearchIndex);
    }


    public IReadOnlyList<NodeDefinition> Builtins => BuiltinNodeDefinitions.All;

    public IReadOnlyList<string> ScriptNames => _index.ScriptNames.ToList();

    public WikiDocStats WikiStats => _docs.Stats;


    public NodeDefinition? Find(string? definitionId)
    {
        if (string.IsNullOrEmpty(definitionId)) return null;

        var builtin = BuiltinNodeDefinitions.Find(definitionId);
        if (builtin != null) return builtin;

        var owner = OwnerScriptOf(definitionId);
        if (owner == null) return null;

        return ForScript(owner).FirstOrDefault(d =>
            string.Equals(d.Id, definitionId, StringComparison.OrdinalIgnoreCase));
    }


    public IReadOnlyList<NodeDefinition> ForScript(string scriptName) =>
        _byScript.GetOrAdd(scriptName, BuildForScript);


    public PaletteSearchResult Search(string? query, int limit = 50, string? scriptFilter = null)
    {
        IEnumerable<PaletteEntry> candidates = _searchIndex.Value;

        if (!string.IsNullOrWhiteSpace(scriptFilter))
        {
            candidates = candidates.Where(e =>
                e.Category.Equals(scriptFilter, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            var needle = query.Trim();
            candidates = candidates
                .Where(e => e.Title.Contains(needle, StringComparison.OrdinalIgnoreCase)
                            || e.Category.Contains(needle, StringComparison.OrdinalIgnoreCase))


                .OrderBy(e => e.Title.Equals(needle, StringComparison.OrdinalIgnoreCase) ? 0
                    : e.Title.StartsWith(needle, StringComparison.OrdinalIgnoreCase) ? 1 : 2)
                .ThenBy(e => e.Title.Length)
                .ThenBy(e => e.Title, StringComparer.OrdinalIgnoreCase);
        }

        var all = candidates.ToList();
        return new PaletteSearchResult(all.Take(Math.Max(0, limit)).ToList(), all.Count);
    }









    private IReadOnlyList<PaletteEntry> BuildSearchIndex()
    {
        var entries = BuiltinNodeDefinitions.All
            .Select(d => new PaletteEntry(d.Id, d.Title, d.Category, d.Kind, d.Title, d.IsPure))
            .ToList();

        foreach (var scriptName in _index.ScriptNames)
        {
            var script = _index.Resolve(scriptName);
            if (script == null) continue;

            foreach (var symbol in PapyrusSymbols.DocumentSymbols(script))
            {
                switch (symbol.Kind)
                {
                    case PapyrusSymbolKind.Function:
                        entries.Add(new PaletteEntry(
                            CallId(scriptName, symbol.Name, IsGlobalSignature(symbol.Signature)),
                            symbol.Name, scriptName, GraphNodeKind.Call, symbol.Signature, IsPure: false));
                        break;

                    case PapyrusSymbolKind.Event:
                        entries.Add(new PaletteEntry(
                            EventId(scriptName, symbol.Name),
                            symbol.Name, scriptName, GraphNodeKind.EventEntry, symbol.Signature, IsPure: false));
                        entries.Add(new PaletteEntry(
                            RemoteEventId(scriptName, symbol.Name),
                            scriptName + "." + symbol.Name, scriptName, GraphNodeKind.EventEntry,
                            RemoteSignature(scriptName, symbol.Name, symbol.Signature), IsPure: false));
                        break;

                    case PapyrusSymbolKind.CustomEvent:
                        entries.Add(new PaletteEntry(
                            RemoteEventId(scriptName, symbol.Name),
                            scriptName + "." + symbol.Name, scriptName, GraphNodeKind.EventEntry,
                            RemoteSignature(scriptName, symbol.Name, declaredSignature: null), IsPure: false));
                        break;

                    case PapyrusSymbolKind.Property:
                        entries.Add(new PaletteEntry(
                            PropertyGetId(scriptName, symbol.Name),
                            symbol.Name, scriptName, GraphNodeKind.PropertyGet, symbol.Signature, IsPure: true));
                        entries.Add(new PaletteEntry(
                            PropertySetId(scriptName, symbol.Name),
                            symbol.Name, scriptName, GraphNodeKind.PropertySet, symbol.Signature, IsPure: false));
                        break;
                }
            }
        }

        return entries;
    }

    private static bool IsGlobalSignature(string? signature) =>
        signature != null && signature.Contains(" global", StringComparison.OrdinalIgnoreCase);

    private IReadOnlyList<NodeDefinition> BuildForScript(string scriptName)
    {
        var script = _index.Resolve(scriptName);
        if (script == null) return Array.Empty<NodeDefinition>();

        var definitions = new List<NodeDefinition>();
        var scriptDoc = _docs.Script(scriptName);

        foreach (var function in script.Functions)
            definitions.Add(FunctionDefinition(scriptName, function));

        foreach (var declared in script.Events)
        {
            definitions.Add(EventDefinition(scriptName, declared));
            definitions.Add(RemoteEventDefinition(
                scriptName,
                declared.Name,
                EventDefinition(scriptName, declared).DataOutputs,
                _docs.Function(scriptName, declared.Name)?.Summary));
        }



        foreach (var declared in script.CustomEvents)
        {
            definitions.Add(RemoteEventDefinition(
                scriptName, declared.Name, new[] { CustomEventArgsPin() }, summary: null));
        }

        foreach (var property in script.Properties)
        {
            definitions.Add(PropertyGetDefinition(scriptName, property));
            if (property.Kind != PapyrusPropertyKind.AutoReadOnly)
                definitions.Add(PropertySetDefinition(scriptName, property));
        }

        _ = scriptDoc;
        return definitions;
    }

    private NodeDefinition FunctionDefinition(string scriptName, PapyrusFunctionDecl function)
    {
        var docs = _docs.Function(scriptName, function.Name);
        var pins = new List<PinDefinition>
        {
            new() { Id = PinIds.Exec, Direction = PinDirection.In, Kind = PinKind.Exec },
        };

        if (!function.IsGlobal)
        {
            pins.Add(new PinDefinition
            {
                Id = PinIds.Self,
                Label = "Target",
                Direction = PinDirection.In,
                Kind = PinKind.Data,
                Type = PinTypeExpr.Concrete(scriptName),
            });
        }

        foreach (var parameter in function.Parameters)
            pins.Add(ParameterPin(parameter, docs));

        pins.Add(new PinDefinition { Id = PinIds.Then, Direction = PinDirection.Out, Kind = PinKind.Exec });

        if (function.ReturnType != null)
        {
            pins.Add(new PinDefinition
            {
                Id = PinIds.Return,
                Label = "Return",
                Direction = PinDirection.Out,
                Kind = PinKind.Data,
                Type = TypeOf(function.ReturnType),
                Description = docs?.ReturnValue,
            });
        }

        return new NodeDefinition
        {
            Id = CallId(scriptName, function.Name, function.IsGlobal),
            Kind = GraphNodeKind.Call,
            Title = function.Name,
            Category = scriptName,
            Summary = docs?.Summary,
            OwnerScript = scriptName,
            MemberName = function.Name,
            IsGlobal = function.IsGlobal,


            IsPure = false,
            LocalNameHint = LocalNameHint(function.Name),
            Pins = pins,
        };
    }

    private NodeDefinition EventDefinition(string scriptName, PapyrusEventDecl declared)
    {
        var docs = _docs.Function(scriptName, declared.Name);
        var pins = new List<PinDefinition>
        {
            new() { Id = PinIds.Exec, Direction = PinDirection.Out, Kind = PinKind.Exec },
        };

        foreach (var parameter in declared.Parameters)
        {
            pins.Add(new PinDefinition
            {
                Id = PinIds.Parameter(parameter.Name),
                Label = parameter.Name,
                Direction = PinDirection.Out,
                Kind = PinKind.Data,
                Type = TypeOf(parameter.Type),
                Description = docs?.Parameters.TryGetValue(parameter.Name, out var doc) == true
                    ? doc.Description
                    : null,
            });
        }

        return new NodeDefinition
        {
            Id = EventId(scriptName, declared.Name),
            Kind = GraphNodeKind.EventEntry,
            Title = declared.Name,
            Category = scriptName,
            Summary = docs?.Summary,
            OwnerScript = scriptName,
            MemberName = declared.Name,
            Pins = pins,
        };
    }















    private NodeDefinition RemoteEventDefinition(
        string scriptName, string eventName, IEnumerable<PinDefinition> tail, string? summary)
    {
        var pins = new List<PinDefinition>
        {
            new() { Id = PinIds.Exec, Direction = PinDirection.Out, Kind = PinKind.Exec },
            new()
            {
                Id = PinIds.Parameter(RemoteSenderName),
                Label = RemoteSenderName,
                Direction = PinDirection.Out,
                Kind = PinKind.Data,
                Type = PinTypeExpr.Concrete(scriptName),
                Description = "The object that raised the event.",
            },
        };
        pins.AddRange(tail);

        return new NodeDefinition
        {
            Id = RemoteEventId(scriptName, eventName),
            Kind = GraphNodeKind.EventEntry,
            Title = scriptName + "." + eventName,
            Category = scriptName,
            Summary = summary,
            OwnerScript = scriptName,
            MemberName = eventName,
            IsRemoteEvent = true,
            Pins = pins,
        };
    }


    public const string RemoteSenderName = "akSender";













    internal static string RemoteSignature(string scriptName, string eventName, string? declaredSignature)
    {
        var sender = scriptName + " " + RemoteSenderName;

        if (declaredSignature == null)
            return $"Event {scriptName}.{eventName}({sender}, Var[] akArgs)";

        int open = declaredSignature.IndexOf('(');
        int close = declaredSignature.LastIndexOf(')');
        var declared = open >= 0 && close > open
            ? declaredSignature[(open + 1)..close].Trim()
            : "";

        var parameters = declared.Length == 0 ? sender : sender + ", " + declared;
        return $"Event {scriptName}.{eventName}({parameters})";
    }


    private static PinDefinition CustomEventArgsPin() => new()
    {
        Id = PinIds.Parameter("akArgs"),
        Label = "akArgs",
        Direction = PinDirection.Out,
        Kind = PinKind.Data,
        Type = PinTypeExpr.Concrete("Var", isArray: true),
        Description = "The values the sender passed to SendCustomEvent.",
    };

    private NodeDefinition PropertyGetDefinition(string scriptName, PapyrusPropertyDecl property) => new()
    {
        Id = PropertyGetId(scriptName, property.Name),
        Kind = GraphNodeKind.PropertyGet,
        Title = "Get " + property.Name,
        Category = scriptName,
        OwnerScript = scriptName,
        MemberName = property.Name,
        IsPure = true,
        LocalNameHint = LocalNameHint(property.Name),
        Pins = new[]
        {
            new PinDefinition
            {
                Id = PinIds.Self, Label = "Target", Direction = PinDirection.In, Kind = PinKind.Data,
                Type = PinTypeExpr.Concrete(scriptName),
            },
            new PinDefinition
            {
                Id = PinIds.Value, Label = property.Name, Direction = PinDirection.Out, Kind = PinKind.Data,
                Type = TypeOf(property.Type),
            },
        },
    };

    private NodeDefinition PropertySetDefinition(string scriptName, PapyrusPropertyDecl property) => new()
    {
        Id = PropertySetId(scriptName, property.Name),
        Kind = GraphNodeKind.PropertySet,
        Title = "Set " + property.Name,
        Category = scriptName,
        OwnerScript = scriptName,
        MemberName = property.Name,
        Pins = new[]
        {
            new PinDefinition { Id = PinIds.Exec, Direction = PinDirection.In, Kind = PinKind.Exec },
            new PinDefinition
            {
                Id = PinIds.Self, Label = "Target", Direction = PinDirection.In, Kind = PinKind.Data,
                Type = PinTypeExpr.Concrete(scriptName),
            },
            new PinDefinition
            {
                Id = PinIds.Value, Label = property.Name, Direction = PinDirection.In, Kind = PinKind.Data,
                Type = TypeOf(property.Type),
            },
            new PinDefinition { Id = PinIds.Then, Direction = PinDirection.Out, Kind = PinKind.Exec },
        },
    };

    private static PinDefinition ParameterPin(PapyrusParameter parameter, WikiFunctionDoc? docs)
    {
        WikiParameterDoc? parameterDoc = null;
        docs?.Parameters.TryGetValue(parameter.Name, out parameterDoc);

        return new PinDefinition
        {
            Id = PinIds.Argument(parameter.Name),
            Label = parameter.Name,
            Direction = PinDirection.In,
            Kind = PinKind.Data,
            Type = TypeOf(parameter.Type),
            IsOptional = parameter.DefaultValue != null,
            DeclaredDefault = DefaultText(parameter.DefaultValue),
            Description = parameterDoc?.Description,
        };
    }











    public static string? DefaultText(PapyrusExpression? expression) => expression switch
    {
        null => null,

        PapyrusLiteralExpression literal => literal.Kind switch
        {
            PapyrusLiteralKind.String => "\"" + literal.Text
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"") + "\"",
            PapyrusLiteralKind.None => "None",
            _ => literal.Text,
        },

        PapyrusUnaryExpression unary when unary.Operand is PapyrusLiteralExpression inner
            => "-" + inner.Text,

        PapyrusIdentifierExpression identifier => identifier.Name,

        _ => null,
    };

    private static PinTypeExpr TypeOf(PapyrusTypeRef? reference) =>
        reference == null
            ? PinTypeExpr.Concrete("None")
            : PinTypeExpr.Concrete(reference.Name, reference.IsArray);









    public static string LocalNameHint(string memberName)
    {
        foreach (var prefix in new[] { "Get", "Is", "Has", "Can" })
        {
            if (memberName.Length <= prefix.Length) continue;
            if (!memberName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            if (!char.IsUpper(memberName[prefix.Length])) continue;
            memberName = memberName[prefix.Length..];
            break;
        }

        return memberName.Length == 0
            ? "value"
            : char.ToLowerInvariant(memberName[0]) + memberName[1..];
    }



    public static string CallId(string script, string member, bool isGlobal) =>
        (isGlobal ? "global:" : "call:") + script + "." + member;

    public static string EventId(string script, string member) => "event:" + script + "." + member;


    public static string RemoteEventId(string script, string member) => "remote:" + script + "." + member;

    public static string PropertyGetId(string script, string member) => "prop.get:" + script + "." + member;

    public static string PropertySetId(string script, string member) => "prop.set:" + script + "." + member;


    public static string? OwnerScriptOf(string definitionId)
    {
        int colon = definitionId.IndexOf(':');
        if (colon < 0) return null;

        var body = definitionId[(colon + 1)..];
        int dot = body.LastIndexOf('.');
        return dot <= 0 ? null : body[..dot];
    }


    public static string? MemberNameOf(string definitionId)
    {
        int colon = definitionId.IndexOf(':');
        if (colon < 0) return null;

        var body = definitionId[(colon + 1)..];
        int dot = body.LastIndexOf('.');
        return dot < 0 || dot == body.Length - 1 ? null : body[(dot + 1)..];
    }
}
