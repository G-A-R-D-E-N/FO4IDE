using System.IO;
using Newtonsoft.Json;

namespace FO4RecordEditor.Services;

public sealed record ModGroup(string Name, List<string> Plugins);

public static class ModGroupsService
{
    private static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     "FO4RecordEditor");
    private static readonly string FilePath = Path.Combine(Dir, "modgroups.json");

    private static List<ModGroup>? _groups;
    private static readonly object _lock = new();

    public static IReadOnlyList<ModGroup> Groups
    {
        get { lock (_lock) { EnsureLoaded(); return _groups!; } }
    }

    private static void EnsureLoaded()
    {
        if (_groups != null) return;
        try
        {
            _groups = File.Exists(FilePath)
                ? JsonConvert.DeserializeObject<List<ModGroup>>(File.ReadAllText(FilePath)) ?? new()
                : new();
        }
        catch { _groups = new(); }
    }

    private static void Save()
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(FilePath, JsonConvert.SerializeObject(_groups, Formatting.Indented));
    }

    public static string Create(string name, IEnumerable<string> plugins)
    {
        lock (_lock)
        {
            EnsureLoaded();
            name = (name ?? "").Trim();
            if (name.Length == 0) return ToolError.Fail("A ModGroup needs a name.");
            if (_groups!.Any(g => string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase)))
                return ToolError.Fail($"A ModGroup named '{name}' already exists. Use update_mod_group to change it.");

            var list = plugins.Select(p => p.Trim()).Where(p => p.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (list.Count < 2) return ToolError.Fail("A ModGroup needs at least two plugins -- one plugin has nothing to be grouped with.");

            _groups!.Add(new ModGroup(name, list));
            Save();
            return $"Created ModGroup '{name}' with {list.Count} plugin(s): [{string.Join(", ", list)}].";
        }
    }

    public static string Update(string name, IEnumerable<string>? plugins, string? newName)
    {
        lock (_lock)
        {
            EnsureLoaded();
            var g = _groups!.FirstOrDefault(g => string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase));
            if (g == null) return ToolError.Fail($"No ModGroup named '{name}'.");

            var idx = _groups!.IndexOf(g);
            var finalName = string.IsNullOrWhiteSpace(newName) ? g.Name : newName.Trim();

            if (!string.Equals(finalName, g.Name, StringComparison.OrdinalIgnoreCase) &&
                _groups.Any(o => !ReferenceEquals(o, g) && string.Equals(o.Name, finalName, StringComparison.OrdinalIgnoreCase)))
                return ToolError.Fail($"A ModGroup named '{finalName}' already exists.");
            var finalPlugins = plugins == null
                ? g.Plugins
                : plugins.Select(p => p.Trim()).Where(p => p.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (finalPlugins.Count < 2) return ToolError.Fail("A ModGroup needs at least two plugins.");

            _groups[idx] = new ModGroup(finalName, finalPlugins);
            Save();
            return $"Updated ModGroup '{finalName}': [{string.Join(", ", finalPlugins)}].";
        }
    }

    public static string Delete(string name)
    {
        lock (_lock)
        {
            EnsureLoaded();
            var removed = _groups!.RemoveAll(g => string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase));
            if (removed == 0) return ToolError.Fail($"No ModGroup named '{name}'.");
            Save();
            return $"Deleted ModGroup '{name}'.";
        }
    }

    public static void Reload() { lock (_lock) { _groups = null; EnsureLoaded(); } }

    public static bool IsSuppressed(IReadOnlyCollection<string> plugins)
    {
        lock (_lock)
        {
            EnsureLoaded();
            return plugins.Count > 0 && _groups!.Any(g =>
            {
                var set = new HashSet<string>(g.Plugins, StringComparer.OrdinalIgnoreCase);
                return plugins.All(set.Contains);
            });
        }
    }
}
