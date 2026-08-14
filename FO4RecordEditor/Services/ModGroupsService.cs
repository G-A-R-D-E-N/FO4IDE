using System.IO;
using Newtonsoft.Json;

namespace FO4RecordEditor.Services;

/// <summary>A named set of plugins meant to be used together (xEdit "ModGroups").</summary>
public sealed record ModGroup(string Name, List<string> Plugins);

/// <summary>
/// xEdit's ModGroups: declaring that a set of plugins are meant to be used together stops the
/// conflicts BETWEEN them from being reported as problems -- they are the intended, designed-in
/// kind, not the accidental kind. On a 651-plugin load order the conflict scan finds 45,240
/// conflicting records; a large share of those are exactly this (a framework plus its official
/// patches, a bundle of compatibility patches for one mod suite), and without a way to say so the
/// count is too big to act on.
///
/// A static class, matching how the rest of this layer (<see cref="WriteService"/>,
/// <see cref="ConflictScanner"/>, <see cref="ProtectedPlugins"/>) holds its state -- both the WPF
/// shell and the headless MCP host need the same groups without a DI container to thread an
/// instance through. Persisted the same way Settings is: JSON beside settings.json in the app data
/// folder, because this is a per-installation preference, not something that travels with a plugin.
///
/// A FormKey's conflict is suppressed when every plugin touching it belongs to ONE common group --
/// not "any group at all", since a plugin can be a member of several groups for several reasons, and
/// only a group that covers every party to that specific conflict actually vouches for it.
/// </summary>
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
            // A rename onto another group's name would make every later lookup by name ambiguous,
            // including this method's own.
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

    /// <summary>Force a re-read from disk (the desktop Settings dialog and the MCP host are separate
    /// processes on Linux, each with their own in-memory copy).</summary>
    public static void Reload() { lock (_lock) { _groups = null; EnsureLoaded(); } }

    /// <summary>True when some single group's membership is a superset of every plugin in <paramref name="plugins"/>.</summary>
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
