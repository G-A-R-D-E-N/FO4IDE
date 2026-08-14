namespace FO4RecordEditor.Models;
public sealed record PluginError(
    ErrorSeverity Severity, ErrorCategory Category, string Plugin,
    string FormKey, string Field, string Description, bool FixAvailable);
