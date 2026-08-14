using System.IO;
using System.Linq;
using System.Text;

namespace FO4RecordEditor.Services;








public static class ModInspectService
{
    private static readonly (string Category, string[] Extensions)[] ExtensionCategories =
    {
        ("meshes", new[] { ".nif" }),
        ("textures", new[] { ".dds" }),
        ("materials", new[] { ".bgsm", ".bgem" }),
        ("animations", new[] { ".hkx" }),
        ("sounds", new[] { ".wav", ".xwm" }),
        ("scripts_pex", new[] { ".pex" }),
        ("scripts_psc", new[] { ".psc" }),
        ("plugins", new[] { ".esp", ".esm", ".esl" }),
        ("archives", new[] { ".ba2", ".bsa" }),
    };

    public static string CatalogFolder(string modPath)
    {
        if (string.IsNullOrWhiteSpace(modPath)) return "Provide a mod folder path.";
        if (!Directory.Exists(modPath)) return ToolError.Fail($"Folder not found: '{modPath}'.");

        var counts = ExtensionCategories.ToDictionary(
            c => c.Category,
            c => (count: 0, dirs: new SortedSet<string>(StringComparer.OrdinalIgnoreCase)),
            StringComparer.Ordinal);

        int voiceCount = 0;
        var voiceTypes = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        int animDataCount = 0;
        var animDataDirs = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        int totalFiles = 0, otherCount = 0;

        IEnumerable<string> files;
        try { files = Directory.EnumerateFiles(modPath, "*", SearchOption.AllDirectories); }
        catch (Exception ex) { return ToolError.Fail($"Could not walk '{modPath}': {ex.Message}"); }

        foreach (var fullPath in files)
        {
            var fileName = Path.GetFileName(fullPath);
            if (string.Equals(fileName, "inspection_report.json", StringComparison.OrdinalIgnoreCase)) continue;

            var relPath = Path.GetRelativePath(modPath, fullPath);
            var relParts = relPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);


            if (relParts.Length >= 2 &&
                string.Equals(relParts[0], "SCRIPTS", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(relParts[1], "SOURCE", StringComparison.OrdinalIgnoreCase))
                continue;

            totalFiles++;
            var ext = Path.GetExtension(fullPath);

            if (string.Equals(ext, ".txt", StringComparison.OrdinalIgnoreCase) &&
                relParts.Any(p => string.Equals(p, "animtextdata", StringComparison.OrdinalIgnoreCase)))
            {
                animDataCount++;
                if (relParts.Length >= 2) animDataDirs.Add(Path.Combine(relParts[0], relParts[1]));
                continue;
            }

            if (string.Equals(ext, ".fuz", StringComparison.OrdinalIgnoreCase))
            {
                voiceCount++;
                var lowerParts = relParts.Select(p => p.ToLowerInvariant()).ToList();
                var voiceIdx = lowerParts.IndexOf("voice");
                if (voiceIdx >= 0 && voiceIdx + 2 < relParts.Length) voiceTypes.Add(relParts[voiceIdx + 2]);
                continue;
            }

            var matched = false;
            foreach (var (cat, exts) in ExtensionCategories)
            {
                if (!exts.Contains(ext, StringComparer.OrdinalIgnoreCase)) continue;
                var (c, dirs) = counts[cat];
                if (relParts.Length >= 2) dirs.Add(Path.Combine(relParts[0], relParts[1]));
                counts[cat] = (c + 1, dirs);
                matched = true;
                break;
            }
            if (!matched) otherCount++;
        }

        if (totalFiles == 0) return $"'{modPath}' contains no files.";

        var sb = new StringBuilder();
        sb.AppendLine($"Catalog of '{Path.GetFileName(modPath.TrimEnd('\\', '/'))}' ({totalFiles} file(s) total):");
        foreach (var (cat, _) in ExtensionCategories)
        {
            var (count, dirs) = counts[cat];
            if (count == 0) continue;
            var dirsStr = dirs.Count > 0 ? $" [{string.Join(", ", dirs.Take(8))}{(dirs.Count > 8 ? ", ..." : "")}]" : "";
            sb.AppendLine($"  {cat}: {count}{dirsStr}");
        }
        if (voiceCount > 0)
            sb.AppendLine($"  voice: {voiceCount}{(voiceTypes.Count > 0 ? $" (types: {string.Join(", ", voiceTypes)})" : "")}");
        if (animDataCount > 0)
            sb.AppendLine($"  anim_text_data: {animDataCount}{(animDataDirs.Count > 0 ? $" [{string.Join(", ", animDataDirs.Take(8))}]" : "")}");
        if (otherCount > 0)
            sb.AppendLine($"  other/unrecognized: {otherCount}");

        return sb.ToString().TrimEnd();
    }
}
