using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using FO4RecordEditor.Services.Papyrus;

namespace FO4RecordEditor.Core.Tests;

public class PapyrusAnalysisServiceTests
{
    [Theory]
    [InlineData("abc\ndef", 1, 1, 0)]
    [InlineData("abc\ndef", 2, 1, 4)]
    [InlineData("abc\ndef", 2, 3, 6)]
    [InlineData("abc\r\ndef", 2, 1, 5)]
    public void Offset_of_line_and_column(string text, int line, int column, int expected)
    {
        PapyrusAnalysisService.OffsetOf(text, line, column).Should().Be(expected);
    }

    [Theory]
    [InlineData("abc", 0, 1)]
    [InlineData("abc", 1, 0)]
    [InlineData("abc", 5, 1)]
    [InlineData("abc\ndef", 1, 9)]
    public void Out_of_range_positions_are_rejected_rather_than_clamped(string text, int line, int column)
    {
        PapyrusAnalysisService.OffsetOf(text, line, column).Should().Be(-1);
    }

    [Fact]
    public void Check_reports_a_clean_file()
    {
        using var root = new SourceRoot();
        var file = root.Write("A.psc", "ScriptName A\nFunction F()\nEndFunction\n");

        var result = PapyrusAnalysisService.Check(file);
        result.Should().StartWith("RESULT: 1 clean, 0 with syntax errors, 0 with name or type errors");
    }

    [Fact]
    public void Check_without_the_semantic_pass_still_says_it_only_parsed()
    {
        using var root = new SourceRoot();
        var file = root.Write("A.psc", "ScriptName A\nFunction F()\nEndFunction\n");

        var result = PapyrusAnalysisService.Check(file, semantic: false);
        result.Should().StartWith("RESULT: 1 clean, 0 with syntax errors,");
        result.Should().Contain("does not resolve names or check types");
    }

    [Fact]
    public void Check_reports_a_name_the_script_does_not_define()
    {
        using var root = new SourceRoot();
        var file = root.Write("A.psc", "ScriptName A\nFunction F()\n    int n = nope\nEndFunction\n");

        var result = PapyrusAnalysisService.Check(file);
        result.Should().Contain("1 with name or type errors");
        result.Should().Contain(PapyrusDiagnosticCodes.UnresolvedName);
    }

    [Fact]
    public void Check_counts_files_whose_sources_were_incomplete_separately()
    {
        using var root = new SourceRoot();
        var file = root.Write("A.psc", "ScriptName A extends NotOnAnyRoot\nFunction F()\n    Inherited()\nEndFunction\n");

        var result = PapyrusAnalysisService.Check(file);
        result.Should().Contain("0 with name or type errors");
        result.Should().Contain("not on the roots");
    }

    [Fact]
    public void Check_reports_the_error_position()
    {
        using var root = new SourceRoot();
        var file = root.Write("A.psc", "ScriptName A\nFunction F(\nEndFunction\n");

        var result = PapyrusAnalysisService.Check(file);
        result.Should().StartWith("RESULT: 0 clean, 1 with syntax errors");
        result.Should().Contain("A.psc(");
    }

    [Fact]
    public void Check_walks_a_folder()
    {
        using var root = new SourceRoot();
        root.Write("A.psc", "ScriptName A\n");
        root.Write("nested/B.psc", "ScriptName nested:B\n");

        PapyrusAnalysisService.Check(root.Path).Should().StartWith("RESULT: 2 clean, 0 with syntax errors");
    }

    [Fact]
    public void Check_refuses_a_pex_with_guidance()
    {
        using var root = new SourceRoot();
        var file = root.Write("A.pex", "not really a pex");

        PapyrusAnalysisService.Check(file).Should().Contain("decompile_papyrus");
    }

    [Fact]
    public void Check_reports_a_missing_path_rather_than_throwing()
    {
        PapyrusAnalysisService.Check(Path.Combine(Path.GetTempPath(), "fo4re-no-such-file.psc"))
            .Should().Contain("Not found");
    }

    [Fact]
    public void Outline_lists_declarations_with_positions()
    {
        using var root = new SourceRoot();
        var file = root.Write("A.psc", "ScriptName A\nint Property Health auto\nFunction F()\nEndFunction\n");

        var result = PapyrusAnalysisService.Outline(file);
        result.Should().Contain("Property");
        result.Should().Contain("int Property Health Auto");
        result.Should().Contain("Function F()");
    }

    [Fact]
    public void Outline_still_works_on_a_file_with_errors_and_says_so()
    {
        using var root = new SourceRoot();
        var file = root.Write("A.psc", "ScriptName A\nint Property Health auto\n%%%\n");

        var result = PapyrusAnalysisService.Outline(file);
        result.Should().Contain("syntax error");
        result.Should().Contain("Health");
    }

    [Fact]
    public void Definition_resolves_across_files_through_extends()
    {
        using var root = new SourceRoot();
        root.Write("Parent.psc", "ScriptName Parent\nFunction DoStuff()\n{Parent version}\nEndFunction\n");
        var child = root.Write("Child.psc", "ScriptName Child extends Parent\nFunction F()\n  DoStuff()\nEndFunction\n");

        var result = PapyrusAnalysisService.Definition(child, 3, 3);

        result.Should().Contain("RESULT: Function DoStuff");
        result.Should().Contain("in: Parent");
        result.Should().Contain("Parent.psc(2,10)");
        result.Should().Contain("Parent version");
    }

    [Fact]
    public void Definition_says_plainly_when_it_cannot_resolve()
    {
        using var root = new SourceRoot();
        var file = root.Write("A.psc", "ScriptName A\nFunction F()\n  GetThing().Unknown()\nEndFunction\n");

        var result = PapyrusAnalysisService.Definition(file, 3, 15);
        result.Should().StartWith("RESULT: not resolved.");
        result.Should().Contain("no type checker");
    }

    [Fact]
    public void Definition_rejects_a_position_outside_the_file()
    {
        using var root = new SourceRoot();
        var file = root.Write("A.psc", "ScriptName A\n");

        PapyrusAnalysisService.Definition(file, 99, 1).Should().Contain("outside");
    }

    [Fact]
    public void Analyze_returns_diagnostics_and_outline_from_buffer_text_not_from_disk()
    {
        using var root = new SourceRoot();

        var file = root.Write("A.psc", "ScriptName A\nint Property Health auto\n");
        var buffer = "ScriptName A\nint Property Health auto\nFunction F(\nEndFunction\n";

        var json = JObject.Parse(PapyrusAnalysisService.AnalyzeJson(buffer, file));

        ((int)json["errorCount"]!).Should().BeGreaterThan(0);
        var first = json["diagnostics"]!.First!;
        ((int)first["line"]!).Should().Be(3);
        ((string)first["severity"]!).Should().Be("error");
        json["symbols"]!.Select(s => (string)s["name"]!).Should().Contain("Health");
    }

    [Fact]
    public void Analyze_of_a_clean_buffer_reports_no_errors()
    {
        var json = JObject.Parse(PapyrusAnalysisService.AnalyzeJson("ScriptName A extends B\n"));
        ((int)json["errorCount"]!).Should().Be(0);
        ((string)json["script"]!).Should().Be("A");
        ((string)json["extends"]!).Should().Be("B");
    }

    [Fact]
    public void Analyze_survives_an_unsaved_buffer_with_no_path()
    {
        var json = JObject.Parse(PapyrusAnalysisService.AnalyzeJson("ScriptName A\n", null));
        ((int)json["errorCount"]!).Should().Be(0);
    }

    [Fact]
    public void Symbol_at_resolves_within_the_buffer_and_says_it_is_the_same_file()
    {
        using var root = new SourceRoot();
        var text = "ScriptName A\nint Property Health auto\nFunction F()\n  Health = 1\nEndFunction\n";
        var file = root.Write("A.psc", text);
        var offset = text.IndexOf("Health = 1", StringComparison.Ordinal);

        var json = JObject.Parse(PapyrusAnalysisService.SymbolAtJson(text, file, offset));

        ((bool)json["resolved"]!).Should().BeTrue();
        ((string)json["kind"]!).Should().Be("Property");
        ((bool)json["sameFile"]!).Should().BeTrue();
        ((int)json["line"]!).Should().Be(2);
    }

    [Fact]
    public void Symbol_at_reports_another_file_as_not_same_file_so_the_panel_knows_to_open_it()
    {
        using var root = new SourceRoot();
        root.Write("Parent.psc", "ScriptName Parent\nFunction DoStuff()\nEndFunction\n");
        var text = "ScriptName Child extends Parent\nFunction F()\n  DoStuff()\nEndFunction\n";
        var file = root.Write("Child.psc", text);
        var offset = text.IndexOf("DoStuff()", StringComparison.Ordinal);

        var json = JObject.Parse(PapyrusAnalysisService.SymbolAtJson(text, file, offset));

        ((bool)json["resolved"]!).Should().BeTrue();
        ((bool)json["sameFile"]!).Should().BeFalse();
        ((string)json["file"]!).Should().EndWith("Parent.psc");
    }

    [Fact]
    public void Symbol_at_reports_unresolved_as_an_answer_rather_than_an_error()
    {
        var text = "ScriptName A\nFunction F()\n  GetThing().Unknown()\nEndFunction\n";
        var json = JObject.Parse(PapyrusAnalysisService.SymbolAtJson(text, null, text.IndexOf("Unknown", StringComparison.Ordinal)));

        ((bool)json["resolved"]!).Should().BeFalse();
        json["error"].Should().BeNull("an unresolvable position is a normal outcome, not a failure");
    }

    [Fact]
    public void Symbol_at_works_on_an_unsaved_buffer_with_no_path()
    {

        var text = "ScriptName A\nint Property Health auto\nFunction F()\n  Health = 1\nEndFunction\n";
        var json = JObject.Parse(PapyrusAnalysisService.SymbolAtJson(text, null, text.IndexOf("Health = 1", StringComparison.Ordinal)));

        ((bool)json["resolved"]!).Should().BeTrue();
        ((bool)json["sameFile"]!).Should().BeTrue();
    }
}
