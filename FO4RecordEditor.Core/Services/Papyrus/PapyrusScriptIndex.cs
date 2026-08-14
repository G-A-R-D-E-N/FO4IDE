using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FO4RecordEditor.Services.Papyrus;

/// <summary>
/// Finds .psc files by script name across a set of import roots, and caches their parse trees.
/// </summary>
/// <remarks>
/// This is the front end's half of what a compiler calls source resolution. The rule it implements
/// is the one the Identifier Reference states and <c>PapyrusService.Compile</c> already relies on:
/// a script's namespace path is its folder path, so <c>MyCoolStuff:Quests:MyQuest</c> is
/// <c>MyCoolStuff/Quests/MyQuest.psc</c> under some root.
/// <para>
/// Roots are ordered and the first match wins, matching how the existing compile path assembles its
/// import list -- caller-supplied roots first, then the base game scripts. That ordering is
/// load-bearing: F4SE ships extended <c>Game.psc</c> and <c>Actor.psc</c> that must shadow the
/// vanilla ones, and getting it backwards is exactly the bug the closed import-root issue fixed.
/// </para>
/// <para>
/// Discovery is eager (a directory walk) but parsing is lazy and cached, keyed on the file's size
/// and last-write time. A whole 18,800-file corpus parses in about 3.5 seconds, so the cache exists
/// to keep an editor responsive per keystroke, not because a cold walk would be unaffordable.
/// </para>
/// <para>
/// Nothing here does type resolution. Lookups are by name, walking the <c>Extends</c> chain -- which
/// is enough to point go-to-definition at the right declaration and not enough to tell you what a
/// dotted expression's type is. That distinction is phase 1 against phase 2.
/// </para>
/// </remarks>
public sealed class PapyrusScriptIndex
{
    private sealed class CacheEntry
    {
        public long Size;
        public long MTimeUtcTicks;
        public PapyrusScript Script = null!;
    }

    private readonly List<string> _roots = new();

    // Script name (namespace-qualified, ':' separated) to file path. First root to define a name
    // keeps it, so later roots act as fallbacks rather than overrides.
    private readonly Dictionary<string, string> _byQualifiedName = new(StringComparer.OrdinalIgnoreCase);

    // Bare script name to file path, so an unqualified reference in a script that used Import still
    // finds a namespaced file. Ambiguous bare names keep the first one found; the qualified map is
    // always tried first, so this only ever fires when there is nothing better.
    private readonly Dictionary<string, string> _byBaseName = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, CacheEntry> _parsed = new(StringComparer.OrdinalIgnoreCase);

    private readonly object _lock = new();

    /// <summary>Import roots, in priority order.</summary>
    public IReadOnlyList<string> Roots => _roots;

    /// <summary>Number of distinct scripts discovered.</summary>
    public int Count
    {
        get { lock (_lock) return _byQualifiedName.Count; }
    }

    /// <summary>Adds a source root and indexes every .psc beneath it.</summary>
    /// <remarks>
    /// Unreadable subdirectories are skipped rather than thrown on: import roots routinely point
    /// into game installs, modlist drives and Wine prefixes where some subtree is not ours to read
    /// or is not a real directory at all, and one such folder must not cost the caller the entire
    /// index. See <see cref="PapyrusFileWalk"/> for what the framework's own recursive enumeration
    /// gets wrong here.
    /// </remarks>
    public void AddRoot(string root)
    {
        if (string.IsNullOrWhiteSpace(root)) return;
        var full = Path.GetFullPath(root);
        if (!Directory.Exists(full)) return;

        lock (_lock)
        {
            if (_roots.Any(r => string.Equals(r, full, StringComparison.OrdinalIgnoreCase))) return;
            _roots.Add(full);

            foreach (var file in PapyrusFileWalk.EnumerateFiles(full, "*.psc"))
            {
                var qualified = QualifiedNameFor(full, file);
                if (qualified.Length == 0) continue;
                if (!_byQualifiedName.ContainsKey(qualified)) _byQualifiedName[qualified] = file;

                var bare = qualified.Substring(qualified.LastIndexOf(':') + 1);
                if (!_byBaseName.ContainsKey(bare)) _byBaseName[bare] = file;
            }
        }
    }

    /// <summary>Turns a file path under <paramref name="root"/> into its Papyrus script name.</summary>
    private static string QualifiedNameFor(string root, string file)
    {
        var relative = Path.GetRelativePath(root, file);
        if (relative.StartsWith("..", StringComparison.Ordinal)) return string.Empty;
        relative = relative.Substring(0, relative.Length - ".psc".Length);
        return relative.Replace(Path.DirectorySeparatorChar, ':').Replace(Path.AltDirectorySeparatorChar, ':');
    }

    /// <summary>The file backing <paramref name="scriptName"/>, or null.</summary>
    public string? FindFile(string scriptName)
    {
        if (string.IsNullOrWhiteSpace(scriptName)) return null;
        lock (_lock)
        {
            if (_byQualifiedName.TryGetValue(scriptName, out var path)) return path;
            var bare = scriptName.Substring(scriptName.LastIndexOf(':') + 1);
            return _byBaseName.TryGetValue(bare, out var fallback) ? fallback : null;
        }
    }

    /// <summary>Every script name known to the index, sorted.</summary>
    public IReadOnlyList<string> ScriptNames
    {
        get
        {
            lock (_lock)
            {
                var names = _byQualifiedName.Keys.ToList();
                names.Sort(StringComparer.OrdinalIgnoreCase);
                return names;
            }
        }
    }

    /// <summary>Parses <paramref name="scriptName"/>, from cache when the file has not changed.</summary>
    public PapyrusScript? Resolve(string scriptName)
    {
        var file = FindFile(scriptName);
        return file == null ? null : ParseCached(file);
    }

    /// <summary>Parses a file, from cache when its size and timestamp are unchanged.</summary>
    public PapyrusScript? ParseCached(string path)
    {
        FileInfo info;
        try
        {
            info = new FileInfo(path);
            if (!info.Exists) return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }

        lock (_lock)
        {
            if (_parsed.TryGetValue(path, out var entry)
                && entry.Size == info.Length
                && entry.MTimeUtcTicks == info.LastWriteTimeUtc.Ticks)
            {
                return entry.Script;
            }
        }

        PapyrusScript script;
        try
        {
            script = PapyrusParser.ParseFile(path);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }

        lock (_lock)
        {
            _parsed[path] = new CacheEntry
            {
                Size = info.Length,
                MTimeUtcTicks = info.LastWriteTimeUtc.Ticks,
                Script = script,
            };
        }
        return script;
    }

    /// <summary>Drops the cached parse of a file, forcing a re-parse on next use.</summary>
    public void Invalidate(string path)
    {
        lock (_lock) _parsed.Remove(path);
    }

    /// <summary>
    /// <paramref name="script"/> followed by each ancestor named by <c>Extends</c>, nearest first.
    /// </summary>
    /// <remarks>
    /// Cycle-guarded, because <c>Extends</c> is just a name here: a mod with two scripts extending
    /// each other, or a stale copy of a base script under a higher-priority root, would otherwise
    /// loop forever. Real Papyrus rejects such a hierarchy, but this front end reads what is on disk
    /// rather than what compiles.
    /// </remarks>
    public IReadOnlyList<PapyrusScript> BaseChain(PapyrusScript script)
    {
        var chain = new List<PapyrusScript> { script };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { script.Name };

        var current = script;
        while (!string.IsNullOrEmpty(current.Extends))
        {
            if (!seen.Add(current.Extends!)) break;
            var parent = Resolve(current.Extends!);
            if (parent == null) break;
            chain.Add(parent);
            current = parent;
        }
        return chain;
    }

    /// <summary>
    /// The declaration named <paramref name="memberName"/> on <paramref name="script"/> or an ancestor.
    /// </summary>
    /// <remarks>
    /// Search order is properties, then variables, then functions, then events, then structs, then
    /// custom events -- and the whole order is repeated for each script in the chain before moving
    /// up. Papyrus forbids one script declaring two of these with the same name, so within a script
    /// the order is arbitrary; across the chain, nearest-first is what shadowing means.
    /// </remarks>
    public PapyrusDeclaration? FindMember(PapyrusScript script, string memberName, out PapyrusScript? owner)
    {
        owner = null;
        if (string.IsNullOrEmpty(memberName)) return null;

        foreach (var level in BaseChain(script))
        {
            var hit = FindMemberOn(level, memberName);
            if (hit != null)
            {
                owner = level;
                return hit;
            }
        }
        return null;
    }

    /// <summary>Looks up a member on one script, ignoring its ancestors.</summary>
    public static PapyrusDeclaration? FindMemberOn(PapyrusScript script, string memberName)
    {
        static bool Is(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        foreach (var p in script.Properties) if (Is(p.Name, memberName)) return p;
        foreach (var v in script.Variables) if (Is(v.Name, memberName)) return v;
        foreach (var f in script.Functions) if (Is(f.Name, memberName)) return f;
        foreach (var e in script.Events) if (Is(e.Name, memberName)) return e;
        foreach (var s in script.Structs) if (Is(s.Name, memberName)) return s;
        foreach (var c in script.CustomEvents) if (Is(c.Name, memberName)) return c;

        // State overrides come last: the empty-state version is the declaration a reader wants, and
        // it is the one the language requires to exist.
        foreach (var state in script.States)
        {
            foreach (var f in state.Functions) if (Is(f.Name, memberName)) return f;
            foreach (var e in state.Events) if (Is(e.Name, memberName)) return e;
        }
        return null;
    }
}
