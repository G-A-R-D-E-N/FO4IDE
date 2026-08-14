using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FO4RecordEditor.Core.Tests;

internal static class TestRoots
{

    public const string RealScriptRootsVariable = "FO4RE_PSC_ROOTS";

    public const string F4SESourceVariable = "FO4RE_F4SE_SRC";

    public static string Fixtures => Path.Combine(AppContext.BaseDirectory, "Fixtures");

    public static string BaseStubs => Path.Combine(Fixtures, "BaseStubs");

    public static string GraphScripts => Path.Combine(Fixtures, "GraphScripts");

    public static IReadOnlyList<string> RootsFrom(string variable)
    {
        var value = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrWhiteSpace(value)) return Array.Empty<string>();
        return value
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(Directory.Exists)
            .ToList();
    }

    public static IReadOnlyList<string> RealScriptRoots() => RootsFrom(RealScriptRootsVariable);
}
