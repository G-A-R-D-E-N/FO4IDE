using System;
using System.Collections.Generic;

namespace FO4RecordEditor.Services.Graph;

public enum GraphSeverity
{
    Warning,
    Error,
}

/// <summary>
/// One problem, anchored to the node and pin that caused it.
/// </summary>
/// <remarks>
/// The node and pin are structured fields rather than text in the message, because the canvas paints
/// a node red by looking them up and the tests assert on them. A diagnostic that only named its
/// subject in prose would be unusable by both.
/// </remarks>
public sealed record GraphDiagnostic
{
    public required string Code { get; init; }

    public required GraphSeverity Severity { get; init; }

    public required string Message { get; init; }

    /// <summary>The node at fault, when one node is at fault.</summary>
    public string? NodeId { get; init; }

    /// <summary>The pin at fault, within <see cref="NodeId"/>.</summary>
    public string? PinId { get; init; }

    public string? WireId { get; init; }

    /// <summary>
    /// Other nodes implicated, in a meaningful order.
    /// </summary>
    /// <remarks>
    /// A cycle names every node on it in traversal order, and a definite-assignment failure names
    /// the producer as well as the consumer. Reporting only one end of either leaves the author
    /// looking for the other.
    /// </remarks>
    public IReadOnlyList<string> RelatedNodes { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Where this landed in generated source, set only on diagnostics mapped back from it.
    /// </summary>
    public int SourceLine { get; init; }

    /// <summary>The originating <c>PAP</c> code, when this was mapped back from the Papyrus compiler.</summary>
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

/// <summary>
/// The diagnostic codes the graph subsystem raises.
/// </summary>
/// <remarks>
/// Numbered in blocks by stage, mirroring the <c>PAP####</c> convention so a mixed list reads
/// unambiguously. A code is part of the contract once a test asserts on it, so codes are never
/// reused for a different meaning.
/// </remarks>
public static class GraphDiagnosticCodes
{
    // 0001 to 0009: loading a document.
    public const string UnsupportedSchema = "GRA0001";
    public const string MalformedDocument = "GRA0002";

    // 0010 to 0019: wires, as a structural matter.
    public const string IncompatibleWireType = "GRA0010";
    public const string NarrowingWireNeedsCast = "GRA0011";
    public const string WireDirection = "GRA0012";
    public const string MultipleDataSources = "GRA0013";
    public const string MultipleExecSuccessors = "GRA0014";
    public const string JaggedArray = "GRA0015";
    public const string PinKindMismatch = "GRA0016";
    public const string DanglingWire = "GRA0017";

    // 0020 to 0029: control flow.
    public const string ExecCycle = "GRA0020";
    public const string DataCycle = "GRA0021";
    public const string UnreachableExec = "GRA0022";
    public const string UnstructuredFlow = "GRA0023";
    public const string NoEntryNodes = "GRA0024";
    public const string LoopExitOutsideLoop = "GRA0025";

    // 0030 to 0039: names and references.
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

    // 0040 to 0049: dataflow analysis.
    public const string NotAllPathsReturn = "GRA0040";
    public const string UseBeforeAssignment = "GRA0041";
    public const string ReturnValueMissing = "GRA0042";
    public const string ReturnValueUnexpected = "GRA0043";
    public const string LoopConditionFromLoopBody = "GRA0044";
    public const string OrphanNode = "GRA0045";

    // 0050 to 0059: the F4SE binding surface.
    public const string DuplicateNativeBinding = "GRA0050";
    public const string NativeArityUnsupported = "GRA0051";
    public const string UnmappedNativeType = "GRA0052";
    public const string NativeVersionMismatch = "GRA0053";
    public const string InvalidBindingName = "GRA0054";
    public const string StructOwnerMismatch = "GRA0055";
    public const string NoModules = "GRA0056";

    // 0900: the emitter got somewhere it should not have.
    public const string InternalEmitterFault = "GRA0900";
}
