using FluentAssertions;
using FO4RecordEditor.Services.Papyrus;

namespace FO4RecordEditor.Core.Tests;

public class PapyrusConversionsTests
{

    private static bool Inherits(string child, string ancestor)
    {

        var chain = new[] { "actor", "objectreference", "form" };
        int c = System.Array.IndexOf(chain, child.ToLowerInvariant());
        int a = System.Array.IndexOf(chain, ancestor.ToLowerInvariant());
        return c >= 0 && a >= 0 && c <= a;
    }

    private static readonly PapyrusType Actor = PapyrusType.Object("Actor");
    private static readonly PapyrusType ObjectRef = PapyrusType.Object("ObjectReference");
    private static readonly PapyrusType Form = PapyrusType.Object("Form");

    [Theory]
    [InlineData(PapyrusTypeKind.Int)]
    [InlineData(PapyrusTypeKind.Float)]
    [InlineData(PapyrusTypeKind.String)]
    [InlineData(PapyrusTypeKind.Var)]
    public void Anything_implicitly_becomes_a_bool(PapyrusTypeKind kind)
    {
        PapyrusConversions.IsImplicit(Of(kind), PapyrusType.Bool).Should().BeTrue();
    }

    [Fact]
    public void Objects_and_arrays_implicitly_become_bools_too()
    {
        PapyrusConversions.IsImplicit(Actor, PapyrusType.Bool, Inherits).Should().BeTrue();
        PapyrusConversions.IsImplicit(PapyrusType.ArrayOf(PapyrusType.Int), PapyrusType.Bool).Should().BeTrue();
    }

    [Fact]
    public void Anything_implicitly_becomes_a_string()
    {
        PapyrusConversions.IsImplicit(PapyrusType.Int, PapyrusType.String).Should().BeTrue();
        PapyrusConversions.IsImplicit(Actor, PapyrusType.String, Inherits).Should().BeTrue();
        PapyrusConversions.IsImplicit(PapyrusType.ArrayOf(PapyrusType.Int), PapyrusType.String).Should().BeTrue();
    }

    [Fact]
    public void Int_implicitly_becomes_a_float_but_nothing_else_does()
    {
        PapyrusConversions.IsImplicit(PapyrusType.Int, PapyrusType.Float).Should().BeTrue();
        PapyrusConversions.IsImplicit(PapyrusType.String, PapyrusType.Float).Should().BeFalse();
    }

    [Fact]
    public void Nothing_implicitly_becomes_an_int_but_float_and_string_cast_explicitly()
    {
        PapyrusConversions.IsImplicit(PapyrusType.Float, PapyrusType.Int).Should().BeFalse(
            "float to int truncates, so the compiler makes you write it");
        PapyrusConversions.IsExplicit(PapyrusType.Float, PapyrusType.Int).Should().BeTrue();
        PapyrusConversions.IsExplicit(PapyrusType.String, PapyrusType.Int).Should().BeTrue();
    }

    [Fact]
    public void An_object_implicitly_becomes_its_parent_but_not_its_child()
    {
        PapyrusConversions.IsImplicit(Actor, ObjectRef, Inherits).Should().BeTrue("Actor is an ObjectReference");
        PapyrusConversions.IsImplicit(Actor, Form, Inherits).Should().BeTrue("and transitively a Form");
        PapyrusConversions.IsImplicit(Form, Actor, Inherits).Should().BeFalse("not every Form is an Actor");
    }

    [Fact]
    public void A_downcast_is_explicit_only()
    {
        PapyrusConversions.IsExplicit(Form, Actor, Inherits).Should().BeTrue();
    }

    [Fact]
    public void Unrelated_objects_do_not_cast_either_way()
    {
        var keyword = PapyrusType.Object("Keyword");
        PapyrusConversions.IsImplicit(Actor, keyword, Inherits).Should().BeFalse();
        PapyrusConversions.IsExplicit(Actor, keyword, Inherits).Should().BeFalse();
    }

    [Fact]
    public void Everything_but_an_array_implicitly_becomes_a_var()
    {
        PapyrusConversions.IsImplicit(PapyrusType.Int, PapyrusType.Var).Should().BeTrue();
        PapyrusConversions.IsImplicit(Actor, PapyrusType.Var, Inherits).Should().BeTrue();
        PapyrusConversions.IsImplicit(PapyrusType.ArrayOf(PapyrusType.Int), PapyrusType.Var).Should().BeFalse();
    }

    [Fact]
    public void Arrays_cast_to_arrays_only_explicitly_and_only_when_the_elements_do()
    {
        var ints = PapyrusType.ArrayOf(PapyrusType.Int);
        var floats = PapyrusType.ArrayOf(PapyrusType.Float);
        var actors = PapyrusType.ArrayOf(Actor);
        var forms = PapyrusType.ArrayOf(Form);

        PapyrusConversions.IsImplicit(ints, floats).Should().BeFalse();
        PapyrusConversions.IsExplicit(ints, floats).Should().BeTrue();
        PapyrusConversions.IsExplicit(actors, forms, Inherits).Should().BeTrue();
    }

    [Fact]
    public void Nothing_casts_to_a_struct()
    {
        var point = PapyrusType.StructOf("A", "Point");
        PapyrusConversions.IsExplicit(PapyrusType.Int, point).Should().BeFalse();
        PapyrusConversions.IsExplicit(Actor, point, Inherits).Should().BeFalse();
        point.Equals(PapyrusType.StructOf("A", "Point")).Should().BeTrue("a struct still equals itself");
    }

    [Fact]
    public void None_is_assignable_to_every_reference_type()
    {
        PapyrusConversions.IsImplicit(PapyrusType.None, Actor, Inherits).Should().BeTrue();
        PapyrusConversions.IsImplicit(PapyrusType.None, PapyrusType.ArrayOf(PapyrusType.Int)).Should().BeTrue();
        PapyrusConversions.IsImplicit(PapyrusType.None, PapyrusType.StructOf("A", "Point")).Should().BeTrue();
    }

    [Fact]
    public void The_error_type_converts_both_ways_so_one_failure_does_not_cascade()
    {
        PapyrusConversions.IsImplicit(PapyrusType.Error, Actor, Inherits).Should().BeTrue();
        PapyrusConversions.IsImplicit(Actor, PapyrusType.Error, Inherits).Should().BeTrue();
    }

    private static PapyrusType Of(PapyrusTypeKind kind) => kind switch
    {
        PapyrusTypeKind.Int => PapyrusType.Int,
        PapyrusTypeKind.Float => PapyrusType.Float,
        PapyrusTypeKind.String => PapyrusType.String,
        PapyrusTypeKind.Var => PapyrusType.Var,
        PapyrusTypeKind.Bool => PapyrusType.Bool,
        _ => PapyrusType.None,
    };
}
