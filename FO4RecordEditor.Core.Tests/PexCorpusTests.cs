using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using FO4RecordEditor.Services.Papyrus;
using Xunit.Abstractions;

namespace FO4RecordEditor.Core.Tests;



















public class PexCorpusTests
{
    private readonly ITestOutputHelper _output;

    public PexCorpusTests(ITestOutputHelper output) => _output = output;

    private const string CorpusVariable = "FO4RE_PEX_CORPUS";

    private static IReadOnlyList<string> CorpusFiles()
    {
        var roots = Environment.GetEnvironmentVariable(CorpusVariable);
        if (string.IsNullOrWhiteSpace(roots)) return Array.Empty<string>();




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















    private static Kind Classify(byte[] bytes)
    {
        if (bytes.Length < 8) return Kind.NotPex;
        uint magic = BitConverter.ToUInt32(bytes, 0);
        if (magic == 0xDEC057FAu) return Kind.OtherGame;
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
