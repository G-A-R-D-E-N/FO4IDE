using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FO4RecordEditor.Services.Papyrus;

public sealed class PapyrusUserFlagTable
{
    private readonly Dictionary<string, uint> _masks = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<PexUserFlag> _flags = new();

    public IReadOnlyList<PexUserFlag> Flags => _flags;

    public uint MaskFor(string flagName) =>
        _masks.TryGetValue(flagName, out var mask) ? mask : 0u;

    public bool Knows(string flagName) => _masks.ContainsKey(flagName);

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

    public static PapyrusUserFlagTable Parse(string text)
    {
        var table = new PapyrusUserFlagTable();
        var composites = new List<(string name, string[] children)>();

        foreach (var raw in StripComments(text).Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;

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
