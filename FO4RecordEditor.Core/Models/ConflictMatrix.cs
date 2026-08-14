using System.Collections.Generic;

namespace FO4RecordEditor.Models;






public sealed class ConflictMatrix
{
    public required string FormKey { get; init; }
    public required string EditorID { get; init; }
    public required string Type { get; init; }

    public required IReadOnlyList<string> Plugins { get; init; }
    public required string Winner { get; init; }
    public required IReadOnlyList<ConflictFieldRow> Rows { get; init; }



    public string Level { get; init; } = "noconflict";
}

public sealed class ConflictFieldRow
{
    public required string Field { get; init; }
    public required string DisplayLabel { get; init; }
    public required int Level { get; init; }


    public required IReadOnlyList<string> Values { get; init; }
    public required bool Differs { get; init; }



    public IReadOnlyList<string> Statuses { get; init; } = System.Array.Empty<string>();


    public string Severity { get; init; } = "none";




    public bool IsSummary { get; init; }

    public bool HasChildren { get; init; }


    public string EditKind { get; init; } = "Text";

    public IReadOnlyList<string>? EnumOptions { get; init; }




    public string Kind { get; init; } = "Value";



    public string Group { get; init; } = "";


    public string GroupLabel { get; init; } = "";


    public string? RefType { get; init; }

    public string? RefTypes { get; init; }


    public string V0 => Get(0); public string V1 => Get(1); public string V2 => Get(2); public string V3 => Get(3);
    public string V4 => Get(4); public string V5 => Get(5); public string V6 => Get(6); public string V7 => Get(7);
    private string Get(int i) => i < Values.Count ? Values[i] : "";
}
