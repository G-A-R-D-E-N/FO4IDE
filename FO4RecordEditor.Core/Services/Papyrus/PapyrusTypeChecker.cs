using System;
using System.Collections.Generic;
using System.Linq;

namespace FO4RecordEditor.Services.Papyrus;























public sealed class PapyrusTypeChecker
{
    private readonly PapyrusScriptIndex _index;
    private readonly Dictionary<(string, string), bool> _inheritsCache = new();

    public PapyrusTypeChecker(PapyrusScriptIndex index) =>
        _index = index ?? throw new ArgumentNullException(nameof(index));

    public IReadOnlyList<PapyrusDiagnostic> Check(PapyrusResolution resolution)
    {
        if (resolution == null) throw new ArgumentNullException(nameof(resolution));


        if (!resolution.BaseChainComplete) return Array.Empty<PapyrusDiagnostic>();

        var state = new State(resolution, resolution.Script.FilePath);
        var script = resolution.Script;

        foreach (var v in script.Variables) CheckInitializer(state, v.Type, v.Initializer);
        foreach (var p in script.Properties)
        {
            CheckInitializer(state, p.Type, p.Initializer);
            if (p.Getter != null) CheckCallable(state, p.Getter);
            if (p.Setter != null) CheckCallable(state, p.Setter);
        }
        foreach (var s in script.Structs)
        {
            foreach (var m in s.Members) CheckInitializer(state, m.Type, m.Initializer);
        }

        foreach (var fn in script.Functions) { CheckOverride(state, fn); CheckCallable(state, fn); }
        foreach (var ev in script.Events) CheckCallable(state, ev);
        foreach (var st in script.States)
        {
            foreach (var fn in st.Functions) CheckCallable(state, fn);
            foreach (var ev in st.Events) CheckCallable(state, ev);
        }

        return state.Diagnostics;
    }

    private sealed class State
    {
        public State(PapyrusResolution resolution, string? file)
        {
            Resolution = resolution;
            File = file;
        }

        public PapyrusResolution Resolution { get; }
        public string? File { get; }
        public List<PapyrusDiagnostic> Diagnostics { get; } = new();


        public PapyrusType? ReturnType;

        public void Report(string code, string message, PapyrusSpan span) =>
            Diagnostics.Add(new PapyrusDiagnostic(code, PapyrusSeverity.Error, message, span, File));
    }



    private void CheckCallable(State state, PapyrusCallableDecl callable)
    {
        state.ReturnType = callable is PapyrusFunctionDecl { ReturnType: not null } fn
            ? state.Resolution.TypeOf(fn.ReturnType)
            : null;


        var seenDefault = false;
        foreach (var p in callable.Parameters)
        {
            if (p.DefaultValue != null) seenDefault = true;
            else if (seenDefault)
            {
                state.Report(
                    PapyrusDiagnosticCodes.ParameterOrder,
                    $"'{p.Name}' has no default value, but an earlier parameter does. " +
                    "Every parameter after one with a default must have one too.",
                    p.Span);
            }
            CheckInitializer(state, p.Type, p.DefaultValue);
        }

        CheckStatements(state, callable.Body);
        state.ReturnType = null;
    }





    private void CheckOverride(State state, PapyrusFunctionDecl fn)
    {
        var script = state.Resolution.Script;
        if (string.IsNullOrEmpty(script.Extends)) return;

        var chain = _index.BaseChain(script);
        for (int i = 1; i < chain.Count; i++)
        {
            var parentDecl = PapyrusScriptIndex.FindMemberOn(chain[i], fn.Name);
            if (parentDecl is not PapyrusFunctionDecl parent) continue;

            if (parent.Parameters.Count != fn.Parameters.Count)
            {
                state.Report(
                    PapyrusDiagnosticCodes.OverrideMismatch,
                    $"'{fn.Name}' overrides {chain[i].Name}.{parent.Name}, which takes " +
                    $"{parent.Parameters.Count} parameter(s), not {fn.Parameters.Count}.",
                    fn.NameSpan);
            }
            return;
        }
    }



    private void CheckStatements(State state, IEnumerable<PapyrusStatement> body)
    {
        foreach (var stmt in body) CheckStatement(state, stmt);
    }

    private void CheckStatement(State state, PapyrusStatement stmt)
    {
        switch (stmt)
        {
            case PapyrusDefineStatement def:
                CheckExpression(state, def.Initializer);
                if (def.Initializer != null)
                {
                    CheckAssignable(
                        state, state.Resolution.TypeOf(def.Type),
                        state.Resolution.TypeOf(def.Initializer), def.Initializer.Span, $"initialise '{def.Name}'");
                }
                break;

            case PapyrusAssignStatement assign:
            {
                CheckExpression(state, assign.Target);
                CheckExpression(state, assign.Value);




                if (assign.Operator == PapyrusTokenKind.Assign)
                {
                    CheckAssignable(
                        state, state.Resolution.TypeOf(assign.Target),
                        state.Resolution.TypeOf(assign.Value), assign.Value.Span, "assign");
                }
                break;
            }

            case PapyrusExpressionStatement expr:
                CheckExpression(state, expr.Expression);
                break;

            case PapyrusReturnStatement ret:
            {
                CheckExpression(state, ret.Value);
                if (ret.Value == null) break;
                if (state.ReturnType == null)
                {
                    state.Report(
                        PapyrusDiagnosticCodes.TypeMismatch,
                        "This function returns nothing, so it cannot return a value.",
                        ret.Span);
                    break;
                }
                CheckAssignable(
                    state, state.ReturnType, state.Resolution.TypeOf(ret.Value), ret.Value.Span, "return");
                break;
            }

            case PapyrusIfStatement iff:
                foreach (var branch in iff.Branches)
                {
                    CheckExpression(state, branch.Condition);
                    CheckStatements(state, branch.Body);
                }
                if (iff.ElseBody != null) CheckStatements(state, iff.ElseBody);
                break;

            case PapyrusWhileStatement wh:
                CheckExpression(state, wh.Condition);
                CheckStatements(state, wh.Body);
                break;
        }
    }

    private void CheckInitializer(State state, PapyrusTypeRef declared, PapyrusExpression? initializer)
    {
        if (initializer == null) return;
        CheckExpression(state, initializer);
        CheckAssignable(
            state, state.Resolution.TypeOf(declared), state.Resolution.TypeOf(initializer),
            initializer.Span, "initialise");
    }



    private void CheckExpression(State state, PapyrusExpression? expr)
    {
        switch (expr)
        {
            case null:
                return;

            case PapyrusCallExpression call:
                CheckExpression(state, call.Callee);
                foreach (var arg in call.Arguments) CheckExpression(state, arg.Value);
                CheckCall(state, call);
                return;

            case PapyrusCastExpression cast:
            {
                CheckExpression(state, cast.Operand);
                var from = state.Resolution.TypeOf(cast.Operand);
                var to = state.Resolution.TypeOf(cast.Type);
                if (!PapyrusConversions.IsExplicit(from, to, Inherits))
                {
                    state.Report(
                        PapyrusDiagnosticCodes.InvalidCast,
                        $"'{from}' cannot be cast to '{to}'.", cast.Span);
                }
                return;
            }

            case PapyrusIndexExpression index:
                CheckExpression(state, index.Target);
                CheckExpression(state, index.Index);
                return;

            default:
                foreach (var child in expr.Children)
                {
                    if (child is PapyrusExpression sub) CheckExpression(state, sub);
                }
                return;
        }
    }


    private void CheckCall(State state, PapyrusCallExpression call)
    {
        var binding = state.Resolution.BindingFor(call);




        if (binding?.Declaration is not PapyrusCallableDecl declaration) return;

        var parameters = declaration.Parameters;
        var required = parameters.Count(p => p.DefaultValue == null);
        var positional = call.Arguments.Count(a => a.Name == null);

        if (call.Arguments.Count > parameters.Count)
        {
            state.Report(
                PapyrusDiagnosticCodes.ArgumentCount,
                $"'{declaration.Name}' takes at most {parameters.Count} argument(s), " +
                $"but {call.Arguments.Count} were given.",
                call.Span);
            return;
        }



        if (positional < required && call.Arguments.Count < required)
        {
            state.Report(
                PapyrusDiagnosticCodes.ArgumentCount,
                $"'{declaration.Name}' needs {required} argument(s), but {call.Arguments.Count} were given.",
                call.Span);
            return;
        }

        for (int i = 0; i < call.Arguments.Count; i++)
        {
            var arg = call.Arguments[i];
            PapyrusParameter? parameter;

            if (arg.Name == null)
            {
                if (i >= parameters.Count) continue;
                parameter = parameters[i];
            }
            else
            {
                parameter = parameters.FirstOrDefault(
                    p => string.Equals(p.Name, arg.Name, StringComparison.OrdinalIgnoreCase));
                if (parameter == null)
                {
                    state.Report(
                        PapyrusDiagnosticCodes.UnknownArgumentName,
                        $"'{declaration.Name}' has no parameter named '{arg.Name}'.",
                        arg.NameSpan);
                    continue;
                }
            }



            var expected = ParameterType(parameter, binding.Owner);
            CheckAssignable(
                state, expected, state.Resolution.TypeOf(arg.Value), arg.Value.Span,
                $"pass to '{parameter.Name}'");
        }
    }


    private PapyrusType ParameterType(PapyrusParameter parameter, PapyrusScript? owner)
    {
        var name = parameter.Type.Name;
        var primitive = PapyrusType.Primitive(name);
        PapyrusType? resolved = primitive;

        if (resolved == null && owner != null)
        {
            foreach (var level in _index.BaseChain(owner))
            {
                if (level.Structs.Any(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)))
                {
                    resolved = PapyrusType.StructOf(level.Name, name);
                    break;
                }
            }
        }

        if (resolved == null)
        {
            var script = _index.Resolve(name);

            resolved = script == null ? PapyrusType.Error : PapyrusType.Object(script.Name);
        }

        return parameter.Type.IsArray ? PapyrusType.ArrayOf(resolved) : resolved;
    }



    private void CheckAssignable(State state, PapyrusType target, PapyrusType value, PapyrusSpan span, string what)
    {
        if (target.Kind == PapyrusTypeKind.Error || value.Kind == PapyrusTypeKind.Error) return;
        if (PapyrusConversions.IsImplicit(value, target, Inherits)) return;

        state.Report(
            PapyrusDiagnosticCodes.TypeMismatch,
            $"Cannot {what}: '{value}' is not compatible with '{target}'.",
            span);
    }


    private bool Inherits(string child, string ancestor)
    {
        if (string.Equals(child, ancestor, StringComparison.OrdinalIgnoreCase)) return true;

        var key = (child.ToLowerInvariant(), ancestor.ToLowerInvariant());
        if (_inheritsCache.TryGetValue(key, out var cached)) return cached;

        var script = _index.Resolve(child);
        var result = script != null
            && _index.BaseChain(script)
                .Any(s => string.Equals(s.Name, ancestor, StringComparison.OrdinalIgnoreCase));

        _inheritsCache[key] = result;
        return result;
    }
}
