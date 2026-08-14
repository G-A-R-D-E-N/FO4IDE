using System.IO;
using FluentAssertions;
using FO4RecordEditor.Services;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace FO4RecordEditor.Tests;

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

    [Fact]
    public void ExteriorCellsAreRefused()
    {
        var plan = Plan("Vault111Ext", out var reason);
        if (Skipped(reason)) return;

        plan!["error"]!.Value<string>().Should().Contain("exterior");
        plan["groups"].Should().BeNull();
    }

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
