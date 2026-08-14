using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using FO4RecordEditor.Services.Graph;
using FO4RecordEditor.Services.Papyrus;
using Xunit.Abstractions;

namespace FO4RecordEditor.Core.Tests;

public class GraphRoundTripTests
{
    private readonly ITestOutputHelper _output;

    public GraphRoundTripTests(ITestOutputHelper output) => _output = output;

    public static IEnumerable<object[]> Fixtures =>
        GraphFixtures.All.Select(f => new object[] { f.Name });

    private static GraphFixture Fixture(string name) =>
        GraphFixtures.All.First(f => f.Name == name);

    private static PapyrusCompiler TextCompiler() =>
        new(PapyrusCompiler.IndexFor(new[] { TestRoots.BaseStubs, TestRoots.GraphScripts }));

    private static PexFile CompileReference(GraphFixture fixture)
    {

        var root = Directory.CreateTempSubdirectory("fo4re-reference-");
        try
        {
            File.WriteAllText(Path.Combine(root.FullName, "Fixture.psc"), fixture.Reference);
            var index = PapyrusCompiler.IndexFor(new[] { root.FullName, TestRoots.BaseStubs, TestRoots.GraphScripts });
            var script = PapyrusParser.Parse(fixture.Reference, "Fixture.psc");
            var compiled = new PapyrusCompiler(index).Compile(script, sourceFileName: "Fixture.psc");

            compiled.Success.Should().BeTrue(
                $"{fixture.Name}'s reference script should compile; "
                + string.Join(" | ", compiled.Diagnostics.Select(d => $"{d.Code} {d.Message}")));
            return compiled.Pex!;
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Every_fixture_graph_compiles_clean(string name)
    {
        var fixture = Fixture(name);
        var result = GraphTestEnvironment.Compile(fixture.Build());

        if (result.Source != null) _output.WriteLine(result.Source);
        result.Errors.Should().BeEmpty(GraphTestEnvironment.Describe(result.Diagnostics));
        result.Success.Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Generated_source_always_reparses_clean(string name)
    {

        var result = GraphTestEnvironment.Compile(Fixture(name).Build());

        PapyrusParser.Parse(result.Source!, "Fixture.psc")
            .Diagnostics.Where(d => d.Severity == PapyrusSeverity.Error)
            .Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Oracle1_the_compiled_object_decompiles_to_source_that_recompiles(string name)
    {

        var fixture = Fixture(name);
        var first = GraphTestEnvironment.Compile(fixture.Build());
        first.Success.Should().BeTrue(GraphTestEnvironment.Describe(first.Diagnostics));

        var directory = Directory.CreateTempSubdirectory("fo4re-roundtrip-");
        try
        {
            var pexPath = Path.Combine(directory.FullName, "Fixture.pex");
            first.Pex!.WriteFile(pexPath);

            var body = StripResultLine(PapyrusDecompiler.Decompile(pexPath, assembly: false));

            if (body.Contains(".code", StringComparison.OrdinalIgnoreCase))
            {
                _output.WriteLine($"{name}: decompiler produced an assembly listing; not comparable.");
                return;
            }

            var reparsed = PapyrusParser.Parse(body, "Fixture.psc");
            reparsed.Diagnostics.Where(d => d.Severity == PapyrusSeverity.Error)
                .Should().BeEmpty($"{name}: decompiled source should parse");

            reparsed.Name.Should().BeEquivalentTo(first.Ir!.Name);

            (reparsed.Extends ?? "").Should().BeOneOf(first.Ir.Extends ?? "", "ScriptObject");

            reparsed.Events.Concat(reparsed.States.SelectMany(s => s.Events))
                .Select(e => e.Name).OrderBy(n => n)
                .Should().BeEquivalentTo(
                    first.Ir.Callables.Where(c => c.IsEvent).Select(c => c.Name).OrderBy(n => n));
            reparsed.Functions.Concat(reparsed.States.SelectMany(s => s.Functions))
                .Select(f => f.Name).OrderBy(n => n)
                .Should().BeEquivalentTo(
                    first.Ir.Callables.Where(c => !c.IsEvent).Select(c => c.Name).OrderBy(n => n));

            File.WriteAllText(Path.Combine(directory.FullName, "Fixture.psc"), body);
            var index = PapyrusCompiler.IndexFor(
                new[] { directory.FullName, TestRoots.BaseStubs, TestRoots.GraphScripts });

            var second = new PapyrusCompiler(index).Compile(reparsed, sourceFileName: "Fixture.psc");
            second.Success.Should().BeTrue(
                $"{name}: recompiling the decompiled source should succeed; "
                + string.Join(" | ", second.Diagnostics.Select(d => $"{d.Code} {d.Message}")));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Oracle2_the_graph_produces_what_a_person_would_have_written(string name)
    {

        var fixture = Fixture(name);
        var generated = GraphTestEnvironment.Compile(fixture.Build());
        generated.Success.Should().BeTrue(GraphTestEnvironment.Describe(generated.Diagnostics));

        var reference = CompileReference(fixture);

        var difference = PexComparer.FirstDifference(reference, generated.Pex!, "handwritten", "graph");
        if (difference != null)
        {
            _output.WriteLine("--- generated ---");
            _output.WriteLine(generated.Source);
            _output.WriteLine("--- reference ---");
            _output.WriteLine(fixture.Reference);
        }

        difference.Should().BeNull($"{name} should compile to what the hand-written script does");
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Oracle3_the_generated_script_has_the_same_shape_as_the_reference(string name)
    {

        var fixture = Fixture(name);
        var generated = GraphTestEnvironment.Compile(fixture.Build());
        generated.Success.Should().BeTrue(GraphTestEnvironment.Describe(generated.Diagnostics));

        var mine = PapyrusParser.Parse(generated.Source!, "Fixture.psc");
        var theirs = PapyrusParser.Parse(fixture.Reference, "Fixture.psc");

        mine.Name.Should().BeEquivalentTo(theirs.Name);
        (mine.Extends ?? "").Should().BeEquivalentTo(theirs.Extends ?? "");

        mine.Events.Select(e => e.Name).OrderBy(n => n)
            .Should().BeEquivalentTo(theirs.Events.Select(e => e.Name).OrderBy(n => n));
        mine.Functions.Select(f => f.Name).OrderBy(n => n)
            .Should().BeEquivalentTo(theirs.Functions.Select(f => f.Name).OrderBy(n => n));
        mine.Properties.Select(p => p.Name).OrderBy(n => n)
            .Should().BeEquivalentTo(theirs.Properties.Select(p => p.Name).OrderBy(n => n));
        mine.Variables.Select(v => v.Name).OrderBy(n => n)
            .Should().BeEquivalentTo(theirs.Variables.Select(v => v.Name).OrderBy(n => n));
    }

    [Fact]
    public void The_whole_fixture_suite_reports_its_own_numbers()
    {

        int attempted = 0, compiled = 0, oracle2 = 0, oracle1 = 0;
        var refusals = new List<string>();

        foreach (var fixture in GraphFixtures.All)
        {
            attempted++;
            var result = GraphTestEnvironment.Compile(fixture.Build());
            if (!result.Success)
            {
                refusals.Add($"{fixture.Name}: {GraphTestEnvironment.Describe(result.Errors)}");
                continue;
            }
            compiled++;

            if (PexComparer.FirstDifference(CompileReference(fixture), result.Pex!) == null) oracle2++;
            if (DecompilesAndRecompiles(result)) oracle1++;
        }

        _output.WriteLine(
            $"BENCH graph.fixtures attempted={attempted} compiled={compiled} "
            + $"matchedHandWritten={oracle2} decompileRecompiles={oracle1}");
        foreach (var refusal in refusals) _output.WriteLine("  REFUSED " + refusal);

        attempted.Should().BeGreaterThanOrEqualTo(24, "the suite is meant to cover 24 patterns");
        compiled.Should().Be(attempted, "every fixture is ours, so anything less is a defect");
        oracle2.Should().Be(attempted, "generated instructions should match hand-written Papyrus");
        oracle1.Should().Be(attempted, "every object should decompile to source that recompiles");
    }

    private static bool DecompilesAndRecompiles(GraphCompileResult result)
    {
        var directory = Directory.CreateTempSubdirectory("fo4re-summary-");
        try
        {
            var pexPath = Path.Combine(directory.FullName, "Fixture.pex");
            result.Pex!.WriteFile(pexPath);

            var body = StripResultLine(PapyrusDecompiler.Decompile(pexPath, assembly: false));
            if (body.Contains(".code", StringComparison.OrdinalIgnoreCase)) return false;

            var reparsed = PapyrusParser.Parse(body, "Fixture.psc");
            if (reparsed.Diagnostics.Any(d => d.Severity == PapyrusSeverity.Error)) return false;

            File.WriteAllText(Path.Combine(directory.FullName, "Fixture.psc"), body);
            var index = PapyrusCompiler.IndexFor(
                new[] { directory.FullName, TestRoots.BaseStubs, TestRoots.GraphScripts });

            return new PapyrusCompiler(index).Compile(reparsed, sourceFileName: "Fixture.psc").Success;
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static string StripResultLine(string decompiled)
    {

        var newline = decompiled.IndexOf('\n');
        return newline >= 0 && decompiled.StartsWith("RESULT:", StringComparison.Ordinal)
            ? decompiled[(newline + 1)..]
            : decompiled;
    }
}
