using System;
using FluentAssertions;
using FO4RecordEditor.Services;
using Xunit;

namespace FO4RecordEditor.Tests;

public class LooseModReleaseTests
{
    private sealed class FakeMod : IDisposable
    {
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }

    private static void Clear(string name)
    {
        MutagenLoader.LooseMods.TryRemove(name, out _);
        MutagenLoader.EditableMods.TryRemove(name, out _);
        MutagenLoader.InvalidateModIndex(name);
    }

    [Fact]
    public void ReplaceLooseMod_DisposesTheInstanceItDisplaces()
    {
        const string name = "ReleaseTest_Replace.esp";
        Clear(name);
        var previous = new FakeMod();
        var current = new FakeMod();

        MutagenLoader.ReplaceLooseMod(name, previous);
        MutagenLoader.ReplaceLooseMod(name, current);

        previous.Disposed.Should().BeTrue("the displaced overlay's mmap must be released");
        current.Disposed.Should().BeFalse();
        MutagenLoader.LooseMods[name].Should().BeSameAs(current);

        Clear(name);
    }

    [Fact]
    public void ReplaceLooseMod_WithTheSameInstance_DoesNotDisposeIt()
    {
        const string name = "ReleaseTest_Same.esp";
        Clear(name);
        var mod = new FakeMod();

        MutagenLoader.ReplaceLooseMod(name, mod);
        MutagenLoader.ReplaceLooseMod(name, mod);

        mod.Disposed.Should().BeFalse("re-registering the live instance must not tear it down");

        Clear(name);
    }

    [Fact]
    public void ReleaseLooseMod_DisposesAndRemoves()
    {
        const string name = "ReleaseTest_Release.esp";
        Clear(name);
        var mod = new FakeMod();

        MutagenLoader.ReplaceLooseMod(name, mod);
        MutagenLoader.ReleaseLooseMod(name);

        mod.Disposed.Should().BeTrue();
        MutagenLoader.LooseMods.ContainsKey(name).Should().BeFalse();
    }

    [Fact]
    public void ReleaseLooseMod_NeverDisposesTheInstanceOpenForEditing()
    {
        const string name = "ReleaseTest_Editable.esp";
        Clear(name);
        var editable = new FakeMod();

        MutagenLoader.EditableMods[name] = editable;
        MutagenLoader.ReplaceLooseMod(name, editable);
        MutagenLoader.ReleaseLooseMod(name);

        editable.Disposed.Should().BeFalse("WriteService still holds this mod for editing");

        Clear(name);
    }

    [Fact]
    public void OpeningForEditing_StillReleasesTheReadOnlyOverlayItDisplaces()
    {
        const string name = "ReleaseTest_Handoff.esp";
        Clear(name);
        var readOnlyOverlay = new FakeMod();
        var editable = new FakeMod();

        MutagenLoader.ReplaceLooseMod(name, readOnlyOverlay);
        MutagenLoader.EditableMods[name] = editable;
        MutagenLoader.ReplaceLooseMod(name, editable);

        readOnlyOverlay.Disposed.Should().BeTrue("this is the handle that blocked saving over the file");
        editable.Disposed.Should().BeFalse();

        Clear(name);
    }

    [Fact]
    public void ReleaseLooseMod_DropsTheModIndexBeforeDisposing()
    {
        const string name = "ReleaseTest_Index.esp";
        Clear(name);
        var mod = new FakeMod();

        MutagenLoader.ReplaceLooseMod(name, mod);
        MutagenLoader.SeedModIndexForTest(name, mod);
        MutagenLoader.ModIndexCacheContains(name).Should().BeTrue();

        MutagenLoader.ReleaseLooseMod(name);

        MutagenLoader.ModIndexCacheContains(name).Should().BeFalse(
            "an index keeps the mod as its Source and would read from a disposed overlay");
    }

    [Fact]
    public void ReleaseLooseMod_OnAnUnknownPlugin_IsHarmless()
    {
        var act = () => MutagenLoader.ReleaseLooseMod("ReleaseTest_NeverRegistered.esp");
        act.Should().NotThrow();
    }
}
