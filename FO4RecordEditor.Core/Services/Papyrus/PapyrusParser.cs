using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FO4RecordEditor.Services.Papyrus;

/// <summary>
/// Hand-written recursive descent parser for Papyrus, producing a <see cref="PapyrusScript"/>.
/// </summary>
/// <remarks>
/// Hand-written rather than generated because the language is genuinely tiny: 56 BNF productions,
/// 45 keywords, and exactly five statement forms (define, assign, return, if, while). There is no
/// <c>for</c>, no <c>switch</c>, no <c>do/while</c>, no <c>break</c> or <c>continue</c>, and no
/// exceptions. The complete grammar is the Creation Kit wiki's Papyrus Language Reference, all 20
/// pages of it, mirrored under <c>tools/ckwiki</c>; every production this file implements is quoted
/// in the comment above the method that implements it.
/// <para>
/// <b>This parser never throws on bad input and never returns null.</b> It records a diagnostic,
/// recovers to the next line or block terminator, and carries on, so a file that is mid-keystroke
/// still yields a symbol table. That is a requirement, not politeness: the point of phase 1 is
/// editor intelligence, and an outline that vanishes whenever the file is briefly invalid is worse
/// than no outline.
/// </para>
/// <para>
/// Phase 1 stops at syntax. Nothing here binds a name to a declaration, checks a type, or resolves
/// an override -- that is the resolver and type checker, which is the part of a real compiler this
/// deliberately does not attempt. <see cref="PapyrusScriptIndex"/> does name-based lookup on top of
/// these trees, which is enough for go-to-definition and hover and not enough for codegen.
/// </para>
/// </remarks>
public sealed class PapyrusParser
{
    private readonly IReadOnlyList<PapyrusToken> _tokens;
    private readonly DiagnosticBag _diagnostics;
    private int _index;

    private PapyrusParser(IReadOnlyList<PapyrusToken> tokens, DiagnosticBag diagnostics)
    {
        _tokens = tokens;
        _diagnostics = diagnostics;
    }

    /// <summary>Parses Papyrus source text.</summary>
    /// <param name="text">The .psc contents.</param>
    /// <param name="filePath">Path to attach to diagnostics and to the script, if it came from disk.</param>
    public static PapyrusScript Parse(string text, string? filePath = null)
    {
        var bag = new DiagnosticBag();
        var tokens = PapyrusLexer.Lex(text, bag);
        var script = new PapyrusParser(tokens, bag).ParseScript();
        script.FilePath = filePath;
        bag.SetFile(filePath);
        script.Diagnostics = bag.Items;
        return script;
    }

    /// <summary>Reads and parses a .psc file.</summary>
    public static PapyrusScript ParseFile(string path) => Parse(File.ReadAllText(path), path);

    // -----------------------------------------------------------------------------------------
    // Token plumbing
    // -----------------------------------------------------------------------------------------

    private PapyrusToken Current => _tokens[_index];

    private PapyrusTokenKind Kind => _tokens[_index].Kind;

    private PapyrusToken Peek(int offset)
    {
        var i = _index + offset;
        return i < _tokens.Count ? _tokens[i] : _tokens[_tokens.Count - 1];
    }

    private bool AtEnd => Kind == PapyrusTokenKind.EndOfFile;

    private PapyrusToken Advance()
    {
        var token = _tokens[_index];
        if (_index < _tokens.Count - 1) _index++;
        return token;
    }

    private bool Match(PapyrusTokenKind kind)
    {
        if (Kind != kind) return false;
        Advance();
        return true;
    }

    private PapyrusToken Expect(PapyrusTokenKind kind, string what)
    {
        if (Kind == kind) return Advance();
        _diagnostics.Report(
            PapyrusDiagnosticCodes.ExpectedToken,
            $"Expected {what}, found '{Describe(Current)}'.",
            Current.Span);
        // A zero-length synthetic token at the current position keeps spans monotonic without
        // consuming input the caller may still be able to recover on.
        return new PapyrusToken(kind, string.Empty, new PapyrusSpan(Current.Span.Start, 0, Current.Span.Line, Current.Span.Column));
    }

    private static string Describe(PapyrusToken token) => token.Kind switch
    {
        PapyrusTokenKind.EndOfFile => "end of file",
        PapyrusTokenKind.Newline => "end of line",
        _ => token.Text,
    };

    private void SkipNewlines()
    {
        while (Kind == PapyrusTokenKind.Newline) Advance();
    }

    /// <summary>Consumes the statement terminator, complaining about trailing junk first.</summary>
    private void ExpectEndOfLine()
    {
        if (Kind == PapyrusTokenKind.Newline || AtEnd)
        {
            Match(PapyrusTokenKind.Newline);
            return;
        }
        _diagnostics.Report(
            PapyrusDiagnosticCodes.UnexpectedToken,
            $"Unexpected '{Describe(Current)}' at end of line.",
            Current.Span);
        SkipToNextLine();
    }

    private void SkipToNextLine()
    {
        while (!AtEnd && Kind != PapyrusTokenKind.Newline) Advance();
        Match(PapyrusTokenKind.Newline);
    }

    /// <summary>
    /// Pulls in a documentation comment belonging to the declaration just parsed.
    /// </summary>
    /// <remarks>
    /// Per the wiki, a <c>{ ... }</c> comment may only follow a script header, property, group,
    /// struct member or function definition, so scanning past blank lines to find one cannot steal
    /// it from anything else -- there is nothing else it could attach to.
    /// </remarks>
    private string? TakeDocComment()
    {
        var save = _index;
        SkipNewlines();
        if (Kind == PapyrusTokenKind.DocComment) return Advance().Text;
        _index = save;
        return null;
    }

    /// <summary>Collects trailing flag words. Both keyword flags and user flags land here.</summary>
    /// <remarks>
    /// User flags -- <c>Hidden</c>, <c>Conditional</c>, <c>Mandatory</c>, <c>CollapsedOnRef</c> and
    /// friends -- are not language keywords. They are defined by <c>Institute_Papyrus_Flags.flg</c>,
    /// which ships with the Creation Kit and is not in the game archives. Accepting any identifier
    /// here is what lets this front end read scripts on a machine that has no CK installed; a real
    /// compiler back end would have to validate the names against that file.
    /// </remarks>
    private void ParseFlags(List<string> into)
    {
        while (true)
        {
            switch (Kind)
            {
                case PapyrusTokenKind.Identifier:
                case PapyrusTokenKind.Const:
                case PapyrusTokenKind.Auto:
                case PapyrusTokenKind.AutoReadOnly:
                case PapyrusTokenKind.DebugOnly:
                case PapyrusTokenKind.BetaOnly:
                case PapyrusTokenKind.Native:
                case PapyrusTokenKind.Global:
                    into.Add(Advance().Text.ToLowerInvariant());
                    continue;
                default:
                    return;
            }
        }
    }

    // -----------------------------------------------------------------------------------------
    // Script structure
    //   <header line>
    //   (<import> | <variable definition> | <struct definition> | <custom event definition> |
    //    <property definition> | <group definition> | <state definition> | <function definition> |
    //    <event definition>)*
    // -----------------------------------------------------------------------------------------

    private PapyrusScript ParseScript()
    {
        var script = new PapyrusScript();
        ParseHeader(script);

        while (!AtEnd)
        {
            SkipNewlines();
            if (AtEnd) break;

            var before = _index;
            ParseScriptMember(script);
            // Every path above should consume something. This guard is what makes "never hangs" a
            // property of the parser rather than a hope about its branches.
            if (_index == before) Advance();
        }

        var start = script.NameSpan.Start;
        var last = _tokens[_tokens.Count - 1].Span;
        script.Span = new PapyrusSpan(0, Math.Max(last.End, start), 1, 1);
        return script;
    }

    /// <summary>
    /// <c>&lt;Header Line&gt; ::= 'ScriptName' &lt;identifier&gt; ['extends' &lt;identifier&gt;] ['Native'] (&lt;flags&gt;)*</c>
    /// </summary>
    private void ParseHeader(PapyrusScript script)
    {
        SkipNewlines();
        // A stray doc comment before the header is not legal, but tolerating it costs nothing and
        // keeps a file that opens with a banner comment from losing its whole symbol table.
        while (Kind == PapyrusTokenKind.DocComment)
        {
            script.Documentation ??= Advance().Text;
            SkipNewlines();
        }

        if (Kind != PapyrusTokenKind.ScriptName)
        {
            _diagnostics.Report(
                PapyrusDiagnosticCodes.ExpectedScriptName,
                "Script must start with a 'ScriptName' header line.",
                Current.Span);
            return;
        }

        var keyword = Advance();
        var name = ParseQualifiedName(out var nameSpan);
        script.Name = name;
        script.NameSpan = nameSpan;
        script.Span = keyword.Span.To(nameSpan);

        if (Kind == PapyrusTokenKind.Extends)
        {
            Advance();
            script.Extends = ParseQualifiedName(out var extendsSpan);
            script.ExtendsSpan = extendsSpan;
        }

        ParseFlags(script.Flags);
        ExpectEndOfLine();
        script.Documentation ??= TakeDocComment();
    }

    /// <summary>
    /// <c>&lt;full script name&gt; ::= &lt;identifier&gt; (':' &lt;identifier&gt;)*</c>
    /// </summary>
    /// <remarks>
    /// The colons are namespace separators and they are also folder separators: a script named
    /// <c>MyCoolStuff:Quests:MyQuest</c> lives at <c>MyCoolStuff\Quests\MyQuest.psc</c>. That
    /// equivalence is what <see cref="PapyrusScriptIndex"/> uses to find a script on disk from its
    /// name, and it is the same rule the existing <c>PapyrusService.Compile</c> staging already
    /// implements for the shell-out compiler.
    /// </remarks>
    private string ParseQualifiedName(out PapyrusSpan span)
    {
        var first = Expect(PapyrusTokenKind.Identifier, "an identifier");
        span = first.Span;
        var name = first.Text;
        while (Kind == PapyrusTokenKind.Colon)
        {
            Advance();
            var part = Expect(PapyrusTokenKind.Identifier, "an identifier after ':'");
            name += ":" + part.Text;
            span = span.To(part.Span);
        }
        return name;
    }

    private void ParseScriptMember(PapyrusScript script)
    {
        switch (Kind)
        {
            case PapyrusTokenKind.Import:
                ParseImport(script);
                return;

            case PapyrusTokenKind.Struct:
                script.Structs.Add(ParseStruct());
                return;

            case PapyrusTokenKind.CustomEvent:
                ParseCustomEvent(script);
                return;

            case PapyrusTokenKind.Group:
                ParseGroup(script);
                return;

            case PapyrusTokenKind.State:
            case PapyrusTokenKind.Auto when Peek(1).Kind == PapyrusTokenKind.State:
                script.States.Add(ParseState());
                return;

            case PapyrusTokenKind.Event:
                script.Events.Add(ParseEvent(null));
                return;

            case PapyrusTokenKind.Function:
                script.Functions.Add(ParseFunction(null, null));
                return;

            case PapyrusTokenKind.EndOfFile:
                return;
        }

        // Everything left starts with a type: a variable, a property, or a function with a return
        // type. Which of the three only becomes clear after the type is read.
        if (!TryParseType(out var type))
        {
            _diagnostics.Report(
                PapyrusDiagnosticCodes.UnexpectedToken,
                $"Unexpected '{Describe(Current)}' at script level.",
                Current.Span);
            SkipToNextLine();
            return;
        }

        switch (Kind)
        {
            case PapyrusTokenKind.Function:
                script.Functions.Add(ParseFunction(type, null));
                return;
            case PapyrusTokenKind.Property:
                script.Properties.Add(ParseProperty(type, null));
                return;
            case PapyrusTokenKind.Identifier:
                script.Variables.Add(ParseVariable(type, allowDocumentation: false));
                return;
            default:
                _diagnostics.Report(
                    PapyrusDiagnosticCodes.UnexpectedToken,
                    $"Expected 'Function', 'Property' or a variable name after type '{type}', found '{Describe(Current)}'.",
                    Current.Span);
                SkipToNextLine();
                return;
        }
    }

    /// <summary><c>'Import' &lt;identifier&gt;</c></summary>
    private void ParseImport(PapyrusScript script)
    {
        var keyword = Advance();
        var name = ParseQualifiedName(out var nameSpan);
        var import = new PapyrusImport(name, nameSpan) { Span = keyword.Span.To(nameSpan) };
        script.Imports.Add(import);
        ExpectEndOfLine();
    }

    /// <summary><c>'CustomEvent' &lt;identifier&gt;</c></summary>
    private void ParseCustomEvent(PapyrusScript script)
    {
        var keyword = Advance();
        var name = Expect(PapyrusTokenKind.Identifier, "a custom event name");
        script.CustomEvents.Add(new PapyrusCustomEventDecl
        {
            Name = name.Text,
            NameSpan = name.Span,
            Span = keyword.Span.To(name.Span),
        });
        ExpectEndOfLine();
    }

    // -----------------------------------------------------------------------------------------
    // Types
    //   <type>       ::= ('int'|'float'|'bool'|'string'|'var'|<full script name>) ['[' ']']
    //   <array type> ::= <element type> '[' ']'
    // -----------------------------------------------------------------------------------------

    /// <remarks>
    /// <c>ScriptEventName</c>, <c>CustomEventName</c> and <c>StructVarName</c> are keywords, but they
    /// appear where a type appears -- they are the Function Reference's "special parameter types",
    /// which accept only a raw string literal and are checked by the compiler against the events or
    /// struct members of the preceding parameter. Syntactically they are types, so they belong here.
    /// </remarks>
    private static bool IsTypeStart(PapyrusTokenKind kind) => kind switch
    {
        PapyrusTokenKind.Int or PapyrusTokenKind.Float or PapyrusTokenKind.Bool
            or PapyrusTokenKind.String or PapyrusTokenKind.Var or PapyrusTokenKind.Identifier
            or PapyrusTokenKind.ScriptEventName or PapyrusTokenKind.CustomEventName
            or PapyrusTokenKind.StructVarName => true,
        _ => false,
    };

    /// <summary>Parses a type without consuming anything if the current token cannot start one.</summary>
    private bool TryParseType(out PapyrusTypeRef type)
    {
        type = null!;
        if (!IsTypeStart(Kind)) return false;
        type = ParseType();
        return true;
    }

    private PapyrusTypeRef ParseType()
    {
        var start = Current.Span;
        string name;

        switch (Kind)
        {
            case PapyrusTokenKind.Int:
            case PapyrusTokenKind.Float:
            case PapyrusTokenKind.Bool:
            case PapyrusTokenKind.String:
            case PapyrusTokenKind.Var:
            case PapyrusTokenKind.ScriptEventName:
            case PapyrusTokenKind.CustomEventName:
            case PapyrusTokenKind.StructVarName:
                name = Advance().Text.ToLowerInvariant();
                break;
            case PapyrusTokenKind.Identifier:
                name = ParseQualifiedName(out var span);
                start = start.To(span);
                break;
            default:
                _diagnostics.Report(
                    PapyrusDiagnosticCodes.ExpectedType,
                    $"Expected a type, found '{Describe(Current)}'.",
                    Current.Span);
                return new PapyrusTypeRef("", false, Current.Span);
        }

        var isArray = false;
        if (Kind == PapyrusTokenKind.LBracket && Peek(1).Kind == PapyrusTokenKind.RBracket)
        {
            Advance();
            var close = Advance();
            isArray = true;
            start = start.To(close.Span);
        }

        return new PapyrusTypeRef(name, isArray, start);
    }

    // -----------------------------------------------------------------------------------------
    // Variables, structs, properties, groups
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// <c>&lt;variable definition&gt; ::= &lt;type&gt; &lt;identifier&gt; ['=' &lt;constant&gt;] (&lt;flags&gt;)*</c>
    /// </summary>
    private PapyrusVariableDecl ParseVariable(PapyrusTypeRef type, bool allowDocumentation)
    {
        var name = Expect(PapyrusTokenKind.Identifier, "a variable name");
        var decl = new PapyrusVariableDecl
        {
            Type = type,
            Name = name.Text,
            NameSpan = name.Span,
        };

        if (Match(PapyrusTokenKind.Assign)) decl.Initializer = ParseExpression();

        ParseFlags(decl.Flags);
        decl.Span = type.Span.To(_tokens[Math.Max(0, _index - 1)].Span);
        ExpectEndOfLine();
        // Only struct members may carry a doc string, per the Variable Reference.
        if (allowDocumentation) decl.Documentation = TakeDocComment();
        return decl;
    }

    /// <summary>
    /// <c>&lt;struct&gt; ::= 'struct' &lt;identifier&gt; &lt;variable definition&gt;+ 'endstruct'</c>
    /// </summary>
    private PapyrusStructDecl ParseStruct()
    {
        var keyword = Advance();
        var name = Expect(PapyrusTokenKind.Identifier, "a struct name");
        var decl = new PapyrusStructDecl { Name = name.Text, NameSpan = name.Span };
        ExpectEndOfLine();
        decl.Documentation = TakeDocComment();

        while (true)
        {
            SkipNewlines();
            if (AtEnd || Kind == PapyrusTokenKind.EndStruct) break;

            var before = _index;
            if (TryParseType(out var memberType))
            {
                decl.Members.Add(ParseVariable(memberType, allowDocumentation: true));
            }
            else
            {
                _diagnostics.Report(
                    PapyrusDiagnosticCodes.UnexpectedToken,
                    $"Expected a struct member or 'EndStruct', found '{Describe(Current)}'.",
                    Current.Span);
                SkipToNextLine();
            }
            if (_index == before) Advance();
        }

        var end = ExpectBlockEnd(PapyrusTokenKind.EndStruct, "EndStruct", keyword.Span);
        if (decl.Members.Count == 0)
        {
            _diagnostics.Report(
                PapyrusDiagnosticCodes.StructNeedsMember,
                $"Struct '{decl.Name}' must declare at least one member.",
                decl.NameSpan);
        }
        decl.Span = keyword.Span.To(end);
        return decl;
    }

    /// <summary>
    /// <c>&lt;property&gt;</c>, <c>&lt;auto property&gt;</c> and <c>&lt;auto read-only property&gt;</c>.
    /// </summary>
    /// <remarks>
    /// The three forms are only distinguishable after the flag list: an auto property ends on its
    /// own line, a full one opens a block of Get/Set functions closed by <c>EndProperty</c>.
    /// </remarks>
    private PapyrusPropertyDecl ParseProperty(PapyrusTypeRef type, string? groupName)
    {
        var keyword = Advance(); // 'Property'
        var name = Expect(PapyrusTokenKind.Identifier, "a property name");
        var decl = new PapyrusPropertyDecl
        {
            Type = type,
            Name = name.Text,
            NameSpan = name.Span,
            GroupName = groupName,
        };

        if (Match(PapyrusTokenKind.Assign)) decl.Initializer = ParseExpression();

        ParseFlags(decl.Flags);
        decl.Kind = decl.HasFlag("autoreadonly")
            ? PapyrusPropertyKind.AutoReadOnly
            : decl.HasFlag("auto") ? PapyrusPropertyKind.Auto : PapyrusPropertyKind.Full;

        decl.Span = type.Span.To(_tokens[Math.Max(0, _index - 1)].Span);
        ExpectEndOfLine();
        decl.Documentation = TakeDocComment();

        // A property is the one declaration whose single-line and block forms are told apart only
        // by what follows: no Auto flag means a full property, which then consumes everything up to
        // EndProperty. So a header that did not even yield a name must not open a block -- otherwise
        // one bad property line eats the rest of the file, and every declaration after it vanishes
        // from the outline. Bailing out here costs nothing: the header is already reported.
        if (decl.Kind != PapyrusPropertyKind.Full || name.Text.Length == 0) return decl;

        while (true)
        {
            SkipNewlines();
            if (AtEnd || Kind == PapyrusTokenKind.EndProperty) break;

            var before = _index;
            PapyrusTypeRef? returnType = null;
            if (Kind != PapyrusTokenKind.Function) TryParseType(out returnType!);

            if (Kind == PapyrusTokenKind.Function)
            {
                var accessor = ParseFunction(returnType, null);
                if (string.Equals(accessor.Name, "get", StringComparison.OrdinalIgnoreCase)) decl.Getter = accessor;
                else if (string.Equals(accessor.Name, "set", StringComparison.OrdinalIgnoreCase)) decl.Setter = accessor;
            }
            else
            {
                _diagnostics.Report(
                    PapyrusDiagnosticCodes.UnexpectedToken,
                    $"Expected a Get or Set function, or 'EndProperty', found '{Describe(Current)}'.",
                    Current.Span);
                SkipToNextLine();
            }
            if (_index == before) Advance();
        }

        var end = ExpectBlockEnd(PapyrusTokenKind.EndProperty, "EndProperty", keyword.Span);
        if (decl.Getter == null && decl.Setter == null)
        {
            _diagnostics.Report(
                PapyrusDiagnosticCodes.PropertyNeedsAccessor,
                $"Property '{decl.Name}' is not auto and has neither a Get nor a Set function.",
                decl.NameSpan);
        }
        decl.Span = decl.Span.To(end);
        return decl;
    }

    /// <summary>
    /// <c>&lt;group&gt; ::= 'Group' &lt;identifier&gt; &lt;flags&gt; (&lt;property&gt;)+ 'endGroup'</c>
    /// </summary>
    private void ParseGroup(PapyrusScript script)
    {
        var keyword = Advance();
        var name = Expect(PapyrusTokenKind.Identifier, "a group name");
        var group = new PapyrusGroupDecl { Name = name.Text, NameSpan = name.Span };
        ParseFlags(group.Flags);
        ExpectEndOfLine();
        group.Documentation = TakeDocComment();

        while (true)
        {
            SkipNewlines();
            if (AtEnd || Kind == PapyrusTokenKind.EndGroup) break;

            var before = _index;
            if (TryParseType(out var type) && Kind == PapyrusTokenKind.Property)
            {
                var property = ParseProperty(type, group.Name);
                group.Properties.Add(property);
                // Also listed on the script so a caller asking "what properties does this script
                // expose?" does not have to know about grouping.
                script.Properties.Add(property);
            }
            else
            {
                _diagnostics.Report(
                    PapyrusDiagnosticCodes.UnexpectedToken,
                    $"Expected a property or 'EndGroup', found '{Describe(Current)}'.",
                    Current.Span);
                SkipToNextLine();
            }
            if (_index == before) Advance();
        }

        var end = ExpectBlockEnd(PapyrusTokenKind.EndGroup, "EndGroup", keyword.Span);
        group.Span = keyword.Span.To(end);
        script.Groups.Add(group);
    }

    /// <summary>
    /// <c>&lt;state&gt; ::= ['Auto'] 'State' &lt;identifier&gt; &lt;function or event&gt;* 'EndState'</c>
    /// </summary>
    private PapyrusStateDecl ParseState()
    {
        var start = Current.Span;
        var isAuto = Match(PapyrusTokenKind.Auto);
        Expect(PapyrusTokenKind.State, "'State'");
        var name = Expect(PapyrusTokenKind.Identifier, "a state name");
        var state = new PapyrusStateDecl { Name = name.Text, NameSpan = name.Span, IsAuto = isAuto };
        ExpectEndOfLine();

        while (true)
        {
            SkipNewlines();
            if (AtEnd || Kind == PapyrusTokenKind.EndState) break;

            var before = _index;
            if (Kind == PapyrusTokenKind.Event)
            {
                state.Events.Add(ParseEvent(state.Name));
            }
            else if (Kind == PapyrusTokenKind.Function)
            {
                state.Functions.Add(ParseFunction(null, state.Name));
            }
            else if (TryParseType(out var returnType) && Kind == PapyrusTokenKind.Function)
            {
                state.Functions.Add(ParseFunction(returnType, state.Name));
            }
            else
            {
                _diagnostics.Report(
                    PapyrusDiagnosticCodes.UnexpectedToken,
                    $"Expected a function, an event or 'EndState', found '{Describe(Current)}'.",
                    Current.Span);
                SkipToNextLine();
            }
            if (_index == before) Advance();
        }

        var end = ExpectBlockEnd(PapyrusTokenKind.EndState, "EndState", start);
        state.Span = start.To(end);
        return state;
    }

    private PapyrusSpan ExpectBlockEnd(PapyrusTokenKind kind, string what, PapyrusSpan openedAt)
    {
        if (Kind == kind)
        {
            var token = Advance();
            ExpectEndOfLine();
            return token.Span;
        }
        _diagnostics.Report(
            PapyrusDiagnosticCodes.UnterminatedBlock,
            $"Block opened here is never closed; expected '{what}'.",
            openedAt);
        return Current.Span;
    }

    // -----------------------------------------------------------------------------------------
    // Functions and events
    //   <function header> ::= [<type>] 'Function' <identifier> '(' [<parameters>] ')'
    //                         ('global' | 'native')* <flags>*
    //   <event header>    ::= 'Event' <identifier> '(' [<parameters>] ')' ['Native'] <flags>*
    //   <remote event>    ::= 'Event' <object type> '.' <identifier> '(' ... ')' ['Native'] <flags>*
    // -----------------------------------------------------------------------------------------

    private PapyrusFunctionDecl ParseFunction(PapyrusTypeRef? returnType, string? stateName)
    {
        var keyword = Advance(); // 'Function'
        var start = returnType?.Span ?? keyword.Span;
        var name = Expect(PapyrusTokenKind.Identifier, "a function name");
        var fn = new PapyrusFunctionDecl
        {
            ReturnType = returnType,
            Name = name.Text,
            NameSpan = name.Span,
            StateName = stateName,
        };

        ParseParameterList(fn.Parameters);
        ParseFlags(fn.Flags);
        fn.IsGlobal = fn.HasFlag("global");
        fn.IsNative = fn.HasFlag("native");
        ExpectEndOfLine();
        fn.Documentation = TakeDocComment();

        // A native function is implemented by the game and has no body or terminator.
        if (fn.IsNative)
        {
            fn.Span = start.To(name.Span);
            return fn;
        }

        ParseBlock(fn.Body, PapyrusTokenKind.EndFunction);
        var end = ExpectBlockEnd(PapyrusTokenKind.EndFunction, "EndFunction", start);
        fn.Span = start.To(end);
        return fn;
    }

    private PapyrusEventDecl ParseEvent(string? stateName)
    {
        var keyword = Advance(); // 'Event'
        var evt = new PapyrusEventDecl { StateName = stateName };

        var first = ParseQualifiedName(out var firstSpan);
        if (Kind == PapyrusTokenKind.Dot)
        {
            // Remote or custom event handler: Event ObjectReference.OnActivate(...)
            Advance();
            evt.RemoteObjectType = first;
            var name = Expect(PapyrusTokenKind.Identifier, "an event name after '.'");
            evt.Name = name.Text;
            evt.NameSpan = name.Span;
        }
        else
        {
            evt.Name = first;
            evt.NameSpan = firstSpan;
        }

        ParseParameterList(evt.Parameters);
        ParseFlags(evt.Flags);
        evt.IsNative = evt.HasFlag("native");
        ExpectEndOfLine();
        evt.Documentation = TakeDocComment();

        if (evt.IsNative)
        {
            evt.Span = keyword.Span.To(evt.NameSpan);
            return evt;
        }

        ParseBlock(evt.Body, PapyrusTokenKind.EndEvent);
        var end = ExpectBlockEnd(PapyrusTokenKind.EndEvent, "EndEvent", keyword.Span);
        evt.Span = keyword.Span.To(end);
        return evt;
    }

    /// <summary>
    /// <c>&lt;parameters&gt; ::= &lt;parameter&gt; (',' &lt;parameter&gt;)*</c>,
    /// <c>&lt;parameter&gt; ::= &lt;type&gt; &lt;identifier&gt; ['=' &lt;constant&gt;]</c>
    /// </summary>
    private void ParseParameterList(List<PapyrusParameter> into)
    {
        Expect(PapyrusTokenKind.LParen, "'('");
        if (Match(PapyrusTokenKind.RParen)) return;

        while (true)
        {
            var before = _index;
            if (!TryParseType(out var type))
            {
                _diagnostics.Report(
                    PapyrusDiagnosticCodes.ExpectedType,
                    $"Expected a parameter type, found '{Describe(Current)}'.",
                    Current.Span);
                break;
            }

            var name = Expect(PapyrusTokenKind.Identifier, "a parameter name");
            var parameter = new PapyrusParameter
            {
                Type = type,
                Name = name.Text,
                NameSpan = name.Span,
                Span = type.Span.To(name.Span),
            };
            if (Match(PapyrusTokenKind.Assign))
            {
                parameter.DefaultValue = ParseExpression();
                parameter.Span = parameter.Span.To(parameter.DefaultValue.Span);
            }
            into.Add(parameter);

            if (Match(PapyrusTokenKind.Comma))
            {
                if (_index == before) Advance();
                continue;
            }
            break;
        }

        Expect(PapyrusTokenKind.RParen, "')'");
    }

    // -----------------------------------------------------------------------------------------
    // Statements. Five forms, per the Statement Reference: define, assign, return, if, while.
    // -----------------------------------------------------------------------------------------

    private static bool IsBlockTerminator(PapyrusTokenKind kind) => kind switch
    {
        PapyrusTokenKind.EndFunction or PapyrusTokenKind.EndEvent or PapyrusTokenKind.EndIf
            or PapyrusTokenKind.EndWhile or PapyrusTokenKind.Else or PapyrusTokenKind.ElseIf
            or PapyrusTokenKind.EndProperty or PapyrusTokenKind.EndState or PapyrusTokenKind.EndStruct
            or PapyrusTokenKind.EndGroup or PapyrusTokenKind.EndOfFile => true,
        _ => false,
    };

    private void ParseBlock(List<PapyrusStatement> into, params PapyrusTokenKind[] terminators)
    {
        while (true)
        {
            SkipNewlines();
            if (AtEnd) return;
            if (IsBlockTerminator(Kind)) return;
            if (terminators.Contains(Kind)) return;

            var before = _index;
            var statement = ParseStatement();
            if (statement != null) into.Add(statement);
            if (_index == before) Advance();
        }
    }

    private PapyrusStatement? ParseStatement()
    {
        switch (Kind)
        {
            case PapyrusTokenKind.If:
                return ParseIf();
            case PapyrusTokenKind.While:
                return ParseWhile();
            case PapyrusTokenKind.Return:
                return ParseReturn();
        }

        if (TryParseDefine(out var define)) return define;

        // Whatever is left is an expression, and it is an assignment if an assignment operator
        // follows it. Papyrus has no standalone increment or compound-expression statement, so
        // anything else is a bare call.
        var expression = ParseExpression();
        if (IsAssignmentOperator(Kind))
        {
            var op = Advance();
            var value = ParseExpression();
            var assign = new PapyrusAssignStatement
            {
                Target = expression,
                Operator = op.Kind,
                Value = value,
                Span = expression.Span.To(value.Span),
            };
            ExpectEndOfLine();
            return assign;
        }

        var statement = new PapyrusExpressionStatement { Expression = expression, Span = expression.Span };
        ExpectEndOfLine();
        return statement;
    }

    private static bool IsAssignmentOperator(PapyrusTokenKind kind) => kind switch
    {
        PapyrusTokenKind.Assign or PapyrusTokenKind.PlusAssign or PapyrusTokenKind.MinusAssign
            or PapyrusTokenKind.StarAssign or PapyrusTokenKind.SlashAssign
            or PapyrusTokenKind.PercentAssign => true,
        _ => false,
    };

    /// <summary>
    /// <c>&lt;define statement&gt; ::= &lt;type&gt; &lt;identifier&gt; ['=' &lt;expression&gt;]</c>
    /// </summary>
    /// <remarks>
    /// This is the one place the grammar needs lookahead past a whole construct. A statement opening
    /// with an identifier is a definition if a *second* identifier follows the type, and an
    /// assignment otherwise -- <c>MyType x = 1</c> against <c>x = 1</c>, and worse,
    /// <c>Foo[] bar</c> against <c>foo[0] = 1</c>, where the difference is whether the brackets are
    /// empty. Speculating and rewinding the token index is exact, and cheap because the tokens are
    /// already materialised in a list.
    /// </remarks>
    private bool TryParseDefine(out PapyrusStatement? statement)
    {
        statement = null;
        if (!IsTypeStart(Kind)) return false;

        var save = _index;
        var savedDiagnostics = _diagnostics.Items.Count;

        var type = ParseType();
        if (Kind != PapyrusTokenKind.Identifier)
        {
            _index = save;
            _diagnostics.TruncateTo(savedDiagnostics);
            return false;
        }

        var name = Advance();
        var define = new PapyrusDefineStatement
        {
            Type = type,
            Name = name.Text,
            NameSpan = name.Span,
            Span = type.Span.To(name.Span),
        };
        if (Match(PapyrusTokenKind.Assign))
        {
            define.Initializer = ParseExpression();
            define.Span = define.Span.To(define.Initializer.Span);
        }
        ParseFlags(define.Flags);
        ExpectEndOfLine();
        statement = define;
        return true;
    }

    /// <summary>
    /// <c>&lt;if statement&gt; ::= 'if' &lt;expression&gt; &lt;statement&gt;*
    /// ['elseif' &lt;expression&gt; &lt;statement&gt;*]* ['else' &lt;statement&gt;*] 'endIf'</c>
    /// </summary>
    private PapyrusStatement ParseIf()
    {
        var keyword = Advance();
        var statement = new PapyrusIfStatement();

        var condition = ParseExpression();
        ExpectEndOfLine();
        var branch = new PapyrusIfBranch { Condition = condition };
        ParseBlock(branch.Body);
        branch.Span = condition.Span.To(branch.Body.Count > 0 ? branch.Body[branch.Body.Count - 1].Span : condition.Span);
        statement.Branches.Add(branch);

        while (Kind == PapyrusTokenKind.ElseIf)
        {
            Advance();
            var elseCondition = ParseExpression();
            ExpectEndOfLine();
            var elseBranch = new PapyrusIfBranch { Condition = elseCondition };
            ParseBlock(elseBranch.Body);
            elseBranch.Span = elseCondition.Span.To(
                elseBranch.Body.Count > 0 ? elseBranch.Body[elseBranch.Body.Count - 1].Span : elseCondition.Span);
            statement.Branches.Add(elseBranch);
        }

        if (Kind == PapyrusTokenKind.Else)
        {
            Advance();
            ExpectEndOfLine();
            statement.ElseBody = new List<PapyrusStatement>();
            ParseBlock(statement.ElseBody);
        }

        var end = ExpectBlockEnd(PapyrusTokenKind.EndIf, "EndIf", keyword.Span);
        statement.Span = keyword.Span.To(end);
        return statement;
    }

    /// <summary><c>'while' &lt;expression&gt; &lt;statement&gt;* 'endWhile'</c></summary>
    private PapyrusStatement ParseWhile()
    {
        var keyword = Advance();
        var statement = new PapyrusWhileStatement { Condition = ParseExpression() };
        ExpectEndOfLine();
        ParseBlock(statement.Body);
        var end = ExpectBlockEnd(PapyrusTokenKind.EndWhile, "EndWhile", keyword.Span);
        statement.Span = keyword.Span.To(end);
        return statement;
    }

    /// <summary><c>'Return' [&lt;expression&gt;]</c></summary>
    private PapyrusStatement ParseReturn()
    {
        var keyword = Advance();
        var statement = new PapyrusReturnStatement { Span = keyword.Span };
        if (Kind != PapyrusTokenKind.Newline && !AtEnd)
        {
            statement.Value = ParseExpression();
            statement.Span = keyword.Span.To(statement.Value.Span);
        }
        ExpectEndOfLine();
        return statement;
    }

    // -----------------------------------------------------------------------------------------
    // Expressions. Precedence follows the Expression Reference exactly, lowest binding first:
    //   '||'  '&&'  comparison  '+' '-'  '*' '/' '%'  unary '-' '!'  'as' 'is'  '.'  '()'
    // -----------------------------------------------------------------------------------------

    private PapyrusExpression ParseExpression() => ParseOr();

    private PapyrusExpression ParseOr() => ParseLeftAssociative(ParseAnd, PapyrusTokenKind.Or);

    private PapyrusExpression ParseAnd() => ParseLeftAssociative(ParseComparison, PapyrusTokenKind.And);

    private PapyrusExpression ParseComparison() => ParseLeftAssociative(
        ParseAdditive,
        PapyrusTokenKind.Equal, PapyrusTokenKind.NotEqual, PapyrusTokenKind.Less,
        PapyrusTokenKind.Greater, PapyrusTokenKind.LessEqual, PapyrusTokenKind.GreaterEqual);

    private PapyrusExpression ParseAdditive() => ParseLeftAssociative(
        ParseMultiplicative, PapyrusTokenKind.Plus, PapyrusTokenKind.Minus);

    private PapyrusExpression ParseMultiplicative() => ParseLeftAssociative(
        ParseUnary, PapyrusTokenKind.Star, PapyrusTokenKind.Slash, PapyrusTokenKind.Percent);

    private PapyrusExpression ParseLeftAssociative(
        Func<PapyrusExpression> operand,
        params PapyrusTokenKind[] operators)
    {
        var left = operand();
        while (operators.Contains(Kind))
        {
            var op = Advance();
            var right = operand();
            left = new PapyrusBinaryExpression
            {
                Left = left,
                Operator = op.Kind,
                Right = right,
                Span = left.Span.To(right.Span),
            };
        }
        return left;
    }

    /// <summary><c>&lt;unary expression&gt; ::= ['-' | '!'] &lt;cast atom&gt;</c></summary>
    private PapyrusExpression ParseUnary()
    {
        if (Kind == PapyrusTokenKind.Minus || Kind == PapyrusTokenKind.Not)
        {
            var op = Advance();
            var operand = ParseUnary();
            return new PapyrusUnaryExpression
            {
                Operator = op.Kind,
                Operand = operand,
                Span = op.Span.To(operand.Span),
            };
        }
        return ParseCast();
    }

    /// <summary><c>&lt;cast atom&gt; ::= &lt;dot atom&gt; ['as' &lt;type&gt;]</c>, plus the <c>is</c> check.</summary>
    private PapyrusExpression ParseCast()
    {
        var expression = ParseDotChain();
        while (Kind == PapyrusTokenKind.As || Kind == PapyrusTokenKind.Is)
        {
            var op = Advance();
            var type = ParseType();
            expression = op.Kind == PapyrusTokenKind.As
                ? new PapyrusCastExpression { Operand = expression, Type = type, Span = expression.Span.To(type.Span) }
                : new PapyrusTypeCheckExpression { Operand = expression, Type = type, Span = expression.Span.To(type.Span) };
        }
        return expression;
    }

    /// <summary>
    /// <c>&lt;dot atom&gt; ::= &lt;array atom&gt; ('.' &lt;array func or id&gt;)*</c>
    /// </summary>
    private PapyrusExpression ParseDotChain()
    {
        var expression = ParsePostfix(ParseAtom());

        while (Kind == PapyrusTokenKind.Dot)
        {
            Advance();
            // 'Length' is a keyword, not an identifier, but it is spelled after a dot like any
            // member. Anything else keyword-shaped here is a real syntax error.
            PapyrusToken name;
            if (Kind == PapyrusTokenKind.Identifier || Kind == PapyrusTokenKind.Length)
            {
                name = Advance();
            }
            else
            {
                _diagnostics.Report(
                    PapyrusDiagnosticCodes.ExpectedIdentifier,
                    $"Expected a member name after '.', found '{Describe(Current)}'.",
                    Current.Span);
                name = new PapyrusToken(PapyrusTokenKind.Identifier, string.Empty, Current.Span);
            }

            expression = new PapyrusMemberExpression
            {
                Target = expression,
                Name = name.Text,
                NameSpan = name.Span,
                Span = expression.Span.To(name.Span),
            };
            expression = ParsePostfix(expression);
        }

        return expression;
    }

    /// <summary>Applies any call parentheses and array subscripts sitting after an atom.</summary>
    private PapyrusExpression ParsePostfix(PapyrusExpression expression)
    {
        while (true)
        {
            if (Kind == PapyrusTokenKind.LParen)
            {
                var call = new PapyrusCallExpression { Callee = expression };
                ParseArgumentList(call.Arguments, out var closeSpan);
                call.Span = expression.Span.To(closeSpan);
                expression = call;
                continue;
            }

            if (Kind == PapyrusTokenKind.LBracket)
            {
                Advance();
                var index = ParseExpression();
                var close = Expect(PapyrusTokenKind.RBracket, "']'");
                expression = new PapyrusIndexExpression
                {
                    Target = expression,
                    Index = index,
                    Span = expression.Span.To(close.Span),
                };
                continue;
            }

            return expression;
        }
    }

    /// <summary><c>&lt;parameter&gt; ::= [&lt;identifier&gt; '='] &lt;expression&gt;</c></summary>
    private void ParseArgumentList(List<PapyrusArgument> into, out PapyrusSpan closeSpan)
    {
        var open = Expect(PapyrusTokenKind.LParen, "'('");
        closeSpan = open.Span;

        if (Kind == PapyrusTokenKind.RParen)
        {
            closeSpan = Advance().Span;
            return;
        }

        while (true)
        {
            var before = _index;
            var argument = new PapyrusArgument();

            // A named argument is an identifier followed by a single '='; '==' is a comparison and
            // must not be mistaken for one, which is why the lexer's two-character pass matters here.
            if (Kind == PapyrusTokenKind.Identifier && Peek(1).Kind == PapyrusTokenKind.Assign)
            {
                var name = Advance();
                Advance();
                argument.Name = name.Text;
                argument.NameSpan = name.Span;
            }

            argument.Value = ParseExpression();
            argument.Span = argument.Name == null ? argument.Value.Span : argument.NameSpan.To(argument.Value.Span);
            into.Add(argument);

            if (Match(PapyrusTokenKind.Comma))
            {
                if (_index == before) Advance();
                continue;
            }
            break;
        }

        var close = Expect(PapyrusTokenKind.RParen, "')'");
        closeSpan = close.Span;
    }

    /// <summary>
    /// <c>&lt;atom&gt; ::= ('(' &lt;expression&gt; ')') | ('new' &lt;type&gt; '[' &lt;int&gt; ']') | &lt;func or id&gt;</c>,
    /// widened to include literals and <c>new &lt;struct type&gt;</c>.
    /// </summary>
    private PapyrusExpression ParseAtom()
    {
        var token = Current;
        switch (Kind)
        {
            case PapyrusTokenKind.LParen:
            {
                Advance();
                var inner = ParseExpression();
                Expect(PapyrusTokenKind.RParen, "')'");
                // The parentheses are not kept as a node: they exist only to override precedence,
                // which the tree shape already records.
                return inner;
            }

            case PapyrusTokenKind.New:
            {
                Advance();
                var type = ParseType();
                if (Kind == PapyrusTokenKind.LBracket)
                {
                    Advance();
                    var size = ParseExpression();
                    var close = Expect(PapyrusTokenKind.RBracket, "']'");
                    return new PapyrusNewArrayExpression
                    {
                        ElementType = type,
                        Size = size,
                        Span = token.Span.To(close.Span),
                    };
                }
                return new PapyrusNewStructExpression { Type = type, Span = token.Span.To(type.Span) };
            }

            case PapyrusTokenKind.IntLiteral:
                Advance();
                return new PapyrusLiteralExpression
                { Kind = PapyrusLiteralKind.Int, Text = token.Text, Span = token.Span };

            case PapyrusTokenKind.FloatLiteral:
                Advance();
                return new PapyrusLiteralExpression
                { Kind = PapyrusLiteralKind.Float, Text = token.Text, Span = token.Span };

            case PapyrusTokenKind.StringLiteral:
                Advance();
                return new PapyrusLiteralExpression
                { Kind = PapyrusLiteralKind.String, Text = token.Text, Span = token.Span };

            case PapyrusTokenKind.True:
            case PapyrusTokenKind.False:
                Advance();
                return new PapyrusLiteralExpression
                { Kind = PapyrusLiteralKind.Bool, Text = token.Text.ToLowerInvariant(), Span = token.Span };

            case PapyrusTokenKind.None:
                Advance();
                return new PapyrusLiteralExpression
                { Kind = PapyrusLiteralKind.None, Text = "none", Span = token.Span };

            case PapyrusTokenKind.Identifier:
            {
                // A namespaced script name can appear as the receiver of a global call:
                // MyNamespace:MyScript.MyGlobal(). Read the colons here so the dot chain sees one
                // atom rather than a member access on a namespace part.
                var name = ParseQualifiedName(out var span);
                return new PapyrusIdentifierExpression { Name = name, Span = span };
            }

            // 'Length' after a dot is handled in the dot chain; standing alone it is a bare
            // reference to the array-length pseudo-property, which some scripts do write.
            case PapyrusTokenKind.Length:
                Advance();
                return new PapyrusIdentifierExpression { Name = token.Text, Span = token.Span };

            // These three are type names in a parameter position ("ScriptEventName", etc) but they
            // are also legal identifiers in practice in older scripts. Accept them as names.
            case PapyrusTokenKind.ScriptEventName:
            case PapyrusTokenKind.CustomEventName:
            case PapyrusTokenKind.StructVarName:
                Advance();
                return new PapyrusIdentifierExpression { Name = token.Text, Span = token.Span };

            default:
                _diagnostics.Report(
                    PapyrusDiagnosticCodes.ExpectedExpression,
                    $"Expected an expression, found '{Describe(Current)}'.",
                    Current.Span);
                // Do not consume: the caller's end-of-line recovery decides how far to skip, and
                // eating the token here would hide a block terminator from it.
                return new PapyrusErrorExpression { Span = new PapyrusSpan(token.Span.Start, 0, token.Span.Line, token.Span.Column) };
        }
    }
}
