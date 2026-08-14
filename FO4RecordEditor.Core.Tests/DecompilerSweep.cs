using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using FO4RecordEditor.Services.Papyrus;
using Xunit;
using Xunit.Abstractions;

namespace FO4RecordEditor.Core.Tests;






















public class DecompilerSweep
{
    private readonly ITestOutputHelper _output;

    public DecompilerSweep(ITestOutputHelper output) => _output = output;

    [Fact]
    public void How_much_of_the_corpus_decompiles_to_the_same_program()
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

        int compiled = 0, matched = 0, assembly = 0, differed = 0, failed = 0;
        var reasons = new Dictionary<string, int>(StringComparer.Ordinal);
        var differences = new List<string>();

        var scratch = Directory.CreateTempSubdirectory("fo4re-decompile-sweep-");
        try
        {
            var pexPath = Path.Combine(scratch.FullName, "Sweep.pex");

            foreach (var path in files)
            {
                var name = Path.GetFileName(path);

                PexFile original;
                try
                {
                    var parsed = PapyrusParser.Parse(File.ReadAllText(path), name);
                    if (parsed.HasErrors) continue;

                    var first = new PapyrusCompiler(index).Compile(parsed, sourceFileName: name);



                    if (!first.Success || first.Pex == null) continue;

                    original = first.Pex;
                    compiled++;
                    original.WriteFile(pexPath);
                }
                catch { continue; }

                try
                {
                    var text = PapyrusDecompiler.Decompile(pexPath, assembly: false);
                    var newline = text.IndexOf('\n');
                    if (newline >= 0 && text.StartsWith("RESULT:", StringComparison.Ordinal))
                        text = text[(newline + 1)..];

                    if (text.Contains(".code", StringComparison.OrdinalIgnoreCase))
                    {

                        assembly++;
                        continue;
                    }

                    var reparsed = PapyrusParser.Parse(text, name);
                    if (reparsed.HasErrors)
                    {
                        failed++;
                        Count(reasons, "decompiled source did not parse");
                        continue;
                    }

                    var second = new PapyrusCompiler(index).Compile(reparsed, sourceFileName: name);
                    if (!second.Success || second.Pex == null)
                    {
                        failed++;
                        var diagnostic = second.Diagnostics.FirstOrDefault(d => d.Severity == PapyrusSeverity.Error);
                        Count(reasons, "did not recompile: " + (diagnostic?.Code ?? "unknown"));
                        continue;
                    }

                    var difference = PexComparer.FirstDifference(original, second.Pex, "original", "decompiled");
                    if (difference == null) { matched++; continue; }

                    differed++;
                    if (differences.Count < 20) differences.Add($"{name}: {difference}");
                }
                catch (Exception e)
                {
                    failed++;
                    Count(reasons, "decompiler threw " + e.GetType().Name);
                }
            }
        }
        finally
        {
            scratch.Delete(recursive: true);
        }

        var percent = compiled == 0 ? 0 : matched * 100.0 / compiled;
        _output.WriteLine($"DECOMPILE files={files.Count} compiled={compiled} matched={matched} "
                          + $"differed={differed} assembly={assembly} failed={failed} "
                          + $"fidelity={percent:F1}%");

        foreach (var reason in reasons.OrderByDescending(r => r.Value).Take(15))
            _output.WriteLine($"  REASON {reason.Value,5}  {reason.Key}");
        foreach (var difference in differences)
            _output.WriteLine("  DIFF " + difference);

        files.Should().NotBeEmpty();
    }

    private static void Count(Dictionary<string, int> into, string key) =>
        into[key] = into.TryGetValue(key, out var n) ? n + 1 : 1;
}
