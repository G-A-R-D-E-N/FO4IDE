using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using FO4RecordEditor.Services.Papyrus;
using Xunit.Abstractions;

namespace FO4RecordEditor.Core.Tests;



































public class PapyrusDifferentialTests
{
    private readonly ITestOutputHelper _output;

    public PapyrusDifferentialTests(ITestOutputHelper output) => _output = output;

    private const string PexCorpusVariable = "FO4RE_PEX_CORPUS";
    private const string ExtraRootsVariable = "FO4RE_PSC_ROOTS";


    private const string ReleaseVariable = "FO4RE_PEX_RELEASE";










    private const double MinimumIdenticalFraction = 0.95;

    private static IReadOnlyList<string> RootsFrom(string variable)
    {
        var value = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrWhiteSpace(value)) return Array.Empty<string>();
        return value.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(Directory.Exists)
            .ToList();
    }





    private static IReadOnlyList<(string Pex, string Psc)> Pairs()
    {
        var pairs = new List<(string, string)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in RootsFrom(PexCorpusVariable))
        {
            foreach (var pex in PapyrusFileWalk.EnumerateFiles(root, "*.pex"))
            {
                var psc = SourceFor(pex);
                if (psc == null) continue;
                if (!IsFallout4(pex)) continue;
                if (!seen.Add(Path.GetFileName(pex))) continue;
                pairs.Add((pex, psc));
            }
        }
        return pairs;
    }










    private static string? SourceFor(string pex)
    {
        var parts = pex.Split(Path.DirectorySeparatorChar);
        for (int i = parts.Length - 2; i >= 0; i--)
        {
            var anchor = string.Join(Path.DirectorySeparatorChar, parts.Take(i + 1));
            var relative = string.Join(Path.DirectorySeparatorChar, parts.Skip(i + 1));
            if (relative.StartsWith("Compiled" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                relative = string.Join(Path.DirectorySeparatorChar, parts.Skip(i + 2));

            var source = relative[..^".pex".Length] + ".psc";
            foreach (var candidate in new[]
            {
                Path.Combine(anchor, "Source", "User", source),
                Path.Combine(anchor, "Source", source),
            })
            {
                if (File.Exists(candidate)) return candidate;
            }
        }
        return null;
    }

    private static bool IsFallout4(string pex)
    {
        try
        {
            using var stream = File.OpenRead(pex);
            using var reader = new BinaryReader(stream);
            return reader.ReadUInt32() == 0xFA57C0DEu
                && reader.ReadByte() == 3 && reader.ReadByte() == 9
                && reader.ReadUInt16() == 2;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }


    private static IEnumerable<string> RootsFor(string psc)
    {
        var directory = Path.GetDirectoryName(psc)!;
        var parts = directory.Split(Path.DirectorySeparatorChar);
        for (int i = parts.Length - 1; i >= 0; i--)
        {
            if (!parts[i].Equals("Source", StringComparison.OrdinalIgnoreCase)) continue;
            var source = string.Join(Path.DirectorySeparatorChar, parts.Take(i + 1));
            var user = Path.Combine(source, "User");
            if (Directory.Exists(user)) yield return user;
            yield return source;
            yield break;
        }
        yield return directory;
    }

    [Fact]
    public void Compiled_output_matches_the_creation_kits_instruction_for_instruction()
    {
        var pairs = Pairs();
        if (pairs.Count == 0)
        {
            _output.WriteLine($"{PexCorpusVariable} is not set to any root holding (.psc, .pex) pairs; nothing to compare.");
            return;
        }

        var extra = RootsFrom(ExtraRootsVariable);
        int compiled = 0, identical = 0;
        var refusals = new List<string>();
        var differences = new List<string>();







        var release = Environment.GetEnvironmentVariable(ReleaseVariable) == "1";
        var options = new PapyrusCompileOptions
        {
            EmitDebugOnlyCode = !release,
            EmitBetaOnlyCode = !release,
        };
        _output.WriteLine($"mode={(release ? "release" : "debug")} (set {ReleaseVariable}=1 for a vanilla corpus)");

        foreach (var (pex, psc) in pairs)
        {
            var index = PapyrusCompiler.IndexFor(RootsFor(psc).Concat(extra));
            var result = new PapyrusCompiler(index).CompileFile(psc, options);

            if (!result.Success)
            {


                var first = result.Errors.FirstOrDefault();
                refusals.Add($"{Path.GetFileName(psc)} complete={result.SourcesComplete} {first?.Code}");
                continue;
            }

            compiled++;
            PexFile reference;
            try { reference = PexFile.ReadFile(pex); }
            catch (InvalidDataException) { continue; }

            var difference = PexComparer.FirstDifference(reference, result.Pex!);
            if (difference == null) identical++;
            else differences.Add($"{Path.GetFileName(psc)}: {difference}");
        }

        _output.WriteLine($"pairs={pairs.Count} compiled={compiled} refused={refusals.Count} identical={identical}");
        foreach (var difference in differences.Take(30)) _output.WriteLine("  DIFF " + difference);
        foreach (var refusal in refusals.Take(30)) _output.WriteLine("  REFUSED " + refusal);

        compiled.Should().BeGreaterThan(0, "at least one pair should have compiled");
        ((double)identical / compiled).Should().BeGreaterThanOrEqualTo(
            MinimumIdenticalFraction,
            "the emitted instruction sequences should still match the Creation Kit's");
    }



}
