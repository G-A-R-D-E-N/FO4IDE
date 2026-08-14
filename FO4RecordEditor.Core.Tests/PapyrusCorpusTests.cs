using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using FluentAssertions;
using FO4RecordEditor.Services.Papyrus;
using Xunit.Abstractions;

namespace FO4RecordEditor.Core.Tests;

public class PapyrusCorpusTests
{
    private readonly ITestOutputHelper _output;

    public PapyrusCorpusTests(ITestOutputHelper output) => _output = output;

    private const string CorpusVariable = "FO4RE_PSC_CORPUS";

    private static IReadOnlyList<string> CorpusFiles()
    {
        var roots = Environment.GetEnvironmentVariable(CorpusVariable);
        if (string.IsNullOrWhiteSpace(roots)) return Array.Empty<string>();

        var files = new List<string>();
        foreach (var root in roots.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            files.AddRange(PapyrusFileWalk.EnumerateFiles(root, "*.psc"));
        }
        return files.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    [Fact]
    public void Every_script_on_disk_parses_without_crashing()
    {
        var files = CorpusFiles();
        if (files.Count == 0)
        {
            _output.WriteLine($"{CorpusVariable} is not set; corpus sweep skipped.");
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        var clean = 0;
        var withErrors = new List<string>();
        var byCode = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var file in files)
        {
            PapyrusScript script;
            try
            {
                script = PapyrusParser.ParseFile(file);
            }
            catch (Exception ex) when (ex is not IOException and not UnauthorizedAccessException)
            {

                throw new Xunit.Sdk.XunitException($"Parsing threw on {file}: {ex}");
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            if (!script.HasErrors)
            {
                clean++;
                continue;
            }

            withErrors.Add(file);
            foreach (var d in script.Diagnostics.Where(d => d.Severity == PapyrusSeverity.Error))
            {
                byCode[d.Code] = byCode.GetValueOrDefault(d.Code) + 1;
            }
        }
        stopwatch.Stop();

        _output.WriteLine($"{files.Count} files, {clean} clean, {withErrors.Count} with errors, {stopwatch.ElapsedMilliseconds} ms");
        foreach (var kv in byCode.OrderByDescending(k => k.Value)) _output.WriteLine($"  {kv.Key}: {kv.Value}");
        foreach (var file in withErrors.Take(40)) _output.WriteLine($"  {file}");

        var rate = (double)clean / files.Count;
        rate.Should().BeGreaterThan(0.995,
            "the known-bad files in a real corpus are a handful of headerless fragments, not a systematic gap");
    }

    [Fact]
    public void Every_script_on_disk_yields_symbols_without_crashing()
    {
        var files = CorpusFiles();
        if (files.Count == 0)
        {
            _output.WriteLine($"{CorpusVariable} is not set; corpus sweep skipped.");
            return;
        }

        var index = new PapyrusScriptIndex();
        var symbols = 0L;

        foreach (var file in files)
        {
            PapyrusScript script;
            try
            {
                script = PapyrusParser.ParseFile(file);
            }
            catch (IOException) { continue; }
            catch (UnauthorizedAccessException) { continue; }

            symbols += PapyrusSymbols.DocumentSymbols(script).Count;

            var length = Math.Max(1, script.Span.End);
            for (var offset = 0; offset < length; offset += Math.Max(1, length / 25))
            {
                PapyrusSymbols.Hover(index, script, offset);
            }
        }

        _output.WriteLine($"{files.Count} files, {symbols} symbols");
        symbols.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Every_script_on_disk_resolves_without_crashing()
    {
        var files = CorpusFiles();
        if (files.Count == 0)
        {
            _output.WriteLine($"{CorpusVariable} is not set; corpus sweep skipped.");
            return;
        }

        var index = new PapyrusScriptIndex();
        foreach (var root in (Environment.GetEnvironmentVariable(CorpusVariable) ?? string.Empty)
                 .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            index.AddRoot(root);
        }

        var resolver = new PapyrusResolver(index);
        var stopwatch = Stopwatch.StartNew();
        int resolved = 0, completeChain = 0, cleanOfComplete = 0;
        var byCode = new Dictionary<string, int>(StringComparer.Ordinal);
        var examples = new List<string>();

        foreach (var file in files)
        {
            var script = index.ParseCached(file);
            if (script == null || script.HasErrors) continue;

            PapyrusResolution result;
            try
            {
                result = resolver.Resolve(script);
            }
            catch (Exception ex)
            {

                throw new Xunit.Sdk.XunitException($"Resolving threw on {file}: {ex}");
            }

            resolved++;
            if (!result.BaseChainComplete) continue;

            completeChain++;
            if (result.Diagnostics.Count == 0)
            {
                cleanOfComplete++;
                continue;
            }

            foreach (var d in result.Diagnostics) byCode[d.Code] = byCode.GetValueOrDefault(d.Code) + 1;
            if (examples.Count < 40) examples.Add($"{file}: {result.Diagnostics[0]}");
        }
        stopwatch.Stop();

        var cleanRate = completeChain == 0 ? 0 : (double)cleanOfComplete / completeChain;
        _output.WriteLine(
            $"{files.Count} files; {resolved} resolved; {completeChain} with complete sources; " +
            $"{cleanOfComplete} of those clean ({cleanRate:P2}); {stopwatch.ElapsedMilliseconds} ms");
        foreach (var kv in byCode.OrderByDescending(k => k.Value)) _output.WriteLine($"  {kv.Key}: {kv.Value}");
        foreach (var e in examples) _output.WriteLine("  " + e);

        resolved.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Every_script_on_disk_type_checks_without_crashing()
    {
        var files = CorpusFiles();
        if (files.Count == 0)
        {
            _output.WriteLine($"{CorpusVariable} is not set; corpus sweep skipped.");
            return;
        }

        var index = new PapyrusScriptIndex();
        foreach (var root in (Environment.GetEnvironmentVariable(CorpusVariable) ?? string.Empty)
                 .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            index.AddRoot(root);
        }

        var resolver = new PapyrusResolver(index);
        var checker = new PapyrusTypeChecker(index);
        var stopwatch = Stopwatch.StartNew();
        int checked_ = 0, clean = 0;
        var byCode = new Dictionary<string, int>(StringComparer.Ordinal);
        var examples = new List<string>();

        foreach (var file in files)
        {
            var script = index.ParseCached(file);
            if (script == null || script.HasErrors) continue;

            IReadOnlyList<PapyrusDiagnostic> diagnostics;
            try
            {
                var resolution = resolver.Resolve(script);
                if (!resolution.BaseChainComplete) continue;
                diagnostics = checker.Check(resolution);
            }
            catch (Exception ex)
            {
                throw new Xunit.Sdk.XunitException($"Type checking threw on {file}: {ex}");
            }

            checked_++;
            if (diagnostics.Count == 0) { clean++; continue; }

            foreach (var d in diagnostics) byCode[d.Code] = byCode.GetValueOrDefault(d.Code) + 1;
            if (examples.Count < 40) examples.Add($"{file}: {diagnostics[0]}");
        }
        stopwatch.Stop();

        var rate = checked_ == 0 ? 0 : (double)clean / checked_;
        _output.WriteLine(
            $"{checked_} scripts type-checked with complete sources; {clean} clean ({rate:P2}); " +
            $"{stopwatch.ElapsedMilliseconds} ms");
        foreach (var kv in byCode.OrderByDescending(k => k.Value)) _output.WriteLine($"  {kv.Key}: {kv.Value}");
        foreach (var e in examples) _output.WriteLine("  " + e);

        checked_.Should().BeGreaterThan(0);
    }
}
