using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using FO4RecordEditor.Services.Papyrus;

namespace FO4RecordEditor.Core.Tests;










public class PapyrusResolverTests : IDisposable
{
    private readonly string _root;

    public PapyrusResolverTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "fo4re-resolve-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private void Write(string scriptName, string source)
    {
        var path = Path.Combine(_root, scriptName.Replace(':', Path.DirectorySeparatorChar) + ".psc");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, source);
    }

    private PapyrusResolution Resolve(string scriptName)
    {
        var index = new PapyrusScriptIndex();
        index.AddRoot(_root);
        var script = index.Resolve(scriptName);
        script.Should().NotBeNull($"{scriptName} should have been indexed");
        return new PapyrusResolver(index).Resolve(script!);
    }


    private static List<PapyrusBinding> BindingsNamed(PapyrusResolution r, string name) =>
        r.Bindings
            .Where(kv => kv.Key is PapyrusIdentifierExpression id
                         && string.Equals(id.Name, name, StringComparison.OrdinalIgnoreCase))
            .Select(kv => kv.Value)
            .ToList();

    private static PapyrusBinding Only(PapyrusResolution r, string name)
    {
        var all = BindingsNamed(r, name);
        all.Should().HaveCount(1, $"exactly one identifier named {name} was expected");
        return all[0];
    }


    private static PapyrusType AssignedTypeIn(PapyrusResolution r, string target)
    {
        var assign = Walk(r.Script)
            .OfType<PapyrusAssignStatement>()
            .Single(a => a.Target is PapyrusIdentifierExpression id
                         && string.Equals(id.Name, target, StringComparison.OrdinalIgnoreCase));
        return r.TypeOf(assign.Value);
    }

    private static IEnumerable<PapyrusNode> Walk(PapyrusNode node)
    {
        yield return node;
        foreach (var child in node.Children)
        {
            if (child == null) continue;
            foreach (var n in Walk(child)) yield return n;
        }
    }



    [Fact]
    public void A_local_binds_to_its_definition_with_its_declared_type()
    {
        Write("A", @"
ScriptName A
Function Go()
    int count = 1
    count = 2
EndFunction");

        var r = Resolve("A");

        var binding = Only(r, "count");
        binding.Kind.Should().Be(PapyrusBindingKind.Local);
        binding.Type.Should().Be(PapyrusType.Int);
    }

    [Fact]
    public void A_parameter_binds_with_its_declared_type()
    {
        Write("A", @"
ScriptName A
Function Go(string label)
    label = ""x""
EndFunction");

        var r = Resolve("A");

        Only(r, "label").Kind.Should().Be(PapyrusBindingKind.Parameter);
        Only(r, "label").Type.Should().Be(PapyrusType.String);
    }


    [Fact]
    public void A_variable_defined_in_a_block_is_not_visible_after_it()
    {
        Write("A", @"
ScriptName A
Function Go(bool flag)
    If flag
        int inner = 1
    EndIf
    inner = 2
EndFunction");

        var r = Resolve("A");

        r.Diagnostics.Should().ContainSingle()
            .Which.Code.Should().Be(PapyrusDiagnosticCodes.UnresolvedName);
        r.Diagnostics[0].Message.Should().Contain("inner");
    }



    [Fact]
    public void An_initializer_is_resolved_before_the_variable_it_initializes_exists()
    {
        Write("A", @"
ScriptName A
int outer
Function Go()
    int outer2 = outer
EndFunction");

        var r = Resolve("A");

        Only(r, "outer").Kind.Should().Be(PapyrusBindingKind.ScriptVariable);
        r.Diagnostics.Should().BeEmpty();
    }



    [Fact]
    public void A_member_inherited_from_a_parent_binds_to_the_parents_declaration()
    {
        Write("Base", @"
ScriptName Base
int Counter
Function Tick()
EndFunction");
        Write("Child", @"
ScriptName Child extends Base
Function Go()
    Counter = 1
    Tick()
EndFunction");

        var r = Resolve("Child");

        var counter = Only(r, "Counter");
        counter.Kind.Should().Be(PapyrusBindingKind.ScriptVariable);
        counter.Owner!.Name.Should().Be("Base");
        counter.Type.Should().Be(PapyrusType.Int);

        Only(r, "Tick").Kind.Should().Be(PapyrusBindingKind.Function);
        r.Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void A_property_resolves_with_its_type()
    {
        Write("A", @"
ScriptName A
int Property Health Auto
Function Go()
    Health = 5
EndFunction");

        var r = Resolve("A");

        var health = Only(r, "Health");
        health.Kind.Should().Be(PapyrusBindingKind.Property);
        health.Type.Should().Be(PapyrusType.Int);
    }


    [Fact]
    public void Self_is_the_type_of_the_script_being_resolved()
    {
        Write("A", @"
ScriptName A
Function Go()
    A other = Self
EndFunction");

        var r = Resolve("A");

        var self = Only(r, "Self");
        self.Kind.Should().Be(PapyrusBindingKind.SelfKeyword);
        self.Type.Should().Be(PapyrusType.Object("A"));
    }


    [Fact]
    public void Parent_is_the_type_of_the_parent_script()
    {
        Write("Base", @"
ScriptName Base
Function Go()
EndFunction");
        Write("Child", @"
ScriptName Child extends Base
Function Go()
    Parent.Go()
EndFunction");

        var r = Resolve("Child");

        Only(r, "Parent").Type.Should().Be(PapyrusType.Object("Base"));
        r.Diagnostics.Should().BeEmpty();
    }


    [Fact]
    public void A_global_function_cannot_see_script_members()
    {
        Write("A", @"
ScriptName A
int Counter
int Function Go() global
    return Counter
EndFunction");

        var r = Resolve("A");

        r.Diagnostics.Should().ContainSingle()
            .Which.Message.Should().Contain("Counter");
    }



    [Fact]
    public void A_member_on_an_object_typed_expression_resolves_on_that_scripts_chain()
    {
        Write("Target", @"
ScriptName Target
float Property Weight Auto");
        Write("A", @"
ScriptName A
Target Property Thing Auto
Function Go()
    float w = Thing.Weight
EndFunction");

        var r = Resolve("A");

        var member = r.Bindings.Values.Single(b => b.Name == "Weight");
        member.Kind.Should().Be(PapyrusBindingKind.Property);
        member.Owner!.Name.Should().Be("Target");
        member.Type.Should().Be(PapyrusType.Float);
        r.Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void A_member_that_does_not_exist_on_a_known_type_is_reported()
    {
        Write("Target", @"
ScriptName Target
float Property Weight Auto");
        Write("A", @"
ScriptName A
Target Property Thing Auto
Function Go()
    float w = Thing.Nope
EndFunction");

        var r = Resolve("A");

        r.Diagnostics.Should().ContainSingle()
            .Which.Code.Should().Be(PapyrusDiagnosticCodes.UnknownMember);
        r.Diagnostics[0].Message.Should().Contain("Nope").And.Contain("Target");
    }


    [Fact]
    public void A_script_name_receiver_resolves_a_global_function()
    {
        Write("Game", @"
ScriptName Game
Actor Function GetPlayer() global native");
        Write("Actor", "ScriptName Actor");
        Write("A", @"
ScriptName A
Function Go()
    Actor p = Game.GetPlayer()
EndFunction");

        var r = Resolve("A");

        Only(r, "Game").Kind.Should().Be(PapyrusBindingKind.Script);



        r.Bindings.Values.Where(b => b.Name == "GetPlayer").Should().NotBeEmpty()
            .And.OnlyContain(b => b.Kind == PapyrusBindingKind.Function
                                  && b.Type.Equals(PapyrusType.Object("Actor")));
        r.Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void An_imported_scripts_global_function_is_callable_unqualified()
    {
        Write("Utility", @"
ScriptName Utility
float Function GetCurrentRealTime() global native");
        Write("A", @"
ScriptName A
Import Utility
Function Go()
    float t = GetCurrentRealTime()
EndFunction");

        var r = Resolve("A");

        var fn = Only(r, "GetCurrentRealTime");
        fn.Kind.Should().Be(PapyrusBindingKind.Function);
        fn.Owner!.Name.Should().Be("Utility");
        r.Diagnostics.Should().BeEmpty();
    }


    [Fact]
    public void An_import_does_not_bring_in_instance_members()
    {
        Write("Utility", @"
ScriptName Utility
int NotGlobal");
        Write("A", @"
ScriptName A
Import Utility
Function Go()
    int x = NotGlobal
EndFunction");

        var r = Resolve("A");

        r.Diagnostics.Should().ContainSingle()
            .Which.Message.Should().Contain("NotGlobal");
    }



    [Fact]
    public void An_array_element_has_the_element_type()
    {
        Write("A", @"
ScriptName A
Function Go()
    string[] names = new string[4]
    string first = names[0]
EndFunction");

        var r = Resolve("A");

        var declaration = Walk(r.Script).OfType<PapyrusDefineStatement>().First(d => d.Name == "names");
        r.TypeOf(declaration.Initializer!).Should().Be(PapyrusType.ArrayOf(PapyrusType.String));

        var element = Walk(r.Script).OfType<PapyrusIndexExpression>().Single();
        r.TypeOf(element).Should().Be(PapyrusType.String, "indexing an array yields its element type");
        r.Diagnostics.Should().BeEmpty();
    }

    [Theory]
    [InlineData("Length", PapyrusTypeKind.Int)]
    [InlineData("Find", PapyrusTypeKind.Int)]
    [InlineData("RFind", PapyrusTypeKind.Int)]
    [InlineData("Add", PapyrusTypeKind.None)]
    [InlineData("Clear", PapyrusTypeKind.None)]
    [InlineData("RemoveLast", PapyrusTypeKind.None)]
    public void Array_builtins_resolve(string member, PapyrusTypeKind expected)
    {
        Write("A", $@"
ScriptName A
Function Go()
    int[] items = new int[4]
    int x = items.{member}
EndFunction");

        var r = Resolve("A");

        r.Bindings.Values.Should().ContainSingle(b => b.Kind == PapyrusBindingKind.ArrayMember)
            .Which.Type.Kind.Should().Be(expected);
    }



    [Fact]
    public void A_struct_member_resolves_with_its_declared_type()
    {
        Write("A", @"
ScriptName A
Struct Point
    int X
    float Y
EndStruct
Function Go()
    Point p = new Point
    float y = p.Y
EndFunction");

        var r = Resolve("A");

        Only(r, "p").Type.Kind.Should().Be(PapyrusTypeKind.Struct);
        var member = r.Bindings.Values.Single(b => b.Kind == PapyrusBindingKind.StructMember);
        member.Name.Should().Be("Y");
        member.Type.Should().Be(PapyrusType.Float);
        r.Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void Two_scripts_same_named_structs_are_distinct_types()
    {
        Write("A", @"
ScriptName A
Struct Point
    int X
EndStruct");
        Write("B", @"
ScriptName B
Struct Point
    int X
EndStruct");

        var index = new PapyrusScriptIndex();
        index.AddRoot(_root);
        var resolver = new PapyrusResolver(index);
        var a = resolver.Resolve(index.Resolve("A")!);
        var b = resolver.Resolve(index.Resolve("B")!);

        var aPoint = a.Script.Structs.Single();
        var bPoint = b.Script.Structs.Single();
        PapyrusType.StructOf("A", aPoint.Name).Should().NotBe(PapyrusType.StructOf("B", bPoint.Name));
    }



    [Theory]
    [InlineData("1 + 2", PapyrusTypeKind.Int)]
    [InlineData("1 + 2.0", PapyrusTypeKind.Float)]
    [InlineData("1.0 * 2", PapyrusTypeKind.Float)]
    [InlineData("\"a\" + 1", PapyrusTypeKind.String)]
    [InlineData("1 < 2", PapyrusTypeKind.Bool)]
    [InlineData("1 == 2", PapyrusTypeKind.Bool)]
    [InlineData("true && false", PapyrusTypeKind.Bool)]
    [InlineData("!true", PapyrusTypeKind.Bool)]
    [InlineData("-3", PapyrusTypeKind.Int)]
    public void Operators_produce_the_expected_type(string expression, PapyrusTypeKind expected)
    {
        Write("A", $@"
ScriptName A
var slot
Function Go()
    slot = {expression}
EndFunction");

        var r = Resolve("A");

        AssignedTypeIn(r, "slot").Kind.Should().Be(expected);
    }

    [Fact]
    public void A_cast_has_the_type_it_casts_to_and_a_type_check_is_a_bool()
    {
        Write("A", @"
ScriptName A
var slot
Function Go(float f)
    slot = f as int
    slot = f is int
EndFunction");

        var r = Resolve("A");

        var assigns = Walk(r.Script).OfType<PapyrusAssignStatement>().ToList();
        r.TypeOf(assigns[0].Value).Should().Be(PapyrusType.Int);
        r.TypeOf(assigns[1].Value).Should().Be(PapyrusType.Bool);
    }






    [Fact]
    public void A_script_whose_parent_is_missing_reports_nothing_and_says_so()
    {
        Write("Child", @"
ScriptName Child extends SomethingNotOnDisk
Function Go()
    InheritedThing = 1
    AlsoInherited()
EndFunction");

        var r = Resolve("Child");

        r.BaseChainComplete.Should().BeFalse();
        r.Diagnostics.Should().BeEmpty("every one of those names could be declared in the missing parent");
    }

    [Fact]
    public void A_missing_import_also_suppresses_reporting()
    {
        Write("A", @"
ScriptName A
Import NotOnDisk
Function Go()
    MaybeFromTheImport()
EndFunction");

        var r = Resolve("A");

        r.BaseChainComplete.Should().BeFalse();
        r.Diagnostics.Should().BeEmpty();
    }




    [Fact]
    public void A_call_qualified_by_a_script_that_is_not_on_the_roots_is_not_a_typo()
    {
        Write("A", @"
ScriptName A
Function Go()
    SomeFrameworkNotOnDisk.RegisterFor(""A"")
EndFunction");

        var r = Resolve("A");

        r.BaseChainComplete.Should().BeFalse();
        r.Diagnostics.Should().BeEmpty();
    }


    [Fact]
    public void A_bare_unknown_name_used_as_a_value_is_still_reported()
    {
        Write("A", @"
ScriptName A
Function Go()
    int x = SomethingUndefined
EndFunction");

        var r = Resolve("A");

        r.Diagnostics.Should().ContainSingle()
            .Which.Code.Should().Be(PapyrusDiagnosticCodes.UnresolvedName);
    }

    [Fact]
    public void A_complete_script_with_a_genuine_typo_is_reported()
    {
        Write("A", @"
ScriptName A
int Counter
Function Go()
    Countr = 1
EndFunction");

        var r = Resolve("A");

        r.BaseChainComplete.Should().BeTrue();
        r.Diagnostics.Should().ContainSingle()
            .Which.Code.Should().Be(PapyrusDiagnosticCodes.UnresolvedName);
    }
}
