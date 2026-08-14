using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using FO4RecordEditor.Services.Graph.F4SE;
using Xunit.Abstractions;

namespace FO4RecordEditor.Core.Tests;

public class F4SECorpusTests
{
    private readonly ITestOutputHelper _output;

    public F4SECorpusTests(ITestOutputHelper output) => _output = output;

    private static readonly Regex RegistrationCall = new(@"\bRegisterFunction\s*\(", RegexOptions.Compiled);

    private static IReadOnlyList<string> ModuleDirectories()
    {
        var found = new List<string>();
        foreach (var root in TestRoots.RootsFrom(TestRoots.F4SESourceVariable))
        {
            foreach (var file in Directory.EnumerateFiles(root, "PapyrusVM.h", SearchOption.AllDirectories))
            {
                var directory = Path.GetDirectoryName(file)!;
                if (Directory.GetFiles(directory, "Papyrus*.cpp").Length > 0) found.Add(directory);
            }
        }
        return found.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(p => p, StringComparer.Ordinal).ToList();
    }

    [Fact]
    public void Every_registration_in_a_real_tree_is_recovered()
    {
        var directories = ModuleDirectories();
        if (directories.Count == 0)
        {
            _output.WriteLine(
                $"{TestRoots.F4SESourceVariable} is not set to a directory holding an F4SE source tree; nothing to scan.");
            return;
        }

        var extractor = new F4SERegistrationExtractor();
        int totalPresent = 0, totalRecovered = 0;

        foreach (var directory in directories)
        {
            int present = 0;
            foreach (var file in Directory.GetFiles(directory, "Papyrus*.cpp"))
            {
                present += RegistrationCall.Matches(
                    F4SECppScanner.BlankComments(File.ReadAllText(file))).Count;
            }

            var schemas = extractor.ExtractDirectory(directory);
            var natives = schemas.SelectMany(s => s.Natives).ToList();
            var problems = schemas.SelectMany(s => s.Problems).ToList();
            var structs = schemas.SelectMany(s => s.Structs).ToList();

            var unmapped = natives
                .Where(n => n.ReturnType.Name == F4SERegistrationExtractor.UnknownTypeName
                            || n.Parameters.Any(p => p.Type.Name == F4SERegistrationExtractor.UnknownTypeName))
                .ToList();

            var latent = natives.Count(n => n.IsLatent);
            var noWait = natives.Count(n => n.NoWait);
            var globals = natives.Count(n => n.IsGlobal);

            _output.WriteLine(
                $"{Shorten(directory)}: present={present} recovered={natives.Count} "
                + $"structs={structs.Count} globals={globals} latent={latent} noWait={noWait} "
                + $"unmappedTypes={unmapped.Count} problems={problems.Count}");

            foreach (var problem in problems.Take(20)) _output.WriteLine("  PROBLEM " + problem);
            foreach (var native in unmapped.Take(20))
                _output.WriteLine($"  UNMAPPED {native.PapyrusClass}.{native.FunctionName} at {native.SourceLine}");

            natives.Count.Should().Be(
                present,
                $"every registration in {Shorten(directory)} should be recovered");
            problems.Should().BeEmpty($"{Shorten(directory)} should scan without a reported problem");
            unmapped.Should().BeEmpty(
                $"every C++ type used in {Shorten(directory)} should have a Papyrus mapping");

            totalPresent += present;
            totalRecovered += natives.Count;
        }

        _output.WriteLine($"TOTAL present={totalPresent} recovered={totalRecovered} trees={directories.Count}");
        totalPresent.Should().BeGreaterThan(0, "a real tree should contain registrations");
    }

    [Fact]
    public void Recovered_bindings_are_internally_consistent()
    {
        var directories = ModuleDirectories();
        if (directories.Count == 0)
        {
            _output.WriteLine($"{TestRoots.F4SESourceVariable} is not set; nothing to scan.");
            return;
        }

        var extractor = new F4SERegistrationExtractor();
        int checked_ = 0;

        foreach (var directory in directories)
        {
            foreach (var native in extractor.ExtractDirectory(directory).SelectMany(s => s.Natives))
            {
                checked_++;
                native.FunctionName.Should().NotBeNullOrWhiteSpace();
                native.PapyrusClass.Should().NotBeNullOrWhiteSpace();
                native.CppFunctionName.Should().NotBeNullOrWhiteSpace();
                native.Arity.Should().Be(native.Parameters.Count);
                native.IsGlobal.Should().Be(native.CppBaseType == F4SETypeMap.StaticFunctionTag);
                native.SourceLine.Should().BeGreaterThan(0);
            }
        }

        _output.WriteLine($"checked {checked_} recovered bindings");
        checked_.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Every_papyrus_class_a_tree_registers_is_reported()
    {
        var directories = ModuleDirectories();
        if (directories.Count == 0)
        {
            _output.WriteLine($"{TestRoots.F4SESourceVariable} is not set; nothing to scan.");
            return;
        }

        var extractor = new F4SERegistrationExtractor();

        foreach (var directory in directories)
        {
            var natives = extractor.ExtractDirectory(directory).SelectMany(s => s.Natives).ToList();
            if (natives.Count == 0) continue;

            _output.WriteLine($"--- {Shorten(directory)} ---");
            foreach (var group in natives.GroupBy(n => n.PapyrusClass).OrderByDescending(g => g.Count()))
            {
                var bases = group.Select(n => n.CppBaseType).Distinct().OrderBy(b => b);
                _output.WriteLine($"  {group.Key,-22} {group.Count(),3}  <- {string.Join(", ", bases)}");
            }
        }
    }

    private static string Shorten(string directory)
    {
        var parts = directory.Split(Path.DirectorySeparatorChar);
        return string.Join('/', parts.Skip(Math.Max(0, parts.Length - 3)));
    }
}
