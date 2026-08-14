using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using FO4RecordEditor.Services.Graph;
using FO4RecordEditor.Services.Papyrus;
using Xunit.Abstractions;

namespace FO4RecordEditor.Core.Tests;

/// <summary>
/// Source in, graph, source out, and the same instructions at both ends.
/// </summary>
/// <remarks>
/// This is the only claim about the lifter worth making. A graph that merely loads proves nothing:
/// the question is whether the graph <em>means</em> what the file meant, and the only way to answer
/// it is to compile both and compare instruction for instruction with the same
/// <see cref="PexComparer"/> the Creation Kit differential uses.
/// <para>
/// The corpus is the fixture reference sources, which are hand-written Papyrus covering branches,
/// loops, states, remote events, arrays, casts and early returns. They were written as the target
/// of the lowering, which makes them an independent input here rather than something shaped to suit
/// the lifter.
/// </para>
/// </remarks>
public class GraphLiftRoundTripTests
{
    private readonly ITestOutputHelper _output;

    public GraphLiftRoundTripTests(ITestOutputHelper output) => _output = output;

    public static IEnumerable<object[]> Fixtures =>
        GraphFixtures.All.Select(f => new object[] { f.Name });

    private static PapyrusScriptIndex Index(string extraRoot) =>
        PapyrusCompiler.IndexFor(new[] { extraRoot, TestRoots.BaseStubs, TestRoots.GraphScripts });

    /// <summary>Compiles source the way GraphCompiler does, publishing it to a root first.</summary>
    private static (PexFile? Pex, string Diagnostics) CompileText(string text)
    {
        var root = Directory.CreateTempSubdirectory("fo4re-lift-");
        try
        {
            File.WriteAllText(Path.Combine(root.FullName, "Fixture.psc"), text);
            var parsed = PapyrusParser.Parse(text, "Fixture.psc");
            var compiled = new PapyrusCompiler(Index(root.FullName))
                .Compile(parsed, sourceFileName: "Fixture.psc");

            return (compiled.Pex,
                string.Join(" | ", compiled.Diagnostics.Select(d => $"{d.Code} {d.Message}")));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    /// <summary>Parses source, lifts it to a graph, and compiles the graph back to a .pex.</summary>
    private (PexFile? Pex, string Report) ThroughGraph(string text)
    {
        var root = Directory.CreateTempSubdirectory("fo4re-lift-src-");
        try
        {
            File.WriteAllText(Path.Combine(root.FullName, "Fixture.psc"), text);
            var index = Index(root.FullName);

            var parsed = PapyrusParser.Parse(text, "Fixture.psc");
            var lifted = new GraphLifter(index).Lift(parsed);

            if (!lifted.Success)
            {
                return (null, "LIFT REFUSED: " + string.Join(" | ",
                    lifted.Diagnostics.Select(d => d.Code + " " + d.Message)));
            }

            var compiled = new GraphCompiler(index).Compile(lifted.Document!, new GraphCompileOptions());
            if (!compiled.Success)
            {
                return (null, "REGENERATED SOURCE DID NOT COMPILE: " + string.Join(" | ",
                    compiled.Diagnostics.Select(d => d.Code + " " + d.Message))
                    + "\n---- generated ----\n" + compiled.Source);
            }

            return (compiled.Pex, compiled.Source ?? "");
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void A_fixture_source_survives_a_trip_through_the_graph(string name)
    {
        var fixture = GraphFixtures.All.First(f => f.Name == name);

        var (original, originalDiagnostics) = CompileText(fixture.Reference);
        original.Should().NotBeNull($"the reference itself should compile; {originalDiagnostics}");

        var (rebuilt, report) = ThroughGraph(fixture.Reference);
        rebuilt.Should().NotBeNull(report);

        var difference = PexComparer.FirstDifference(original!, rebuilt!);
        if (difference != null) _output.WriteLine("REGENERATED SOURCE:\n" + report);

        difference.Should().BeNull(
            $"{name}: the graph should mean exactly what the source meant");
    }

    [Fact]
    public void A_source_the_lifter_cannot_express_is_refused_rather_than_mangled()
    {
        // A loop whose condition calls a function. The graph evaluates a condition from a data wire
        // and binds an impure producer to a local before the loop, so it would be tested once. That
        // is a different script, so it has to be refused.
        var text = """
            Scriptname Fixture extends ObjectReference

            Event OnLoad()
                While (IsEnabled())
                    Utility.Wait(1.0)
                EndWhile
            EndEvent
            """;

        var root = Directory.CreateTempSubdirectory("fo4re-lift-refuse-");
        try
        {
            File.WriteAllText(Path.Combine(root.FullName, "Fixture.psc"), text);
            var lifted = new GraphLifter(Index(root.FullName))
                .Lift(PapyrusParser.Parse(text, "Fixture.psc"));

            lifted.Success.Should().BeFalse("a condition that is called each pass cannot be lifted");
            lifted.Diagnostics.Should().Contain(d => d.Message.Contains("condition"),
                string.Join(" | ", lifted.Diagnostics.Select(d => d.Message)));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void A_source_that_does_not_parse_is_refused_without_a_document()
    {
        var lifted = new GraphLifter(Index(TestRoots.BaseStubs))
            .Lift(PapyrusParser.Parse("Scriptname Fixture extends\n Function (", "Fixture.psc"));

        lifted.Success.Should().BeFalse();
        lifted.Document.Should().BeNull("nothing should be handed back for a file that did not parse");
    }
}
