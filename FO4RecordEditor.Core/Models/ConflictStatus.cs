namespace FO4RecordEditor.Models;

/// <summary>Per-record conflict state used to colour the Explorer tree (xEdit-style).</summary>
public enum ConflictStatus
{
    None,             // not in conflict
    ConflictWinner,   // multiple plugins touch this record and THIS one wins (orange)
    ConflictLoser     // overridden by a later plugin -- this version is hidden (red)
}
