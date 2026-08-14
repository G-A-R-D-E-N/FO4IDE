using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FO4RecordEditor.Services.Papyrus;


























public sealed class PapyrusScriptIndex
{
    private sealed class CacheEntry
    {
        public long Size;
        public long MTimeUtcTicks;
        public PapyrusScript Script = null!;
    }

    private readonly List<string> _roots = new();



    private readonly Dictionary<string, string> _byQualifiedName = new(StringComparer.OrdinalIgnoreCase);




    private readonly Dictionary<string, string> _byBaseName = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, CacheEntry> _parsed = new(StringComparer.OrdinalIgnoreCase);

    private readonly object _lock = new();


    public IReadOnlyList<string> Roots => _roots;


    public int Count
    {
        get { lock (_lock) return _byQualifiedName.Count; }
    }









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


    private static string QualifiedNameFor(string root, string file)
    {
        var relative = Path.GetRelativePath(root, file);
        if (relative.StartsWith("..", StringComparison.Ordinal)) return string.Empty;
        relative = relative.Substring(0, relative.Length - ".psc".Length);
        return relative.Replace(Path.DirectorySeparatorChar, ':').Replace(Path.AltDirectorySeparatorChar, ':');
    }


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


    public PapyrusScript? Resolve(string scriptName)
    {
        var file = FindFile(scriptName);
        return file == null ? null : ParseCached(file);
    }


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


    public void Invalidate(string path)
    {
        lock (_lock) _parsed.Remove(path);
    }










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


    public static PapyrusDeclaration? FindMemberOn(PapyrusScript script, string memberName)
    {
        static bool Is(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        foreach (var p in script.Properties) if (Is(p.Name, memberName)) return p;
        foreach (var v in script.Variables) if (Is(v.Name, memberName)) return v;
        foreach (var f in script.Functions) if (Is(f.Name, memberName)) return f;
        foreach (var e in script.Events) if (Is(e.Name, memberName)) return e;
        foreach (var s in script.Structs) if (Is(s.Name, memberName)) return s;
        foreach (var c in script.CustomEvents) if (Is(c.Name, memberName)) return c;



        foreach (var state in script.States)
        {
            foreach (var f in state.Functions) if (Is(f.Name, memberName)) return f;
            foreach (var e in state.Events) if (Is(e.Name, memberName)) return e;
        }
        return null;
    }
}
