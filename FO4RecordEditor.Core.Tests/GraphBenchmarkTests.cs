using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using FluentAssertions;
using FO4RecordEditor.Services.Graph;
using FO4RecordEditor.Services.Papyrus;
using Xunit.Abstractions;

namespace FO4RecordEditor.Core.Tests;

public class GraphBenchmarkTests
{
    private const string BenchVariable = "FO4RE_GRAPH_BENCH";
    private const int Iterations = 30;

    private readonly ITestOutputHelper _output;

    public GraphBenchmarkTests(ITestOutputHelper output) => _output = output;

    private static bool Enabled =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(BenchVariable));

    private void WriteContext()
    {
        _output.WriteLine($"BENCH context framework={RuntimeInformation.FrameworkDescription}");
        _output.WriteLine($"BENCH context os={RuntimeInformation.OSDescription}");
        _output.WriteLine($"BENCH context cpus={Environment.ProcessorCount}");
        _output.WriteLine($"BENCH context configuration={Configuration()}");
    }

    private static string Configuration()
    {
#if DEBUG
        return "Debug";
#else
        return "Release";
#endif
    }

    private static (double Median, double P95) Summarise(List<double> samples)
    {
        samples.Sort();
        double median = samples[samples.Count / 2];
        double p95 = samples[Math.Min(samples.Count - 1, (int)(samples.Count * 0.95))];
        return (median, p95);
    }

    [Fact]
    public void Graph_translation_speed()
    {
        if (!Enabled)
        {
            _output.WriteLine($"{BenchVariable} is not set; not benchmarking.");
            return;
        }

        WriteContext();

        var fixtures = GraphFixtures.All;
        var compiler = GraphTestEnvironment.Compiler();
        int totalNodes = fixtures.Sum(f => f.Build().Nodes.Count);

        foreach (var fixture in fixtures) compiler.Compile(fixture.Build(), new GraphCompileOptions { StopAfterSource = true });

        var emitSamples = new List<double>();
        var fullSamples = new List<double>();

        for (int i = 0; i < Iterations; i++)
        {
            var documents = fixtures.Select(f => f.Build()).ToList();

            var watch = Stopwatch.StartNew();
            foreach (var document in documents)
                compiler.Compile(document, new GraphCompileOptions { StopAfterSource = true });
            watch.Stop();
            emitSamples.Add(watch.Elapsed.TotalMilliseconds);

            watch.Restart();
            foreach (var document in documents) compiler.Compile(document);
            watch.Stop();
            fullSamples.Add(watch.Elapsed.TotalMilliseconds);
        }

        var emit = Summarise(emitSamples);
        var full = Summarise(fullSamples);

        double nsPerNode = emit.Median * 1_000_000.0 / Math.Max(1, totalNodes);
        double nodesPerSecond = totalNodes / (emit.Median / 1000.0);

        _output.WriteLine(
            $"BENCH graph.emit fixtures={fixtures.Count} nodes={totalNodes} "
            + $"median_ms={emit.Median:F3} p95_ms={emit.P95:F3} n={Iterations}");
        _output.WriteLine(
            $"BENCH graph.emit ns_per_node={nsPerNode:F0} nodes_per_second={nodesPerSecond:F0}");
        _output.WriteLine(
            $"BENCH graph.to_pex median_ms={full.Median:F3} p95_ms={full.P95:F3} n={Iterations}");
        _output.WriteLine(
            $"BENCH graph.to_pex per_fixture_ms={full.Median / fixtures.Count:F3}");

        emit.Median.Should().BeLessThan(4000, "generating source for 24 graphs should stay well under four seconds");
        full.Median.Should().BeLessThan(20000, "compiling 24 graphs to .pex should stay well under twenty seconds");
    }

    [Fact]
    public void Compile_success_rate_across_the_fixture_suite()
    {
        if (!Enabled)
        {
            _output.WriteLine($"{BenchVariable} is not set; not benchmarking.");
            return;
        }

        WriteContext();

        int attempted = 0, clean = 0;
        var refused = new List<string>();

        foreach (var fixture in GraphFixtures.All)
        {
            attempted++;
            var result = GraphTestEnvironment.Compile(fixture.Build());
            if (result.Success && !result.Errors.Any()) clean++;
            else refused.Add($"{fixture.Name}: {GraphTestEnvironment.Describe(result.Errors)}");
        }

        _output.WriteLine($"BENCH graph.success clean={clean} attempted={attempted} refused={refused.Count}");
        foreach (var line in refused) _output.WriteLine("  REFUSED " + line);

        clean.Should().Be(attempted, "every checked-in fixture is ours, so anything less is a defect");
    }

    [Fact]
    public void Palette_build_and_search_speed()
    {
        if (!Enabled)
        {
            _output.WriteLine($"{BenchVariable} is not set; not benchmarking.");
            return;
        }

        WriteContext();

        var roots = TestRoots.RealScriptRoots().Count > 0
            ? TestRoots.RealScriptRoots()
            : new[] { TestRoots.BaseStubs };

        var watch = Stopwatch.StartNew();
        var palette = new NodePalette(PapyrusCompiler.IndexFor(roots));
        var first = palette.Search("Get", limit: 50);
        watch.Stop();

        _output.WriteLine(
            $"BENCH palette.first_search roots={roots.Count} entries={first.Total} "
            + $"cold_ms={watch.Elapsed.TotalMilliseconds:F1}");

        var samples = new List<double>();
        foreach (var query in new[] { "Add", "Get", "Set", "Is", "On", "Play", "Move" })
        {
            var timer = Stopwatch.StartNew();
            palette.Search(query, limit: 50);
            timer.Stop();
            samples.Add(timer.Elapsed.TotalMilliseconds);
        }

        var warm = Summarise(samples);
        _output.WriteLine(
            $"BENCH palette.search median_ms={warm.Median:F3} p95_ms={warm.P95:F3} n={samples.Count}");

        first.Total.Should().BeGreaterThan(0, "the palette should find something on any real root set");
    }
}
