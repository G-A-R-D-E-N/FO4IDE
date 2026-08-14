using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using FO4RecordEditor.Services.Papyrus;
using Xunit.Abstractions;

namespace FO4RecordEditor.Core.Tests;

/// <summary>
/// Checks the reduced base script tree against the real Fallout 4 sources.
/// </summary>
/// <remarks>
/// The stub tree under <c>Fixtures/BaseStubs</c> is a second source of truth, which is the price of
/// a gate that runs on a bare checkout. This is the mitigation: every member the stubs declare has
/// to exist in the real tree with the same shape, so a stub that quietly drifts is caught here
/// rather than by a fixture that compiles against a signature the game does not have.
/// <para>
/// Opt-in on <c>FO4RE_PSC_ROOTS</c>, the same variable the other sweeps use. Unset, it no-ops so a
/// bare checkout stays green, and the residual risk is that nobody runs it. That risk is stated in
/// <c>Fixtures/BaseStubs/README.md</c> rather than left to be discovered.
/// </para>
/// </remarks>
public class BaseStubFidelityTests
{
    private readonly ITestOutputHelper _output;

    public BaseStubFidelityTests(ITestOutputHelper output) => _output = output;

    /// <summary>A member as compared across the two trees: shape only, never body or documentation.</summary>
    private readonly record struct MemberShape(
        string Kind, string Name, string ReturnType, string Parameters, bool IsGlobal);

    private static string TypeText(PapyrusTypeRef? reference) =>
        reference == null ? "None" : (reference.Name + (reference.IsArray ? "[]" : "")).ToLowerInvariant();

    private static string ParameterText(IEnumerable<PapyrusParameter> parameters) =>
        string.Join(", ", parameters.Select(p =>
            TypeText(p.Type) + " " + p.Name.ToLowerInvariant() + (p.DefaultValue != null ? " =" : "")));

    private static IReadOnlyDictionary<string, MemberShape> ShapesOf(PapyrusScript script)
    {
        var shapes = new Dictionary<string, MemberShape>(StringComparer.OrdinalIgnoreCase);

        foreach (var fn in script.Functions)
        {
            shapes["fn:" + fn.Name] = new MemberShape(
                "Function", fn.Name, TypeText(fn.ReturnType), ParameterText(fn.Parameters), fn.IsGlobal);
        }
        foreach (var ev in script.Events)
        {
            shapes["ev:" + ev.Name] = new MemberShape(
                "Event", ev.Name, "None", ParameterText(ev.Parameters), false);
        }
        return shapes;
    }

    /// <summary>Every stub, paired with the real script of the same name.</summary>
    private static IEnumerable<(string Name, PapyrusScript Stub, PapyrusScript Real)> Pairs(
        IReadOnlyList<string> realRoots)
    {
        var index = PapyrusCompiler.IndexFor(realRoots);

        foreach (var path in Directory.GetFiles(TestRoots.BaseStubs, "*.psc").OrderBy(p => p))
        {
            var stub = PapyrusParser.ParseFile(path);
            var real = index.Resolve(Path.GetFileNameWithoutExtension(path));
            if (real == null) continue;
            yield return (Path.GetFileName(path), stub, real);
        }
    }

    [Fact]
    public void Every_stub_member_exists_in_the_real_tree_with_the_same_shape()
    {
        var roots = TestRoots.RealScriptRoots();
        if (roots.Count == 0)
        {
            _output.WriteLine(
                $"{TestRoots.RealScriptRootsVariable} is not set to a real base script root; nothing to compare.");
            return;
        }

        int compared = 0, members = 0;
        var missing = new List<string>();
        var mismatched = new List<string>();

        foreach (var (name, stub, real) in Pairs(roots))
        {
            compared++;

            if (!string.Equals(stub.Extends ?? "", real.Extends ?? "", StringComparison.OrdinalIgnoreCase))
                mismatched.Add($"{name}: extends '{stub.Extends}' vs real '{real.Extends}'");

            var realShapes = ShapesOf(real);
            foreach (var (key, stubShape) in ShapesOf(stub))
            {
                members++;
                if (!realShapes.TryGetValue(key, out var realShape))
                {
                    missing.Add($"{name}: {stubShape.Kind} {stubShape.Name}");
                    continue;
                }

                // Bodies and flags are deliberately not compared: the stubs declare members native
                // precisely so no body has to be reproduced, and that is not a fidelity question.
                if (stubShape.ReturnType != realShape.ReturnType)
                    mismatched.Add($"{name}: {stubShape.Name} returns {stubShape.ReturnType} vs real {realShape.ReturnType}");
                else if (stubShape.Parameters != realShape.Parameters)
                    mismatched.Add($"{name}: {stubShape.Name}({stubShape.Parameters}) vs real ({realShape.Parameters})");
                else if (stubShape.IsGlobal != realShape.IsGlobal)
                    mismatched.Add($"{name}: {stubShape.Name} global {stubShape.IsGlobal} vs real {realShape.IsGlobal}");
            }
        }

        _output.WriteLine($"stubs compared={compared} members={members} missing={missing.Count} mismatched={mismatched.Count}");
        foreach (var line in missing.Take(40)) _output.WriteLine("  MISSING " + line);
        foreach (var line in mismatched.Take(40)) _output.WriteLine("  SHAPE   " + line);

        compared.Should().BeGreaterThan(0, "the real roots should contain scripts the stubs stand in for");
        missing.Should().BeEmpty("a stub must not declare a member the real script does not have");
        mismatched.Should().BeEmpty("a stub member must have the real script's signature");
    }

    [Fact]
    public void The_real_ScriptObject_reports_the_same_incomplete_base_chain_as_the_stub()
    {
        var roots = TestRoots.RealScriptRoots();
        if (roots.Count == 0)
        {
            _output.WriteLine(
                $"{TestRoots.RealScriptRootsVariable} is not set to a real base script root; nothing to compare.");
            return;
        }

        // ScriptObject declares parameters typed CustomEventName and ScriptEventName, which are
        // lexer keywords rather than scripts, so nothing on any root declares them and the resolver
        // records the doubt. This pins that as a property of the real base script rather than a
        // defect in the reduced stand-in, which is why BaseStubTests exempts it.
        var realIndex = PapyrusCompiler.IndexFor(roots);
        var real = realIndex.Resolve("ScriptObject");
        real.Should().NotBeNull("the real roots should contain ScriptObject.psc");

        var realResolution = new PapyrusResolver(realIndex).Resolve(real!);

        var stubIndex = PapyrusCompiler.IndexFor(new[] { TestRoots.BaseStubs });
        var stub = PapyrusParser.ParseFile(Path.Combine(TestRoots.BaseStubs, "ScriptObject.psc"));
        var stubResolution = new PapyrusResolver(stubIndex).Resolve(stub);

        _output.WriteLine(
            $"real BaseChainComplete={realResolution.BaseChainComplete} "
            + $"stub BaseChainComplete={stubResolution.BaseChainComplete}");

        stubResolution.BaseChainComplete.Should().Be(
            realResolution.BaseChainComplete,
            "the stub should behave the way the script it stands in for behaves");
    }
}
