using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace FO4RecordEditor.Services;

/// <summary>
/// Per-signature record counts for a plugin, persisted across process starts.
/// </summary>
/// <remarks>
/// After the lazy/type-scoped index (#85) and the overlay load path (#86) the per-mod index retains
/// almost nothing, but the counts still come from a walk of the plugin and that walk is paid again
/// every process start. Measured on a 663-plugin load order (3,143,002 records): 7.0 s for a cold
/// counts-walk of the whole order, 2.4 s of it Fallout4.esm's first touch, and 646 of 662 plugins
/// under 50 ms. So the remaining cost is latency, not memory.
/// <para>
/// This persists the counts and nothing else -- a plugin's entry is ~147 numbers, and the whole load
/// order is on the order of 100 KB. That is why this is a JSON file beside settings.json rather than
/// the SQLite/FTS index #77 originally proposed: no schema, no second storage engine, and the thing
/// it removes (the cold walk) is the only part of that proposal the measurements still supported.
/// </para>
/// <para>
/// An entry is keyed by the plugin's path and validated against its size and last-write time, so any
/// edit on disk -- including our own save_plugin -- drops it and forces a fresh walk. Plugins the user
/// is actively editing are never cached at all; see <see cref="MutagenLoader.CountsCacheKeyFor"/>.
/// </para>
/// </remarks>
public static class RecordCountCache
{
    private sealed class Entry
    {
        [JsonProperty("size")] public long Size { get; set; }
        // Last-write time as UTC ticks. Ticks rather than a formatted string so a round trip cannot
        // lose sub-second precision and silently start serving a stale entry.
        [JsonProperty("mtime")] public long MTimeUtcTicks { get; set; }
        [JsonProperty("counts")] public Dictionary<string, int> Counts { get; set; } = new(StringComparer.Ordinal);
    }

    private sealed class CacheFile
    {
        [JsonProperty("version")] public int Version { get; set; } = CurrentVersion;
        [JsonProperty("entries")] public Dictionary<string, Entry> Entries { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);
    }

    private const int CurrentVersion = 1;

    // How long after the last Put the file is written. A full-load-order sweep produces hundreds of
    // Puts back to back; without this each one would rewrite the whole file.
    private const int FlushDelayMs = 1000;

    private static readonly object _lock = new();
    private static CacheFile? _cache;
    private static bool _dirty;
    private static System.Threading.Timer? _flushTimer;
    private static bool _exitHookInstalled;

    /// <summary>Disables both reads and writes. Set by tests that must see a real walk.</summary>
    public static bool Enabled { get; set; } = true;

    /// <summary>
    /// Where the cache lives. FO4RE_COUNT_CACHE overrides it, which is how tests get their own file
    /// instead of writing to the user's real one.
    /// </summary>
    public static string CacheFilePath
    {
        get
        {
            var over = Environment.GetEnvironmentVariable("FO4RE_COUNT_CACHE");
            if (!string.IsNullOrWhiteSpace(over)) return over.Trim().Trim('"');
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "FO4RecordEditor", "record-counts.json");
        }
    }

    /// <summary>
    /// The stored counts for a plugin, if the file on disk is still the one they were taken from.
    /// </summary>
    public static bool TryGet(string pluginPath, out Dictionary<string, int> counts)
    {
        counts = new Dictionary<string, int>(StringComparer.Ordinal);
        if (!Enabled || string.IsNullOrWhiteSpace(pluginPath)) return false;

        // Stat first and outside the lock: a missing or changed file is the common invalidation and
        // must not be answered from the cache at all.
        if (!TryStat(pluginPath, out var size, out var mtime)) return false;

        lock (_lock)
        {
            var cache = Load();
            if (!cache.Entries.TryGetValue(Key(pluginPath), out var e)) return false;
            if (e.Size != size || e.MTimeUtcTicks != mtime) return false;
            counts = new Dictionary<string, int>(e.Counts, StringComparer.Ordinal);
            return true;
        }
    }

    /// <summary>Store the counts for a plugin, stamped with the file's current size and write time.</summary>
    public static void Put(string pluginPath, IReadOnlyDictionary<string, int> counts)
    {
        if (!Enabled || string.IsNullOrWhiteSpace(pluginPath) || counts == null) return;
        if (!TryStat(pluginPath, out var size, out var mtime)) return;

        lock (_lock)
        {
            var cache = Load();
            cache.Entries[Key(pluginPath)] = new Entry
            {
                Size = size,
                MTimeUtcTicks = mtime,
                Counts = new Dictionary<string, int>(counts, StringComparer.Ordinal),
            };
            _dirty = true;
            ScheduleFlush();
        }
    }

    /// <summary>Write the cache to disk now, if anything changed since the last write.</summary>
    public static void Flush()
    {
        lock (_lock)
        {
            if (!_dirty || _cache == null) return;
            _dirty = false;

            // Entries whose plugin is gone would otherwise accumulate forever. Pruning here rather
            // than on load keeps the read path from stat-ing the whole load order.
            foreach (var dead in _cache.Entries.Where(kv => !File.Exists(kv.Key)).Select(kv => kv.Key).ToList())
                _cache.Entries.Remove(dead);

            try
            {
                var file = CacheFilePath;
                var dir = Path.GetDirectoryName(file);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                // Write-then-move, so a crash mid-write cannot leave a truncated file that then has
                // to be distinguished from a valid one on the next read.
                var tmp = file + ".tmp";
                File.WriteAllText(tmp, JsonConvert.SerializeObject(_cache, Formatting.Indented));
                File.Move(tmp, file, overwrite: true);
            }
            catch
            {
                // A cache that cannot be written is a slower start, not a failure. Never let it stop
                // the tool from loading a load order.
            }
        }
    }

    /// <summary>Drop the in-memory cache and delete the file. Tests only.</summary>
    public static void ResetForTest()
    {
        lock (_lock)
        {
            _cache = null;
            _dirty = false;
            try { if (File.Exists(CacheFilePath)) File.Delete(CacheFilePath); } catch { }
        }
    }

    /// <summary>
    /// Forget what was loaded into memory, keeping the file. Tests only: this is the state a fresh
    /// process starts in, which is the only way to exercise the read-back half.
    /// </summary>
    public static void ResetInMemoryForTest()
    {
        lock (_lock)
        {
            _cache = null;
            _dirty = false;
        }
    }

    /// <summary>Number of plugins currently held. Tests only.</summary>
    public static int Count
    {
        get { lock (_lock) { return Load().Entries.Count; } }
    }

    private static string Key(string pluginPath)
    {
        try { return Path.GetFullPath(pluginPath); } catch { return pluginPath; }
    }

    private static bool TryStat(string path, out long size, out long mtimeUtcTicks)
    {
        size = 0;
        mtimeUtcTicks = 0;
        try
        {
            var fi = new FileInfo(path);
            if (!fi.Exists) return false;
            size = fi.Length;
            mtimeUtcTicks = fi.LastWriteTimeUtc.Ticks;
            return true;
        }
        catch { return false; }
    }

    // Caller holds _lock.
    private static CacheFile Load()
    {
        if (_cache != null) return _cache;
        try
        {
            var file = CacheFilePath;
            if (File.Exists(file))
            {
                var parsed = JsonConvert.DeserializeObject<CacheFile>(File.ReadAllText(file));
                // A file from a future or unknown version is discarded rather than guessed at.
                if (parsed != null && parsed.Version == CurrentVersion)
                {
                    parsed.Entries = new Dictionary<string, Entry>(parsed.Entries, StringComparer.OrdinalIgnoreCase);
                    _cache = parsed;
                }
            }
        }
        catch
        {
            // Corrupt or unreadable: start empty. The cost is one cold walk, not an error.
        }
        return _cache ??= new CacheFile();
    }

    // Caller holds _lock.
    private static void ScheduleFlush()
    {
        if (!_exitHookInstalled)
        {
            _exitHookInstalled = true;
            // A sweep that ends right before exit would otherwise write nothing, and the next start
            // would pay the full walk again.
            try { AppDomain.CurrentDomain.ProcessExit += (_, _) => Flush(); } catch { }
        }

        if (_flushTimer == null)
            _flushTimer = new System.Threading.Timer(_ => Flush(), null, FlushDelayMs, System.Threading.Timeout.Infinite);
        else
            _flushTimer.Change(FlushDelayMs, System.Threading.Timeout.Infinite);
    }
}
