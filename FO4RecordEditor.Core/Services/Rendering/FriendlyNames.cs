namespace FO4RecordEditor.Services.Rendering;

public static class FriendlyNames
{
    private static readonly Dictionary<string, string> _overrides = new(StringComparer.Ordinal)
    {

        ["TitleString"]              = "Name",

        ["Value"]                    = "Value (caps)",

        ["VirtualMachineAdapter"]    = "Scripts",
        ["VMAD"]                     = "Scripts",

        ["PickUpSound"]              = "Pick-Up Sound",
        ["PutDownSound"]             = "Put-Down Sound",

        ["CompareOperator"]          = "Operator",
        ["ParameterOneRecord"]       = "Parameter 1",
        ["ParameterTwoRecord"]       = "Parameter 2",
        ["ParameterOneNumber"]       = "Parameter 1 (Number)",
        ["ParameterTwoNumber"]       = "Parameter 2 (Number)",
        ["RunOnType"]                = "Run On",
        ["ComparisonValue"]          = "Comparison Value",

        ["CreatedObject"]            = "Created Object",
        ["CreatedObjectCount"]       = "Crafted Count",
        ["WorkbenchKeyword"]         = "Workbench",

        ["MenuDisplayObject"]        = "Menu Display Object",

        ["NPCOwner"]                 = "NPC Owner",
    };

    public static string Label(string raw)
    {
        if (_overrides.TryGetValue(raw, out var v)) return v;
        var s = raw.Length > 0 && raw[0] == '_' ? raw[1..] : raw;
        return SplitCamelCase(s);
    }

    public static string Singular(string plural)
    {
        var friendly = Label(plural);
        if (friendly.EndsWith("ies", StringComparison.Ordinal)) return friendly[..^3] + "y";
        if (friendly.Length > 1 && friendly.EndsWith("s", StringComparison.Ordinal)) return friendly[..^1];
        return friendly;
    }

    public static string LabelPath(string path)
    {
        if (path.Contains('.') || path.Contains('[')) return path;
        return Label(path);
    }

    private static string SplitCamelCase(string s)
    {
        if (string.IsNullOrEmpty(s) || s[0] == '[') return s;
        var sb = new System.Text.StringBuilder(s.Length + 8);
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (i > 0 && char.IsUpper(c))
            {
                char prev = s[i - 1];
                char next = i + 1 < s.Length ? s[i + 1] : '\0';

                bool case_a = char.IsLower(prev);

                bool case_b = char.IsUpper(prev)
                           && next != '\0' && char.IsLower(next)
                           && i >= 2 && char.IsUpper(s[i - 2]);
                if (case_a || case_b) sb.Append(' ');
            }
            sb.Append(c);
        }
        return sb.ToString();
    }
}
