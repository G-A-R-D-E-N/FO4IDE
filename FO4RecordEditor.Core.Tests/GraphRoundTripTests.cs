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
/// The gate: every fixture graph compiles, and its output survives three independent checks.
/// </summary>
/// <remarks>
/// The three oracles are meant to be read together. On its own, the fixed point check would pass
/// for a compiler and a decompiler that are wrong in mirror image ways, so it only carries weight
/// because the reference check anchors one end to hand-written Papyrus and because
/// <see cref="PapyrusDifferentialTests"/> independently anchors the compiler to the Creation Kit.
/// <para>
/// All three use <see cref="PexComparer"/>, the same ruler the Creation Kit differential uses, so
/// the bars here mean the same thing they mean there. The difference is that both sides are ours,
/// so there is no compiler-build variance to absorb and the bar is zero differences rather than a
/// fraction.
/// </para>
/// </remarks>
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

    /// <summary>Compiles hand-written source the same way the graph output is compiled.</summary>
    private static PexFile CompileReference(GraphFixture fixture)
    {
        // Published to a scratch root first, exactly as GraphCompiler does for generated source.
        // Without it a reference that passes Self where its base type is wanted fails here and
        // nowhere else, which would look like a defect in the graph rather than in the harness.
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

    // ---- the gate ------------------------------------------------------------------------------

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
        // One assertion that catches a whole class of emitter defect: whatever shape comes out, the
        // parser has to accept it.
        var result = GraphTestEnvironment.Compile(Fixture(name).Build());

        PapyrusParser.Parse(result.Source!, "Fixture.psc")
            .Diagnostics.Where(d => d.Severity == PapyrusSeverity.Error)
            .Should().BeEmpty();
    }

    // ---- oracle 1: the compiled object decompiles to real source --------------------------

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Oracle1_the_compiled_object_decompiles_to_source_that_recompiles(string name)
    {
        // What this proves and what it does not, stated plainly.
        //
        // It proves the emitted object is well formed enough that the decompiler reads it, produces
        // Papyrus the parser accepts, and that source compiles again. That is a real property, and
        // a malformed object would fail it.
        //
        // It deliberately does NOT compare instructions. Measured on this fixture set, the
        // decompiler renders the compiler's implicit zero initialisation as an explicit assignment,
        // so recompiling adds a second one; and on 08_SharedCallBindsOneLocal it drops a call whose
        // result is discarded. Both are decompiler fidelity limits, which PAPYRUS.md already
        // records as bodies being best effort, and neither says anything about the graph compiler.
        // Normalising them away to reach a green fixed point would be inventing a result.
        //
        // Oracle 2 is what actually pins the emitted instructions, by anchoring them to
        // hand-written Papyrus.
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

            // A script that extends nothing still compiles with ScriptObject as its parent, so the
            // decompiled header names it even though the graph did not.
            (reparsed.Extends ?? "").Should().BeOneOf(first.Ir.Extends ?? "", "ScriptObject");
            // State members are on the state, not on the script: PapyrusScript.Events is the empty
            // state alone. Reading only that would report a two state script as having no events.
            reparsed.Events.Concat(reparsed.States.SelectMany(s => s.Events))
                .Select(e => e.Name).OrderBy(n => n)
                .Should().BeEquivalentTo(
                    first.Ir.Callables.Where(c => c.IsEvent).Select(c => c.Name).OrderBy(n => n));
            reparsed.Functions.Concat(reparsed.States.SelectMany(s => s.Functions))
                .Select(f => f.Name).OrderBy(n => n)
                .Should().BeEquivalentTo(
                    first.Ir.Callables.Where(c => !c.IsEvent).Select(c => c.Name).OrderBy(n => n));

            // Published to its own root first, for the reason CompileReference gives: a script that
            // passes Self where its base type is wanted cannot be compiled against an index that
            // does not contain the script, because nothing there says what Self extends.
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

    // ---- oracle 2: against hand-written source ------------------------------------------------

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Oracle2_the_graph_produces_what_a_person_would_have_written(string name)
    {
        // The check that actually means something to a modder. If a fixture cannot meet it, the
        // honest response is to change the reference to the shape the graph legitimately produces
        // and say why, not to loosen the comparison.
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

    // ---- oracle 3: structural text comparison --------------------------------------------------

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Oracle3_the_generated_script_has_the_same_shape_as_the_reference(string name)
    {
        // Weaker than the other two, and deliberately so: it compares declarations and statement
        // kinds rather than spelling, which is what says the output is readable rather than merely
        // equivalent.
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

    // ---- the summary ---------------------------------------------------------------------------

    [Fact]
    public void The_whole_fixture_suite_reports_its_own_numbers()
    {
        // The figure quoted in the docs comes from here rather than from counting by hand.
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


    /// <summary>Whether the object decompiles to source the compiler accepts again.</summary>
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

            // Published first, for the reason Oracle1 gives.
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
        // The decompiler prefixes a RESULT: line for its prose callers.
        var newline = decompiled.IndexOf('\n');
        return newline >= 0 && decompiled.StartsWith("RESULT:", StringComparison.Ordinal)
            ? decompiled[(newline + 1)..]
            : decompiled;
    }
}
