using System;
using System.Collections.Generic;
using System.Linq;

namespace FO4RecordEditor.Services.Papyrus;

public enum PapyrusSymbolKind
{
    Script,
    Function,
    Event,
    Property,
    Variable,
    Struct,
    StructMember,
    Group,
    State,
    CustomEvent,
    Parameter,
    Local,
    Import,
}

/// <summary>One named thing, with enough position information to jump to it.</summary>
public sealed class PapyrusSymbol
{
    public string Name { get; init; } = string.Empty;

    public PapyrusSymbolKind Kind { get; init; }

    /// <summary>One-line signature for hover and for an outline's detail column.</summary>
    public string Signature { get; init; } = string.Empty;

    public string? Documentation { get; init; }

    /// <summary>Owning script or struct name, or null at script level.</summary>
    public string? Container { get; init; }

    /// <summary>File the declaration is in, when known.</summary>
    public string? File { get; init; }

    /// <summary>Span of the whole declaration.</summary>
    public PapyrusSpan Span { get; init; }

    /// <summary>Span of just the name, which is what a jump should select.</summary>
    public PapyrusSpan NameSpan { get; init; }

    public override string ToString() => $"{Kind} {Name}";
}

/// <summary>
/// Editor-facing queries over a parsed script: outline, go-to-definition and hover.
/// </summary>
/// <remarks>
/// <b>What this is not.</b> There is no type inference here. Resolution is by name, in the order a
/// reader would search: locals and parameters of the enclosing function, then the script's own
/// members, then up the <c>Extends</c> chain, then scripts by name through the index. That answers
/// "where is this declared" correctly for the overwhelming majority of real references and cannot
/// answer it for a member reached through an expression whose type is not written down --
/// <c>GetOwner().MyProperty</c> needs a type checker, which is phase 2.
/// <para>
/// Where it cannot resolve, it returns null rather than a guess. A wrong jump is worse than none.
/// </para>
/// </remarks>
public static class PapyrusSymbols
{
    /// <summary>Flat outline of everything a script declares, in source order within each kind.</summary>
    public static IReadOnlyList<PapyrusSymbol> DocumentSymbols(PapyrusScript script)
    {
        var symbols = new List<PapyrusSymbol>
        {
            new()
            {
                Name = script.Name,
                Kind = PapyrusSymbolKind.Script,
                Signature = script.Signature,
                Documentation = script.Documentation,
                File = script.FilePath,
                Span = script.Span,
                NameSpan = script.NameSpan,
            },
        };

        foreach (var import in script.Imports)
        {
            symbols.Add(new PapyrusSymbol
            {
                Name = import.Name,
                Kind = PapyrusSymbolKind.Import,
                Signature = "Import " + import.Name,
                File = script.FilePath,
                Span = import.Span,
                NameSpan = import.NameSpan,
            });
        }

        foreach (var s in script.Structs)
        {
            symbols.Add(Make(s, PapyrusSymbolKind.Struct, script.Name, script.FilePath));
            foreach (var member in s.Members)
            {
                symbols.Add(Make(member, PapyrusSymbolKind.StructMember, s.Name, script.FilePath));
            }
        }

        foreach (var c in script.CustomEvents) symbols.Add(Make(c, PapyrusSymbolKind.CustomEvent, script.Name, script.FilePath));
        foreach (var g in script.Groups) symbols.Add(Make(g, PapyrusSymbolKind.Group, script.Name, script.FilePath));
        foreach (var p in script.Properties) symbols.Add(Make(p, PapyrusSymbolKind.Property, p.GroupName ?? script.Name, script.FilePath));
        foreach (var v in script.Variables) symbols.Add(Make(v, PapyrusSymbolKind.Variable, script.Name, script.FilePath));
        foreach (var f in script.Functions) symbols.Add(Make(f, PapyrusSymbolKind.Function, script.Name, script.FilePath));
        foreach (var e in script.Events) symbols.Add(Make(e, PapyrusSymbolKind.Event, script.Name, script.FilePath));

        foreach (var state in script.States)
        {
            symbols.Add(Make(state, PapyrusSymbolKind.State, script.Name, script.FilePath));
            foreach (var f in state.Functions) symbols.Add(Make(f, PapyrusSymbolKind.Function, state.Name, script.FilePath));
            foreach (var e in state.Events) symbols.Add(Make(e, PapyrusSymbolKind.Event, state.Name, script.FilePath));
        }

        return symbols;
    }

    private static PapyrusSymbol Make(PapyrusDeclaration decl, PapyrusSymbolKind kind, string? container, string? file) =>
        new()
        {
            Name = decl.Name,
            Kind = kind,
            Signature = decl.Signature,
            Documentation = decl.Documentation,
            Container = container,
            File = file,
            Span = decl.Span,
            NameSpan = decl.NameSpan,
        };

    /// <summary>
    /// The declaration referred to at <paramref name="offset"/>, or null when it cannot be resolved.
    /// </summary>
    /// <param name="index">Used to follow <c>Extends</c> and to resolve other scripts by name.</param>
    /// <param name="script">The parsed script the caret is in.</param>
    /// <param name="offset">0-based character offset of the caret.</param>
    public static PapyrusSymbol? FindDefinition(PapyrusScriptIndex index, PapyrusScript script, int offset)
    {
        var path = script.PathTo(offset);
        if (path.Count == 0) return null;

        // Standing on a declaration's own name is a request for that declaration, not a lookup.
        for (var i = path.Count - 1; i >= 0; i--)
        {
            if (path[i] is PapyrusDeclaration decl && decl.NameSpan.Length > 0 && decl.NameSpan.Contains(offset))
            {
                return Make(decl, KindOf(decl), script.Name, script.FilePath);
            }
            if (path[i] is PapyrusDefineStatement define && define.NameSpan.Contains(offset))
            {
                return new PapyrusSymbol
                {
                    Name = define.Name,
                    Kind = PapyrusSymbolKind.Local,
                    Signature = $"{define.Type} {define.Name}",
                    Container = EnclosingCallable(path)?.Name,
                    File = script.FilePath,
                    Span = define.Span,
                    NameSpan = define.NameSpan,
                };
            }
        }

        var node = path[path.Count - 1];

        // A written type: resolve it as a script name, or as a struct in this script or another.
        if (node is PapyrusTypeRef typeRef) return ResolveTypeName(index, script, typeRef.Name);

        // The Extends clause is not a node of its own; it lives on the script.
        if (script.Extends != null && script.ExtendsSpan.Contains(offset))
        {
            return ResolveTypeName(index, script, script.Extends);
        }

        if (node is PapyrusIdentifierExpression identifier)
        {
            return ResolveIdentifier(index, script, path, identifier.Name, offset);
        }

        // Member access or a qualified call. Without types, the only case that can be answered
        // honestly is a receiver that is itself a script name -- Debug.Trace, Utility.Wait,
        // MyNamespace:MyScript.MyGlobal -- plus members of this script's own chain.
        for (var i = path.Count - 1; i >= 0; i--)
        {
            if (path[i] is not PapyrusMemberExpression member) continue;
            if (!member.NameSpan.Contains(offset)) continue;

            if (member.Target is PapyrusIdentifierExpression receiver)
            {
                var receiverScript = index.Resolve(receiver.Name);
                if (receiverScript != null)
                {
                    var onReceiver = PapyrusScriptIndex.FindMemberOn(receiverScript, member.Name);
                    if (onReceiver != null)
                    {
                        return Make(onReceiver, KindOf(onReceiver), receiverScript.Name, receiverScript.FilePath);
                    }
                }
            }

            // "self.Foo" and "parent.Foo" both land on this script's chain, and so does any member
            // name that happens to be unique to it. This last fallback is a heuristic and is the
            // reason a type checker is still the right phase 2.
            var own = index.FindMember(script, member.Name, out var owner);
            if (own != null && owner != null) return Make(own, KindOf(own), owner.Name, owner.FilePath);
            return null;
        }

        return null;
    }

    /// <summary>Hover text for the caret position: a signature, plus the doc comment if there is one.</summary>
    public static string? Hover(PapyrusScriptIndex index, PapyrusScript script, int offset)
    {
        var symbol = FindDefinition(index, script, offset);
        if (symbol == null) return null;
        return string.IsNullOrWhiteSpace(symbol.Documentation)
            ? symbol.Signature
            : symbol.Signature + "\n\n" + symbol.Documentation.Trim();
    }

    private static PapyrusSymbol? ResolveTypeName(PapyrusScriptIndex index, PapyrusScript script, string name)
    {
        if (string.IsNullOrEmpty(name)) return null;

        // Built-in value types have no declaration to jump to.
        switch (name.ToLowerInvariant())
        {
            case "int":
            case "float":
            case "bool":
            case "string":
            case "var":
            case "scripteventname":
            case "customeventname":
            case "structvarname":
                return null;
        }

        // A struct is written either bare (same script) or as Script:Struct.
        var colon = name.LastIndexOf(':');
        if (colon > 0)
        {
            var ownerName = name.Substring(0, colon);
            var structName = name.Substring(colon + 1);
            var owner = index.Resolve(ownerName);
            var member = owner == null ? null : PapyrusScriptIndex.FindMemberOn(owner, structName);
            if (member is PapyrusStructDecl s) return Make(s, PapyrusSymbolKind.Struct, owner!.Name, owner.FilePath);
        }
        else
        {
            foreach (var local in script.Structs)
            {
                if (string.Equals(local.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return Make(local, PapyrusSymbolKind.Struct, script.Name, script.FilePath);
                }
            }
        }

        var target = index.Resolve(name);
        if (target == null) return null;
        return new PapyrusSymbol
        {
            Name = target.Name,
            Kind = PapyrusSymbolKind.Script,
            Signature = target.Signature,
            Documentation = target.Documentation,
            File = target.FilePath,
            Span = target.Span,
            NameSpan = target.NameSpan,
        };
    }

    /// <summary>
    /// Resolves a bare identifier, innermost scope outward.
    /// </summary>
    /// <remarks>
    /// Locals are only considered if they are declared *before* the caret, which is the language's
    /// own rule -- "variables inside functions must be defined before use" -- and is what stops a
    /// later shadowing definition from capturing an earlier reference.
    /// </remarks>
    private static PapyrusSymbol? ResolveIdentifier(
        PapyrusScriptIndex index,
        PapyrusScript script,
        IReadOnlyList<PapyrusNode> path,
        string name,
        int offset)
    {
        var callable = EnclosingCallable(path);
        if (callable != null)
        {
            foreach (var parameter in callable.Parameters)
            {
                if (string.Equals(parameter.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return Make(parameter, PapyrusSymbolKind.Parameter, callable.Name, script.FilePath);
                }
            }

            PapyrusDefineStatement? best = null;
            foreach (var define in Defines(callable.Body))
            {
                if (define.Span.Start >= offset) continue;
                if (!string.Equals(define.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
                if (best == null || define.Span.Start > best.Span.Start) best = define;
            }
            if (best != null)
            {
                return new PapyrusSymbol
                {
                    Name = best.Name,
                    Kind = PapyrusSymbolKind.Local,
                    Signature = $"{best.Type} {best.Name}",
                    Container = callable.Name,
                    File = script.FilePath,
                    Span = best.Span,
                    NameSpan = best.NameSpan,
                };
            }
        }

        var member = index.FindMember(script, name, out var owner);
        if (member != null && owner != null) return Make(member, KindOf(member), owner.Name, owner.FilePath);

        // An imported script contributes its global functions and structs unqualified.
        foreach (var import in script.Imports)
        {
            var imported = index.Resolve(import.Name);
            if (imported == null) continue;
            var hit = PapyrusScriptIndex.FindMemberOn(imported, name);
            if (hit != null) return Make(hit, KindOf(hit), imported.Name, imported.FilePath);
        }

        // Last, the name may be a script: "Debug", "Utility", or a type used as a global receiver.
        return ResolveTypeName(index, script, name);
    }

    private static PapyrusCallableDecl? EnclosingCallable(IReadOnlyList<PapyrusNode> path)
    {
        for (var i = path.Count - 1; i >= 0; i--)
        {
            if (path[i] is PapyrusCallableDecl callable) return callable;
        }
        return null;
    }

    /// <summary>Every define statement in a body, including the ones nested in if and while blocks.</summary>
    private static IEnumerable<PapyrusDefineStatement> Defines(IEnumerable<PapyrusStatement> body)
    {
        foreach (var statement in body)
        {
            switch (statement)
            {
                case PapyrusDefineStatement define:
                    yield return define;
                    break;
                case PapyrusIfStatement branch:
                    foreach (var arm in branch.Branches)
                    {
                        foreach (var d in Defines(arm.Body)) yield return d;
                    }
                    if (branch.ElseBody != null)
                    {
                        foreach (var d in Defines(branch.ElseBody)) yield return d;
                    }
                    break;
                case PapyrusWhileStatement loop:
                    foreach (var d in Defines(loop.Body)) yield return d;
                    break;
            }
        }
    }

    private static PapyrusSymbolKind KindOf(PapyrusDeclaration decl) => decl switch
    {
        PapyrusScript => PapyrusSymbolKind.Script,
        PapyrusFunctionDecl => PapyrusSymbolKind.Function,
        PapyrusEventDecl => PapyrusSymbolKind.Event,
        PapyrusPropertyDecl => PapyrusSymbolKind.Property,
        PapyrusStructDecl => PapyrusSymbolKind.Struct,
        PapyrusGroupDecl => PapyrusSymbolKind.Group,
        PapyrusStateDecl => PapyrusSymbolKind.State,
        PapyrusCustomEventDecl => PapyrusSymbolKind.CustomEvent,
        PapyrusParameter => PapyrusSymbolKind.Parameter,
        _ => PapyrusSymbolKind.Variable,
    };
}
