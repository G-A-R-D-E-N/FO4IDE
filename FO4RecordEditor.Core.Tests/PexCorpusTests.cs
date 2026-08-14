using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using FO4RecordEditor.Services.Papyrus;
using Xunit.Abstractions;

namespace FO4RecordEditor.Core.Tests;

/// <summary>
/// Reads every .pex on the machine and writes it back, asserting the bytes are identical.
/// </summary>
/// <remarks>
/// This is the acceptance test for the issue #78 phase 2 back end. A hand-built sample proves the
/// writer is self-consistent with the reader; only real compiler output proves it agrees with the
/// Creation Kit, which is the thing an emitted .pex has to be indistinguishable from.
/// <para>
/// Byte-identity is the bar deliberately. "It reads back into an equal model" would pass a writer
/// that drops any field the reader also ignores -- the object size field was exactly that shape --
/// and "the game loads it" is not a check available here at all.
/// </para>
/// <para>
/// Opt-in on <c>FO4RE_PEX_CORPUS</c>, one or more roots separated by
/// <see cref="Path.PathSeparator"/>, the same shape as <see cref="PapyrusCorpusTests"/>. Unset, it
/// no-ops so a bare checkout stays green.
/// </para>
/// </remarks>
public class PexCorpusTests
{
    private readonly ITestOutputHelper _output;

    public PexCorpusTests(ITestOutputHelper output) => _output = output;

    private const string CorpusVariable = "FO4RE_PEX_CORPUS";

    private static IReadOnlyList<string> CorpusFiles()
    {
        var roots = Environment.GetEnvironmentVariable(CorpusVariable);
        if (string.IsNullOrWhiteSpace(roots)) return Array.Empty<string>();

        // PapyrusFileWalk rather than Directory.EnumerateFiles: a whole-drive root is the useful way
        // to run this, and the framework's recursive enumeration aborts the entire sweep on the first
        // directory it cannot open. See that class for the specifics.
        var files = new List<string>();
        foreach (var root in roots.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            files.AddRange(PapyrusFileWalk.EnumerateFiles(root, "*.pex"));
        }
        return files.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    [Fact]
    public void Every_compiled_script_on_disk_round_trips_byte_for_byte()
    {
        var files = CorpusFiles();
        if (files.Count == 0)
        {
            _output.WriteLine($"{CorpusVariable} is not set; corpus round trip skipped.");
            return;
        }

        int read = 0, identical = 0, foreign = 0, notPex = 0;
        var unreadable = new List<string>();
        var differing = new List<string>();

        foreach (var file in files)
        {
            byte[] original;
            PexFile pex;
            try
            {
                original = File.ReadAllBytes(file);
                switch (Classify(original))
                {
                    case Kind.NotPex: notPex++; continue;
                    case Kind.OtherGame: foreign++; continue;
                }
                pex = PexFile.FromBytes(original);
                read++;
            }
            catch (Exception ex)
            {
                unreadable.Add($"{file}: {ex.GetType().Name}: {ex.Message}");
                continue;
            }

            byte[] rewritten;
            try
            {
                rewritten = pex.ToBytes();
            }
            catch (Exception ex)
            {
                differing.Add($"{file}: write threw {ex.GetType().Name}: {ex.Message}");
                continue;
            }

            if (rewritten.AsSpan().SequenceEqual(original)) identical++;
            else differing.Add($"{file}: {Describe(original, rewritten)}");
        }

        _output.WriteLine($"{files.Count} .pex found; {notPex} skipped as not .pex at all; " +
                          $"{foreign} skipped as another game's; {read} read; {identical} byte-identical; " +
                          $"{unreadable.Count} unreadable; {differing.Count} differing.");
        foreach (var group in unreadable.GroupBy(Reason).OrderByDescending(g => g.Count()))
        {
            _output.WriteLine($"UNREADABLE x{group.Count()}: {group.Key}");
            foreach (var f in group.Take(5)) _output.WriteLine("    " + f);
        }
        foreach (var f in differing.Take(20)) _output.WriteLine("DIFFERS    " + f);

        unreadable.Should().BeEmpty("the reader half was already validated against the whole corpus");
        differing.Should().BeEmpty("a .pex the real compiler produced must survive read-then-write unchanged");
        identical.Should().Be(read);
        read.Should().BeGreaterThan(0, "a corpus root was given, so something should have been read");
    }

    private static string Reason(string entry)
    {
        int at = entry.IndexOf(": ", StringComparison.Ordinal);
        return at < 0 ? entry : entry[(at + 2)..];
    }

    private enum Kind { Fallout4, OtherGame, NotPex }

    /// <summary>What a file with a .pex extension actually turned out to be.</summary>
    /// <remarks>
    /// A whole-drive corpus root picks up two kinds of file that are not Fallout 4 compiler output
    /// and say nothing about the writer, so both are excluded rather than counted as failures:
    /// <list type="bullet">
    /// <item>Another Papyrus game's. Skyrim LE is big-endian and a different format generation, and
    /// over a thousand of them sit in the vendored Mutagen checkout as its own test fixtures.
    /// Starfield shares the little-endian magic but carries game id 4 -- one such file exists here,
    /// a Fallout 4 script accidentally built with a Starfield compiler.</item>
    /// <item>Not a .pex at all: plain text under a .pex name, from an unrelated third-party toolkit
    /// checkout that uses them as placeholder fixtures.</item>
    /// </list>
    /// Anything that IS a Fallout 4 .pex has to read and round trip; there is no third category.
    /// </remarks>
    private static Kind Classify(byte[] bytes)
    {
        if (bytes.Length < 8) return Kind.NotPex;
        uint magic = BitConverter.ToUInt32(bytes, 0);
        if (magic == 0xDEC057FAu) return Kind.OtherGame;             // Skyrim LE, big-endian
        if (magic != 0xFA57C0DEu) return Kind.NotPex;
        return BitConverter.ToUInt16(bytes, 6) == 2 ? Kind.Fallout4 : Kind.OtherGame;
    }

    private static string Describe(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return $"length {a.Length} -> {b.Length}";
        for (int i = 0; i < a.Length; i++)
            if (a[i] != b[i]) return $"same length {a.Length}, first difference at 0x{i:X} ({a[i]:X2} -> {b[i]:X2})";
        return "identical";
    }
}
