using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FO4RecordEditor.Services.Papyrus;

























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


    public static PapyrusScript ParseFile(string path) => Parse(File.ReadAllText(path), path);





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









    private string? TakeDocComment()
    {
        var save = _index;
        SkipNewlines();
        if (Kind == PapyrusTokenKind.DocComment) return Advance().Text;
        _index = save;
        return null;
    }









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


            if (_index == before) Advance();
        }

        var start = script.NameSpan.Start;
        var last = _tokens[_tokens.Count - 1].Span;
        script.Span = new PapyrusSpan(0, Math.Max(last.End, start), 1, 1);
        return script;
    }




    private void ParseHeader(PapyrusScript script)
    {
        SkipNewlines();


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


    private void ParseImport(PapyrusScript script)
    {
        var keyword = Advance();
        var name = ParseQualifiedName(out var nameSpan);
        var import = new PapyrusImport(name, nameSpan) { Span = keyword.Span.To(nameSpan) };
        script.Imports.Add(import);
        ExpectEndOfLine();
    }


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













    private static bool IsTypeStart(PapyrusTokenKind kind) => kind switch
    {
        PapyrusTokenKind.Int or PapyrusTokenKind.Float or PapyrusTokenKind.Bool
            or PapyrusTokenKind.String or PapyrusTokenKind.Var or PapyrusTokenKind.Identifier
            or PapyrusTokenKind.ScriptEventName or PapyrusTokenKind.CustomEventName
            or PapyrusTokenKind.StructVarName => true,
        _ => false,
    };


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

        if (allowDocumentation) decl.Documentation = TakeDocComment();
        return decl;
    }




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








    private PapyrusPropertyDecl ParseProperty(PapyrusTypeRef type, string? groupName)
    {
        var keyword = Advance();
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









    private PapyrusFunctionDecl ParseFunction(PapyrusTypeRef? returnType, string? stateName)
    {
        var keyword = Advance();
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
        var keyword = Advance();
        var evt = new PapyrusEventDecl { StateName = stateName };

        var first = ParseQualifiedName(out var firstSpan);
        if (Kind == PapyrusTokenKind.Dot)
        {

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




    private PapyrusExpression ParseDotChain()
    {
        var expression = ParsePostfix(ParseAtom());

        while (Kind == PapyrusTokenKind.Dot)
        {
            Advance();


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



                var name = ParseQualifiedName(out var span);
                return new PapyrusIdentifierExpression { Name = name, Span = span };
            }



            case PapyrusTokenKind.Length:
                Advance();
                return new PapyrusIdentifierExpression { Name = token.Text, Span = token.Span };



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


                return new PapyrusErrorExpression { Span = new PapyrusSpan(token.Span.Start, 0, token.Span.Line, token.Span.Column) };
        }
    }
}
