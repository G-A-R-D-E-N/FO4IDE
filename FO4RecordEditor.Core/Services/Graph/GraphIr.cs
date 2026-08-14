using System;
using System.Collections.Generic;

namespace FO4RecordEditor.Services.Graph;

/// <summary>
/// Anything in the intermediate representation, carrying where it came from.
/// </summary>
/// <remarks>
/// Provenance is on the base type rather than added where convenient, so the source map is complete
/// by construction. An IR node that forgot to record its origin would produce a compiler error the
/// canvas could not attribute to anything.
/// </remarks>
public abstract record IrNode
{
    public string? NodeId { get; init; }

    public string? PinId { get; init; }
}

// ---- expressions ----------------------------------------------------------------------------

/// <summary>A value.</summary>
public abstract record IrExpression : IrNode
{
    /// <summary>The written type this evaluates to, for diagnostics and for cast decisions.</summary>
    public string TypeName { get; init; } = "";

    public bool IsArray { get; init; }
}

/// <summary>A literal, already rendered as the source text it will be emitted as.</summary>
public sealed record IrLiteral(string Text) : IrExpression;

/// <summary>A local, parameter, variable or property named directly.</summary>
public sealed record IrName(string Name) : IrExpression;

public sealed record IrSelf : IrExpression;

public sealed record IrParent : IrExpression;

/// <summary>Dotted access: a property or a struct member.</summary>
public sealed record IrMember(IrExpression Target, string Name) : IrExpression;

/// <summary>Array indexing.</summary>
public sealed record IrIndex(IrExpression Target, IrExpression Index) : IrExpression;

/// <summary>One argument, named when a preceding optional was skipped.</summary>
public sealed record IrArgument(IrExpression Value, string? Name = null);

/// <summary>A call, with or without a receiver.</summary>
public sealed record IrCall(
    IrExpression? Receiver,
    string Name,
    IReadOnlyList<IrArgument> Arguments,
    bool IsGlobal = false) : IrExpression;

public sealed record IrUnary(string Operator, IrExpression Operand) : IrExpression;

public sealed record IrBinary(string Operator, IrExpression Left, IrExpression Right) : IrExpression;

/// <summary>An explicit conversion, which the graph always requires a node for.</summary>
public sealed record IrCast(IrExpression Value, string TargetType, bool TargetIsArray) : IrExpression;

public sealed record IrTypeCheck(IrExpression Value, string TargetType, bool TargetIsArray) : IrExpression;

public sealed record IrNewArray(string ElementType, IrExpression Size) : IrExpression;

public sealed record IrNewStruct(string StructName) : IrExpression;

// ---- statements -----------------------------------------------------------------------------

public abstract record IrStatement : IrNode;

/// <summary>A local declaration, optionally with an initialiser.</summary>
public sealed record IrDefine(string Name, string TypeName, bool IsArray, IrExpression? Value)
    : IrStatement;

public sealed record IrAssign(IrExpression Target, IrExpression Value) : IrStatement;

/// <summary>A call evaluated for its effect, with the result thrown away.</summary>
public sealed record IrExpressionStatement(IrExpression Expression) : IrStatement;

public sealed record IrReturn(IrExpression? Value) : IrStatement;

/// <summary>One arm of an if chain.</summary>
public sealed record IrBranch(IrExpression Condition, IReadOnlyList<IrStatement> Body);

/// <summary>An if, elseif chain and optional else, already folded.</summary>
public sealed record IrIf(IReadOnlyList<IrBranch> Branches, IReadOnlyList<IrStatement>? Else)
    : IrStatement;

public sealed record IrWhile(IrExpression Condition, IReadOnlyList<IrStatement> Body) : IrStatement;

// ---- declarations ---------------------------------------------------------------------------

public sealed record IrParameter(string Name, string TypeName, bool IsArray, string? DefaultText);

/// <summary>A hoisted local.</summary>
public sealed record IrLocal(string Name, string TypeName, bool IsArray);

/// <summary>One emitted function or event.</summary>
public sealed record IrCallable
{
    public required string Name { get; init; }

    public required string EntryNodeId { get; init; }

    public bool IsEvent { get; init; }

    public bool IsGlobal { get; init; }

    /// <summary>The receiver type for a remote event, which is written before the event name.</summary>
    public string? RemoteObjectType { get; init; }

    public string? StateName { get; init; }

    public string? ReturnTypeName { get; init; }

    public bool ReturnIsArray { get; init; }

    public IReadOnlyList<IrParameter> Parameters { get; init; } = Array.Empty<IrParameter>();

    /// <summary>
    /// Locals, hoisted to the top of the body.
    /// </summary>
    /// <remarks>
    /// Function scoped rather than block scoped, and that is not a simplification. A value produced
    /// inside one branch and read after the merge has to outlive the branch, and hoisting makes the
    /// emitted scoping match the definite-assignment analysis exactly. It also means there are no
    /// nested scopes to mangle names across, which is the honest answer to that problem.
    /// </remarks>
    public IReadOnlyList<IrLocal> Locals { get; init; } = Array.Empty<IrLocal>();

    public IReadOnlyList<IrStatement> Body { get; init; } = Array.Empty<IrStatement>();
}

public sealed record IrProperty(
    string Name, string TypeName, bool IsArray, IReadOnlyList<string> Flags, string? InitialText);

public sealed record IrVariable(
    string Name, string TypeName, bool IsArray, IReadOnlyList<string> Flags, string? InitialText);

public sealed record IrStructMember(string Name, string TypeName, bool IsArray);

public sealed record IrStruct(string Name, IReadOnlyList<IrStructMember> Members);

/// <summary>
/// A whole script, ready to print.
/// </summary>
/// <remarks>
/// Mirrors the Papyrus syntax tree one to one, minus the internal setters that make that tree
/// unbuildable from outside its own assembly. It models nothing the language cannot express: there
/// is no for, no break and no goto, because Papyrus has none of them.
/// </remarks>
public sealed record IrScript
{
    public required string Name { get; init; }

    public string? Extends { get; init; }

    public IReadOnlyList<string> Flags { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Imports { get; init; } = Array.Empty<string>();

    public string? DocComment { get; init; }

    public IReadOnlyList<IrStruct> Structs { get; init; } = Array.Empty<IrStruct>();

    public IReadOnlyList<IrVariable> Variables { get; init; } = Array.Empty<IrVariable>();

    public IReadOnlyList<IrProperty> Properties { get; init; } = Array.Empty<IrProperty>();

    public IReadOnlyList<IrCallable> Callables { get; init; } = Array.Empty<IrCallable>();

    /// <summary>The state the script starts in, written as <c>Auto State</c>.</summary>
    public string? AutoState { get; init; }

    /// <summary>Custom events this script declares, written as <c>CustomEvent Name</c>.</summary>
    public IReadOnlyList<string> CustomEvents { get; init; } = Array.Empty<string>();
}
