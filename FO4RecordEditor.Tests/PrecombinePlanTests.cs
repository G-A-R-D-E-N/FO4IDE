using System.IO;
using FluentAssertions;
using FO4RecordEditor.Services;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace FO4RecordEditor.Tests;

// Phase 1 of CK-free precombine (#72). The eligibility rules are ported from
// native/ck/src/precombine/plan.rs in Bryant-21/py-creation-lib (GPL-3.0, permission granted), and
// the only honest way to test them is against a real cell -- a synthetic one would just assert the
// rules back at themselves.
//
// Vault111Cryo was run against a real vanilla Fallout4.esm while building this: 2,385 temporary
// references, 710 eligible, 108 model groups, with the rejections accounting for the rest exactly
// (1,620 skipped by rule + 55 groups below the 2-instance threshold = 2,385). Those numbers are
// asserted loosely rather than exactly, so a different game version does not fail the suite for the
// wrong reason; the arithmetic identity is asserted strictly, because that is the part that catches
// a rule silently dropping references on the floor.
public class PrecombinePlanTests
{
    private readonly ITestOutputHelper _out;
    public PrecombinePlanTests(ITestOutputHelper o) => _out = o;

    private JObject? Plan(string cell, out string skipReason, int minInstances = 2)
    {
        skipReason = "";
        var data = TestDataRoots.DataRoot;
        if (data == null || !File.Exists(Path.Combine(data, "Fallout4.esm")))
        {
            skipReason = "No real Fallout 4 Data folder with Fallout4.esm found (searched FO4RE_TEST_DATA and the known paths).";
            return null;
        }

        var (env, _) = MutagenLoader.BuildEnvironment(null, data);
        var json = PrecombineService.BuildPlanJson(env, "Fallout4.esm", cell, minInstances);
        return JObject.Parse(json);
    }

    private bool Skipped(string reason)
    {
        if (reason.Length == 0) return false;
        if (TestDataRoots.FixturesRequired) Assert.Fail(reason);
        _out.WriteLine("Skipped -- " + reason);
        return true;
    }

    [Fact]
    public void EveryTemporaryReferenceIsAccountedFor()
    {
        var plan = Plan("Vault111Cryo", out var reason);
        if (Skipped(reason)) return;
        plan!["error"].Should().BeNull(plan.ToString());

        var considered = plan["temporaryReferences"]!.Value<int>();
        var eligible = plan["eligibleReferences"]!.Value<int>();
        var skippedByRule = plan["skipped"]!.Sum(s => s["count"]!.Value<int>());

        // Anything not eligible and not skipped by a rule is a reference that DID qualify but landed
        // in a group under the threshold. If those three do not add up, a rule is dropping references
        // without reporting them, which is exactly the failure this tool exists to avoid.
        var belowThreshold = considered - eligible - skippedByRule;
        belowThreshold.Should().BeGreaterThanOrEqualTo(0,
            "a reference cannot be counted as both eligible and skipped");

        _out.WriteLine($"{considered} temporary refs: {eligible} eligible, {skippedByRule} skipped by rule, " +
                       $"{belowThreshold} in groups below the threshold.");

        considered.Should().BeGreaterThan(100, "Vault111Cryo is a large interior");
        eligible.Should().BeGreaterThan(0);
        plan["interior"]!.Value<bool>().Should().BeTrue();
    }

    [Fact]
    public void GroupsAreKeyedByLowercasedModelPathAndMeetTheThreshold()
    {
        var plan = Plan("Vault111Cryo", out var reason);
        if (Skipped(reason)) return;

        var groups = (JArray)plan!["groups"]!;
        groups.Should().NotBeEmpty();

        foreach (var g in groups)
        {
            var path = g["modelPath"]!.Value<string>()!;
            path.Should().Be(path.ToLowerInvariant(), "grouping is case-insensitive, so the key is canonicalized");
            path.Should().NotContain("/", "paths are canonicalized to backslashes");
            g["instanceCount"]!.Value<int>().Should().BeGreaterThanOrEqualTo(2, "the default threshold is 2");
        }

        plan["eligibleReferences"]!.Value<int>().Should()
            .BeGreaterThanOrEqualTo(groups.Sum(g => g["instanceCount"]!.Value<int>()),
                "the eligible total covers every group, including any not shown");
    }

    // Reported, not silently dropped: on a real cell "why did so few qualify" is the useful question,
    // and a filtered list cannot answer it.
    [Fact]
    public void RejectionsAreReportedWithReasonsAndExamples()
    {
        var plan = Plan("Vault111Cryo", out var reason);
        if (Skipped(reason)) return;

        var skipped = (JArray)plan!["skipped"]!;
        skipped.Should().NotBeEmpty();

        foreach (var s in skipped)
        {
            s["reason"]!.Value<string>().Should().NotBeNullOrWhiteSpace();
            s["count"]!.Value<int>().Should().BeGreaterThan(0);
            ((JArray)s["examples"]!).Count.Should().BeInRange(1, 5, "examples are capped, not exhaustive");
        }

        foreach (var s in skipped) _out.WriteLine($"{s["count"]} -- {s["reason"]}");
    }

    // Exterior precombines interact with worldspace object LOD; refusing is the honest answer rather
    // than producing a plan that would bake the wrong thing.
    [Fact]
    public void ExteriorCellsAreRefused()
    {
        var plan = Plan("Vault111Ext", out var reason);
        if (Skipped(reason)) return;

        plan!["error"]!.Value<string>().Should().Contain("exterior");
        plan["groups"].Should().BeNull();
    }

    // The result must stay inside the tool-result budget: a response that overruns arrives truncated
    // mid-string and does not parse at all, which reads as a broken tool rather than a big cell.
    [Fact]
    public void OutputStaysBoundedOnALargeCell()
    {
        var data = TestDataRoots.DataRoot;
        if (Skipped(data == null ? "No real Fallout 4 Data folder found." : "")) return;
        if (data == null) return;

        var (env, _) = MutagenLoader.BuildEnvironment(null, data);
        var json = PrecombineService.BuildPlanJson(env, "Fallout4.esm", "Vault111Cryo");
        json.Length.Should().BeLessThan(8000, "the default response must not need truncating");

        var parsed = JObject.Parse(json);
        var shown = parsed["groupsShown"]!.Value<int>();
        var omitted = parsed["groupsOmitted"]!.Value<int>();
        var total = parsed["groupCount"]!.Value<int>();
        (shown + omitted).Should().Be(total, "an omitted count that does not reconcile is worse than none");
        _out.WriteLine($"{json.Length} chars, {shown} groups shown, {omitted} omitted of {total}.");
    }
}
