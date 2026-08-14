using System.Collections.Generic;

namespace FO4RecordEditor.Models;

/// <summary>
/// A field-level, xEdit-style view of one conflicting record: every plugin that touches the
/// record becomes a column, every field a row. <see cref="ConflictFieldRow.Differs"/> marks the
/// rows where the plugins actually disagree (what truly conflicts).
/// </summary>
public sealed class ConflictMatrix
{
    public required string FormKey { get; init; }
    public required string EditorID { get; init; }
    public required string Type { get; init; }
    /// <summary>Plugins in load order; the last one is the current winner.</summary>
    public required IReadOnlyList<string> Plugins { get; init; }
    public required string Winner { get; init; }
    public required IReadOnlyList<ConflictFieldRow> Rows { get; init; }

    /// <summary>Record-level ConflictAll rollup across all rows:
    /// "onlyone" | "noconflict" | "override" | "conflict" | "critical".</summary>
    public string Level { get; init; } = "noconflict";
}

public sealed class ConflictFieldRow
{
    public required string Field { get; init; }
    public required string DisplayLabel { get; init; }
    public required int Level { get; init; }

    /// <summary>Value per plugin, parallel to <see cref="ConflictMatrix.Plugins"/>. "" = not set.</summary>
    public required IReadOnlyList<string> Values { get; init; }
    public required bool Differs { get; init; }

    /// <summary>Per-plugin conflict status, parallel to <see cref="Values"/>:
    /// "notdefined" | "master" | "identical" | "win" | "override" | "lose" | "only".</summary>
    public IReadOnlyList<string> Statuses { get; init; } = System.Array.Empty<string>();

    /// <summary>Row-level severity: "none" | "override" | "conflict" | "critical".</summary>
    public string Severity { get; init; } = "none";

    /// <summary>This row has a one-line summary (e.g. a condition or component) AND deeper sub-field
    /// rows beneath it -- the grid collapses those children by default and expands them on click,
    /// like double-clicking an array element in xEdit.</summary>
    public bool IsSummary { get; init; }
    /// <summary>True if any deeper rows belong to this one (so the grid shows an expand chevron).</summary>
    public bool HasChildren { get; init; }

    /// <summary>How a value cell edits: "Text", "Bool" (checkbox), or "Enum" (dropdown).</summary>
    public string EditKind { get; init; } = "Text";
    /// <summary>Dropdown options when EditKind == "Enum".</summary>
    public IReadOnlyList<string>? EnumOptions { get; init; }

    /// <summary>What kind of change this row represents: "Value" | "Flag" | "FormID".
    /// Classified here rather than inferred in the UI, so the Conflicts view's sub-tab counts and
    /// the donut cannot drift from what the grid actually shows.</summary>
    public string Kind { get; init; } = "Value";

    /// <summary>The top-level subrecord this row belongs under (its first path segment), used as
    /// the collapsible group header in the Conflicts view. Empty for top-level rows.</summary>
    public string Group { get; init; } = "";

    /// <summary>Friendly form of <see cref="Group"/> for the group header.</summary>
    public string GroupLabel { get; init; } = "";

    /// <summary>Short display label for the picker chip when EditKind == "Ref" (e.g. "Keyword").</summary>
    public string? RefType { get; init; }
    /// <summary>Comma-separated concrete record types the link may target (the picker's filter set).</summary>
    public string? RefTypes { get; init; }

    // Bindable accessors for up to 8 plugin columns (DataGrid columns are generated to match).
    public string V0 => Get(0); public string V1 => Get(1); public string V2 => Get(2); public string V3 => Get(3);
    public string V4 => Get(4); public string V5 => Get(5); public string V6 => Get(6); public string V7 => Get(7);
    private string Get(int i) => i < Values.Count ? Values[i] : "";
}
