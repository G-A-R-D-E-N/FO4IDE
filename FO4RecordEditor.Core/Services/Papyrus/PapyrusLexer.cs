using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace FO4RecordEditor.Services.Papyrus;

public sealed class PapyrusLexer
{
    private readonly string _text;
    private readonly DiagnosticBag _diagnostics;
    private int _pos;
    private int _line = 1;
    private int _lineStart;

    private PapyrusLexer(string text, DiagnosticBag diagnostics)
    {
        _text = text;
        _diagnostics = diagnostics;
    }

    internal static List<PapyrusToken> Lex(string text, DiagnosticBag diagnostics) =>
        new PapyrusLexer(text, diagnostics).LexAll();

    public static (IReadOnlyList<PapyrusToken> Tokens, IReadOnlyList<PapyrusDiagnostic> Diagnostics) Lex(string text)
    {
        var bag = new DiagnosticBag();
        var tokens = new PapyrusLexer(text, bag).LexAll();
        return (tokens, bag.Items);
    }

    private char Current => _pos < _text.Length ? _text[_pos] : '\0';

    private char Peek(int offset = 1) => _pos + offset < _text.Length ? _text[_pos + offset] : '\0';

    private bool AtEnd => _pos >= _text.Length;

    private int Column => _pos - _lineStart + 1;

    private PapyrusSpan SpanFrom(int start, int line, int column) =>
        new(start, _pos - start, line, column);

    private List<PapyrusToken> LexAll()
    {
        var tokens = new List<PapyrusToken>(Math.Max(16, _text.Length / 4));
        while (true)
        {
            var token = Next();
            tokens.Add(token);
            if (token.Kind == PapyrusTokenKind.EndOfFile) break;
        }
        return tokens;
    }

    private PapyrusToken Next()
    {
        SkipTrivia();

        var start = _pos;
        var line = _line;
        var column = Column;

        if (AtEnd) return new PapyrusToken(PapyrusTokenKind.EndOfFile, string.Empty, SpanFrom(start, line, column));

        var c = Current;

        if (c == '\r' || c == '\n')
        {
            ConsumeNewline();
            return new PapyrusToken(PapyrusTokenKind.Newline, "\n", SpanFrom(start, line, column));
        }

        if (c == '_' || char.IsLetter(c)) return LexWord(start, line, column);

        if (char.IsDigit(c)) return LexNumber(start, line, column);

        if (c == '"') return LexString(start, line, column);

        if (c == '{') return LexDocComment(start, line, column);

        return LexOperator(start, line, column);
    }

    private void SkipTrivia()
    {
        while (!AtEnd)
        {
            var c = Current;

            if (c == ' ' || c == '\t')
            {
                _pos++;
                continue;
            }

            if (c == ';' && Peek() == '/')
            {
                SkipBlockComment();
                continue;
            }

            if (c == ';')
            {
                while (!AtEnd && Current != '\r' && Current != '\n') _pos++;
                continue;
            }

            if (c == '\\' && IsContinuation())
            {
                SkipContinuation();
                continue;
            }

            return;
        }
    }

    private bool IsContinuation()
    {
        var i = _pos + 1;
        while (i < _text.Length && (_text[i] == ' ' || _text[i] == '\t')) i++;

        if (i < _text.Length && _text[i] == ';' && !(i + 1 < _text.Length && _text[i + 1] == '/'))
        {
            while (i < _text.Length && _text[i] != '\r' && _text[i] != '\n') i++;
        }
        return i >= _text.Length || _text[i] == '\r' || _text[i] == '\n';
    }

    private void SkipContinuation()
    {
        _pos++;
        while (!AtEnd && Current != '\r' && Current != '\n') _pos++;
        if (!AtEnd) ConsumeNewline();
    }

    private void SkipBlockComment()
    {
        var start = _pos;
        var line = _line;
        var column = Column;
        _pos += 2;
        while (!AtEnd)
        {
            if (Current == '/' && Peek() == ';')
            {
                _pos += 2;
                return;
            }
            if (Current == '\r' || Current == '\n') ConsumeNewline();
            else _pos++;
        }
        _diagnostics.Report(
            PapyrusDiagnosticCodes.UnterminatedBlockComment,
            "Block comment is never closed; expected '/;'.",
            SpanFrom(start, line, column));
    }

    private void ConsumeNewline()
    {
        if (Current == '\r' && Peek() == '\n') _pos += 2;
        else _pos++;
        _line++;
        _lineStart = _pos;
    }

    private PapyrusToken LexWord(int start, int line, int column)
    {
        while (!AtEnd && (Current == '_' || char.IsLetterOrDigit(Current))) _pos++;
        var text = _text.Substring(start, _pos - start);
        var kind = PapyrusKeywords.TryGet(text, out var keyword) ? keyword : PapyrusTokenKind.Identifier;
        return new PapyrusToken(kind, text, SpanFrom(start, line, column));
    }

    private PapyrusToken LexNumber(int start, int line, int column)
    {
        if (Current == '0' && (Peek() == 'x' || Peek() == 'X'))
        {
            _pos += 2;
            var digits = 0;
            while (!AtEnd && Uri.IsHexDigit(Current))
            {
                _pos++;
                digits++;
            }
            var hex = _text.Substring(start, _pos - start);
            if (digits == 0)
            {
                _diagnostics.Report(
                    PapyrusDiagnosticCodes.MalformedNumber,
                    "Hexadecimal literal has no digits after '0x'.",
                    SpanFrom(start, line, column));
            }
            return new PapyrusToken(PapyrusTokenKind.IntLiteral, hex, SpanFrom(start, line, column));
        }

        while (!AtEnd && char.IsDigit(Current)) _pos++;

        var isFloat = false;

        if (Current == '.' && char.IsDigit(Peek()))
        {
            isFloat = true;
            _pos++;
            while (!AtEnd && char.IsDigit(Current)) _pos++;
        }

        if (Current == 'f' || Current == 'F')
        {
            isFloat = true;
            _pos++;
        }

        var text = _text.Substring(start, _pos - start);
        return new PapyrusToken(
            isFloat ? PapyrusTokenKind.FloatLiteral : PapyrusTokenKind.IntLiteral,
            text,
            SpanFrom(start, line, column));
    }

    private PapyrusToken LexString(int start, int line, int column)
    {
        _pos++;
        var sb = new StringBuilder();
        var terminated = false;

        while (!AtEnd)
        {
            var c = Current;
            if (c == '"')
            {
                _pos++;
                terminated = true;
                break;
            }
            if (c == '\r' || c == '\n') break;

            if (c == '\\' && _pos + 1 < _text.Length)
            {
                _pos++;
                var esc = Current;
                _pos++;
                sb.Append(esc switch
                {
                    'n' => "\n",
                    't' => "\t",
                    'r' => "\r",
                    '\\' => "\\",
                    '"' => "\"",

                    _ => "\\" + esc,
                });
                continue;
            }

            sb.Append(c);
            _pos++;
        }

        if (!terminated)
        {
            _diagnostics.Report(
                PapyrusDiagnosticCodes.UnterminatedString,
                "String literal is never closed.",
                SpanFrom(start, line, column));
        }

        return new PapyrusToken(PapyrusTokenKind.StringLiteral, sb.ToString(), SpanFrom(start, line, column));
    }

    private PapyrusToken LexDocComment(int start, int line, int column)
    {
        _pos++;
        var contentStart = _pos;
        var terminated = false;
        while (!AtEnd)
        {
            if (Current == '}')
            {
                terminated = true;
                break;
            }
            if (Current == '\r' || Current == '\n') ConsumeNewline();
            else _pos++;
        }

        var content = _text.Substring(contentStart, _pos - contentStart);
        if (terminated) _pos++;
        else
        {
            _diagnostics.Report(
                PapyrusDiagnosticCodes.UnterminatedDocComment,
                "Documentation comment is never closed; expected '}'.",
                SpanFrom(start, line, column));
        }

        return new PapyrusToken(PapyrusTokenKind.DocComment, content, SpanFrom(start, line, column));
    }

    private PapyrusToken LexOperator(int start, int line, int column)
    {
        var c = Current;
        var two = _pos + 1 < _text.Length ? _text.Substring(_pos, 2) : string.Empty;

        PapyrusTokenKind kind;
        switch (two)
        {
            case "==": kind = PapyrusTokenKind.Equal; break;
            case "!=": kind = PapyrusTokenKind.NotEqual; break;
            case "<=": kind = PapyrusTokenKind.LessEqual; break;
            case ">=": kind = PapyrusTokenKind.GreaterEqual; break;
            case "||": kind = PapyrusTokenKind.Or; break;
            case "&&": kind = PapyrusTokenKind.And; break;
            case "+=": kind = PapyrusTokenKind.PlusAssign; break;
            case "-=": kind = PapyrusTokenKind.MinusAssign; break;
            case "*=": kind = PapyrusTokenKind.StarAssign; break;
            case "/=": kind = PapyrusTokenKind.SlashAssign; break;
            case "%=": kind = PapyrusTokenKind.PercentAssign; break;
            default: kind = PapyrusTokenKind.EndOfFile; break;
        }

        if (kind != PapyrusTokenKind.EndOfFile)
        {
            _pos += 2;
            return new PapyrusToken(kind, two, SpanFrom(start, line, column));
        }

        switch (c)
        {
            case '(': kind = PapyrusTokenKind.LParen; break;
            case ')': kind = PapyrusTokenKind.RParen; break;
            case '[': kind = PapyrusTokenKind.LBracket; break;
            case ']': kind = PapyrusTokenKind.RBracket; break;
            case ',': kind = PapyrusTokenKind.Comma; break;
            case '.': kind = PapyrusTokenKind.Dot; break;
            case ':': kind = PapyrusTokenKind.Colon; break;
            case '=': kind = PapyrusTokenKind.Assign; break;
            case '<': kind = PapyrusTokenKind.Less; break;
            case '>': kind = PapyrusTokenKind.Greater; break;
            case '!': kind = PapyrusTokenKind.Not; break;
            case '+': kind = PapyrusTokenKind.Plus; break;
            case '-': kind = PapyrusTokenKind.Minus; break;
            case '*': kind = PapyrusTokenKind.Star; break;
            case '/': kind = PapyrusTokenKind.Slash; break;
            case '%': kind = PapyrusTokenKind.Percent; break;
            default:
                _pos++;
                _diagnostics.Report(
                    PapyrusDiagnosticCodes.UnexpectedCharacter,
                    $"Unexpected character '{c}'.",
                    SpanFrom(start, line, column));

                return Next();
        }

        _pos++;
        return new PapyrusToken(kind, c.ToString(CultureInfo.InvariantCulture), SpanFrom(start, line, column));
    }
}
