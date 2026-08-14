using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using FO4RecordEditor.Services.Graph.F4SE;
using Xunit.Abstractions;

namespace FO4RecordEditor.Core.Tests;

public class F4SEOracleCorpusTests
{
    private readonly ITestOutputHelper _output;

    public F4SEOracleCorpusTests(ITestOutputHelper output) => _output = output;

    private sealed record Tree(string Label, string CppDirectory, string MergedScripts, string? VanillaScripts);

    private static IReadOnlyList<Tree> Trees()
    {
        var trees = new List<Tree>();

        foreach (var root in TestRoots.RootsFrom(TestRoots.F4SESourceVariable))
        {
            foreach (var directory in Directory.EnumerateDirectories(root))
            {

                var cpp = Path.Combine(directory, "f4se");
                var modified = Path.Combine(directory, "scripts", "modified");
                var vanilla = Path.Combine(directory, "scripts", "vanilla");
                if (Directory.Exists(cpp) && Directory.Exists(modified))
                {
                    trees.Add(new Tree(
                        Path.GetFileName(directory), cpp, modified,
                        Directory.Exists(vanilla) ? vanilla : null));
                    continue;
                }

                var cpp2 = Path.Combine(directory, "src", "f4se", "f4se");
                var merged = Path.Combine(directory, "Data", "Scripts", "Source");
                if (Directory.Exists(cpp2) && Directory.Exists(merged))
                {

                    var sibling = Directory.EnumerateDirectories(root)
                        .Select(d => Path.Combine(d, "scripts", "vanilla"))
                        .FirstOrDefault(Directory.Exists);
                    trees.Add(new Tree(Path.GetFileName(directory), cpp2, merged, sibling));
                }
            }
        }

        return trees.OrderBy(t => t.Label, StringComparer.Ordinal).ToList();
    }

    [Fact]
    public void Recovered_registrations_agree_with_the_papyrus_declarations()
    {
        var trees = Trees();
        if (trees.Count == 0)
        {
            _output.WriteLine($"{TestRoots.F4SESourceVariable} is not set to an F4SE tree; nothing to cross check.");
            return;
        }

        var extractor = new F4SERegistrationExtractor();

        foreach (var tree in trees)
        {
            var recovered = extractor.ExtractDirectory(tree.CppDirectory).SelectMany(s => s.Natives).ToList();
            var oracle = F4SENativeOracle.Build(tree.MergedScripts, tree.VanillaScripts);
            var result = F4SECrossCheck.Compare(recovered, oracle);

            _output.WriteLine(
                $"{tree.Label}: recovered={result.Recovered} declared={result.Declared} matched={result.Matched} "
                + $"cppOnly={result.CppOnly.Count} pscOnly={result.PscOnly.Count} "
                + $"mismatched={result.Mismatches.Count} "
                + $"latent={result.LatentOnlyInCpp} noWait={result.NoWaitOnlyInCpp} "
                + $"scriptsRead={oracle.ScriptsRead} fragmentsRepaired={oracle.FragmentsRepaired}");

            foreach (var problem in oracle.Problems.Take(10)) _output.WriteLine("  ORACLE  " + problem);
            foreach (var mismatch in result.Mismatches.Take(25)) _output.WriteLine("  MISMATCH " + mismatch);
            foreach (var only in result.CppOnly.Take(25))
                _output.WriteLine($"  CPP-ONLY {only.PapyrusClass}.{only.FunctionName}/{only.Arity}");
            foreach (var only in result.PscOnly.Take(25)) _output.WriteLine("  PSC-ONLY " + only);

            result.Recovered.Should().BeGreaterThan(0, $"{tree.Label} should hold registrations");
            result.Mismatches.Should().BeEmpty(
                $"a recovered signature in {tree.Label} should match the declaration of the same function");
        }
    }

    [Fact]
    public void The_two_shipped_versions_differ_only_in_ways_the_diff_can_name()
    {
        var trees = Trees();
        if (trees.Count < 2)
        {
            _output.WriteLine($"{TestRoots.F4SESourceVariable} does not hold two F4SE trees; nothing to diff.");
            return;
        }

        var extractor = new F4SERegistrationExtractor();
        var surfaces = trees
            .Select(t => (t.Label, Natives: extractor.ExtractDirectory(t.CppDirectory)
                .SelectMany(s => s.Natives).ToList()))
            .OrderBy(s => s.Natives.Count)
            .ToList();

        var older = surfaces.First();
        var newer = surfaces.Last();
        var delta = F4SEVersionDiff.Compare(older.Natives, newer.Natives);

        _output.WriteLine(
            $"{older.Label} ({older.Natives.Count}) -> {newer.Label} ({newer.Natives.Count}): "
            + $"added={delta.Added.Count} removed={delta.Removed.Count} changed={delta.Changed.Count}");

        foreach (var binding in delta.Added.Take(25))
            _output.WriteLine($"  ADDED   {binding.PapyrusClass}.{binding.FunctionName}/{binding.Arity}");
        foreach (var binding in delta.Removed.Take(25))
            _output.WriteLine($"  REMOVED {binding.PapyrusClass}.{binding.FunctionName}/{binding.Arity}");
        foreach (var change in delta.Changed.Take(25)) _output.WriteLine("  CHANGED " + change);

        (newer.Natives.Count - older.Natives.Count)
            .Should().Be(delta.Added.Count - delta.Removed.Count,
                "every net registration difference should be named as added or removed");
    }
}
