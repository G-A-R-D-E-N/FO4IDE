using System;
using System.Collections.Generic;

namespace FO4RecordEditor.Services.Papyrus;

/// <summary>
/// A half-open range of source text, carried by every token and AST node.
/// </summary>
/// <remarks>
/// Both a byte-ish offset/length pair and a line/column pair are stored, because the two consumers
/// want different things: an editor selects on offsets, and a compiler-style diagnostic line reads
/// as "file(line,col): message". Deriving one from the other on demand would mean re-scanning the
/// source, so the lexer -- which already knows both -- records both once.
/// <para>
/// <see cref="Line"/> and <see cref="Column"/> are 1-based to match the Papyrus compiler's own error
/// format; <see cref="Start"/> is 0-based to match how a text editor indexes a buffer.
/// </para>
/// </remarks>
public readonly struct PapyrusSpan : IEquatable<PapyrusSpan>
{
    public PapyrusSpan(int start, int length, int line, int column)
    {
        Start = start;
        Length = length;
        Line = line;
        Column = column;
    }

    /// <summary>0-based character offset of the first character.</summary>
    public int Start { get; }

    /// <summary>Character count. Zero-length spans are legal and mark an insertion point.</summary>
    public int Length { get; }

    /// <summary>1-based line of <see cref="Start"/>.</summary>
    public int Line { get; }

    /// <summary>1-based column of <see cref="Start"/>.</summary>
    public int Column { get; }

    /// <summary>0-based offset one past the last character.</summary>
    public int End => Start + Length;

    /// <summary>True when <paramref name="offset"/> falls inside the span, or on its closing edge.</summary>
    /// <remarks>
    /// The closing edge counts as inside because an editor caret sitting immediately after an
    /// identifier is still "on" that identifier as far as hover and go-to-definition are concerned.
    /// </remarks>
    public bool Contains(int offset) => offset >= Start && offset <= End;

    /// <summary>A span covering both operands, used to give a parent node its children's extent.</summary>
    public PapyrusSpan To(PapyrusSpan other)
    {
        var start = Math.Min(Start, other.Start);
        var end = Math.Max(End, other.End);
        // Keep the earlier line/column: the merged span starts wherever the earlier operand did.
        return Start <= other.Start
            ? new PapyrusSpan(start, end - start, Line, Column)
            : new PapyrusSpan(start, end - start, other.Line, other.Column);
    }

    public bool Equals(PapyrusSpan other) =>
        Start == other.Start && Length == other.Length && Line == other.Line && Column == other.Column;

    public override bool Equals(object? obj) => obj is PapyrusSpan s && Equals(s);

    public override int GetHashCode() => HashCode.Combine(Start, Length, Line, Column);

    public override string ToString() => $"({Line},{Column})+{Length}";
}

public enum PapyrusSeverity
{
    Warning,
    Error,
}

/// <summary>
/// One lexer or parser complaint, positioned in the source.
/// </summary>
/// <remarks>
/// Diagnostics are values, not exceptions: the front end recovers and keeps going so a file with one
/// typo still yields a usable AST and a full error list, which is what an editor needs. Throwing on
/// the first bad token would give the panel one squiggle per keystroke and no symbols at all.
/// <para>
/// <see cref="Code"/> is a stable short identifier (<c>PAP0007</c>) so tests can assert on the kind
/// of failure without pinning the wording of the message.
/// </para>
/// </remarks>
public sealed class PapyrusDiagnostic
{
    public PapyrusDiagnostic(string code, PapyrusSeverity severity, string message, PapyrusSpan span, string? file = null)
    {
        Code = code;
        Severity = severity;
        Message = message;
        Span = span;
        File = file;
    }

    public string Code { get; }

    public PapyrusSeverity Severity { get; }

    public string Message { get; }

    public PapyrusSpan Span { get; }

    /// <summary>Source path, when the text came from a file. Null for in-memory text.</summary>
    public string? File { get; internal set; }

    public override string ToString()
    {
        var where = File ?? "<text>";
        var kind = Severity == PapyrusSeverity.Error ? "error" : "warning";
        return $"{where}({Span.Line},{Span.Column}): {kind} {Code}: {Message}";
    }
}

/// <summary>Diagnostic codes, in one place so the tests and the UI agree on them.</summary>
public static class PapyrusDiagnosticCodes
{
    // Lexer
    public const string UnterminatedString = "PAP0001";
    public const string UnterminatedBlockComment = "PAP0002";
    public const string UnterminatedDocComment = "PAP0003";
    public const string UnexpectedCharacter = "PAP0004";
    public const string MalformedNumber = "PAP0005";

    // Parser
    public const string ExpectedToken = "PAP0010";
    public const string ExpectedScriptName = "PAP0011";
    public const string ExpectedIdentifier = "PAP0012";
    public const string ExpectedType = "PAP0013";
    public const string ExpectedExpression = "PAP0014";
    public const string UnexpectedToken = "PAP0015";
    public const string UnterminatedBlock = "PAP0016";
    public const string PropertyNeedsAccessor = "PAP0017";
    public const string StructNeedsMember = "PAP0018";
    public const string TooManyErrors = "PAP0019";

    // Declaration check, before resolution: a duplicate makes the symbol table ambiguous, so it is
    // reported once here rather than as a cascade of consequences later.
    public const string DuplicateDeclaration = "PAP0020";

    // Resolver. These are only ever raised when the sources were complete enough to be sure; see
    // PapyrusResolution.BaseChainComplete.
    public const string UnresolvedName = "PAP0030";
    public const string UnknownMember = "PAP0031";

    // Type checker. Same rule: nothing is raised unless the sources were complete enough to be sure.
    public const string TypeMismatch = "PAP0040";
    public const string ArgumentCount = "PAP0041";
    public const string UnknownArgumentName = "PAP0042";
    public const string InvalidCast = "PAP0043";
    public const string OverrideMismatch = "PAP0044";
    public const string ParameterOrder = "PAP0045";

    // Code generator. Unlike the two layers above, these are not suppressed when sources are
    // incomplete: a name the back end cannot resolve is a thing it cannot emit an instruction for,
    // and emitting a guess would be worse than refusing. See PapyrusCodeGenerator.
    public const string CannotEmit = "PAP0050";
    public const string UnknownCallTarget = "PAP0051";
    public const string NonConstantInitializer = "PAP0052";
}

/// <summary>Collects diagnostics and stops the parser from looping forever on hopeless input.</summary>
internal sealed class DiagnosticBag
{
    /// <summary>
    /// Cap on reported diagnostics. A file that is not Papyrus at all (a .pex renamed, say) would
    /// otherwise produce one error per token; past a couple of hundred the list is noise, and the
    /// parser stops recording rather than stops parsing so the AST is still whatever could be read.
    /// </summary>
    internal const int MaxDiagnostics = 200;

    private readonly List<PapyrusDiagnostic> _items = new();
    private bool _capped;

    public IReadOnlyList<PapyrusDiagnostic> Items => _items;

    public bool HasErrors
    {
        get
        {
            foreach (var d in _items)
            {
                if (d.Severity == PapyrusSeverity.Error) return true;
            }
            return false;
        }
    }

    public void Report(string code, string message, PapyrusSpan span, PapyrusSeverity severity = PapyrusSeverity.Error)
    {
        if (_capped) return;
        if (_items.Count >= MaxDiagnostics)
        {
            _capped = true;
            _items.Add(new PapyrusDiagnostic(
                PapyrusDiagnosticCodes.TooManyErrors,
                PapyrusSeverity.Error,
                $"More than {MaxDiagnostics} errors; not reporting the rest.",
                span));
            return;
        }
        _items.Add(new PapyrusDiagnostic(code, severity, message, span));
    }

    /// <summary>Drops everything reported after <paramref name="count"/>.</summary>
    /// <remarks>
    /// Used by the parser's one speculative path -- deciding whether a statement is a definition or
    /// an assignment -- so that rewinding the token index also rewinds the complaints the failed
    /// attempt made. Without this, <c>x = 1</c> would report a bogus "expected a type" every time.
    /// </remarks>
    public void TruncateTo(int count)
    {
        if (_capped) return;
        if (count < _items.Count) _items.RemoveRange(count, _items.Count - count);
    }

    public void SetFile(string? file)
    {
        foreach (var d in _items) d.File = file;
    }
}
