using System;
using System.Collections.Generic;

namespace FO4RecordEditor.Services.Papyrus;

















public enum PapyrusTokenKind
{
    EndOfFile,


    Newline,

    Identifier,
    IntLiteral,
    FloatLiteral,
    StringLiteral,


    DocComment,


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


    public static IReadOnlyCollection<string> All => Map.Keys;

    public static bool TryGet(string word, out PapyrusTokenKind kind) => Map.TryGetValue(word, out kind);
}
