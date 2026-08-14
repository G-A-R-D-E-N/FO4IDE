using System.Linq;
using System.Reflection;
using FluentAssertions;
using FO4RecordEditor.Services;
using Xunit;
using Xunit.Abstractions;

namespace FO4RecordEditor.Tests;

public class EnvParameterContractTests
{
    private readonly ITestOutputHelper _out;
    public EnvParameterContractTests(ITestOutputHelper o) => _out = o;

    [Fact]
    public void NoWriteServiceMethodGivesEnvADefaultValue()
    {
        var offenders = typeof(WriteService)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .SelectMany(m => m.GetParameters().Select(p => (m, p)))
            .Where(x => x.p.Name == "env" && x.p.HasDefaultValue)
            .Select(x => $"{x.m.Name}({x.p.Name} = {x.p.DefaultValue ?? "null"})")
            .Distinct()
            .ToList();

        if (offenders.Count > 0)
            _out.WriteLine("env has a default on:\n  " + string.Join("\n  ", offenders));

        offenders.Should().BeEmpty(
            "a defaulted env can be omitted silently, which disables load-order master ordering and " +
            "produces a plugin that hangs the game on load -- pass null explicitly if you truly have none");
    }

    [Theory]
    [InlineData("SavePlugin")]
    [InlineData("SaveScriptPatch")]
    [InlineData("StripMastersClean")]
    public void SavePathsRequireEnv(string methodName)
    {
        var overloads = typeof(WriteService)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.Name == methodName)
            .ToList();

        overloads.Should().NotBeEmpty($"{methodName} should exist -- update this test if it was renamed");

        foreach (var m in overloads)
        {
            var env = m.GetParameters().SingleOrDefault(p => p.Name == "env");
            env.Should().NotBeNull($"{methodName} must take an env parameter");
            env!.HasDefaultValue.Should().BeFalse($"{methodName}'s env must stay required");
        }
    }
}
