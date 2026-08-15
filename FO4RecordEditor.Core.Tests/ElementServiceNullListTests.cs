using System.Reflection;
using FluentAssertions;
using FO4RecordEditor.Services;
using Mutagen.Bethesda.Fallout4;
using Mutagen.Bethesda.Plugins;

namespace FO4RecordEditor.Core.Tests;

public sealed class ElementServiceNullListTests
{
    [Fact]
    public void ResolveForAdd_InitializesNullConcreteList()
    {
        var holder = new ConcreteHolder();

        Resolve(holder, initializeNullLists: true).Should().BeTrue();

        holder.Items.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void ResolveForAdd_InitializesNullInterfaceList()
    {
        var holder = new InterfaceHolder();

        Resolve(holder, initializeNullLists: true).Should().BeTrue();

        holder.Items.Should().NotBeNull().And.BeEmpty();
        holder.Items.Should().BeOfType<List<TestElement>>();
    }

    [Fact]
    public void ResolveForNonAdd_DoesNotMutateNullList()
    {
        var holder = new ConcreteHolder();

        Resolve(holder, initializeNullLists: false).Should().BeTrue();

        holder.Items.Should().BeNull();
    }

    [Fact]
    public void AddElement_InitializesWeaponObjectTemplatesAndRoundTrips()
    {
        var plugin = $"NullListWeapon_{Guid.NewGuid():N}.esp";
        var recordId = "NullListWeapon";
        var outPath = Path.Combine(Path.GetTempPath(), plugin);

        try
        {
            WriteService.CreatePlugin(plugin).Should().Contain("Created new plugin");
            WriteService.CreateRecord(plugin, "WEAP", recordId, null).Should().Contain("Created WEAP");

            var mod = WriteService.GetMutable(plugin);
            mod.Should().NotBeNull();
            var weapon = mod!.Weapons.Single(w => w.EditorID == recordId);
            weapon.ObjectTemplates.Should().BeNull();

            ElementService.AddElement(plugin, recordId, "ObjectTemplates", null, null)
                .Should().Contain("Added").And.Contain("ObjectTemplate");

            weapon.ObjectTemplates.Should().NotBeNull();
            weapon.ObjectTemplates!.Should().ContainSingle();

            WriteService.SavePlugin(plugin, outPath, null).Should().Contain("Saved");
            File.Exists(outPath).Should().BeTrue();

            using var reload = Fallout4Mod.CreateFromBinaryOverlay(
                ModPath.FromPath(outPath), Fallout4Release.Fallout4);
            var reloadedWeapon = reload.Weapons.Single(w => w.EditorID == recordId);
            reloadedWeapon.ObjectTemplates.Should().NotBeNull();
            reloadedWeapon.ObjectTemplates!.Should().ContainSingle();
        }
        finally
        {
            try { File.Delete(outPath); } catch { }
        }
    }

    private static bool Resolve(object record, bool initializeNullLists)
    {
        var method = typeof(ElementService).GetMethod(
            "TryResolve",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("ElementService.TryResolve was not found.");

        object?[] args = { record, "Items", initializeNullLists, null, null };
        var result = (bool)(method.Invoke(null, args)
            ?? throw new InvalidOperationException("ElementService.TryResolve returned null."));

        if (!result)
            throw new InvalidOperationException($"ElementService.TryResolve failed: {args[4]}");

        return result;
    }

    private sealed class ConcreteHolder
    {
        public List<TestElement>? Items { get; set; }
    }

    private sealed class InterfaceHolder
    {
        public IList<TestElement>? Items { get; set; }
    }

    private sealed class TestElement
    {
    }
}
