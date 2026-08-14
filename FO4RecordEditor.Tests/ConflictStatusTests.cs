using FluentAssertions;
using FO4RecordEditor.Services;
using Xunit;

namespace FO4RecordEditor.Tests;




public class ConflictStatusTests
{
    [Fact]
    public void Float_formatting_difference_is_not_a_conflict()
    {
        var (statuses, severity) = MutagenLoader.ClassifyRow(new[] { "1.0", "1.000000" }, "Text");
        statuses.Should().Equal("master", "identical");
        severity.Should().Be("none");
    }

    [Fact]
    public void Simple_two_way_override()
    {
        var (statuses, severity) = MutagenLoader.ClassifyRow(new[] { "10", "20" }, "Text");
        statuses.Should().Equal("master", "win");
        severity.Should().Be("override");
    }

    [Fact]
    public void Three_way_disagreement_is_a_conflict()
    {
        var (statuses, severity) = MutagenLoader.ClassifyRow(new[] { "10", "20", "30" }, "Text");
        statuses.Should().Equal("master", "lose", "win");
        severity.Should().Be("conflict");
    }

    [Fact]
    public void Column_matching_winner_but_not_last_is_override()
    {

        var (statuses, _) = MutagenLoader.ClassifyRow(new[] { "10", "20", "20" }, "Text");
        statuses.Should().Equal("master", "override", "win");
    }

    [Fact]
    public void Missing_column_is_notdefined_and_present_plus_missing_differs()
    {
        var (statuses, severity) = MutagenLoader.ClassifyRow(new[] { "10", "" }, "Text");
        statuses.Should().Equal("only", "notdefined");
        severity.Should().Be("override");
    }

    [Fact]
    public void Broken_ref_winner_is_critical()
    {
        var (statuses, severity) = MutagenLoader.ClassifyRow(new[] { "001234:Base.esp", "Null" }, "Ref");
        statuses.Should().Equal("master", "win");
        severity.Should().Be("critical");
    }

    [Fact]
    public void All_empty_is_notdefined_and_none()
    {
        var (statuses, severity) = MutagenLoader.ClassifyRow(new[] { "", "" }, "Text");
        statuses.Should().Equal("notdefined", "notdefined");
        severity.Should().Be("none");
    }

    [Fact]
    public void CanonValue_normalizes_numeric_forms()
    {
        MutagenLoader.CanonValue("1.0").Should().Be(MutagenLoader.CanonValue("1.000000"));
        MutagenLoader.CanonValue("abc").Should().Be("abc");
    }

    [Fact]
    public void CanonValue_treats_scientific_notation_as_equal_to_plain()
    {
        MutagenLoader.CanonValue("1e5").Should().Be(MutagenLoader.CanonValue("100000"));
    }

    [Fact]
    public void CanonValue_keeps_genuinely_different_numbers_distinct()
    {
        MutagenLoader.CanonValue("10").Should().NotBe(MutagenLoader.CanonValue("20"));
    }

    [Fact]
    public void CanonValue_does_not_parse_formkey_as_number()
    {

        MutagenLoader.CanonValue("001234:Base.esp").Should().Be("001234:Base.esp");
    }

    [Fact]
    public void Override_with_third_agreeing_with_winner_is_override_not_conflict()
    {

        var (_, severity) = MutagenLoader.ClassifyRow(new[] { "10", "20", "20" }, "Text");
        severity.Should().Be("override");
    }
}
