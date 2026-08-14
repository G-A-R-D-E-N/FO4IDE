using System;
using System.Collections.Generic;

namespace FO4RecordEditor.Services.Graph;

public enum GraphSeverity
{
    Warning,
    Error,
}









public sealed record GraphDiagnostic
{
    public required string Code { get; init; }

    public required GraphSeverity Severity { get; init; }

    public required string Message { get; init; }


    public string? NodeId { get; init; }


    public string? PinId { get; init; }

    public string? WireId { get; init; }









    public IReadOnlyList<string> RelatedNodes { get; init; } = Array.Empty<string>();




    public int SourceLine { get; init; }


    public string? PapyrusCode { get; init; }

    public static GraphDiagnostic Error(string code, string message, string? nodeId = null, string? pinId = null) =>
        new() { Code = code, Severity = GraphSeverity.Error, Message = message, NodeId = nodeId, PinId = pinId };

    public static GraphDiagnostic Warning(string code, string message, string? nodeId = null, string? pinId = null) =>
        new() { Code = code, Severity = GraphSeverity.Warning, Message = message, NodeId = nodeId, PinId = pinId };

    public override string ToString()
    {
        var where = NodeId == null ? "" : $" [{NodeId}{(PinId == null ? "" : ":" + PinId)}]";
        return $"{Code} {Severity}: {Message}{where}";
    }
}









public static class GraphDiagnosticCodes
{

    public const string UnsupportedSchema = "GRA0001";
    public const string MalformedDocument = "GRA0002";


    public const string IncompatibleWireType = "GRA0010";
    public const string NarrowingWireNeedsCast = "GRA0011";
    public const string WireDirection = "GRA0012";
    public const string MultipleDataSources = "GRA0013";
    public const string MultipleExecSuccessors = "GRA0014";
    public const string JaggedArray = "GRA0015";
    public const string PinKindMismatch = "GRA0016";
    public const string DanglingWire = "GRA0017";


    public const string ExecCycle = "GRA0020";
    public const string DataCycle = "GRA0021";
    public const string UnreachableExec = "GRA0022";
    public const string UnstructuredFlow = "GRA0023";
    public const string NoEntryNodes = "GRA0024";
    public const string LoopExitOutsideLoop = "GRA0025";


    public const string MissingRequiredInput = "GRA0030";
    public const string UndeclaredReference = "GRA0031";
    public const string DuplicateVariableName = "GRA0032";
    public const string UnknownCallTarget = "GRA0033";
    public const string ArgumentCount = "GRA0034";
    public const string UnknownStructMember = "GRA0035";
    public const string UnknownNodeDefinition = "GRA0036";
    public const string DuplicateDeclaration = "GRA0037";
    public const string InvalidScriptHeader = "GRA0038";
    public const string UnconnectedSelf = "GRA0039";


    public const string NotAllPathsReturn = "GRA0040";
    public const string UseBeforeAssignment = "GRA0041";
    public const string ReturnValueMissing = "GRA0042";
    public const string ReturnValueUnexpected = "GRA0043";
    public const string LoopConditionFromLoopBody = "GRA0044";
    public const string OrphanNode = "GRA0045";


    public const string DuplicateNativeBinding = "GRA0050";
    public const string NativeArityUnsupported = "GRA0051";
    public const string UnmappedNativeType = "GRA0052";
    public const string NativeVersionMismatch = "GRA0053";
    public const string InvalidBindingName = "GRA0054";
    public const string StructOwnerMismatch = "GRA0055";
    public const string NoModules = "GRA0056";


    public const string InternalEmitterFault = "GRA0900";
}
