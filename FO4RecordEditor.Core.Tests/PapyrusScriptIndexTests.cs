using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using FO4RecordEditor.Services.Papyrus;

namespace FO4RecordEditor.Core.Tests;


public sealed class SourceRoot : IDisposable
{
    public SourceRoot()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "fo4re-psc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string Write(string relativePath, string contents)
    {
        var full = System.IO.Path.Combine(Path, relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        File.WriteAllText(full, contents);
        return full;
    }

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); }
        catch (IOException) { }
    }
}

public class PapyrusScriptIndexTests
{
    [Fact]
    public void Folder_layout_becomes_the_namespaced_script_name()
    {
        using var root = new SourceRoot();
        root.Write("MyCoolStuff/Quests/MyQuest.psc", "ScriptName MyCoolStuff:Quests:MyQuest\n");

        var index = new PapyrusScriptIndex();
        index.AddRoot(root.Path);

        index.ScriptNames.Should().Equal("MyCoolStuff:Quests:MyQuest");
        index.Resolve("MyCoolStuff:Quests:MyQuest").Should().NotBeNull();
    }

    [Fact]
    public void Unqualified_name_still_finds_a_namespaced_script()
    {

        using var root = new SourceRoot();
        root.Write("MyNamespace/MyScript.psc", "ScriptName MyNamespace:MyScript\n");

        var index = new PapyrusScriptIndex();
        index.AddRoot(root.Path);

        index.Resolve("MyScript")!.Name.Should().Be("MyNamespace:MyScript");
    }

    [Fact]
    public void First_root_wins()
    {


        using var first = new SourceRoot();
        using var second = new SourceRoot();
        first.Write("Actor.psc", "ScriptName Actor\nFunction FromF4SE()\nEndFunction\n");
        second.Write("Actor.psc", "ScriptName Actor\nFunction FromVanilla()\nEndFunction\n");

        var index = new PapyrusScriptIndex();
        index.AddRoot(first.Path);
        index.AddRoot(second.Path);

        index.Resolve("Actor")!.Functions.Single().Name.Should().Be("FromF4SE");
    }

    [Fact]
    public void Resolving_an_unknown_script_returns_null_rather_than_throwing()
    {
        using var root = new SourceRoot();
        var index = new PapyrusScriptIndex();
        index.AddRoot(root.Path);
        index.Resolve("NoSuchScript").Should().BeNull();
        index.FindFile("NoSuchScript").Should().BeNull();
    }

    [Fact]
    public void Missing_root_is_ignored()
    {
        var index = new PapyrusScriptIndex();
        index.AddRoot(Path.Combine(Path.GetTempPath(), "fo4re-does-not-exist-" + Guid.NewGuid().ToString("N")));
        index.Roots.Should().BeEmpty();
        index.Count.Should().Be(0);
    }

    [Fact]
    public void Parse_is_cached_until_the_file_changes()
    {
        using var root = new SourceRoot();
        var file = root.Write("A.psc", "ScriptName A\nFunction One()\nEndFunction\n");

        var index = new PapyrusScriptIndex();
        index.AddRoot(root.Path);

        var first = index.Resolve("A");
        ReferenceEquals(index.Resolve("A"), first).Should().BeTrue("an unchanged file must not re-parse");


        File.WriteAllText(file, "ScriptName A\nFunction One()\nEndFunction\nFunction Two()\nEndFunction\n");
        index.Resolve("A")!.Functions.Should().HaveCount(2);
    }

    [Fact]
    public void Base_chain_walks_extends_nearest_first()
    {
        using var root = new SourceRoot();
        root.Write("Child.psc", "ScriptName Child extends Middle\n");
        root.Write("Middle.psc", "ScriptName Middle extends Root\n");
        root.Write("Root.psc", "ScriptName Root\n");

        var index = new PapyrusScriptIndex();
        index.AddRoot(root.Path);

        index.BaseChain(index.Resolve("Child")!).Select(s => s.Name)
            .Should().Equal("Child", "Middle", "Root");
    }

    [Fact]
    public void Base_chain_survives_a_cycle()
    {


        using var root = new SourceRoot();
        root.Write("A.psc", "ScriptName A extends B\n");
        root.Write("B.psc", "ScriptName B extends A\n");

        var index = new PapyrusScriptIndex();
        index.AddRoot(root.Path);

        index.BaseChain(index.Resolve("A")!).Select(s => s.Name).Should().Equal("A", "B");
    }

    [Fact]
    public void Member_lookup_prefers_the_nearest_script_in_the_chain()
    {
        using var root = new SourceRoot();
        root.Write("Child.psc", "ScriptName Child extends Parent\nint Function Shared()\nEndFunction\n");
        root.Write("Parent.psc", "ScriptName Parent\nint Function Shared()\nEndFunction\nint Function OnlyOnParent()\nEndFunction\n");

        var index = new PapyrusScriptIndex();
        index.AddRoot(root.Path);
        var child = index.Resolve("Child")!;

        index.FindMember(child, "Shared", out var owner).Should().NotBeNull();
        owner!.Name.Should().Be("Child");

        index.FindMember(child, "OnlyOnParent", out var parentOwner).Should().NotBeNull();
        parentOwner!.Name.Should().Be("Parent");

        index.FindMember(child, "NotAnywhere", out _).Should().BeNull();
    }

    [Fact]
    public void Member_lookup_is_case_insensitive_like_the_language()
    {
        using var root = new SourceRoot();
        root.Write("A.psc", "ScriptName A\nint Property MyValue auto\n");
        var index = new PapyrusScriptIndex();
        index.AddRoot(root.Path);

        index.FindMember(index.Resolve("a")!, "myvalue", out _).Should().NotBeNull();
    }
}
