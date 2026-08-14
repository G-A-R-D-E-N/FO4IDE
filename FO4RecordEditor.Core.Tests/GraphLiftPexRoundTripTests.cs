using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using FO4RecordEditor.Services.Graph;
using FO4RecordEditor.Services.Papyrus;
using Xunit.Abstractions;

namespace FO4RecordEditor.Core.Tests;

public class GraphLiftPexRoundTripTests
{
    private readonly ITestOutputHelper _output;

    public GraphLiftPexRoundTripTests(ITestOutputHelper output) => _output = output;

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

        var newline = decompiled.IndexOf('\n');
        return newline >= 0 && decompiled.StartsWith("RESULT:", StringComparison.Ordinal)
            ? decompiled[(newline + 1)..]
            : decompiled;
    }

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

    private const int PexRoundTripBaseline = 28;
}
