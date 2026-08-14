using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using FO4RecordEditor.Services.Graph;
using FO4RecordEditor.Services.Papyrus;
using Xunit;

namespace FO4RecordEditor.Core.Tests;









public class GraphScriptLoaderTests
{
    private const string Source = """
        Scriptname Fixture extends ObjectReference

        Event OnLoad()
            Disable(false)
        EndEvent
        """;

    private static string[] Roots => new[] { TestRoots.BaseStubs, TestRoots.GraphScripts };

    [Fact]
    public void A_source_script_becomes_a_graph()
    {
        var directory = Directory.CreateTempSubdirectory("fo4re-loader-psc-");
        try
        {
            var path = Path.Combine(directory.FullName, "Fixture.psc");
            File.WriteAllText(path, Source);

            var result = GraphScriptLoader.Load(path, Roots);

            result.Failure.Should().BeNull();
            result.Success.Should().BeTrue(string.Join(" | ", result.Diagnostics.Select(d => d.Message)));
            result.Document!.Nodes.Should().NotBeEmpty();
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public void A_compiled_script_becomes_the_same_graph_as_its_source()
    {



        var directory = Directory.CreateTempSubdirectory("fo4re-loader-pex-");
        try
        {
            var sourcePath = Path.Combine(directory.FullName, "Fixture.psc");
            File.WriteAllText(sourcePath, Source);

            var index = PapyrusCompiler.IndexFor(new[] { directory.FullName }.Concat(Roots));
            var original = new PapyrusCompiler(index)
                .Compile(PapyrusParser.Parse(Source, "Fixture.psc"), sourceFileName: "Fixture.psc");
            original.Pex.Should().NotBeNull();

            var pexPath = Path.Combine(directory.FullName, "Fixture.pex");
            original.Pex!.WriteFile(pexPath);

            File.Delete(sourcePath);

            var result = GraphScriptLoader.Load(pexPath, Roots);
            result.Failure.Should().BeNull();
            result.Success.Should().BeTrue(string.Join(" | ", result.Diagnostics.Select(d => d.Message)));

            var rebuilt = new GraphCompiler(index).Compile(result.Document!, new GraphCompileOptions());
            rebuilt.Success.Should().BeTrue(
                string.Join(" | ", rebuilt.Diagnostics.Select(d => $"{d.Code} {d.Message}")));

            PexComparer.FirstDifference(original.Pex, rebuilt.Pex!, "original", "loaded")
                .Should().BeNull("the graph should mean what the compiled object meant");
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public void A_script_that_refuses_to_lift_comes_back_with_the_reason_and_no_document()
    {



        var directory = Directory.CreateTempSubdirectory("fo4re-loader-refuse-");
        try
        {
            var path = Path.Combine(directory.FullName, "Fixture.psc");
            File.WriteAllText(path, """
                Scriptname Fixture extends ObjectReference

                Event OnLoad()
                    While (IsEnabled())
                        Utility.Wait(1.0)
                    EndWhile
                EndEvent
                """);

            var result = GraphScriptLoader.Load(path, Roots);

            result.Failure.Should().BeNull("a refusal is an answer, not a failure to read the file");
            result.Success.Should().BeFalse();
            result.Document.Should().BeNull();
            result.Diagnostics.Should().NotBeEmpty();
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public void A_script_is_checked_against_its_own_folder()
    {



        var directory = Directory.CreateTempSubdirectory("fo4re-loader-roots-");
        try
        {
            var path = Path.Combine(directory.FullName, "Fixture.psc");
            File.WriteAllText(path, """
                Scriptname Fixture extends ObjectReference

                Event OnLoad()
                    MoveTo(Self, 0.0, 0.0, 0.0, true)
                EndEvent
                """);

            var result = GraphScriptLoader.Load(path, Roots);

            result.Failure.Should().BeNull();
            result.Success.Should().BeTrue(string.Join(" | ", result.Diagnostics.Select(d => d.Message)));
        }
        finally { directory.Delete(recursive: true); }
    }












    [Fact]
    public void A_lifted_property_read_validates_against_the_pin_the_getter_declares()
    {
        var directory = Directory.CreateTempSubdirectory("fo4re-loader-prop-");
        try
        {
            File.WriteAllText(Path.Combine(directory.FullName, "Holder.psc"),
                "Scriptname Holder extends ObjectReference\nint Property Count Auto\n");
            var path = Path.Combine(directory.FullName, "Reader.psc");
            File.WriteAllText(path, """
                Scriptname Reader extends ObjectReference

                Event OnLoad()
                    Holder store = Self as Holder
                    Debug.Trace("count " + store.Count)
                EndEvent
                """);

            var roots = new[] { directory.FullName, TestRoots.BaseStubs, TestRoots.GraphScripts };
            var result = GraphScriptLoader.Load(path, roots);

            result.Failure.Should().BeNull();
            result.Success.Should().BeTrue(string.Join(" | ", result.Diagnostics.Select(d => d.Message)));

            var validation = new GraphCompiler(GraphCompiler.IndexFor(roots)).Validate(result.Document!);
            validation.Diagnostics
                .Where(d => d.Severity == GraphSeverity.Error)
                .Select(d => $"{d.Code} {d.Message}")
                .Should().BeEmpty("a lifted property read should name a pin the getter declares");
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public void A_file_that_is_not_a_script_is_refused_by_name()
    {
        var directory = Directory.CreateTempSubdirectory("fo4re-loader-other-");
        try
        {
            var path = Path.Combine(directory.FullName, "Fixture.fograph");
            File.WriteAllText(path, "{}");

            var result = GraphScriptLoader.Load(path, Roots);

            result.Failure.Should().Contain("Fixture.fograph");
            result.Success.Should().BeFalse();
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public void A_missing_file_is_refused_rather_than_thrown()
    {
        var result = GraphScriptLoader.Load(
            Path.Combine(Path.GetTempPath(), "fo4re-not-here-" + Guid.NewGuid().ToString("N") + ".psc"),
            Roots);

        result.Failure.Should().NotBeNull();
        result.Success.Should().BeFalse();
    }

    [Theory]
    [InlineData("Fixture.psc", true)]
    [InlineData("Fixture.PEX", true)]
    [InlineData("Fixture.fograph", false)]
    [InlineData("Fixture", false)]
    public void The_openable_extensions_are_the_two_the_loader_reads(string name, bool expected) =>
        GraphScriptLoader.IsScript(name).Should().Be(expected);
}
