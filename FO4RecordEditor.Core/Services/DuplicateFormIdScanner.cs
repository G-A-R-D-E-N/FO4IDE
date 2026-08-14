using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Text;

namespace FO4RecordEditor.Services;

/// <summary>
/// Scans Fallout 4 plugin record headers without materializing record bodies. The vendored Mutagen
/// overlay intentionally keeps the last record when a file contains duplicate FormIDs, matching the
/// engine and keeping the rest of the load order usable. This scanner preserves that tolerant load
/// while making the malformed file visible to validation and the Problems drawer.
/// </summary>
internal static class DuplicateFormIdScanner
{
    internal sealed record Duplicate(uint RawFormId, int Count, IReadOnlyList<string> RecordTypes);
    internal sealed record Result(IReadOnlyList<Duplicate> Duplicates, string? Error = null);

    private sealed record CacheEntry(
        long Length,
        long LastWriteUtcTicks,
        long Generation,
        Result Result);

    private sealed class DuplicateAccumulator(string firstType, string secondType)
    {
        internal int Count { get; private set; } = 2;
        internal List<string> RecordTypes { get; } = [firstType, secondType];
        internal void Add(string recordType)
        {
            Count++;
            RecordTypes.Add(recordType);
        }
    }

    private static readonly StringComparer PathComparer =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private static readonly ConcurrentDictionary<string, CacheEntry> Cache = new(PathComparer);
    private static readonly ConcurrentDictionary<string, long> PendingScans = new(PathComparer);
    private static readonly ConcurrentDictionary<string, long> Generations = new(PathComparer);

    private const int HeaderLength = 24;
    private const int MaxGroupDepth = 128;

    internal static bool TryGetCached(string path, out Result result)
    {
        result = new Result([]);
        try
        {
            var fullPath = Path.GetFullPath(path);
            var info = new FileInfo(fullPath);
            var generation = CurrentGeneration(fullPath);
            if (!info.Exists) return false;
            if (!Cache.TryGetValue(fullPath, out var cached) ||
                cached.Length != info.Length ||
                cached.LastWriteUtcTicks != info.LastWriteTimeUtc.Ticks ||
                cached.Generation != generation)
                return false;
            result = cached.Result;
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Warm the raw-integrity cache away from the UI thread. Explicit validation commands
    /// still call <see cref="Scan"/> synchronously so they can return a complete answer.</summary>
    internal static void QueueScan(string path)
    {
        string fullPath;
        try { fullPath = Path.GetFullPath(path); }
        catch { return; }
        if (TryGetCached(fullPath, out _)) return;

        var generation = CurrentGeneration(fullPath);
        if (!PendingScans.TryAdd(fullPath, generation)) return;

        _ = Task.Run(() =>
        {
            try { Scan(fullPath); }
            finally
            {
                // Invalidation can remove the old marker and a later request can add a marker for a
                // newer generation while this task is still running. Only remove our own generation.
                if (PendingScans.TryGetValue(fullPath, out var pendingGeneration) &&
                    pendingGeneration == generation)
                    PendingScans.TryRemove(fullPath, out _);
            }
        });
    }

    internal static Result Scan(string path) => Scan(path, openOverride: null);

    internal static Result ScanForTest(string path, Func<string, FileStream> openOverride) =>
        Scan(path, openOverride);

    private static Result Scan(string path, Func<string, FileStream>? openOverride)
    {
        string? fullPath = null;
        long originalLength = 0;
        long originalLastWriteUtcTicks = 0;
        long generation = 0;
        try
        {
            fullPath = Path.GetFullPath(path);
            generation = CurrentGeneration(fullPath);
            var info = new FileInfo(fullPath);
            if (!info.Exists) return new Result([], $"Plugin file does not exist: {fullPath}");

            originalLength = info.Length;
            originalLastWriteUtcTicks = info.LastWriteTimeUtc.Ticks;
            if (Cache.TryGetValue(fullPath, out var cached) &&
                cached.Length == originalLength &&
                cached.LastWriteUtcTicks == originalLastWriteUtcTicks &&
                cached.Generation == generation)
                return cached.Result;

            // Keep one compact first-occurrence entry per unique FormID. Lists are allocated only
            // for IDs that actually collide instead of once for every record in a large plugin.
            var firstTypes = new Dictionary<uint, string>();
            var duplicates = new Dictionary<uint, DuplicateAccumulator>();
            using (var stream = openOverride?.Invoke(fullPath) ?? new FileStream(
                       fullPath,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.ReadWrite | FileShare.Delete,
                       bufferSize: 64 * 1024,
                       options: FileOptions.SequentialScan))
            {
                ScanRange(stream, 0, stream.Length, firstTypes, duplicates, depth: 0);
            }

            var after = new FileInfo(fullPath);
            if (!after.Exists ||
                after.Length != originalLength ||
                after.LastWriteTimeUtc.Ticks != originalLastWriteUtcTicks ||
                CurrentGeneration(fullPath) != generation)
                return new Result([], "Plugin changed or was invalidated while duplicate FormIDs were being scanned; retry validation.");

            var result = new Result(duplicates
                .OrderBy(pair => pair.Key)
                .Select(pair => new Duplicate(
                    pair.Key,
                    pair.Value.Count,
                    pair.Value.RecordTypes.ToArray()))
                .ToArray());
            StoreCacheIfCurrent(
                fullPath,
                new CacheEntry(originalLength, originalLastWriteUtcTicks, generation, result));
            return result;
        }
        catch (Exception ex)
        {
            var result = new Result([], $"{ex.GetType().Name}: {ex.Message}");
            // Cache stable malformed-file results too, so reopening the Problems drawer does not
            // repeatedly walk the same corrupt bytes and the error remains visible to the UI.
            try
            {
                if (ex is InvalidDataException && fullPath != null)
                {
                    var after = new FileInfo(fullPath);
                    if (after.Exists && after.Length == originalLength &&
                        after.LastWriteTimeUtc.Ticks == originalLastWriteUtcTicks)
                    {
                        StoreCacheIfCurrent(
                            fullPath,
                            new CacheEntry(
                                originalLength,
                                originalLastWriteUtcTicks,
                                generation,
                                result));
                    }
                }
            }
            catch { }
            return result;
        }
    }

    internal static void Invalidate(string path)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            Generations.AddOrUpdate(fullPath, 1, static (_, current) => unchecked(current + 1));
            Cache.TryRemove(fullPath, out _);
            PendingScans.TryRemove(fullPath, out _);
        }
        catch { }
    }

    private static long CurrentGeneration(string fullPath) =>
        Generations.GetOrAdd(fullPath, 0);

    private static bool StoreCacheIfCurrent(string fullPath, CacheEntry entry)
    {
        if (CurrentGeneration(fullPath) != entry.Generation) return false;
        Cache[fullPath] = entry;
        if (CurrentGeneration(fullPath) == entry.Generation) return true;

        // Invalidation raced between the first generation check and the assignment. Remove only
        // this exact stale entry so a newer scan cannot be deleted by the older task.
        ((ICollection<KeyValuePair<string, CacheEntry>>)Cache)
            .Remove(new KeyValuePair<string, CacheEntry>(fullPath, entry));
        return false;
    }

    private static void ScanRange(
        FileStream stream,
        long start,
        long end,
        Dictionary<uint, string> firstTypes,
        Dictionary<uint, DuplicateAccumulator> duplicates,
        int depth)
    {
        if (depth > MaxGroupDepth)
            throw new InvalidDataException(
                $"Plugin group nesting exceeds the safe limit of {MaxGroupDepth} levels.");

        var header = new byte[HeaderLength];
        var position = start;

        while (position < end)
        {
            if (end - position < HeaderLength)
                throw new InvalidDataException($"Truncated record/group header at 0x{position:X}.");

            stream.Position = position;
            stream.ReadExactly(header);
            var signature = Encoding.ASCII.GetString(header, 0, 4);
            var size = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4, 4));

            if (signature == "GRUP")
            {
                if (size < HeaderLength)
                    throw new InvalidDataException($"GRUP at 0x{position:X} has invalid size {size}.");

                var groupEnd = checked(position + size);
                if (groupEnd > end)
                    throw new InvalidDataException($"GRUP at 0x{position:X} extends past its parent.");

                ScanRange(stream, position + HeaderLength, groupEnd, firstTypes, duplicates, depth + 1);
                position = groupEnd;
                continue;
            }

            var recordEnd = checked(position + HeaderLength + size);
            if (recordEnd > end)
                throw new InvalidDataException($"{signature} record at 0x{position:X} extends past its parent.");

            if (signature != "TES4")
            {
                var rawFormId = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(12, 4));
                if (duplicates.TryGetValue(rawFormId, out var duplicate))
                {
                    duplicate.Add(signature);
                }
                else if (firstTypes.TryGetValue(rawFormId, out var firstType))
                {
                    duplicates.Add(rawFormId, new DuplicateAccumulator(firstType, signature));
                }
                else
                {
                    firstTypes.Add(rawFormId, signature);
                }
            }

            position = recordEnd;
        }

        if (position != end)
            throw new InvalidDataException($"Record walk ended at 0x{position:X}, expected 0x{end:X}.");
    }
}
