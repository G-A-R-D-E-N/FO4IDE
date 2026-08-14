using System.Linq;
using FluentAssertions;
using FO4RecordEditor.Services.Papyrus;

namespace FO4RecordEditor.Core.Tests;

public class PapyrusSymbolsTests
{

    private static (string Source, int Offset) Caret(string marked)
    {
        var offset = marked.IndexOf('|');
        offset.Should().BeGreaterThanOrEqualTo(0, "the test source must mark a caret with '|'");
        return (marked.Remove(offset, 1), offset);
    }

    [Fact]
    public void Document_symbols_cover_every_declaration_kind()
    {
        var script = PapyrusParser.Parse(@"ScriptName S extends Parent
import Utility
struct Point
  float X
endStruct
CustomEvent Ping
Group G
  int Property InGroup auto
EndGroup
int Property Loose auto
int myVar
Function Fn()
EndFunction
Event OnActivate(ObjectReference a)
EndEvent
State Idle
  Function Fn()
  EndFunction
EndState
");
        var symbols = PapyrusSymbols.DocumentSymbols(script);

        symbols.Select(s => s.Kind).Should().Contain(new[]
        {
            PapyrusSymbolKind.Script,
            PapyrusSymbolKind.Import,
            PapyrusSymbolKind.Struct,
            PapyrusSymbolKind.StructMember,
            PapyrusSymbolKind.CustomEvent,
            PapyrusSymbolKind.Group,
            PapyrusSymbolKind.Property,
            PapyrusSymbolKind.Variable,
            PapyrusSymbolKind.Function,
            PapyrusSymbolKind.Event,
            PapyrusSymbolKind.State,
        });

        symbols.Single(s => s.Name == "InGroup").Container.Should().Be("G");
        symbols.Count(s => s.Name == "Fn").Should().Be(2, "the state override is its own symbol");
    }

    [Fact]
    public void Definition_of_a_local_points_at_the_local()
    {
        var (source, offset) = Caret(@"ScriptName S
Function F()
  int counter = 0
  counter = |counter + 1
EndFunction
");
        var script = PapyrusParser.Parse(source);
        var symbol = PapyrusSymbols.FindDefinition(new PapyrusScriptIndex(), script, offset);

        symbol.Should().NotBeNull();
        symbol!.Kind.Should().Be(PapyrusSymbolKind.Local);
        symbol.Name.Should().Be("counter");
    }

    [Fact]
    public void A_local_declared_after_the_caret_does_not_capture_the_reference()
    {

        var (source, offset) = Caret(@"ScriptName S
int shared
Function F()
  shared = |shared + 1
  int shared = 2
EndFunction
");
        var script = PapyrusParser.Parse(source);
        var symbol = PapyrusSymbols.FindDefinition(new PapyrusScriptIndex(), script, offset);

        symbol!.Kind.Should().Be(PapyrusSymbolKind.Variable);
    }

    [Fact]
    public void Definition_of_a_parameter()
    {
        var (source, offset) = Caret(@"ScriptName S
Function F(int howMuch)
  x = |howMuch
EndFunction
");
        var script = PapyrusParser.Parse(source);
        PapyrusSymbols.FindDefinition(new PapyrusScriptIndex(), script, offset)!
            .Kind.Should().Be(PapyrusSymbolKind.Parameter);
    }

    [Fact]
    public void Definition_of_a_property_on_this_script()
    {
        var (source, offset) = Caret(@"ScriptName S
int Property Health auto
Function F()
  |Health = 10
EndFunction
");
        var script = PapyrusParser.Parse(source);
        var symbol = PapyrusSymbols.FindDefinition(new PapyrusScriptIndex(), script, offset);

        symbol!.Kind.Should().Be(PapyrusSymbolKind.Property);
        symbol.Signature.Should().Be("int Property Health Auto");
    }

    [Fact]
    public void Definition_follows_extends_into_the_parent_script()
    {
        using var root = new SourceRoot();
        root.Write("Parent.psc", "ScriptName Parent\nFunction DoStuff()\n{Parent version}\nEndFunction\n");
        var (source, offset) = Caret("ScriptName Child extends Parent\nFunction F()\n  |DoStuff()\nEndFunction\n");
        var childFile = root.Write("Child.psc", source);

        var index = new PapyrusScriptIndex();
        index.AddRoot(root.Path);
        var script = index.ParseCached(childFile)!;

        var symbol = PapyrusSymbols.FindDefinition(index, script, offset);
        symbol!.Name.Should().Be("DoStuff");
        symbol.Container.Should().Be("Parent");
        symbol.File.Should().EndWith("Parent.psc");
    }

    [Fact]
    public void Definition_of_a_global_call_through_its_script_name()
    {
        using var root = new SourceRoot();
        root.Write("Utility.psc", "ScriptName Utility Native\nfloat Function GetCurrentRealTime() global native\n");
        var (source, offset) = Caret("ScriptName S\nFunction F()\n  float t = Utility.Get|CurrentRealTime()\nEndFunction\n");
        var file = root.Write("S.psc", source);

        var index = new PapyrusScriptIndex();
        index.AddRoot(root.Path);

        var symbol = PapyrusSymbols.FindDefinition(index, index.ParseCached(file)!, offset);
        symbol!.Name.Should().Be("GetCurrentRealTime");
        symbol.Container.Should().Be("Utility");
    }

    [Fact]
    public void Definition_of_an_imported_global()
    {
        using var root = new SourceRoot();
        root.Write("Helpers.psc", "ScriptName Helpers\nint Function MyGlobal() global\nEndFunction\n");
        var (source, offset) = Caret("ScriptName S\nimport Helpers\nFunction F()\n  x = |MyGlobal()\nEndFunction\n");
        var file = root.Write("S.psc", source);

        var index = new PapyrusScriptIndex();
        index.AddRoot(root.Path);

        PapyrusSymbols.FindDefinition(index, index.ParseCached(file)!, offset)!
            .Container.Should().Be("Helpers");
    }

    [Fact]
    public void Definition_of_a_type_name_lands_on_the_script()
    {
        using var root = new SourceRoot();
        root.Write("ObjectReference.psc", "ScriptName ObjectReference extends Form Native\n");
        var (source, offset) = Caret("ScriptName S\nObject|Reference Property Ref auto\n");
        var file = root.Write("S.psc", source);

        var index = new PapyrusScriptIndex();
        index.AddRoot(root.Path);

        var symbol = PapyrusSymbols.FindDefinition(index, index.ParseCached(file)!, offset);
        symbol!.Kind.Should().Be(PapyrusSymbolKind.Script);
        symbol.Name.Should().Be("ObjectReference");
    }

    [Fact]
    public void Definition_of_the_extends_clause()
    {
        using var root = new SourceRoot();
        root.Write("Parent.psc", "ScriptName Parent\n");
        var (source, offset) = Caret("ScriptName Child extends Par|ent\n");
        var file = root.Write("Child.psc", source);

        var index = new PapyrusScriptIndex();
        index.AddRoot(root.Path);

        PapyrusSymbols.FindDefinition(index, index.ParseCached(file)!, offset)!
            .Name.Should().Be("Parent");
    }

    [Fact]
    public void Built_in_types_have_no_definition_to_jump_to()
    {
        var (source, offset) = Caret("ScriptName S\ni|nt Property X auto\n");
        var script = PapyrusParser.Parse(source);
        PapyrusSymbols.FindDefinition(new PapyrusScriptIndex(), script, offset).Should().BeNull();
    }

    [Fact]
    public void An_unresolvable_reference_returns_null_rather_than_a_guess()
    {

        var (source, offset) = Caret(@"ScriptName S
Function F()
  GetSomething().Totally|Unknown()
EndFunction
");
        var script = PapyrusParser.Parse(source);
        PapyrusSymbols.FindDefinition(new PapyrusScriptIndex(), script, offset).Should().BeNull();
    }

    [Fact]
    public void Standing_on_a_declaration_name_resolves_to_that_declaration()
    {
        var (source, offset) = Caret("ScriptName S\nint Function My|Func()\nEndFunction\n");
        var script = PapyrusParser.Parse(source);
        var symbol = PapyrusSymbols.FindDefinition(new PapyrusScriptIndex(), script, offset);

        symbol!.Name.Should().Be("MyFunc");
        symbol.Kind.Should().Be(PapyrusSymbolKind.Function);
    }

    [Fact]
    public void Hover_shows_the_signature_and_the_doc_comment()
    {
        var (source, offset) = Caret(@"ScriptName S
int Property MyProperty auto
{This property is fun}
Function F()
  x = |MyProperty
EndFunction
");
        var script = PapyrusParser.Parse(source);
        var hover = PapyrusSymbols.Hover(new PapyrusScriptIndex(), script, offset);

        hover.Should().Be("int Property MyProperty Auto\n\nThis property is fun");
    }

    [Fact]
    public void Hover_on_nothing_returns_null()
    {
        var script = PapyrusParser.Parse("ScriptName S\n");
        PapyrusSymbols.Hover(new PapyrusScriptIndex(), script, 100000).Should().BeNull();
    }
}
