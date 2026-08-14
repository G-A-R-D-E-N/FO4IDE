using System;
using System.Collections.Generic;

namespace FO4RecordEditor.Services.Papyrus;

/// <summary>
/// Every token the Papyrus lexer produces.
/// </summary>
/// <remarks>
/// Keywords get one kind each rather than a single <c>Keyword</c> kind plus a string compare: the
/// parser branches on them constantly, and the language has exactly 45, which is small enough that
/// an enum is the cheaper and more legible choice.
/// <para>
/// The 45 come from the Creation Kit wiki's Keyword Reference, mirrored in <c>tools/ckwiki</c>. Words
/// that look like keywords but are not on that list -- <c>Hidden</c>, <c>Conditional</c>,
/// <c>Mandatory</c>, <c>Collapsed</c>, <c>Default</c> -- are *user flags*, defined by the
/// <c>Institute_Papyrus_Flags.flg</c> file that ships with the Creation Kit rather than by the
/// language. They lex as identifiers, and the flag-list parser accepts identifiers, which is what
/// lets this front end read scripts without that file present.
/// </para>
/// </remarks>
public enum PapyrusTokenKind
{
    EndOfFile,

    /// <summary>A line break that terminates a statement. Suppressed after a <c>\</c> continuation.</summary>
    Newline,

    Identifier,
    IntLiteral,
    FloatLiteral,
    StringLiteral,

    /// <summary>A <c>{ ... }</c> documentation comment. Kept because it is attached to declarations.</summary>
    DocComment,

    // Keywords, alphabetically as the wiki lists them.
    As,
    Auto,
    AutoReadOnly,
    BetaOnly,
    Bool,
    Const,
    CustomEvent,
    CustomEventName,
    DebugOnly,
    Else,
    ElseIf,
    EndEvent,
    EndFunction,
    EndGroup,
    EndIf,
    EndProperty,
    EndState,
    EndStruct,
    EndWhile,
    Event,
    Extends,
    False,
    Float,
    Function,
    Global,
    Group,
    If,
    Import,
    Is,
    Int,
    Length,
    Native,
    New,
    None,
    Property,
    Return,
    ScriptName,
    ScriptEventName,
    State,
    String,
    Struct,
    StructVarName,
    True,
    Var,
    While,

    // Punctuation and operators.
    LParen,
    RParen,
    LBracket,
    RBracket,
    Comma,
    Dot,
    Colon,
    Assign,
    PlusAssign,
    MinusAssign,
    StarAssign,
    SlashAssign,
    PercentAssign,
    Equal,
    NotEqual,
    Less,
    Greater,
    LessEqual,
    GreaterEqual,
    Or,
    And,
    Not,
    Plus,
    Minus,
    Star,
    Slash,
    Percent,
}

/// <summary>One lexed token: its kind, its source span, and its text.</summary>
/// <remarks>
/// <see cref="Text"/> is the raw source slice for most kinds, but for a string literal it is the
/// *decoded* value (escapes resolved, quotes stripped) and for a doc comment it is the inner text
/// with the braces stripped. Those two are the only kinds whose useful value differs from their
/// spelling, and every consumer wants the value, so decoding once in the lexer beats re-decoding at
/// each use site. The span still covers the original spelling, so editor positions stay correct.
/// </remarks>
public readonly struct PapyrusToken
{
    public PapyrusToken(PapyrusTokenKind kind, string text, PapyrusSpan span)
    {
        Kind = kind;
        Text = text;
        Span = span;
    }

    public PapyrusTokenKind Kind { get; }

    public string Text { get; }

    public PapyrusSpan Span { get; }

    public override string ToString() => $"{Kind} '{Text}' {Span}";
}

/// <summary>Keyword table. Papyrus keywords are case-insensitive.</summary>
public static class PapyrusKeywords
{
    private static readonly Dictionary<string, PapyrusTokenKind> Map =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["as"] = PapyrusTokenKind.As,
            ["auto"] = PapyrusTokenKind.Auto,
            ["autoreadonly"] = PapyrusTokenKind.AutoReadOnly,
            ["betaonly"] = PapyrusTokenKind.BetaOnly,
            ["bool"] = PapyrusTokenKind.Bool,
            ["const"] = PapyrusTokenKind.Const,
            ["customevent"] = PapyrusTokenKind.CustomEvent,
            ["customeventname"] = PapyrusTokenKind.CustomEventName,
            ["debugonly"] = PapyrusTokenKind.DebugOnly,
            ["else"] = PapyrusTokenKind.Else,
            ["elseif"] = PapyrusTokenKind.ElseIf,
            ["endevent"] = PapyrusTokenKind.EndEvent,
            ["endfunction"] = PapyrusTokenKind.EndFunction,
            ["endgroup"] = PapyrusTokenKind.EndGroup,
            ["endif"] = PapyrusTokenKind.EndIf,
            ["endproperty"] = PapyrusTokenKind.EndProperty,
            ["endstate"] = PapyrusTokenKind.EndState,
            ["endstruct"] = PapyrusTokenKind.EndStruct,
            ["endwhile"] = PapyrusTokenKind.EndWhile,
            ["event"] = PapyrusTokenKind.Event,
            ["extends"] = PapyrusTokenKind.Extends,
            ["false"] = PapyrusTokenKind.False,
            ["float"] = PapyrusTokenKind.Float,
            ["function"] = PapyrusTokenKind.Function,
            ["global"] = PapyrusTokenKind.Global,
            ["group"] = PapyrusTokenKind.Group,
            ["if"] = PapyrusTokenKind.If,
            ["import"] = PapyrusTokenKind.Import,
            ["is"] = PapyrusTokenKind.Is,
            ["int"] = PapyrusTokenKind.Int,
            ["length"] = PapyrusTokenKind.Length,
            ["native"] = PapyrusTokenKind.Native,
            ["new"] = PapyrusTokenKind.New,
            ["none"] = PapyrusTokenKind.None,
            ["property"] = PapyrusTokenKind.Property,
            ["return"] = PapyrusTokenKind.Return,
            ["scriptname"] = PapyrusTokenKind.ScriptName,
            ["scripteventname"] = PapyrusTokenKind.ScriptEventName,
            ["state"] = PapyrusTokenKind.State,
            ["string"] = PapyrusTokenKind.String,
            ["struct"] = PapyrusTokenKind.Struct,
            ["structvarname"] = PapyrusTokenKind.StructVarName,
            ["true"] = PapyrusTokenKind.True,
            ["var"] = PapyrusTokenKind.Var,
            ["while"] = PapyrusTokenKind.While,
        };

    /// <summary>The 45 language keywords, for tests and for editor syntax highlighting.</summary>
    public static IReadOnlyCollection<string> All => Map.Keys;

    public static bool TryGet(string word, out PapyrusTokenKind kind) => Map.TryGetValue(word, out kind);
}
