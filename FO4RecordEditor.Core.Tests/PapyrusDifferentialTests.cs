using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using FO4RecordEditor.Services.Papyrus;
using Xunit.Abstractions;

namespace FO4RecordEditor.Core.Tests;

/// <summary>
/// Compiles real sources and compares the result against the <c>.pex</c> the Creation Kit produced
/// from the same file.
/// </summary>
/// <remarks>
/// The acceptance test for issue #78's code generator, and the only one that can say the emitted
/// instructions are the ones the game has been running for a decade rather than merely
/// self-consistent.
/// <para>
/// <b>The comparison is structural, and that is not a softening of the bar -- it is the bar.</b> The
/// <c>.pex</c> writer is held to byte-identity because its input is a file that already exists. A
/// code generator's input is source, and the Creation Kit's output is not a function of the source
/// alone: struct members, variables, properties and functions are written in a hash order that
/// differs between two compiles of different files, temporaries are numbered from an object-wide
/// counter with gaps in it, and the four compiler builds present in the corpus disagree with each
/// other about whether <c>int x = f()</c> writes straight into <c>x</c> and whether comparing to
/// <c>None</c> casts first. So this compares per-function instruction sequences with temporary names
/// collapsed, which is exactly the part that is a function of the source.
/// </para>
/// <para>
/// <b>Each script is compiled against its own roots.</b> Resolving a whole drive at once scores 58.8%
/// and measures the corpus rather than the compiler: 98.2% of the reachable <c>.psc</c> share a bare
/// script name with another file, 29 of them called <c>Game.psc</c>. The roots here are the source
/// tree the <c>.pex</c> was built from, plus whatever <c>FO4RE_PSC_ROOTS</c> supplies for the base
/// game.
/// </para>
/// <para>
/// Opt-in on <c>FO4RE_PEX_CORPUS</c>, roots separated by <see cref="Path.PathSeparator"/>, like the
/// other two sweeps. Unset, it no-ops so a bare checkout stays green. A pair is only formed where a
/// <c>.psc</c> sits at the layout the toolchain uses -- <c>Scripts/X.pex</c> beside
/// <c>Scripts/Source/User/X.psc</c> -- so a same-named file from an unrelated mod is never compared
/// against the wrong binary.
/// </para>
/// </remarks>
public class PapyrusDifferentialTests
{
    private readonly ITestOutputHelper _output;

    public PapyrusDifferentialTests(ITestOutputHelper output) => _output = output;

    private const string PexCorpusVariable = "FO4RE_PEX_CORPUS";
    private const string ExtraRootsVariable = "FO4RE_PSC_ROOTS";

    /// <summary>Set to 1 when the corpus is release built, as Bethesda's shipped scripts are.</summary>
    private const string ReleaseVariable = "FO4RE_PEX_RELEASE";

    /// <summary>
    /// How much of the corpus is allowed to differ before the sweep is a failure.
    /// </summary>
    /// <remarks>
    /// Not zero, and deliberately: the corpus contains output from compiler builds that disagree
    /// with each other, so a run that demanded zero would be asserting that every build agrees. The
    /// measured figure at the time of writing is 163 of 167 compiled files identical; the four are
    /// named in <c>PAPYRUS.md</c>. This is a regression guard, so it sits just under that.
    /// </remarks>
    private const double MinimumIdenticalFraction = 0.95;

    private static IReadOnlyList<string> RootsFrom(string variable)
    {
        var value = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrWhiteSpace(value)) return Array.Empty<string>();
        return value.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(Directory.Exists)
            .ToList();
    }

    /// <summary>
    /// Every <c>.pex</c> under the corpus roots that has a <c>.psc</c> in the layout the compiler
    /// writes, and is a Fallout 4 file.
    /// </summary>
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

    /// <summary>
    /// The source a compiled script came from, derived from the path rather than searched for.
    /// </summary>
    /// <remarks>
    /// The toolchain writes <c>&lt;anchor&gt;/X.pex</c> (or <c>&lt;anchor&gt;/Compiled/X.pex</c>)
    /// beside <c>&lt;anchor&gt;/Source/User/X.psc</c>, namespace folders preserved on both sides.
    /// Matching on the bare file name instead would pair a mod's script with an unrelated copy of
    /// the same name, which is the trap the corpus is full of.
    /// </remarks>
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

    /// <summary>The source roots one script compiles against: its own tree, then the shared ones.</summary>
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

        // How the corpus was built has to match how it is rebuilt here, and the two shipped
        // conventions differ: Bethesda's own scripts are release builds with DebugOnly and BetaOnly
        // stripped, while a mod is usually shipped debug. Comparing a release binary against a debug
        // rebuild reports a difference on every script that calls Debug.Trace, which is not a
        // compiler defect and drowns out the ones that are. Measured on the vanilla corpus, getting
        // this wrong costs about eleven points.
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
                // Refusing a script whose sources are not all present is the intended behaviour, not
                // a failure: an unresolved callee has unknown arity once defaults exist.
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

    // The comparison itself lives in PexComparer, shared with the graph roundtrip oracles so both
    // are measured by one ruler.
}
