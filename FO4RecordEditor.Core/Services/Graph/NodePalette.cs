using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using FO4RecordEditor.Services.Papyrus;

namespace FO4RecordEditor.Services.Graph;

/// <summary>One entry in a palette search result.</summary>
/// <remarks>
/// Deliberately without pins. A search returning full pin lists would grow a sixty result payload
/// from a few kilobytes to a few hundred, and the canvas only needs pins for the type it actually
/// places.
/// </remarks>
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

/// <summary>A page of search results, with the true total behind it.</summary>
public sealed record PaletteSearchResult(IReadOnlyList<PaletteEntry> Entries, int Total)
{
    /// <summary>Whether the result was capped.</summary>
    public bool Truncated => Entries.Count < Total;
}

/// <summary>
/// The node types on offer, generated from whatever scripts the user actually has.
/// </summary>
/// <remarks>
/// Built lazily. The eager pass is only a lightweight name index over the script index, which reuses
/// its parse cache; full definitions for a script are materialised the first time something asks for
/// them. A corpus of nearly eight thousand scripts yields tens of thousands of definitions, and
/// building them all up front would cost seconds and hundreds of megabytes for a palette the user
/// searches a handful of entries from.
/// </remarks>
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

    /// <summary>The built-in node types, which need no script to exist.</summary>
    public IReadOnlyList<NodeDefinition> Builtins => BuiltinNodeDefinitions.All;

    public IReadOnlyList<string> ScriptNames => _index.ScriptNames.ToList();

    public WikiDocStats WikiStats => _docs.Stats;

    /// <summary>One definition by id, built-in or generated.</summary>
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

    /// <summary>Every definition a script contributes, built on demand and cached.</summary>
    public IReadOnlyList<NodeDefinition> ForScript(string scriptName) =>
        _byScript.GetOrAdd(scriptName, BuildForScript);

    /// <summary>Definitions whose title or owner matches, capped.</summary>
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
                // An exact title, then a prefix, then anything: typing "Add" should not bury
                // Add under AddInventoryEventFilter.
                .OrderBy(e => e.Title.Equals(needle, StringComparison.OrdinalIgnoreCase) ? 0
                    : e.Title.StartsWith(needle, StringComparison.OrdinalIgnoreCase) ? 1 : 2)
                .ThenBy(e => e.Title.Length)
                .ThenBy(e => e.Title, StringComparer.OrdinalIgnoreCase);
        }

        var all = candidates.ToList();
        return new PaletteSearchResult(all.Take(Math.Max(0, limit)).ToList(), all.Count);
    }

    /// <summary>
    /// The lightweight index every search runs over.
    /// </summary>
    /// <remarks>
    /// Built from document symbols, which reuse the index's parse cache, so this costs one parse per
    /// script and no definition construction. Entries carry a signature for display and nothing
    /// else; anything richer is fetched per placed node.
    /// </remarks>
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

        // A custom event is only ever handled remotely: the script that declares it raises it, and
        // some other script listens. So there is no local override counterpart to build.
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
            // Never inferred from the body: an impure call bound to a local keeps evaluation order
            // fixed by the exec graph, which is what makes the emitted source deterministic.
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

    /// <summary>
    /// A handler for an event another object raises, written <c>Event Owner.Name(...)</c>.
    /// </summary>
    /// <remarks>
    /// The sender is a real parameter and comes first, of the raising script's type. That is the
    /// shape every dotted handler in the surveyed corpus has. Authors name it various things
    /// (`akSender`, `aSender`, plain `sender`), so the name is a convention rather than a rule, and
    /// `akSender` is what gets emitted.
    /// <para>
    /// This is a separate definition from the local override rather than a flag on it, because the
    /// pin set genuinely differs by the sender pin and pins are derived from the definition id
    /// rather than saved.
    /// </para>
    /// </remarks>
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

    /// <summary>The name emitted for a remote handler's sender parameter.</summary>
    public const string RemoteSenderName = "akSender";

    /// <summary>
    /// What a remote handler's signature reads as in the palette.
    /// </summary>
    /// <remarks>
    /// Built rather than reusing the declaring event's own signature. The two differ by the dotted
    /// name and the sender parameter, so showing the local one would describe a node the author
    /// cannot get by placing this entry, which is exactly the sort of thing the palette is read for.
    /// <para>
    /// A null <paramref name="declaredSignature"/> means a custom event, whose payload Papyrus fixes
    /// at <c>Var[]</c> and which therefore has no declared parameter list to extend.
    /// </para>
    /// </remarks>
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

    /// <summary>The fixed argument payload Papyrus gives every custom event handler.</summary>
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

    /// <summary>
    /// A declared default as the source text an emitter would write.
    /// </summary>
    /// <remarks>
    /// The AST carries an expression, not a string, and its default <c>ToString</c> is the C# type
    /// name. Only the shapes that legally appear in a default position are rendered: a literal, a
    /// negated number, and an identifier such as an enum-like constant. Anything else returns null,
    /// which leaves the pin optional but without a shown default rather than showing something
    /// wrong.
    /// </remarks>
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

    /// <summary>
    /// A readable stem for the local a node's result binds to.
    /// </summary>
    /// <remarks>
    /// Strips a leading Get, Is, Has or Can and lower-camels the rest, so <c>GetPlayer</c> becomes
    /// <c>player</c> and <c>IsDead</c> becomes <c>dead</c>. Deterministic, so emitted source does
    /// not change between runs.
    /// </remarks>
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

    // ---- definition ids ---------------------------------------------------------------------

    public static string CallId(string script, string member, bool isGlobal) =>
        (isGlobal ? "global:" : "call:") + script + "." + member;

    public static string EventId(string script, string member) => "event:" + script + "." + member;

    /// <summary>A handler for an event another object raises, local override's counterpart.</summary>
    public static string RemoteEventId(string script, string member) => "remote:" + script + "." + member;

    public static string PropertyGetId(string script, string member) => "prop.get:" + script + "." + member;

    public static string PropertySetId(string script, string member) => "prop.set:" + script + "." + member;

    /// <summary>The script a generated definition id names, or null for a built-in.</summary>
    public static string? OwnerScriptOf(string definitionId)
    {
        int colon = definitionId.IndexOf(':');
        if (colon < 0) return null;

        var body = definitionId[(colon + 1)..];
        int dot = body.LastIndexOf('.');
        return dot <= 0 ? null : body[..dot];
    }

    /// <summary>The member a generated definition id names, or null.</summary>
    public static string? MemberNameOf(string definitionId)
    {
        int colon = definitionId.IndexOf(':');
        if (colon < 0) return null;

        var body = definitionId[(colon + 1)..];
        int dot = body.LastIndexOf('.');
        return dot < 0 || dot == body.Length - 1 ? null : body[(dot + 1)..];
    }
}
