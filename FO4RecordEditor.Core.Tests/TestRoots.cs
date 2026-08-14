using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FO4RecordEditor.Core.Tests;

/// <summary>
/// Where the checked-in fixture data sits at run time, and where the optional real corpora sit.
/// </summary>
/// <remarks>
/// Fixtures are copied beside the test binary by the project file, so paths resolve from
/// <see cref="AppContext.BaseDirectory"/> rather than from the source tree. That keeps the tests
/// working under <c>dotnet test</c>, under a published test binary, and from any working directory.
/// </remarks>
internal static class TestRoots
{
    /// <summary>The environment variable naming real base script roots, shared with the corpus sweeps.</summary>
    public const string RealScriptRootsVariable = "FO4RE_PSC_ROOTS";

    /// <summary>The environment variable naming an F4SE source tree.</summary>
    public const string F4SESourceVariable = "FO4RE_F4SE_SRC";

    /// <summary>The root every checked-in fixture lives under.</summary>
    public static string Fixtures => Path.Combine(AppContext.BaseDirectory, "Fixtures");

    /// <summary>
    /// The reduced base script tree the graph gate compiles against.
    /// </summary>
    public static string BaseStubs => Path.Combine(Fixtures, "BaseStubs");

    /// <summary>
    /// Fixture-owned scripts that are not stand-ins for anything the game ships.
    /// </summary>
    /// <remarks>
    /// Kept apart from <see cref="BaseStubs"/> on purpose. Everything under BaseStubs is checked
    /// member by member against the real tree by BaseStubFidelityTests, so a script with no real
    /// counterpart cannot live there without making that check meaningless.
    /// </remarks>
    public static string GraphScripts => Path.Combine(Fixtures, "GraphScripts");

    /// <summary>Roots parsed out of a path-separator-delimited environment variable, existing ones only.</summary>
    public static IReadOnlyList<string> RootsFrom(string variable)
    {
        var value = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrWhiteSpace(value)) return Array.Empty<string>();
        return value
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(Directory.Exists)
            .ToList();
    }

    /// <summary>The real base script roots, empty when the sweep is not opted into.</summary>
    public static IReadOnlyList<string> RealScriptRoots() => RootsFrom(RealScriptRootsVariable);
}
