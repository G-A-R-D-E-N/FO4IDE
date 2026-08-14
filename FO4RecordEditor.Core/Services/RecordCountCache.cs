using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace FO4RecordEditor.Services;

public static class RecordCountCache
{
    private sealed class Entry
    {
        [JsonProperty("size")] public long Size { get; set; }

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

    private const int FlushDelayMs = 1000;

    private static readonly object _lock = new();
    private static CacheFile? _cache;
    private static bool _dirty;
    private static System.Threading.Timer? _flushTimer;
    private static bool _exitHookInstalled;

    public static bool Enabled { get; set; } = true;

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

    public static bool TryGet(string pluginPath, out Dictionary<string, int> counts)
    {
        counts = new Dictionary<string, int>(StringComparer.Ordinal);
        if (!Enabled || string.IsNullOrWhiteSpace(pluginPath)) return false;

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

    public static void Flush()
    {
        lock (_lock)
        {
            if (!_dirty || _cache == null) return;
            _dirty = false;

            foreach (var dead in _cache.Entries.Where(kv => !File.Exists(kv.Key)).Select(kv => kv.Key).ToList())
                _cache.Entries.Remove(dead);

            try
            {
                var file = CacheFilePath;
                var dir = Path.GetDirectoryName(file);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                var tmp = file + ".tmp";
                File.WriteAllText(tmp, JsonConvert.SerializeObject(_cache, Formatting.Indented));
                File.Move(tmp, file, overwrite: true);
            }
            catch
            {

            }
        }
    }

    public static void ResetForTest()
    {
        lock (_lock)
        {
            _cache = null;
            _dirty = false;
            try { if (File.Exists(CacheFilePath)) File.Delete(CacheFilePath); } catch { }
        }
    }

    public static void ResetInMemoryForTest()
    {
        lock (_lock)
        {
            _cache = null;
            _dirty = false;
        }
    }

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

    private static CacheFile Load()
    {
        if (_cache != null) return _cache;
        try
        {
            var file = CacheFilePath;
            if (File.Exists(file))
            {
                var parsed = JsonConvert.DeserializeObject<CacheFile>(File.ReadAllText(file));

                if (parsed != null && parsed.Version == CurrentVersion)
                {
                    parsed.Entries = new Dictionary<string, Entry>(parsed.Entries, StringComparer.OrdinalIgnoreCase);
                    _cache = parsed;
                }
            }
        }
        catch
        {

        }
        return _cache ??= new CacheFile();
    }

    private static void ScheduleFlush()
    {
        if (!_exitHookInstalled)
        {
            _exitHookInstalled = true;

            try { AppDomain.CurrentDomain.ProcessExit += (_, _) => Flush(); } catch { }
        }

        if (_flushTimer == null)
            _flushTimer = new System.Threading.Timer(_ => Flush(), null, FlushDelayMs, System.Threading.Timeout.Infinite);
        else
            _flushTimer.Change(FlushDelayMs, System.Threading.Timeout.Infinite);
    }
}
