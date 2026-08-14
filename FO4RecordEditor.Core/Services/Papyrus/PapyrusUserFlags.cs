using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FO4RecordEditor.Services.Papyrus;

/// <summary>
/// The user-flag table a <c>.pex</c> carries: flag name to bit index.
/// </summary>
/// <remarks>
/// User flags -- <c>Hidden</c>, <c>Conditional</c>, <c>Mandatory</c>, <c>CollapsedOnRef</c>,
/// <c>CollapsedOnBase</c>, <c>Default</c> -- are not language keywords. They are declared by
/// <c>Institute_Papyrus_Flags.flg</c>, which ships with the Creation Kit and is not in the game
/// archives. The parser accepts any identifier in a flag position precisely so it can read scripts
/// on a machine with no CK, but a back end has to turn those names into bits.
/// <para>
/// <b>This is what takes the CK off the critical path for compiling.</b> The file is 60 lines of
/// declarations, so it is parsed when it is available and otherwise supplied from
/// <see cref="Fallout4Default"/> -- which is not a guess: it is the table every one of the 1,496
/// real <c>.pex</c> on the development machine carries, and it agrees bit for bit with the shipped
/// <c>.flg</c>. Emission order differs between real files (the CK writes its hash order), so this
/// emits ascending bit order, which is deterministic.
/// </para>
/// <para>
/// A composite declaration (<c>Flag Collapsed CollapsedOnRef &amp; CollapsedOnBase</c>) has no bit
/// of its own -- the file says so in its own header comment: "This flag will NOT appear in the
/// object, only the ones it is made up of" -- so it expands to a mask instead.
/// </para>
/// </remarks>
public sealed class PapyrusUserFlagTable
{
    private readonly Dictionary<string, uint> _masks = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<PexUserFlag> _flags = new();

    /// <summary>The flags to write into a <c>.pex</c>, ascending by bit.</summary>
    public IReadOnlyList<PexUserFlag> Flags => _flags;

    /// <summary>The bit mask a written flag name contributes, or 0 for a name this table does not know.</summary>
    public uint MaskFor(string flagName) =>
        _masks.TryGetValue(flagName, out var mask) ? mask : 0u;

    public bool Knows(string flagName) => _masks.ContainsKey(flagName);

    /// <summary>The combined mask of every user flag in <paramref name="written"/>.</summary>
    /// <remarks>
    /// Language keywords land in the same list as user flags on an AST declaration, so they are
    /// simply names this table does not know and contribute nothing.
    /// </remarks>
    public uint MaskFor(IEnumerable<string> written)
    {
        uint mask = 0;
        foreach (var name in written) mask |= MaskFor(name);
        return mask;
    }

    private void Declare(string name, byte bit)
    {
        _masks[name] = 1u << bit;
        if (_flags.All(f => f.Index != bit)) _flags.Add(new PexUserFlag { Name = name.ToLowerInvariant(), Index = bit });
    }

    private void DeclareComposite(string name, uint mask) => _masks[name] = mask;

    private void Sort() => _flags.Sort((a, b) => a.Index.CompareTo(b.Index));

    /// <summary>
    /// The Fallout 4 table, as carried by every real <c>.pex</c> measured and as declared by the
    /// shipped <c>Institute_Papyrus_Flags.flg</c>.
    /// </summary>
    public static PapyrusUserFlagTable Fallout4Default()
    {
        var table = new PapyrusUserFlagTable();
        table.Declare("Hidden", 0);
        table.Declare("Conditional", 1);
        table.Declare("Default", 2);
        table.Declare("CollapsedOnRef", 3);
        table.Declare("CollapsedOnBase", 4);
        table.Declare("Mandatory", 5);
        table.DeclareComposite("Collapsed", (1u << 3) | (1u << 4));
        table.Sort();
        return table;
    }

    /// <summary>Parses an <c>Institute_Papyrus_Flags.flg</c>, falling back to nothing on a bad file.</summary>
    /// <remarks>
    /// The grammar is three forms, given in the file's own header: <c>Flag name index</c>, the same
    /// with a brace-delimited list of the declaration kinds it may appear on, and
    /// <c>Flag name child (&amp; child)+</c> for a composite. Only the name-to-bit mapping matters
    /// to a writer, so the allowed-kinds list is skipped rather than enforced -- validating it would
    /// reject nothing a real script does and would fire on flags a future game adds.
    /// </remarks>
    public static PapyrusUserFlagTable Parse(string text)
    {
        var table = new PapyrusUserFlagTable();
        var composites = new List<(string name, string[] children)>();

        foreach (var raw in StripComments(text).Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;

            // "{" opens the allowed-kinds list, which is not part of the mapping.
            int brace = line.IndexOf('{');
            if (brace >= 0) line = line[..brace].Trim();
            if (line.Length == 0) continue;

            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3 || !parts[0].Equals("Flag", StringComparison.OrdinalIgnoreCase)) continue;

            if (byte.TryParse(parts[2], out var bit) && bit <= 31)
            {
                table.Declare(parts[1], bit);
                continue;
            }

            var children = parts.Skip(2).Where(p => p != "&").ToArray();
            if (children.Length > 0) composites.Add((parts[1], children));
        }

        foreach (var (name, children) in composites)
        {
            uint mask = 0;
            foreach (var child in children) mask |= table.MaskFor(child);
            if (mask != 0) table.DeclareComposite(name, mask);
        }

        table.Sort();
        return table;
    }

    /// <summary>Parses the file at <paramref name="path"/>, or returns the built-in table.</summary>
    public static PapyrusUserFlagTable FromFileOrDefault(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return Fallout4Default();
        try
        {
            if (!File.Exists(path)) return Fallout4Default();
            var table = Parse(File.ReadAllText(path));
            return table.Flags.Count == 0 ? Fallout4Default() : table;
        }
        catch (IOException)
        {
            return Fallout4Default();
        }
        catch (UnauthorizedAccessException)
        {
            return Fallout4Default();
        }
    }

    /// <summary>The first <c>Institute_Papyrus_Flags.flg</c> under any of <paramref name="roots"/>.</summary>
    public static string? FindFlagFile(IEnumerable<string> roots)
    {
        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root)) continue;
            var direct = Path.Combine(root, "Institute_Papyrus_Flags.flg");
            if (File.Exists(direct)) return direct;
            foreach (var hit in PapyrusFileWalk.EnumerateFiles(root, "Institute_Papyrus_Flags.flg")) return hit;
        }
        return null;
    }

    private static string StripComments(string text)
    {
        var sb = new System.Text.StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '/' && i + 1 < text.Length && text[i + 1] == '/')
            {
                while (i < text.Length && text[i] != '\n') i++;
                if (i < text.Length) sb.Append('\n');
                continue;
            }
            if (text[i] == '/' && i + 1 < text.Length && text[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < text.Length && !(text[i] == '*' && text[i + 1] == '/')) i++;
                i++;
                continue;
            }
            sb.Append(text[i]);
        }
        return sb.ToString();
    }
}
