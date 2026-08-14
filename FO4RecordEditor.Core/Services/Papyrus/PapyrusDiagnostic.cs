using System;
using System.Collections.Generic;

namespace FO4RecordEditor.Services.Papyrus;














public readonly struct PapyrusSpan : IEquatable<PapyrusSpan>
{
    public PapyrusSpan(int start, int length, int line, int column)
    {
        Start = start;
        Length = length;
        Line = line;
        Column = column;
    }


    public int Start { get; }


    public int Length { get; }


    public int Line { get; }


    public int Column { get; }


    public int End => Start + Length;






    public bool Contains(int offset) => offset >= Start && offset <= End;


    public PapyrusSpan To(PapyrusSpan other)
    {
        var start = Math.Min(Start, other.Start);
        var end = Math.Max(End, other.End);

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


    public string? File { get; internal set; }

    public override string ToString()
    {
        var where = File ?? "<text>";
        var kind = Severity == PapyrusSeverity.Error ? "error" : "warning";
        return $"{where}({Span.Line},{Span.Column}): {kind} {Code}: {Message}";
    }
}


public static class PapyrusDiagnosticCodes
{

    public const string UnterminatedString = "PAP0001";
    public const string UnterminatedBlockComment = "PAP0002";
    public const string UnterminatedDocComment = "PAP0003";
    public const string UnexpectedCharacter = "PAP0004";
    public const string MalformedNumber = "PAP0005";


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



    public const string DuplicateDeclaration = "PAP0020";



    public const string UnresolvedName = "PAP0030";
    public const string UnknownMember = "PAP0031";


    public const string TypeMismatch = "PAP0040";
    public const string ArgumentCount = "PAP0041";
    public const string UnknownArgumentName = "PAP0042";
    public const string InvalidCast = "PAP0043";
    public const string OverrideMismatch = "PAP0044";
    public const string ParameterOrder = "PAP0045";




    public const string CannotEmit = "PAP0050";
    public const string UnknownCallTarget = "PAP0051";
    public const string NonConstantInitializer = "PAP0052";
}


internal sealed class DiagnosticBag
{





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
