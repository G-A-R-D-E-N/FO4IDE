using System;
using System.Collections.Generic;
using System.Linq;
using FO4RecordEditor.Services.Papyrus;

namespace FO4RecordEditor.Services.Graph;

public sealed class GraphLiftResult
{
    public GraphDocument? Document { get; init; }

    public IReadOnlyList<GraphDiagnostic> Diagnostics { get; init; } = Array.Empty<GraphDiagnostic>();

    public bool Success =>
        Document != null && !Diagnostics.Any(d => d.Severity == GraphSeverity.Error);
}

public sealed class GraphLifter
{
    private readonly PapyrusScriptIndex _index;

    public GraphLifter(PapyrusScriptIndex index) => _index = index;

    private GraphDocument _doc = null!;
    private PapyrusResolution _resolution = null!;
    private PapyrusScript _script = null!;
    private List<GraphDiagnostic> _problems = null!;
    private int _sequence;
    private double _penX;
    private double _penY;

    public GraphLiftResult Lift(PapyrusScript script)
    {
        if (script == null) throw new ArgumentNullException(nameof(script));

        _script = script;
        _problems = new List<GraphDiagnostic>();
        _sequence = 0;
        _penX = 0;
        _penY = 0;

        if (script.HasErrors)
        {
            foreach (var diagnostic in script.Diagnostics.Where(d => d.Severity == PapyrusSeverity.Error))
                Refuse($"The source did not parse: {diagnostic.Message}");
            return new GraphLiftResult { Diagnostics = _problems };
        }

        _resolution = new PapyrusResolver(_index).Resolve(script);
        _doc = new GraphDocument { Id = "lift" };

        LiftHeader();
        LiftDeclarations();

        foreach (var callable in Callables()) LiftCallable(callable);

        return new GraphLiftResult { Document = _doc, Diagnostics = _problems };
    }

    private void LiftHeader()
    {
        _doc.Header.ScriptName = _script.Name;
        _doc.Header.Extends = _script.Extends;
        _doc.Header.Flags.AddRange(_script.Flags);
        _doc.Header.DocComment = _script.Documentation;

        var auto = _script.States.FirstOrDefault(s => s.IsAuto);
        if (auto != null) _doc.Header.AutoState = auto.Name;
    }

    private void LiftDeclarations()
    {
        foreach (var variable in _script.Variables)
        {
            _doc.Variables.Add(new GraphVariable
            {
                Name = variable.Name,
                Type = Written(variable.Type),
                IsProperty = false,
                Flags = variable.Flags.ToList(),
                Initial = Text(variable.Initializer),
            });
        }

        foreach (var property in _script.Properties)
        {
            if (property.Kind is not (PapyrusPropertyKind.Auto or PapyrusPropertyKind.AutoReadOnly))
            {
                Refuse($"Property '{property.Name}' has explicit Get or Set bodies, which the graph "
                       + "cannot hold yet.", property.NameSpan);
                continue;
            }

            _doc.Variables.Add(new GraphVariable
            {
                Name = property.Name,
                Type = Written(property.Type),
                IsProperty = true,
                Flags = property.Flags.ToList(),
                Initial = Text(property.Initializer),
            });
        }

        foreach (var declared in _script.Structs)
        {
            _doc.Structs.Add(new GraphStruct
            {
                Name = declared.Name,
                Members = declared.Members
                    .Select(m => new GraphVariable { Name = m.Name, Type = Written(m.Type) })
                    .ToList(),
            });
        }

        foreach (var custom in _script.CustomEvents) _doc.CustomEvents.Add(custom.Name);
    }

    private IEnumerable<(PapyrusDeclaration Decl, string? State)> Callables()
    {
        foreach (var function in _script.Functions) yield return (function, null);
        foreach (var declared in _script.Events) yield return (declared, null);

        foreach (var state in _script.States)
        {
            foreach (var function in state.Functions) yield return (function, state.Name);
            foreach (var declared in state.Events) yield return (declared, state.Name);
        }
    }

    private void LiftCallable((PapyrusDeclaration Decl, string? State) callable)
    {
        _penX = 0;
        _penY += 260;

        var entry = EntryNode(callable.Decl, callable.State);
        if (entry == null) return;

        var body = callable.Decl switch
        {
            PapyrusFunctionDecl f => f.Body,
            PapyrusEventDecl e => e.Body,
            _ => null,
        };
        if (body == null) return;

        var open = new List<PinRef> { new(entry, PinIds.Exec) };
        LiftBlock(body, open);
    }

    private string? EntryNode(PapyrusDeclaration declaration, string? state)
    {
        switch (declaration)
        {
            case PapyrusFunctionDecl function:
            {
                var node = Node(BuiltinNodeDefinitions.FunctionEntry, GraphNodeKind.FunctionEntry);
                Config(node, "name", function.Name);
                if (function.ReturnType != null && !IsNone(function.ReturnType))
                    Config(node, "returns", Written(function.ReturnType));
                if (function.HasFlag("global")) Config(node, "global", "true");
                if (state != null) Config(node, "state", state);
                return node;
            }

            case PapyrusEventDecl declared:
            {
                var owner = declared.RemoteObjectType ?? DeclaringScriptOf(declared.Name);
                if (owner == null)
                {
                    Refuse($"Could not work out which script declares event '{declared.Name}'.",
                        declared.NameSpan);
                    return null;
                }

                var id = declared.RemoteObjectType != null
                    ? NodePalette.RemoteEventId(owner, declared.Name)
                    : NodePalette.EventId(owner, declared.Name);

                var node = Node(id, GraphNodeKind.EventEntry);
                if (state != null) Config(node, "state", state);
                return node;
            }

            default:
                Refuse($"'{declaration.Name}' is a kind of declaration the graph has no entry for.",
                    declaration.NameSpan);
                return null;
        }
    }

    private string? DeclaringScriptOf(string eventName)
    {
        var current = _script;
        var guard = 0;

        while (current != null && guard++ < 64)
        {
            if (current.Events.Any(e => e.RemoteObjectType == null
                    && string.Equals(e.Name, eventName, StringComparison.OrdinalIgnoreCase))
                && !ReferenceEquals(current, _script))
            {
                return current.Name;
            }

            if (string.IsNullOrWhiteSpace(current.Extends)) break;
            current = _index.Resolve(current.Extends);
        }

        return _script.Extends ?? _script.Name;
    }

    private List<PinRef> LiftBlock(IEnumerable<PapyrusStatement> body, List<PinRef> open)
    {
        foreach (var statement in body) open = LiftStatement(statement, open);
        return open;
    }

    private List<PinRef> LiftStatement(PapyrusStatement statement, List<PinRef> open)
    {
        switch (statement)
        {
            case PapyrusDefineStatement define:
            {
                var value = define.Initializer == null
                    ? null
                    : LiftExpression(define.Initializer, ref open);

                var node = Node(BuiltinNodeDefinitions.LocalDeclare, GraphNodeKind.LocalDeclare);
                Config(node, "name", define.Name);
                Config(node, "type", Written(define.Type));
                Enter(open, node);
                if (value != null) Attach(value, new PinRef(node, PinIds.Value));
                return new List<PinRef> { new(node, PinIds.Then) };
            }

            case PapyrusAssignStatement assign:
                return LiftAssign(assign, open);

            case PapyrusExpressionStatement expression:
            {
                var before = open.Count;
                LiftExpression(expression.Expression, ref open);
                if (open.Count == before && ReferenceEquals(open, open))
                {

                }
                return open;
            }

            case PapyrusReturnStatement returned:
            {
                var value = returned.Value == null ? null : LiftExpression(returned.Value, ref open);
                var node = Node(BuiltinNodeDefinitions.Return, GraphNodeKind.Return);
                Enter(open, node);
                if (value != null) Attach(value, new PinRef(node, PinIds.Value));
                return new List<PinRef>();
            }

            case PapyrusIfStatement branch:
                return LiftIf(branch, open, 0);

            case PapyrusWhileStatement loop:
                return LiftWhile(loop, open);

            default:
                Refuse($"'{statement.GetType().Name}' is a statement the graph has no node for.",
                    statement.Span);
                return open;
        }
    }

    private List<PinRef> LiftAssign(PapyrusAssignStatement assign, List<PinRef> open)
    {

        var compound = CompoundId(assign.Operator);
        if (compound != null && assign.Target is not PapyrusIdentifierExpression)
        {
            Refuse("A compound assignment to something other than a plain variable cannot be "
                   + "expanded safely, because the target would be evaluated twice.", assign.Span);
            return open;
        }

        if (compound == null && assign.Operator != PapyrusTokenKind.Assign)
        {
            Refuse($"Assignment operator '{assign.Operator}' has no graph node.", assign.Span);
            return open;
        }

        var value = LiftExpression(assign.Value, ref open);

        if (compound != null && value != null && assign.Target is PapyrusIdentifierExpression current)
        {
            var read = Node(BuiltinNodeDefinitions.VariableGet, GraphNodeKind.VariableGet);
            Config(read, "name", current.Name);
            Config(read, "type", TypeName(current));

            var combined = Node(
                BuiltinNodeDefinitions.OperatorPrefix + compound, GraphNodeKind.Binary);
            Wire(new PinRef(read, PinIds.Value), new PinRef(combined, PinIds.Left));
            Attach(value, new PinRef(combined, PinIds.Right));

            value = new Value(new PinRef(combined, PinIds.Return), null);
        }

        switch (assign.Target)
        {
            case PapyrusIdentifierExpression name:
            {
                var node = Node(BuiltinNodeDefinitions.VariableSet, GraphNodeKind.VariableSet);
                Config(node, "name", name.Name);
                Config(node, "type", TypeName(name));
                Enter(open, node);
                if (value != null) Attach(value, new PinRef(node, PinIds.Value));
                return new List<PinRef> { new(node, PinIds.Then) };
            }

            case PapyrusIndexExpression index:
            {
                var target = LiftExpression(index.Target, ref open);
                var at = LiftExpression(index.Index, ref open);

                var node = Node(BuiltinNodeDefinitions.IndexSet, GraphNodeKind.IndexSet);
                Enter(open, node);
                if (target != null) Attach(target, new PinRef(node, PinIds.Array));
                if (at != null) Attach(at, new PinRef(node, PinIds.Index));
                if (value != null) Attach(value, new PinRef(node, PinIds.Value));
                return new List<PinRef> { new(node, PinIds.Then) };
            }

            case PapyrusMemberExpression member:
            {
                var binding = _resolution.BindingFor(member);
                if (binding?.Kind != PapyrusBindingKind.Property || binding.Owner == null)
                {
                    Refuse($"Assigning to '{member.Name}' is not something the graph can express yet.",
                        member.Span);
                    return open;
                }

                var target = LiftExpression(member.Target, ref open);
                var node = Node(
                    NodePalette.PropertySetId(binding.Owner.Name, member.Name),
                    GraphNodeKind.PropertySet);
                Enter(open, node);
                if (target != null) Attach(target, new PinRef(node, PinIds.Self));
                if (value != null) Attach(value, new PinRef(node, PinIds.Value));
                return new List<PinRef> { new(node, PinIds.Then) };
            }

            default:
                Refuse("This assignment target has no graph node.", assign.Target.Span);
                return open;
        }
    }

    private List<PinRef> LiftIf(PapyrusIfStatement statement, List<PinRef> open, int arm)
    {
        var branchArm = statement.Branches[arm];
        var condition = LiftExpression(branchArm.Condition, ref open);

        var node = Node(BuiltinNodeDefinitions.Branch, GraphNodeKind.Branch);
        Enter(open, node);
        if (condition != null) Attach(condition, new PinRef(node, PinIds.Condition));

        var thenOpen = LiftBlock(branchArm.Body, new List<PinRef> { new(node, PinIds.Then) });

        List<PinRef> elseOpen;
        if (arm + 1 < statement.Branches.Count)
        {
            elseOpen = LiftIf(statement, new List<PinRef> { new(node, PinIds.Else) }, arm + 1);
        }
        else if (statement.ElseBody != null)
        {
            elseOpen = LiftBlock(statement.ElseBody, new List<PinRef> { new(node, PinIds.Else) });
        }
        else
        {
            elseOpen = new List<PinRef> { new(node, PinIds.Else) };
        }

        return thenOpen.Concat(elseOpen).ToList();
    }

    private List<PinRef> LiftWhile(PapyrusWhileStatement loop, List<PinRef> open)
    {

        if (ContainsCall(loop.Condition))
        {
            Refuse("This loop's condition calls a function. The graph would evaluate it once "
                   + "before the loop rather than on each pass, so it cannot be lifted.",
                loop.Condition.Span);
            return open;
        }

        var condition = LiftExpression(loop.Condition, ref open);

        var node = Node(BuiltinNodeDefinitions.While, GraphNodeKind.While);
        Enter(open, node);
        if (condition != null) Attach(condition, new PinRef(node, PinIds.Condition));

        var bodyOpen = LiftBlock(loop.Body, new List<PinRef> { new(node, PinIds.Body) });
        foreach (var end in bodyOpen) Wire(end, new PinRef(node, PinIds.Exec));

        return new List<PinRef> { new(node, PinIds.Completed) };
    }

    private static bool ContainsCall(PapyrusNode node) =>
        node is PapyrusCallExpression || node.Children.Any(ContainsCall);

    private sealed record Value(PinRef? Pin, GraphPinValue? Literal);

    private Value? LiftExpression(PapyrusExpression expression, ref List<PinRef> open)
    {
        switch (expression)
        {
            case PapyrusLiteralExpression literal:
            {
                if (literal.Kind == PapyrusLiteralKind.None)
                {
                    var none = Node(BuiltinNodeDefinitions.NoneValue, GraphNodeKind.NoneValue);
                    return new Value(new PinRef(none, PinIds.Value), null);
                }

                return new Value(null, new GraphPinValue
                {
                    Type = LiteralType(literal),
                    Value = LiteralText(literal),
                });
            }

            case PapyrusIdentifierExpression name:
                return LiftIdentifier(name);

            case PapyrusMemberExpression member:
                return LiftMember(member, ref open);

            case PapyrusIndexExpression index:
            {
                var target = LiftExpression(index.Target, ref open);
                var at = LiftExpression(index.Index, ref open);
                var node = Node(BuiltinNodeDefinitions.IndexGet, GraphNodeKind.IndexGet);
                if (target != null) Attach(target, new PinRef(node, PinIds.Array));
                if (at != null) Attach(at, new PinRef(node, PinIds.Index));
                return new Value(new PinRef(node, PinIds.Return), null);
            }

            case PapyrusCallExpression call:
                return LiftCall(call, ref open);

            case PapyrusUnaryExpression unary:
            {
                var id = unary.Operator switch
                {
                    PapyrusTokenKind.Not => "not",
                    PapyrusTokenKind.Minus => "neg",
                    _ => null,
                };
                if (id == null)
                {
                    Refuse($"Unary operator '{unary.Operator}' has no graph node.", unary.Span);
                    return null;
                }

                var operand = LiftExpression(unary.Operand, ref open);
                var node = Node(BuiltinNodeDefinitions.OperatorPrefix + id, GraphNodeKind.Unary);
                if (operand != null) Attach(operand, new PinRef(node, PinIds.Value));
                return new Value(new PinRef(node, PinIds.Return), null);
            }

            case PapyrusBinaryExpression binary:
            {
                var id = BinaryId(binary.Operator);
                if (id == null)
                {
                    Refuse($"Binary operator '{binary.Operator}' has no graph node.", binary.Span);
                    return null;
                }

                var left = LiftExpression(binary.Left, ref open);
                var right = LiftExpression(binary.Right, ref open);
                var node = Node(BuiltinNodeDefinitions.OperatorPrefix + id, GraphNodeKind.Binary);
                if (left != null) Attach(left, new PinRef(node, PinIds.Left));
                if (right != null) Attach(right, new PinRef(node, PinIds.Right));
                return new Value(new PinRef(node, PinIds.Return), null);
            }

            case PapyrusCastExpression cast:
            {
                var operand = LiftExpression(cast.Operand, ref open);
                var node = Node(BuiltinNodeDefinitions.Cast, GraphNodeKind.Cast);
                Config(node, "type", Written(cast.Type));
                if (operand != null) Attach(operand, new PinRef(node, PinIds.Value));
                return new Value(new PinRef(node, PinIds.Return), null);
            }

            case PapyrusTypeCheckExpression check:
            {
                var operand = LiftExpression(check.Operand, ref open);
                var node = Node(BuiltinNodeDefinitions.TypeCheck, GraphNodeKind.TypeCheck);
                Config(node, "type", Written(check.Type));
                if (operand != null) Attach(operand, new PinRef(node, PinIds.Value));
                return new Value(new PinRef(node, PinIds.Return), null);
            }

            case PapyrusNewArrayExpression created:
            {
                var size = LiftExpression(created.Size, ref open);
                var node = Node(BuiltinNodeDefinitions.NewArray, GraphNodeKind.NewArray);
                Config(node, "type", Written(created.ElementType));
                if (size != null) Attach(size, new PinRef(node, PinIds.Value));
                return new Value(new PinRef(node, PinIds.Return), null);
            }

            default:
                Refuse($"'{expression.GetType().Name}' is an expression the graph has no node for.",
                    expression.Span);
                return null;
        }
    }

    private Value? LiftIdentifier(PapyrusIdentifierExpression name)
    {
        var binding = _resolution.BindingFor(name);

        switch (binding?.Kind)
        {
            case PapyrusBindingKind.SelfKeyword:
            {
                var node = Node(BuiltinNodeDefinitions.Self, GraphNodeKind.Self);
                return new Value(new PinRef(node, PinIds.Value), null);
            }

            case PapyrusBindingKind.ParentKeyword:
            {
                var node = Node(BuiltinNodeDefinitions.Parent, GraphNodeKind.Parent);
                return new Value(new PinRef(node, PinIds.Value), null);
            }

            case PapyrusBindingKind.Local:
            case PapyrusBindingKind.Parameter:
            case PapyrusBindingKind.ScriptVariable:
            case PapyrusBindingKind.Property:
            {
                var node = Node(BuiltinNodeDefinitions.VariableGet, GraphNodeKind.VariableGet);
                Config(node, "name", name.Name);
                Config(node, "type", TypeName(name));
                return new Value(new PinRef(node, PinIds.Value), null);
            }

            default:
                Refuse($"'{name.Name}' could not be resolved to something the graph can read.",
                    name.Span);
                return null;
        }
    }

    private Value? LiftMember(PapyrusMemberExpression member, ref List<PinRef> open)
    {
        var binding = _resolution.BindingFor(member);

        if (binding?.Kind == PapyrusBindingKind.ArrayMember
            && string.Equals(member.Name, "Length", StringComparison.OrdinalIgnoreCase))
        {
            var target = LiftExpression(member.Target, ref open);
            var node = Node(BuiltinNodeDefinitions.ArrayPrefix + "length", GraphNodeKind.ArrayOp);
            if (target != null) Attach(target, new PinRef(node, PinIds.Array));
            return new Value(new PinRef(node, PinIds.Return), null);
        }

        if (binding?.Kind == PapyrusBindingKind.Property && binding.Owner != null)
        {
            var target = LiftExpression(member.Target, ref open);
            var node = Node(
                NodePalette.PropertyGetId(binding.Owner.Name, member.Name),
                GraphNodeKind.PropertyGet);
            if (target != null) Attach(target, new PinRef(node, PinIds.Self));

            return new Value(new PinRef(node, PinIds.Value), null);
        }

        Refuse($"Reading '{member.Name}' is not something the graph can express yet.", member.Span);
        return null;
    }

    private Value? LiftCall(PapyrusCallExpression call, ref List<PinRef> open)
    {
        var binding = _resolution.BindingFor(call.Callee) ?? _resolution.BindingFor(call);
        if (binding?.Owner == null
            || binding.Kind is not (PapyrusBindingKind.Function or PapyrusBindingKind.Event))
        {
            Refuse($"Could not work out what '{call.FunctionName}' calls.", call.Span);
            return null;
        }

        var declared = binding.Declaration as PapyrusFunctionDecl;
        var isGlobal = declared?.HasFlag("global") == true;

        var id = NodePalette.CallId(binding.Owner.Name, binding.Name, isGlobal);
        var node = Node(id, isGlobal ? GraphNodeKind.Call : GraphNodeKind.Call);

        if (!isGlobal && call.Callee is PapyrusMemberExpression receiver)
        {
            var target = LiftExpression(receiver.Target, ref open);
            if (target != null) Attach(target, new PinRef(node, PinIds.Self));
        }

        var parameters = declared?.Parameters ?? new List<PapyrusParameter>();
        for (int i = 0; i < call.Arguments.Count; i++)
        {
            var argument = call.Arguments[i];
            var parameterName = argument.Name
                ?? (i < parameters.Count ? parameters[i].Name : null);

            if (parameterName == null)
            {
                Refuse($"'{binding.Name}' was given more arguments than it declares.", argument.Span);
                continue;
            }

            var value = LiftExpression(argument.Value, ref open);
            if (value != null) Attach(value, new PinRef(node, PinIds.Argument(parameterName)));
        }

        Enter(open, node);
        open = new List<PinRef> { new(node, PinIds.Then) };

        return new Value(new PinRef(node, PinIds.Return), null);
    }

    private string Node(string definitionId, GraphNodeKind kind)
    {
        var id = "n" + (++_sequence);
        _doc.Nodes.Add(new GraphNode
        {
            Id = id,
            Definition = definitionId,
            Kind = kind,
            X = _penX,
            Y = _penY,
        });
        _penX += 40;
        _doc.Invalidate();
        return id;
    }

    private void Config(string nodeId, string key, string? value)
    {
        if (value == null) return;
        _doc.Node(nodeId)!.Config[key] = value;
    }

    private void Enter(List<PinRef> open, string nodeId)
    {
        foreach (var end in open) Wire(end, new PinRef(nodeId, PinIds.Exec));
        open.Clear();
    }

    private void Wire(PinRef from, PinRef to)
    {
        _doc.Wires.Add(new GraphWire { Id = "w" + (++_sequence), From = from, To = to });
        _doc.Invalidate();
    }

    private void Attach(Value value, PinRef target)
    {
        if (value.Pin != null) { Wire(value.Pin.Value, target); return; }
        if (value.Literal != null) _doc.Node(target.Node)!.PinValues[target.Pin] = value.Literal;
    }

    private void Refuse(string message, PapyrusSpan span = default) =>
        _problems.Add(new GraphDiagnostic
        {
            Code = GraphDiagnosticCodes.UnsupportedSchema,
            Severity = GraphSeverity.Error,
            Message = span.Line > 0 ? $"Line {span.Line}: {message}" : message,
        });

    private string TypeName(PapyrusNode node)
    {
        var type = _resolution.TypeOf(node);
        return type.ToString();
    }

    private static string Written(PapyrusTypeRef? type) => type?.ToString() ?? "var";

    private static bool IsNone(PapyrusTypeRef type) =>
        string.Equals(type.ToString(), "None", StringComparison.OrdinalIgnoreCase);

    private static string? Text(PapyrusExpression? expression) =>
        expression is PapyrusLiteralExpression literal ? literal.Text : null;

    private static string LiteralText(PapyrusLiteralExpression literal) =>
        literal.Kind == PapyrusLiteralKind.String
            ? "\"" + literal.Text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\""
            : literal.Text;

    private static string LiteralType(PapyrusLiteralExpression literal) => literal.Kind switch
    {
        PapyrusLiteralKind.Int => "int",
        PapyrusLiteralKind.Float => "float",
        PapyrusLiteralKind.String => "string",
        PapyrusLiteralKind.Bool => "bool",
        _ => "int",
    };

    private static string? CompoundId(PapyrusTokenKind op) => op switch
    {
        PapyrusTokenKind.PlusAssign => "add",
        PapyrusTokenKind.MinusAssign => "sub",
        PapyrusTokenKind.StarAssign => "mul",
        PapyrusTokenKind.SlashAssign => "div",
        PapyrusTokenKind.PercentAssign => "mod",
        _ => null,
    };

    private static string? BinaryId(PapyrusTokenKind op) => op switch
    {
        PapyrusTokenKind.Plus => "add",
        PapyrusTokenKind.Minus => "sub",
        PapyrusTokenKind.Star => "mul",
        PapyrusTokenKind.Slash => "div",
        PapyrusTokenKind.Percent => "mod",
        PapyrusTokenKind.Equal => "eq",
        PapyrusTokenKind.NotEqual => "ne",
        PapyrusTokenKind.Less => "lt",
        PapyrusTokenKind.LessEqual => "le",
        PapyrusTokenKind.Greater => "gt",
        PapyrusTokenKind.GreaterEqual => "ge",
        PapyrusTokenKind.And => "and",
        PapyrusTokenKind.Or => "or",
        _ => null,
    };
}
