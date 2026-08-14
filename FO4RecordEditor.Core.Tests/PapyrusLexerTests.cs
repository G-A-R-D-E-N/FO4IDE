using System.Linq;
using FluentAssertions;
using FO4RecordEditor.Services.Papyrus;

namespace FO4RecordEditor.Core.Tests;

public class PapyrusLexerTests
{
    private static PapyrusTokenKind[] Kinds(string source) =>
        PapyrusLexer.Lex(source).Tokens.Select(t => t.Kind).ToArray();

    private static (IReadOnlyList<PapyrusToken> Tokens, IReadOnlyList<PapyrusDiagnostic> Diagnostics) Lex(string source) =>
        PapyrusLexer.Lex(source);

    [Theory]
    [InlineData("ScriptName", PapyrusTokenKind.ScriptName)]
    [InlineData("scriptname", PapyrusTokenKind.ScriptName)]
    [InlineData("SCRIPTNAME", PapyrusTokenKind.ScriptName)]
    [InlineData("EndIf", PapyrusTokenKind.EndIf)]
    [InlineData("endif", PapyrusTokenKind.EndIf)]
    [InlineData("AutoReadOnly", PapyrusTokenKind.AutoReadOnly)]
    public void Keywords_are_case_insensitive(string word, PapyrusTokenKind expected)
    {
        Lex(word).Tokens[0].Kind.Should().Be(expected);
    }

    [Fact]
    public void Keyword_table_matches_the_language_reference_count()
    {
        // The Creation Kit wiki's Keyword Reference lists exactly 45. If this fails, either a
        // keyword was invented or one was dropped -- both are grammar bugs, not test bugs.
        PapyrusKeywords.All.Should().HaveCount(45);
    }

    [Fact]
    public void Identifier_that_merely_contains_a_keyword_is_not_a_keyword()
    {
        Lex("IfState").Tokens[0].Kind.Should().Be(PapyrusTokenKind.Identifier);
        Lex("_ifs").Tokens[0].Kind.Should().Be(PapyrusTokenKind.Identifier);
    }

    [Fact]
    public void Newline_terminates_a_statement()
    {
        Kinds("x\ny").Should().Equal(
            PapyrusTokenKind.Identifier,
            PapyrusTokenKind.Newline,
            PapyrusTokenKind.Identifier,
            PapyrusTokenKind.EndOfFile);
    }

    [Fact]
    public void Backslash_continues_a_statement_onto_the_next_line()
    {
        Kinds("x = 1 + \\\n 2").Should().Equal(
            PapyrusTokenKind.Identifier,
            PapyrusTokenKind.Assign,
            PapyrusTokenKind.IntLiteral,
            PapyrusTokenKind.Plus,
            PapyrusTokenKind.IntLiteral,
            PapyrusTokenKind.EndOfFile);
    }

    [Fact]
    public void Continuation_still_joins_when_a_comment_follows_the_backslash()
    {
        // "you can put a comment after the slash if you wish" -- Script File Structure.
        Kinds("x = 1 + \\ ; keep going\n 2").Should().Equal(
            PapyrusTokenKind.Identifier,
            PapyrusTokenKind.Assign,
            PapyrusTokenKind.IntLiteral,
            PapyrusTokenKind.Plus,
            PapyrusTokenKind.IntLiteral,
            PapyrusTokenKind.EndOfFile);
    }

    [Fact]
    public void Backslash_inside_a_comment_does_not_continue_the_line()
    {
        // The slash is inside the comment, so line 2 is its own statement.
        Kinds("; comment \\\nx").Should().Equal(
            PapyrusTokenKind.Newline,
            PapyrusTokenKind.Identifier,
            PapyrusTokenKind.EndOfFile);
    }

    [Fact]
    public void Single_line_comment_runs_to_end_of_line()
    {
        Kinds("a ; b c d\ne").Should().Equal(
            PapyrusTokenKind.Identifier,
            PapyrusTokenKind.Newline,
            PapyrusTokenKind.Identifier,
            PapyrusTokenKind.EndOfFile);
    }

    [Fact]
    public void Block_comment_spans_lines_and_is_not_a_single_line_comment()
    {
        Kinds("a ;/ still\ncommented\n/; b").Should().Equal(
            PapyrusTokenKind.Identifier,
            PapyrusTokenKind.Identifier,
            PapyrusTokenKind.EndOfFile);
    }

    [Fact]
    public void Unterminated_block_comment_is_reported()
    {
        var (_, diagnostics) = Lex("a ;/ never closed");
        diagnostics.Should().ContainSingle()
            .Which.Code.Should().Be(PapyrusDiagnosticCodes.UnterminatedBlockComment);
    }

    [Fact]
    public void Doc_comment_is_a_token_carrying_its_inner_text()
    {
        var token = Lex("{hello\nworld}").Tokens[0];
        token.Kind.Should().Be(PapyrusTokenKind.DocComment);
        token.Text.Should().Be("hello\nworld");
    }

    [Fact]
    public void String_escapes_are_decoded_into_the_token_text()
    {
        Lex("\"a\\nb\\t\\\"c\\\"\"").Tokens[0].Text.Should().Be("a\nb\t\"c\"");
    }

    [Fact]
    public void Unknown_escape_keeps_both_characters_so_windows_paths_survive()
    {
        // Dropping the backslash would silently corrupt "Data\Scripts" in a string literal.
        Lex("\"Data\\Scripts\"").Tokens[0].Text.Should().Be("Data\\Scripts");
    }

    [Fact]
    public void Unterminated_string_is_cut_at_the_line_end_and_reported()
    {
        var (tokens, diagnostics) = Lex("\"oops\nx");
        diagnostics.Should().ContainSingle()
            .Which.Code.Should().Be(PapyrusDiagnosticCodes.UnterminatedString);
        // The rest of the file must still tokenise, or one stray quote would blank the outline.
        tokens.Select(t => t.Kind).Should().Contain(PapyrusTokenKind.Identifier);
    }

    [Theory]
    [InlineData("10", PapyrusTokenKind.IntLiteral)]
    [InlineData("0x1F2C8", PapyrusTokenKind.IntLiteral)]
    [InlineData("0XAB", PapyrusTokenKind.IntLiteral)]
    [InlineData("1.5234", PapyrusTokenKind.FloatLiteral)]
    [InlineData("60.0f", PapyrusTokenKind.FloatLiteral)]
    [InlineData("3F", PapyrusTokenKind.FloatLiteral)]
    public void Numeric_literal_kinds(string source, PapyrusTokenKind expected)
    {
        Lex(source).Tokens[0].Kind.Should().Be(expected);
    }

    [Fact]
    public void Minus_is_an_operator_not_part_of_the_literal()
    {
        // Otherwise "a-1" would lex as two operands and never parse as subtraction.
        Kinds("a-1").Should().Equal(
            PapyrusTokenKind.Identifier,
            PapyrusTokenKind.Minus,
            PapyrusTokenKind.IntLiteral,
            PapyrusTokenKind.EndOfFile);
    }

    [Fact]
    public void Hex_literal_without_digits_is_reported()
    {
        Lex("0x").Diagnostics.Should().ContainSingle()
            .Which.Code.Should().Be(PapyrusDiagnosticCodes.MalformedNumber);
    }

    [Fact]
    public void Two_character_operators_win_over_one_character_ones()
    {
        Kinds("a == b != c >= d <= e && f || g += 1").Should().Equal(
            PapyrusTokenKind.Identifier, PapyrusTokenKind.Equal,
            PapyrusTokenKind.Identifier, PapyrusTokenKind.NotEqual,
            PapyrusTokenKind.Identifier, PapyrusTokenKind.GreaterEqual,
            PapyrusTokenKind.Identifier, PapyrusTokenKind.LessEqual,
            PapyrusTokenKind.Identifier, PapyrusTokenKind.And,
            PapyrusTokenKind.Identifier, PapyrusTokenKind.Or,
            PapyrusTokenKind.Identifier, PapyrusTokenKind.PlusAssign,
            PapyrusTokenKind.IntLiteral,
            PapyrusTokenKind.EndOfFile);
    }

    [Fact]
    public void Spans_carry_one_based_line_and_column()
    {
        var tokens = Lex("ab\n  cd").Tokens;
        var cd = tokens.First(t => t.Text == "cd");
        cd.Span.Line.Should().Be(2);
        cd.Span.Column.Should().Be(3);
        cd.Span.Start.Should().Be(5);
        cd.Span.Length.Should().Be(2);
    }

    [Fact]
    public void Carriage_returns_do_not_shift_line_numbers()
    {
        // Every real .psc in the corpus is CRLF; getting this wrong offsets every diagnostic.
        var tokens = Lex("a\r\nb\r\nc").Tokens;
        tokens.First(t => t.Text == "c").Span.Line.Should().Be(3);
    }

    [Fact]
    public void Unexpected_character_is_reported_and_skipped()
    {
        var (tokens, diagnostics) = Lex("a # b");
        diagnostics.Should().ContainSingle()
            .Which.Code.Should().Be(PapyrusDiagnosticCodes.UnexpectedCharacter);
        tokens.Where(t => t.Kind == PapyrusTokenKind.Identifier).Should().HaveCount(2);
    }
}
