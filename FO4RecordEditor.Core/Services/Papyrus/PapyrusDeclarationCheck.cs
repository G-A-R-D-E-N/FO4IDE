using System;
using System.Collections.Generic;
using System.Linq;

namespace FO4RecordEditor.Services.Papyrus;

public static class PapyrusDeclarationCheck
{
    public static IReadOnlyList<PapyrusDiagnostic> Check(PapyrusScript script)
    {
        if (script == null) throw new ArgumentNullException(nameof(script));

        var problems = new List<PapyrusDiagnostic>();

        Unique(script.Functions.Cast<PapyrusDeclaration>().Concat(script.Events),
            "function or event", "the empty state", problems);

        foreach (var state in script.States)
        {
            Unique(state.Functions.Cast<PapyrusDeclaration>().Concat(state.Events),
                "function or event", $"state '{state.Name}'", problems);
        }

        Unique(script.Variables, "variable", "this script", problems);
        Unique(script.Properties, "property", "this script", problems);
        Unique(script.Structs, "struct", "this script", problems);
        Unique(script.States, "state", "this script", problems);
        Unique(script.CustomEvents, "custom event", "this script", problems);

        return problems;
    }

    private static void Unique(
        IEnumerable<PapyrusDeclaration> declarations,
        string kind,
        string scope,
        List<PapyrusDiagnostic> problems)
    {
        var seen = new Dictionary<string, PapyrusDeclaration>(StringComparer.OrdinalIgnoreCase);

        foreach (var declaration in declarations.OrderBy(d => d.NameSpan.Start))
        {
            if (string.IsNullOrEmpty(declaration.Name)) continue;

            var key = KeyOf(declaration);

            if (seen.TryGetValue(key, out var first))
            {
                problems.Add(new PapyrusDiagnostic(
                    PapyrusDiagnosticCodes.DuplicateDeclaration,
                    PapyrusSeverity.Error,
                    $"'{KeyOf(declaration)}' is already declared as a {kind} in {scope}, on line "
                    + $"{first.NameSpan.Line}. Papyrus has no overloading, so the two cannot both exist.",
                    declaration.NameSpan));
                continue;
            }

            seen[key] = declaration;
        }
    }

    private static string KeyOf(PapyrusDeclaration declaration) =>
        declaration is PapyrusEventDecl { RemoteObjectType: { Length: > 0 } owner }
            ? owner + "." + declaration.Name
            : declaration.Name;
}
