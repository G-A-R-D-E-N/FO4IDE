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
/// The same round trip as <see cref="GraphLiftRoundTripTests"/>, but starting from a compiled
/// binary: source, .pex, decompiled text, graph, .pex, compared instruction for instruction against
/// the first .pex.
/// </summary>
/// <remarks>
/// This is the only thing that says the "open a .pex into the canvas" path is trustworthy, because
/// it is the only path that runs the decompiler and the lifter as a pair. The source round trip
/// does not exercise the decompiler at all.
/// <para>
/// It is not expected to reach the source round trip's 28/28, and that is a property of the
/// decompiler, not of the lifter: PAPYRUS.md records function bodies as best effort, and
/// GraphRoundTripTests already measured two specific fidelity limits (implicit zero initialisation
/// rendered as an explicit assignment, and a discarded call result dropped). The number is
/// therefore reported per stage with every failure named, and pinned to a baseline so a regression
/// fails rather than quietly lowering the bar.
/// </para>
/// </remarks>
public class GraphLiftPexRoundTripTests
{
    private readonly ITestOutputHelper _output;

    public GraphLiftPexRoundTripTests(ITestOutputHelper output) => _output = output;

    /// <summary>Where a fixture stopped, in the order the stages run.</summary>
    private enum Stage
    {
        Matched,
        ReferenceDidNotCompile,
        DecompiledToAssembly,
        DecompiledSourceDidNotParse,
        LiftRefused,
        GraphDidNotCompile,
        InstructionsDiffered,
    }

    private sealed record Outcome(string Name, Stage Stage, string Detail);

    private static PapyrusScriptIndex Index(string extraRoot) =>
        PapyrusCompiler.IndexFor(new[] { extraRoot, TestRoots.BaseStubs, TestRoots.GraphScripts });

    private static string StripResultLine(string decompiled)
    {
        // The decompiler prefixes a RESULT: line for its prose callers.
        var newline = decompiled.IndexOf('\n');
        return newline >= 0 && decompiled.StartsWith("RESULT:", StringComparison.Ordinal)
            ? decompiled[(newline + 1)..]
            : decompiled;
    }

    /// <summary>Runs one fixture from hand-written source all the way back to a .pex.</summary>
    private static Outcome RunOne(GraphFixture fixture)
    {
        var root = Directory.CreateTempSubdirectory("fo4re-lift-pex-");
        try
        {
            File.WriteAllText(Path.Combine(root.FullName, "Fixture.psc"), fixture.Reference);
            var index = Index(root.FullName);

            var original = new PapyrusCompiler(index)
                .Compile(PapyrusParser.Parse(fixture.Reference, "Fixture.psc"), sourceFileName: "Fixture.psc");
            if (!original.Success || original.Pex == null)
            {
                return new Outcome(fixture.Name, Stage.ReferenceDidNotCompile,
                    string.Join(" | ", original.Diagnostics.Select(d => $"{d.Code} {d.Message}")));
            }

            // The binary is the input from here on, exactly as it would be off disk.
            var pexPath = Path.Combine(root.FullName, "Fixture.pex");
            original.Pex.WriteFile(pexPath);

            var text = StripResultLine(PapyrusDecompiler.Decompile(pexPath, assembly: false));
            if (text.Contains(".code", StringComparison.OrdinalIgnoreCase))
            {
                return new Outcome(fixture.Name, Stage.DecompiledToAssembly,
                    "a body could not be structured, so that function came back as assembly");
            }

            var parsed = PapyrusParser.Parse(text, "Fixture.psc");
            var parseErrors = parsed.Diagnostics.Where(d => d.Severity == PapyrusSeverity.Error).ToList();
            if (parseErrors.Count > 0)
            {
                return new Outcome(fixture.Name, Stage.DecompiledSourceDidNotParse,
                    string.Join(" | ", parseErrors.Select(d => $"{d.Code} {d.Message}")));
            }

            // The decompiled text is what the panel would lift, so it is what gets published here.
            File.WriteAllText(Path.Combine(root.FullName, "Fixture.psc"), text);
            var lifted = new GraphLifter(Index(root.FullName)).Lift(parsed);
            if (!lifted.Success || lifted.Document == null)
            {
                return new Outcome(fixture.Name, Stage.LiftRefused,
                    string.Join(" | ", lifted.Diagnostics.Select(d => $"{d.Code} {d.Message}")));
            }

            var rebuilt = new GraphCompiler(Index(root.FullName))
                .Compile(lifted.Document, new GraphCompileOptions());
            if (!rebuilt.Success || rebuilt.Pex == null)
            {
                return new Outcome(fixture.Name, Stage.GraphDidNotCompile,
                    string.Join(" | ", rebuilt.Diagnostics.Select(d => $"{d.Code} {d.Message}")));
            }

            var difference = PexComparer.FirstDifference(original.Pex, rebuilt.Pex, "original", "through the graph");
            return difference == null
                ? new Outcome(fixture.Name, Stage.Matched, "")
                : new Outcome(fixture.Name, Stage.InstructionsDiffered, difference);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void A_compiled_binary_survives_a_trip_through_the_graph()
    {
        var outcomes = GraphFixtures.All.Select(RunOne).ToList();
        var matched = outcomes.Count(o => o.Stage == Stage.Matched);

        _output.WriteLine(
            $"PEX LIFT attempted={outcomes.Count} matched={matched} "
            + string.Join(" ", outcomes.Where(o => o.Stage != Stage.Matched)
                .GroupBy(o => o.Stage)
                .OrderBy(g => g.Key)
                .Select(g => $"{g.Key}={g.Count()}")));

        foreach (var failure in outcomes.Where(o => o.Stage != Stage.Matched))
        {
            _output.WriteLine($"  {failure.Name}: {failure.Stage}: {failure.Detail}");
        }

        matched.Should().Be(PexRoundTripBaseline,
            "the .pex round trip is pinned to its measured value, so a regression fails here; "
            + "raising the baseline is the only way to record an improvement");
    }

    /// <summary>
    /// Fixtures that survive the binary round trip today. Measured, not chosen.
    /// </summary>
    /// <remarks>
    /// It started at 12 of 28. The other 16 were four decompiler defects, each named and fixed
    /// rather than absorbed into the baseline: locals hoisted to a declaration that compiles to a
    /// zero assignment the original never had, a call whose result nothing reads dropped with its
    /// temporary, a short circuit read as two separate Ifs so the first operand stopped guarding
    /// anything, and the compiler's own cast to Bool written back as an author's cast.
    /// </remarks>
    private const int PexRoundTripBaseline = 28;
}
