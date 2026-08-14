using System.Collections.Generic;
using System.Linq;

namespace FO4RecordEditor.Services.Papyrus;

public abstract class PapyrusNode
{
    public PapyrusSpan Span { get; internal set; }

    public virtual IEnumerable<PapyrusNode> Children => System.Array.Empty<PapyrusNode>();

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

    public override string ToString() => IsArray ? Name + "[]" : Name;
}

public abstract class PapyrusDeclaration : PapyrusNode
{
    public string Name { get; internal set; } = string.Empty;

    public PapyrusSpan NameSpan { get; internal set; }

    public List<string> Flags { get; } = new();

    public string? Documentation { get; internal set; }

    public bool HasFlag(string flag) =>
        Flags.Any(f => string.Equals(f, flag, System.StringComparison.OrdinalIgnoreCase));

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

    public ParameterSemantic Semantic => Type?.Name switch
    {
        "scripteventname" => ParameterSemantic.ScriptEventName,
        "customeventname" => ParameterSemantic.CustomEventName,
        "structvarname" => ParameterSemantic.StructVarName,
        _ => ParameterSemantic.None,
    };

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

public abstract class PapyrusCallableDecl : PapyrusDeclaration
{
    public List<PapyrusParameter> Parameters { get; } = new();

    public List<PapyrusStatement> Body { get; } = new();

    public bool IsNative { get; internal set; }

    public string? StateName { get; internal set; }

    public override IEnumerable<PapyrusNode> Children => Parameters.Cast<PapyrusNode>().Concat(Body);

    protected string ParameterList => string.Join(", ", Parameters.Select(p => p.Signature));
}

public sealed class PapyrusFunctionDecl : PapyrusCallableDecl
{

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

    Auto,

    AutoReadOnly,

    Full,
}

public sealed class PapyrusPropertyDecl : PapyrusDeclaration
{
    public PapyrusTypeRef Type { get; internal set; } = null!;

    public PapyrusPropertyKind Kind { get; internal set; }

    public PapyrusExpression? Initializer { get; internal set; }

    public PapyrusFunctionDecl? Getter { get; internal set; }

    public PapyrusFunctionDecl? Setter { get; internal set; }

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

public sealed class PapyrusScript : PapyrusDeclaration
{

    public string? FilePath { get; internal set; }

    public string? Extends { get; internal set; }

    public PapyrusSpan ExtendsSpan { get; internal set; }

    public List<PapyrusImport> Imports { get; } = new();

    public List<PapyrusVariableDecl> Variables { get; } = new();

    public List<PapyrusStructDecl> Structs { get; } = new();

    public List<PapyrusCustomEventDecl> CustomEvents { get; } = new();

    public List<PapyrusPropertyDecl> Properties { get; } = new();

    public List<PapyrusGroupDecl> Groups { get; } = new();

    public List<PapyrusStateDecl> States { get; } = new();

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

            .Concat(Properties.Where(p => p.GroupName == null))
            .Concat(Functions)
            .Concat(Events)
            .Concat(States);

    public override string Signature =>
        Extends == null ? $"ScriptName {Name}" : $"ScriptName {Name} extends {Extends}";
}

public abstract class PapyrusStatement : PapyrusNode
{
}

public sealed class PapyrusDefineStatement : PapyrusStatement
{
    public PapyrusTypeRef Type { get; internal set; } = null!;

    public string Name { get; internal set; } = string.Empty;

    public PapyrusSpan NameSpan { get; internal set; }

    public PapyrusExpression? Initializer { get; internal set; }

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

public sealed class PapyrusIfBranch : PapyrusNode
{
    public PapyrusExpression Condition { get; internal set; } = null!;

    public List<PapyrusStatement> Body { get; } = new();

    public override IEnumerable<PapyrusNode> Children =>
        new PapyrusNode[] { Condition }.Concat(Body);
}

public sealed class PapyrusIfStatement : PapyrusStatement
{

    public List<PapyrusIfBranch> Branches { get; } = new();

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

    public string Text { get; internal set; } = string.Empty;
}

public sealed class PapyrusIdentifierExpression : PapyrusExpression
{
    public string Name { get; internal set; } = string.Empty;
}

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

    public PapyrusExpression Callee { get; internal set; } = null!;

    public List<PapyrusArgument> Arguments { get; } = new();

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

public sealed class PapyrusNewStructExpression : PapyrusExpression
{
    public PapyrusTypeRef Type { get; internal set; } = null!;

    public override IEnumerable<PapyrusNode> Children
    {
        get { yield return Type; }
    }
}

public sealed class PapyrusErrorExpression : PapyrusExpression
{
}
