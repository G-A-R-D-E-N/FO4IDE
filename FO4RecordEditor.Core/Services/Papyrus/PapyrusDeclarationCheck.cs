using System;
using System.Collections.Generic;
using System.Linq;

namespace FO4RecordEditor.Services.Papyrus;

/// <summary>
/// Refuses two declarations of the same name in the same scope.
/// </summary>
/// <remarks>
/// Papyrus has no overloading, so a name identifies a declaration outright. Without this check the
/// compiler accepted a duplicate silently and the code generator wrote <b>both</b> into the object:
/// a state whose function list holds two entries called <c>OnLoad</c> is not a valid
/// <c>.pex</c>, and which one the game runs is not something the source says. Merging two scripts by
/// hand is the ordinary way to produce one.
/// <para>
/// This runs before name resolution rather than inside it. A duplicate makes the symbol table
/// ambiguous, so resolving first would report the real problem once and then a cascade of
/// consequences of it.
/// </para>
/// <para>
/// The scopes are only the ones measured to collide in the emitted object. Functions and events
/// share one scope per state because both land in that state's function list, which is what the
/// duplicate emission proved. Whether a variable may share a name with a property is a separate
/// question this does not answer, and it deliberately raises nothing there rather than guess.
/// </para>
/// </remarks>
public static class PapyrusDeclarationCheck
{
    public static IReadOnlyList<PapyrusDiagnostic> Check(PapyrusScript script)
    {
        if (script == null) throw new ArgumentNullException(nameof(script));

        var problems = new List<PapyrusDiagnostic>();

        // Callables, one scope per state. The empty state is a state like any other here.
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

    /// <summary>
    /// One diagnostic per extra declaration, pointing at the extra one.
    /// </summary>
    /// <remarks>
    /// The later declaration is what gets reported, and the first one's line is named in the text.
    /// Reporting the first would send the reader to code that is very likely correct.
    /// </remarks>
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

    /// <summary>
    /// The name a declaration will actually be emitted under.
    /// </summary>
    /// <remarks>
    /// Not simply <see cref="PapyrusDeclaration.Name"/>. A handler for an event another object
    /// raises compiles to <c>::remote_Type_Name</c>, so <c>Event OnLoad()</c> and
    /// <c>Event ObjectReference.OnLoad(...)</c> are two different functions that legitimately live
    /// side by side: one overrides this script's own event, the other listens to someone else's.
    /// Keying on the bare name refuses that, which is a script the game accepts.
    /// </remarks>
    private static string KeyOf(PapyrusDeclaration declaration) =>
        declaration is PapyrusEventDecl { RemoteObjectType: { Length: > 0 } owner }
            ? owner + "." + declaration.Name
            : declaration.Name;
}
