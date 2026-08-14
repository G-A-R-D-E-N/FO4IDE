using System.Collections.Generic;
using System.Linq;

namespace FO4RecordEditor.Services.Papyrus;

/// <summary>
/// Base of every Papyrus syntax node.
/// </summary>
/// <remarks>
/// <see cref="Children"/> exists so position-based queries -- "what is under the caret?" -- can walk
/// the tree generically instead of every query re-listing the node kinds. Anything that adds a child
/// field must extend <c>Children</c> too, or that subtree becomes invisible to go-to-definition.
/// </remarks>
public abstract class PapyrusNode
{
    public PapyrusSpan Span { get; internal set; }

    public virtual IEnumerable<PapyrusNode> Children => System.Array.Empty<PapyrusNode>();

    /// <summary>The innermost node whose span contains <paramref name="offset"/>, or null.</summary>
    public PapyrusNode? FindInnermost(int offset)
    {
        if (!Span.Contains(offset)) return null;
        foreach (var child in Children)
        {
            var hit = child?.FindInnermost(offset);
            if (hit != null) return hit;
        }
        return this;
    }

    /// <summary>The chain from this node down to the innermost node containing <paramref name="offset"/>.</summary>
    public IReadOnlyList<PapyrusNode> PathTo(int offset)
    {
        var path = new List<PapyrusNode>();
        Walk(this, offset, path);
        return path;

        static bool Walk(PapyrusNode node, int off, List<PapyrusNode> acc)
        {
            if (!node.Span.Contains(off)) return false;
            acc.Add(node);
            foreach (var child in node.Children)
            {
                if (child != null && Walk(child, off, acc)) return true;
            }
            return true;
        }
    }
}

/// <summary>
/// A written type: a name, possibly namespace-qualified, possibly an array.
/// </summary>
/// <remarks>
/// The name is stored as written, colons and all (<c>MyNamespace:MyScript:MyStruct</c>). Phase 1 has
/// no resolver, so nothing here is bound to a declaration -- <see cref="PapyrusScriptIndex"/> does
/// name-based lookup on top of this, and a real type checker would replace that.
/// </remarks>
public sealed class PapyrusTypeRef : PapyrusNode
{
    public PapyrusTypeRef(string name, bool isArray, PapyrusSpan span)
    {
        Name = name;
        IsArray = isArray;
        Span = span;
    }

    public string Name { get; }

    public bool IsArray { get; }

    /// <summary>The type as it would be written, e.g. <c>int[]</c>.</summary>
    public override string ToString() => IsArray ? Name + "[]" : Name;
}

/// <summary>Anything with a name, flags and an optional documentation comment.</summary>
public abstract class PapyrusDeclaration : PapyrusNode
{
    public string Name { get; internal set; } = string.Empty;

    /// <summary>Span of just the name, which is what go-to-definition should select.</summary>
    public PapyrusSpan NameSpan { get; internal set; }

    /// <summary>Flags as written. Language keywords and user flags both land here, lowercased.</summary>
    public List<string> Flags { get; } = new();

    public string? Documentation { get; internal set; }

    public bool HasFlag(string flag) =>
        Flags.Any(f => string.Equals(f, flag, System.StringComparison.OrdinalIgnoreCase));

    /// <summary>A one-line signature for hover text.</summary>
    public abstract string Signature { get; }
}

public sealed class PapyrusImport : PapyrusNode
{
    public PapyrusImport(string name, PapyrusSpan nameSpan)
    {
        Name = name;
        NameSpan = nameSpan;
    }

    public string Name { get; }

    public PapyrusSpan NameSpan { get; }
}

public sealed class PapyrusVariableDecl : PapyrusDeclaration
{
    public PapyrusTypeRef Type { get; internal set; } = null!;

    public PapyrusExpression? Initializer { get; internal set; }

    public override IEnumerable<PapyrusNode> Children
    {
        get
        {
            yield return Type;
            if (Initializer != null) yield return Initializer;
        }
    }

    public override string Signature =>
        Flags.Count == 0 ? $"{Type} {Name}" : $"{Type} {Name} {string.Join(" ", Flags)}";
}

public sealed class PapyrusStructDecl : PapyrusDeclaration
{
    public List<PapyrusVariableDecl> Members { get; } = new();

    public override IEnumerable<PapyrusNode> Children => Members;

    public override string Signature => $"Struct {Name}";
}

public sealed class PapyrusCustomEventDecl : PapyrusDeclaration
{
    public override string Signature => $"CustomEvent {Name}";
}

/// <summary>
/// What an argument means to the callee, as distinct from what type of value it is.
/// </summary>
/// <remarks>
/// The Function Reference calls these "special parameter types". They are not Papyrus value types
/// and do not belong in <see cref="PapyrusTypeKind"/>: the expression in that position is a String
/// and stays one. What they carry is a rule about the string's contents, which only the callee
/// knows, so it travels with the parameter rather than with the type.
/// </remarks>
public enum ParameterSemantic
{
    None,
    ScriptEventName,
    CustomEventName,
    StructVarName,
}

public sealed class PapyrusParameter : PapyrusDeclaration
{
    public PapyrusTypeRef Type { get; internal set; } = null!;

    /// <summary>What this argument means to the callee. See <see cref="ParameterSemantic"/>.</summary>
    /// <remarks>
    /// Read off the declared type name, which the parser has already lowercased, rather than stored
    /// separately, so there is one source of truth and no way for the two to disagree.
    /// </remarks>
    public ParameterSemantic Semantic => Type?.Name switch
    {
        "scripteventname" => ParameterSemantic.ScriptEventName,
        "customeventname" => ParameterSemantic.CustomEventName,
        "structvarname" => ParameterSemantic.StructVarName,
        _ => ParameterSemantic.None,
    };

    /// <summary>Default value, which makes the parameter optional.</summary>
    public PapyrusExpression? DefaultValue { get; internal set; }

    public override IEnumerable<PapyrusNode> Children
    {
        get
        {
            yield return Type;
            if (DefaultValue != null) yield return DefaultValue;
        }
    }

    public override string Signature => DefaultValue == null ? $"{Type} {Name}" : $"{Type} {Name} = ...";
}

/// <summary>Shared shape of functions and events: parameters plus a statement body.</summary>
public abstract class PapyrusCallableDecl : PapyrusDeclaration
{
    public List<PapyrusParameter> Parameters { get; } = new();

    public List<PapyrusStatement> Body { get; } = new();

    public bool IsNative { get; internal set; }

    /// <summary>The state this is declared in, or null for the empty state.</summary>
    public string? StateName { get; internal set; }

    public override IEnumerable<PapyrusNode> Children => Parameters.Cast<PapyrusNode>().Concat(Body);

    protected string ParameterList => string.Join(", ", Parameters.Select(p => p.Signature));
}

public sealed class PapyrusFunctionDecl : PapyrusCallableDecl
{
    /// <summary>Return type, or null for a function that returns nothing.</summary>
    public PapyrusTypeRef? ReturnType { get; internal set; }

    public bool IsGlobal { get; internal set; }

    public override IEnumerable<PapyrusNode> Children =>
        ReturnType == null ? base.Children : new PapyrusNode[] { ReturnType }.Concat(base.Children);

    public override string Signature
    {
        get
        {
            var prefix = ReturnType == null ? string.Empty : ReturnType + " ";
            var suffix = string.Empty;
            if (IsGlobal) suffix += " global";
            if (IsNative) suffix += " native";
            return $"{prefix}Function {Name}({ParameterList}){suffix}";
        }
    }
}

public sealed class PapyrusEventDecl : PapyrusCallableDecl
{
    /// <summary>
    /// Object type for a remote or custom event handler (<c>Event ObjectReference.OnActivate(...)</c>),
    /// null for a plain one.
    /// </summary>
    public string? RemoteObjectType { get; internal set; }

    public override string Signature
    {
        get
        {
            var qualified = RemoteObjectType == null ? Name : $"{RemoteObjectType}.{Name}";
            return $"Event {qualified}({ParameterList})" + (IsNative ? " native" : string.Empty);
        }
    }
}

public enum PapyrusPropertyKind
{
    /// <summary>Backed by a hidden variable the compiler generates.</summary>
    Auto,

    /// <summary>Auto, plus immutable, so it must have an initializer.</summary>
    AutoReadOnly,

    /// <summary>Hand-written Get and/or Set functions.</summary>
    Full,
}

public sealed class PapyrusPropertyDecl : PapyrusDeclaration
{
    public PapyrusTypeRef Type { get; internal set; } = null!;

    public PapyrusPropertyKind Kind { get; internal set; }

    public PapyrusExpression? Initializer { get; internal set; }

    public PapyrusFunctionDecl? Getter { get; internal set; }

    public PapyrusFunctionDecl? Setter { get; internal set; }

    /// <summary>Enclosing group name, or null when the property is ungrouped.</summary>
    public string? GroupName { get; internal set; }

    public override IEnumerable<PapyrusNode> Children
    {
        get
        {
            yield return Type;
            if (Initializer != null) yield return Initializer;
            if (Getter != null) yield return Getter;
            if (Setter != null) yield return Setter;
        }
    }

    public override string Signature
    {
        get
        {
            var kind = Kind switch
            {
                PapyrusPropertyKind.Auto => " Auto",
                PapyrusPropertyKind.AutoReadOnly => " AutoReadOnly",
                _ => string.Empty,
            };
            return $"{Type} Property {Name}{kind}";
        }
    }
}

public sealed class PapyrusGroupDecl : PapyrusDeclaration
{
    public List<PapyrusPropertyDecl> Properties { get; } = new();

    public override IEnumerable<PapyrusNode> Children => Properties;

    public override string Signature => $"Group {Name}";
}

public sealed class PapyrusStateDecl : PapyrusDeclaration
{
    public bool IsAuto { get; internal set; }

    public List<PapyrusFunctionDecl> Functions { get; } = new();

    public List<PapyrusEventDecl> Events { get; } = new();

    public override IEnumerable<PapyrusNode> Children =>
        Functions.Cast<PapyrusNode>().Concat(Events);

    public override string Signature => (IsAuto ? "Auto State " : "State ") + Name;
}

/// <summary>One parsed .psc file.</summary>
public sealed class PapyrusScript : PapyrusDeclaration
{
    /// <summary>Path the text came from, or null for in-memory text.</summary>
    public string? FilePath { get; internal set; }

    /// <summary>Parent script name from the <c>Extends</c> clause, or null.</summary>
    public string? Extends { get; internal set; }

    public PapyrusSpan ExtendsSpan { get; internal set; }

    public List<PapyrusImport> Imports { get; } = new();

    public List<PapyrusVariableDecl> Variables { get; } = new();

    public List<PapyrusStructDecl> Structs { get; } = new();

    public List<PapyrusCustomEventDecl> CustomEvents { get; } = new();

    /// <summary>All properties, grouped ones included. <see cref="PapyrusPropertyDecl.GroupName"/> says which.</summary>
    public List<PapyrusPropertyDecl> Properties { get; } = new();

    public List<PapyrusGroupDecl> Groups { get; } = new();

    public List<PapyrusStateDecl> States { get; } = new();

    /// <summary>Empty-state functions. State overrides live on <see cref="PapyrusStateDecl.Functions"/>.</summary>
    public List<PapyrusFunctionDecl> Functions { get; } = new();

    public List<PapyrusEventDecl> Events { get; } = new();

    public IReadOnlyList<PapyrusDiagnostic> Diagnostics { get; internal set; } =
        System.Array.Empty<PapyrusDiagnostic>();

    public bool HasErrors
    {
        get
        {
            foreach (var d in Diagnostics)
            {
                if (d.Severity == PapyrusSeverity.Error) return true;
            }
            return false;
        }
    }

    public override IEnumerable<PapyrusNode> Children =>
        Imports.Cast<PapyrusNode>()
            .Concat(Structs)
            .Concat(CustomEvents)
            .Concat(Variables)
            .Concat(Groups)
            // Grouped properties are reachable through their group; listing them here as well would
            // make FindInnermost return the same node twice on a path walk.
            .Concat(Properties.Where(p => p.GroupName == null))
            .Concat(Functions)
            .Concat(Events)
            .Concat(States);

    public override string Signature =>
        Extends == null ? $"ScriptName {Name}" : $"ScriptName {Name} extends {Extends}";
}

// ---------------------------------------------------------------------------------------------
// Statements
// ---------------------------------------------------------------------------------------------

public abstract class PapyrusStatement : PapyrusNode
{
}

/// <summary>A local variable definition: <c>int x = 5</c>.</summary>
public sealed class PapyrusDefineStatement : PapyrusStatement
{
    public PapyrusTypeRef Type { get; internal set; } = null!;

    public string Name { get; internal set; } = string.Empty;

    public PapyrusSpan NameSpan { get; internal set; }

    public PapyrusExpression? Initializer { get; internal set; }

    /// <summary>
    /// Trailing flags. The wiki's in-function production has none, but <c>const</c> on a local is
    /// real and shipping -- it appears in scripts the Creation Kit compiler accepted -- so the
    /// grammar as published is incomplete here rather than the code being wrong.
    /// </summary>
    public List<string> Flags { get; } = new();

    public override IEnumerable<PapyrusNode> Children
    {
        get
        {
            yield return Type;
            if (Initializer != null) yield return Initializer;
        }
    }
}

public sealed class PapyrusAssignStatement : PapyrusStatement
{
    public PapyrusExpression Target { get; internal set; } = null!;

    /// <summary>One of <c>= += -= *= /= %=</c>.</summary>
    public PapyrusTokenKind Operator { get; internal set; }

    public PapyrusExpression Value { get; internal set; } = null!;

    public override IEnumerable<PapyrusNode> Children
    {
        get
        {
            yield return Target;
            yield return Value;
        }
    }
}

public sealed class PapyrusExpressionStatement : PapyrusStatement
{
    public PapyrusExpression Expression { get; internal set; } = null!;

    public override IEnumerable<PapyrusNode> Children
    {
        get { yield return Expression; }
    }
}

public sealed class PapyrusReturnStatement : PapyrusStatement
{
    public PapyrusExpression? Value { get; internal set; }

    public override IEnumerable<PapyrusNode> Children
    {
        get
        {
            if (Value != null) yield return Value;
        }
    }
}

/// <summary>One <c>if</c> or <c>elseif</c> arm.</summary>
public sealed class PapyrusIfBranch : PapyrusNode
{
    public PapyrusExpression Condition { get; internal set; } = null!;

    public List<PapyrusStatement> Body { get; } = new();

    public override IEnumerable<PapyrusNode> Children =>
        new PapyrusNode[] { Condition }.Concat(Body);
}

public sealed class PapyrusIfStatement : PapyrusStatement
{
    /// <summary>The <c>if</c> arm followed by every <c>elseif</c> arm, in source order.</summary>
    public List<PapyrusIfBranch> Branches { get; } = new();

    /// <summary>The <c>else</c> body, or null when there is no <c>else</c>.</summary>
    public List<PapyrusStatement>? ElseBody { get; internal set; }

    public override IEnumerable<PapyrusNode> Children =>
        ElseBody == null ? Branches : Branches.Cast<PapyrusNode>().Concat(ElseBody);
}

public sealed class PapyrusWhileStatement : PapyrusStatement
{
    public PapyrusExpression Condition { get; internal set; } = null!;

    public List<PapyrusStatement> Body { get; } = new();

    public override IEnumerable<PapyrusNode> Children =>
        new PapyrusNode[] { Condition }.Concat(Body);
}

// ---------------------------------------------------------------------------------------------
// Expressions
// ---------------------------------------------------------------------------------------------

public abstract class PapyrusExpression : PapyrusNode
{
}

public enum PapyrusLiteralKind
{
    Int,
    Float,
    String,
    Bool,
    None,
}

public sealed class PapyrusLiteralExpression : PapyrusExpression
{
    public PapyrusLiteralKind Kind { get; internal set; }

    /// <summary>Source text for numbers, decoded value for strings, "true"/"false"/"none" otherwise.</summary>
    public string Text { get; internal set; } = string.Empty;
}

public sealed class PapyrusIdentifierExpression : PapyrusExpression
{
    public string Name { get; internal set; } = string.Empty;
}

/// <summary>Dotted access: a property, struct member, array <c>Length</c>, or a method's receiver.</summary>
public sealed class PapyrusMemberExpression : PapyrusExpression
{
    public PapyrusExpression Target { get; internal set; } = null!;

    public string Name { get; internal set; } = string.Empty;

    public PapyrusSpan NameSpan { get; internal set; }

    public override IEnumerable<PapyrusNode> Children
    {
        get { yield return Target; }
    }
}

public sealed class PapyrusIndexExpression : PapyrusExpression
{
    public PapyrusExpression Target { get; internal set; } = null!;

    public PapyrusExpression Index { get; internal set; } = null!;

    public override IEnumerable<PapyrusNode> Children
    {
        get
        {
            yield return Target;
            yield return Index;
        }
    }
}

/// <summary>One call argument, which may be named (<c>Foo(bar = 1)</c>).</summary>
public sealed class PapyrusArgument : PapyrusNode
{
    public string? Name { get; internal set; }

    public PapyrusSpan NameSpan { get; internal set; }

    public PapyrusExpression Value { get; internal set; } = null!;

    public override IEnumerable<PapyrusNode> Children
    {
        get { yield return Value; }
    }
}

public sealed class PapyrusCallExpression : PapyrusExpression
{
    /// <summary>
    /// The callee, which is an identifier for a bare call and a member access for a qualified one.
    /// Keeping it as an expression rather than splitting receiver from name means the dotted chain
    /// <c>a.b().c()</c> nests the same way it evaluates.
    /// </summary>
    public PapyrusExpression Callee { get; internal set; } = null!;

    public List<PapyrusArgument> Arguments { get; } = new();

    /// <summary>The called name, for symbol lookup, whichever callee shape it is.</summary>
    public string FunctionName => Callee switch
    {
        PapyrusIdentifierExpression id => id.Name,
        PapyrusMemberExpression m => m.Name,
        _ => string.Empty,
    };

    public override IEnumerable<PapyrusNode> Children =>
        new PapyrusNode[] { Callee }.Concat(Arguments);
}

public sealed class PapyrusUnaryExpression : PapyrusExpression
{
    /// <summary><see cref="PapyrusTokenKind.Minus"/> or <see cref="PapyrusTokenKind.Not"/>.</summary>
    public PapyrusTokenKind Operator { get; internal set; }

    public PapyrusExpression Operand { get; internal set; } = null!;

    public override IEnumerable<PapyrusNode> Children
    {
        get { yield return Operand; }
    }
}

public sealed class PapyrusBinaryExpression : PapyrusExpression
{
    public PapyrusExpression Left { get; internal set; } = null!;

    public PapyrusTokenKind Operator { get; internal set; }

    public PapyrusExpression Right { get; internal set; } = null!;

    public override IEnumerable<PapyrusNode> Children
    {
        get
        {
            yield return Left;
            yield return Right;
        }
    }
}

/// <summary><c>expr as Type</c>.</summary>
public sealed class PapyrusCastExpression : PapyrusExpression
{
    public PapyrusExpression Operand { get; internal set; } = null!;

    public PapyrusTypeRef Type { get; internal set; } = null!;

    public override IEnumerable<PapyrusNode> Children
    {
        get
        {
            yield return Operand;
            yield return Type;
        }
    }
}

/// <summary><c>expr is Type</c>.</summary>
public sealed class PapyrusTypeCheckExpression : PapyrusExpression
{
    public PapyrusExpression Operand { get; internal set; } = null!;

    public PapyrusTypeRef Type { get; internal set; } = null!;

    public override IEnumerable<PapyrusNode> Children
    {
        get
        {
            yield return Operand;
            yield return Type;
        }
    }
}

/// <summary><c>new Type[size]</c>.</summary>
public sealed class PapyrusNewArrayExpression : PapyrusExpression
{
    public PapyrusTypeRef ElementType { get; internal set; } = null!;

    public PapyrusExpression Size { get; internal set; } = null!;

    public override IEnumerable<PapyrusNode> Children
    {
        get
        {
            yield return ElementType;
            yield return Size;
        }
    }
}

/// <summary><c>new StructType</c>.</summary>
public sealed class PapyrusNewStructExpression : PapyrusExpression
{
    public PapyrusTypeRef Type { get; internal set; } = null!;

    public override IEnumerable<PapyrusNode> Children
    {
        get { yield return Type; }
    }
}

/// <summary>Placeholder produced where an expression was required but could not be parsed.</summary>
/// <remarks>
/// Returning a node rather than null keeps every consumer free of null checks and keeps the tree
/// walkable for editor queries even in a file that is mid-edit and does not parse.
/// </remarks>
public sealed class PapyrusErrorExpression : PapyrusExpression
{
}
