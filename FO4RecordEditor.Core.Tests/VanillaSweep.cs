using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using FO4RecordEditor.Services.Graph;
using FO4RecordEditor.Services.Papyrus;
using Xunit.Abstractions;

namespace FO4RecordEditor.Core.Tests;

public class VanillaSweep
{
    private readonly ITestOutputHelper _output;

    public VanillaSweep(ITestOutputHelper output) => _output = output;

    [Fact]
    public void The_duplicate_check_refuses_nothing_that_ships()
    {
        var roots = TestRoots.RootsFrom(TestRoots.RealScriptRootsVariable);
        if (roots.Count == 0)
        {
            _output.WriteLine($"SKIP: {TestRoots.RealScriptRootsVariable} is not set.");
            return;
        }

        var files = roots
            .SelectMany(root => Directory.GetFiles(root, "*.psc", SearchOption.AllDirectories))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

        var offenders = new List<string>();
        int parsed = 0;
        int unparseable = 0;

        foreach (var path in files)
        {
            PapyrusScript script;
            try
            {
                script = PapyrusParser.Parse(File.ReadAllText(path), Path.GetFileName(path));
            }
            catch (Exception e)
            {

                unparseable++;
                _output.WriteLine($"UNPARSEABLE {Path.GetFileName(path)}: {e.GetType().Name}");
                continue;
            }

            if (script.HasErrors) { unparseable++; continue; }
            parsed++;

            foreach (var problem in PapyrusDeclarationCheck.Check(script))
                offenders.Add($"{Path.GetFileName(path)}: {problem.Message}");
        }

        _output.WriteLine(
            $"SWEEP roots={roots.Count} files={files.Count} parsed={parsed} "
            + $"unparseable={unparseable} flagged={offenders.Count}");
        foreach (var line in offenders.Take(40)) _output.WriteLine("  FLAGGED " + line);

        parsed.Should().BeGreaterThan(0, "the sweep is pointless if it read nothing");
        offenders.Should().BeEmpty(
            "a shipped script the Creation Kit compiles must not be refused here");
    }

    [Fact]
    public void How_much_of_the_corpus_lifts_into_a_graph()
    {
        var roots = TestRoots.RootsFrom(TestRoots.RealScriptRootsVariable);
        if (roots.Count == 0)
        {
            _output.WriteLine($"SKIP: {TestRoots.RealScriptRootsVariable} is not set.");
            return;
        }

        var index = PapyrusCompiler.IndexFor(roots.ToArray());
        var files = roots
            .SelectMany(root => Directory.GetFiles(root, "*.psc", SearchOption.AllDirectories))
            .OrderBy(path => path, StringComparer.Ordinal)
            .Take(1500)
            .ToList();

        int lifted = 0;
        int refused = 0;
        var reasons = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var path in files)
        {
            PapyrusScript parsed;
            try { parsed = PapyrusParser.Parse(File.ReadAllText(path), Path.GetFileName(path)); }
            catch { refused++; Count(reasons, "parser threw"); continue; }

            if (parsed.HasErrors) { refused++; Count(reasons, "did not parse"); continue; }

            GraphLiftResult result;
            try { result = new GraphLifter(index).Lift(parsed); }
            catch (Exception e) { refused++; Count(reasons, "lifter threw " + e.GetType().Name); continue; }

            if (result.Success) { lifted++; continue; }

            refused++;
            var first = result.Diagnostics.FirstOrDefault(d => d.Severity == GraphSeverity.Error);
            Count(reasons, Summarise(first?.Message ?? "unknown"));
        }

        var percent = files.Count == 0 ? 0 : lifted * 100.0 / files.Count;
        _output.WriteLine($"LIFT files={files.Count} lifted={lifted} refused={refused} "
                          + $"coverage={percent:F1}%");

        foreach (var reason in reasons.OrderByDescending(r => r.Value).Take(15))
            _output.WriteLine($"  REASON {reason.Value,5}  {reason.Key}");

        files.Should().NotBeEmpty();
    }

    private static void Count(Dictionary<string, int> into, string key) =>
        into[key] = into.TryGetValue(key, out var n) ? n + 1 : 1;

    private static string Summarise(string message)
    {
        var text = System.Text.RegularExpressions.Regex.Replace(message, @"^Line \d+: ", "");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"'[^']*'", "'x'");
        return text.Length > 110 ? text[..110] : text;
    }
}
