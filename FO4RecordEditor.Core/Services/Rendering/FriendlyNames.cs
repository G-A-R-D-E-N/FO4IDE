namespace FO4RecordEditor.Services.Rendering;

/// <summary>
/// Converts raw Mutagen property names to human-friendly display labels.
/// <para>
/// Most names are handled automatically by <see cref="SplitCamelCase"/>:
///   "AttackDamage" → "Attack Damage", "AIData" → "AI Data",
///   "NPCAttackSound" → "NPC Attack Sound", "HTMLParser" → "HTML Parser",
///   "SoundIDs" → "Sound IDs", "HasDistantLOD" → "Has Distant LOD".
/// </para>
/// <para>
/// The override dictionary holds only entries where the desired label differs from the
/// auto-split result (semantic renames, unit annotations, or fixed abbreviations).
/// </para>
/// </summary>
public static class FriendlyNames
{
    private static readonly Dictionary<string, string> _overrides = new(StringComparer.Ordinal)
    {
        // ── Semantic renames (auto-split gives wrong/confusing result) ─────────────────────────────
        // Mutagen names FormList's FULL subrecord "TitleString"; users know it as "Name".
        ["TitleString"]              = "Name",
        // Item sell price -- disambiguate from the generic word "Value" used in conditions.
        ["Value"]                    = "Value (caps)",
        // Papyrus scripting subrecord appears as VirtualMachineAdapter in reflection.
        ["VirtualMachineAdapter"]    = "Scripts",
        ["VMAD"]                     = "Scripts",

        // ── Sound fields with hyphens ─────────────────────────────────────────────────────────────
        ["PickUpSound"]              = "Pick-Up Sound",
        ["PutDownSound"]             = "Put-Down Sound",

        // ── Condition parameters → shorter names ──────────────────────────────────────────────────
        ["CompareOperator"]          = "Operator",
        ["ParameterOneRecord"]       = "Parameter 1",
        ["ParameterTwoRecord"]       = "Parameter 2",
        ["ParameterOneNumber"]       = "Parameter 1 (Number)",
        ["ParameterTwoNumber"]       = "Parameter 2 (Number)",
        ["RunOnType"]                = "Run On",
        ["ComparisonValue"]          = "Comparison Value",

        // ── Crafting ──────────────────────────────────────────────────────────────────────────────
        ["CreatedObject"]            = "Created Object",
        ["CreatedObjectCount"]       = "Crafted Count",
        ["WorkbenchKeyword"]         = "Workbench",

        // ── Menu / UI ─────────────────────────────────────────────────────────────────────────────
        ["MenuDisplayObject"]        = "Menu Display Object",

        // ── NPC-specific ──────────────────────────────────────────────────────────────────────────
        ["NPCOwner"]                 = "NPC Owner",
    };

    /// <summary>
    /// Returns a human-friendly label. Checks the semantic override dictionary first;
    /// otherwise auto-splits CamelCase. Leading underscores are stripped
    /// ("_HasData" → "Has Data").
    /// </summary>
    public static string Label(string raw)
    {
        if (_overrides.TryGetValue(raw, out var v)) return v;
        var s = raw.Length > 0 && raw[0] == '_' ? raw[1..] : raw;
        return SplitCamelCase(s);
    }

    /// <summary>
    /// Converts a collection property name to the singular form of its friendly label.
    /// Used for array-entry row labels: "Items" → "Item", "Conditions" → "Condition",
    /// "Properties" → "Property".
    /// </summary>
    public static string Singular(string plural)
    {
        var friendly = Label(plural);
        if (friendly.EndsWith("ies", StringComparison.Ordinal)) return friendly[..^3] + "y";
        if (friendly.Length > 1 && friendly.EndsWith("s", StringComparison.Ordinal)) return friendly[..^1];
        return friendly;
    }

    /// <summary>Relabel only a plain property name; leave dotted paths and list indices
    /// ("Components[0].Component", "[0]") structurally intact so edit paths still resolve.</summary>
    public static string LabelPath(string path)
    {
        if (path.Contains('.') || path.Contains('[')) return path;
        return Label(path);
    }

    /// <summary>
    /// Splits a PascalCase / CamelCase identifier into space-separated words.
    /// Handles acronyms by looking two characters back:
    /// <list type="bullet">
    ///   <item>"AttackDamage"   → "Attack Damage"    (lowercase→UPPER = new word)</item>
    ///   <item>"AIData"         → "AI Data"           (UPPER UPPER lower: prev-2 is UPPER → split)</item>
    ///   <item>"NPCAttackSound" → "NPC Attack Sound"  (run of 3+ uppercase then word)</item>
    ///   <item>"SoundIDs"       → "Sound IDs"         (prev-2 is lowercase → don't split mid-acronym)</item>
    ///   <item>"HasDistantLOD"  → "Has Distant LOD"   (trailing acronym, next='\0' → no split)</item>
    ///   <item>"HTMLParser"     → "HTML Parser"        (acronym end: prev-2 is UPPER → split before P)</item>
    /// </list>
    /// </summary>
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
                // (a) lowercase → UPPER: straightforward new word.
                bool case_a = char.IsLower(prev);
                // (b) UPPER UPPER→lower: we're at the LAST char of an acronym before a new word
                //     (e.g. 'P' in "HTMLParser" or 'D' in "AIData").
                //     Requires s[i-2] to also be uppercase so we know there IS a preceding acronym
                //     run -- this naturally excludes 2-char sequences like "ID" in "SoundIDs" where
                //     s[i-2] ('d') is lowercase (we're in the middle of the acronym, not the end).
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
