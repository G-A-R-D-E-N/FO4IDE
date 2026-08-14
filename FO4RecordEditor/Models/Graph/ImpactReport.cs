namespace FO4RecordEditor.Models;

public sealed record ImpactReport(
    string TargetFormKey, string TargetEditorID,
    IReadOnlyList<Reference> InboundReferences,
    IReadOnlyList<RecordEntry> AffectedRecords);
