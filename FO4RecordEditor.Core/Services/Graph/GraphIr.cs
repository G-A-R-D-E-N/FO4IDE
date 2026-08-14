using System;
using System.Collections.Generic;

namespace FO4RecordEditor.Services.Graph;









public abstract record IrNode
{
    public string? NodeId { get; init; }

    public string? PinId { get; init; }
}




public abstract record IrExpression : IrNode
{

    public string TypeName { get; init; } = "";

    public bool IsArray { get; init; }
}


public sealed record IrLiteral(string Text) : IrExpression;


public sealed record IrName(string Name) : IrExpression;

public sealed record IrSelf : IrExpression;

public sealed record IrParent : IrExpression;


public sealed record IrMember(IrExpression Target, string Name) : IrExpression;


public sealed record IrIndex(IrExpression Target, IrExpression Index) : IrExpression;


public sealed record IrArgument(IrExpression Value, string? Name = null);


public sealed record IrCall(
    IrExpression? Receiver,
    string Name,
    IReadOnlyList<IrArgument> Arguments,
    bool IsGlobal = false) : IrExpression;

public sealed record IrUnary(string Operator, IrExpression Operand) : IrExpression;

public sealed record IrBinary(string Operator, IrExpression Left, IrExpression Right) : IrExpression;


public sealed record IrCast(IrExpression Value, string TargetType, bool TargetIsArray) : IrExpression;

public sealed record IrTypeCheck(IrExpression Value, string TargetType, bool TargetIsArray) : IrExpression;

public sealed record IrNewArray(string ElementType, IrExpression Size) : IrExpression;

public sealed record IrNewStruct(string StructName) : IrExpression;



public abstract record IrStatement : IrNode;


public sealed record IrDefine(string Name, string TypeName, bool IsArray, IrExpression? Value)
    : IrStatement;

public sealed record IrAssign(IrExpression Target, IrExpression Value) : IrStatement;


public sealed record IrExpressionStatement(IrExpression Expression) : IrStatement;

public sealed record IrReturn(IrExpression? Value) : IrStatement;


public sealed record IrBranch(IrExpression Condition, IReadOnlyList<IrStatement> Body);


public sealed record IrIf(IReadOnlyList<IrBranch> Branches, IReadOnlyList<IrStatement>? Else)
    : IrStatement;

public sealed record IrWhile(IrExpression Condition, IReadOnlyList<IrStatement> Body) : IrStatement;



public sealed record IrParameter(string Name, string TypeName, bool IsArray, string? DefaultText);


public sealed record IrLocal(string Name, string TypeName, bool IsArray);


public sealed record IrCallable
{
    public required string Name { get; init; }

    public required string EntryNodeId { get; init; }

    public bool IsEvent { get; init; }

    public bool IsGlobal { get; init; }


    public string? RemoteObjectType { get; init; }

    public string? StateName { get; init; }

    public string? ReturnTypeName { get; init; }

    public bool ReturnIsArray { get; init; }

    public IReadOnlyList<IrParameter> Parameters { get; init; } = Array.Empty<IrParameter>();










    public IReadOnlyList<IrLocal> Locals { get; init; } = Array.Empty<IrLocal>();

    public IReadOnlyList<IrStatement> Body { get; init; } = Array.Empty<IrStatement>();
}

public sealed record IrProperty(
    string Name, string TypeName, bool IsArray, IReadOnlyList<string> Flags, string? InitialText);

public sealed record IrVariable(
    string Name, string TypeName, bool IsArray, IReadOnlyList<string> Flags, string? InitialText);

public sealed record IrStructMember(string Name, string TypeName, bool IsArray);

public sealed record IrStruct(string Name, IReadOnlyList<IrStructMember> Members);









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


    public string? AutoState { get; init; }


    public IReadOnlyList<string> CustomEvents { get; init; } = Array.Empty<string>();
}
