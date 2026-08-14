using System.IO;
using FluentAssertions;
using FO4RecordEditor.Services;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Fallout4;
using Mutagen.Bethesda.Plugins;
using Xunit;

namespace FO4RecordEditor.Tests;

public sealed class DuplicateFormIdTransientCacheTests
{
    [Fact]
    public void Scanner_DoesNotCacheATransientOpenFailure()
    {
        var plugin = $"TransientScan_{Guid.NewGuid():N}.esp";
        var path = BuildPlugin(plugin);

        try
        {
            DuplicateFormIdScanner.Invalidate(path);
            DuplicateFormIdScanner.ScanForTest(path, _ => throw new IOException("simulated sharing violation"))
                .Error.Should().Contain("IOException");

            var retried = DuplicateFormIdScanner.Scan(path);
            retried.Error.Should().BeNull("releasing a transient lock must make the next scan retry the file");
            retried.Duplicates.Should().BeEmpty();
        }
        finally
        {
            DuplicateFormIdScanner.Invalidate(path);
            try { File.Delete(path); } catch { }
        }
    }

    [Fact]
    public void Scanner_InvalidationPreventsAnInFlightScanFromRepopulatingTheCache()
    {
        var plugin = $"InvalidatedScan_{Guid.NewGuid():N}.esp";
        var path = BuildPlugin(plugin);
        using var openReached = new ManualResetEventSlim(false);
        using var continueOpen = new ManualResetEventSlim(false);

        try
        {
            DuplicateFormIdScanner.Invalidate(path);
            var staleScan = Task.Run(() => DuplicateFormIdScanner.ScanForTest(path, candidate =>
            {
                openReached.Set();
                continueOpen.Wait();
                return new FileStream(
                    candidate,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
            }));

            openReached.Wait();
            DuplicateFormIdScanner.Invalidate(path);
            continueOpen.Set();

            var staleResult = staleScan.GetAwaiter().GetResult();
            staleResult.Error.Should().Contain("invalidated");
            DuplicateFormIdScanner.TryGetCached(path, out _).Should().BeFalse(
                "a scan that started before invalidation must not publish a cache entry");

            DuplicateFormIdScanner.Scan(path).Error.Should().BeNull();
            DuplicateFormIdScanner.TryGetCached(path, out var fresh).Should().BeTrue();
            fresh.Error.Should().BeNull();
        }
        finally
        {
            continueOpen.Set();
            DuplicateFormIdScanner.Invalidate(path);
            try { File.Delete(path); } catch { }
        }
    }

    private static string BuildPlugin(string plugin)
    {
        var path = Path.Combine(Path.GetTempPath(), plugin);
        var mod = new Fallout4Mod(ModKey.FromNameAndExtension(plugin), Fallout4Release.Fallout4);
        mod.Keywords.Add(new Keyword(new FormKey(mod.ModKey, 0x800), Fallout4Release.Fallout4)
        {
            EditorID = "TransientScanKeyword",
        });
        mod.WriteToBinary(path);
        return path;
    }
}
