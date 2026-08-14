using System;
using System.Collections.Generic;
using System.Linq;
using FO4RecordEditor.Services.Papyrus;

namespace FO4RecordEditor.Services.Graph;











public sealed class GraphLinearizer
{
    private readonly GraphDocument _document;
    private readonly GraphValidation _validation;
    private readonly GraphTypeResolver _types;
    private readonly PapyrusScript? _owner;
    private readonly List<GraphDiagnostic> _problems = new();

    public GraphLinearizer(
        GraphDocument document,
        GraphValidation validation,
        GraphTypeResolver types,
        PapyrusScript? owner)
    {
        _document = document;
        _validation = validation;
        _types = types;
        _owner = owner;
    }

    public IReadOnlyList<GraphDiagnostic> Diagnostics => _problems;


    private sealed class Frame
    {
        public required GraphNameTable Names { get; init; }
        public required GraphExecFlow Flow { get; init; }
        public Dictionary<string, string> ResultLocals { get; } = new(StringComparer.Ordinal);
        public List<IrLocal> Locals { get; } = new();
        public HashSet<string> Emitted { get; } = new(StringComparer.Ordinal);


        public HashSet<string> DeclaredLocals { get; } = new(StringComparer.OrdinalIgnoreCase);
        public string? ReturnTypeName { get; init; }
        public bool ReturnIsArray { get; init; }


        public List<LoopFrame> Loops { get; } = new();
    }








    private sealed class LoopFrame
    {
        public required string NodeId { get; init; }

        public string? Sentinel { get; set; }
    }


    public IrScript? Lower(PapyrusScriptIndex index)
    {
        var callables = new List<IrCallable>();
        var flows = new List<GraphExecFlow>();

        foreach (var node in _document.Nodes)
        {
            if (!_validation.Definitions.TryGetValue(node.Id, out var definition)) continue;
            if (definition.Kind is not (GraphNodeKind.EventEntry or GraphNodeKind.FunctionEntry)) continue;

            var callable = LowerCallable(node, definition, index, flows);
            if (callable != null) callables.Add(callable);
        }



        _problems.AddRange(GraphExecFlow.CheckReachability(_document, _validation.Definitions, flows));

        if (_problems.Any(d => d.Severity == GraphSeverity.Error)) return null;

        return new IrScript
        {
            Name = _document.Header.ScriptName,
            Extends = _document.Header.Extends,
            Flags = _document.Header.Flags.ToList(),
            Imports = _document.Header.Imports.ToList(),
            DocComment = _document.Header.DocComment,
            Structs = _document.Structs
                .Select(s => new IrStruct(
                    s.Name,
                    s.Members.Select(m => new IrStructMember(m.Name, BaseType(m.Type), IsArray(m.Type))).ToList()))
                .ToList(),
            Variables = _document.Variables
                .Where(v => !v.IsProperty)
                .Select(v => new IrVariable(v.Name, BaseType(v.Type), IsArray(v.Type), v.Flags.ToList(), v.Initial))
                .ToList(),
            Properties = _document.Variables
                .Where(v => v.IsProperty)
                .Select(v => new IrProperty(v.Name, BaseType(v.Type), IsArray(v.Type), v.Flags.ToList(), v.Initial))
                .ToList(),
            Callables = callables,
            AutoState = string.IsNullOrWhiteSpace(_document.Header.AutoState)
                ? null
                : _document.Header.AutoState,
            CustomEvents = _document.CustomEvents.ToList(),
        };
    }

    private IrCallable? LowerCallable(
        GraphNode entry, NodeDefinition definition, PapyrusScriptIndex index, List<GraphExecFlow> flows)
    {
        var names = new GraphNameTable(index, _owner);
        var flow = new GraphExecFlow(_document, _validation.Definitions, entry.Id);
        flows.Add(flow);

        _problems.AddRange(flow.CheckStructured(_document, _validation.Definitions));
        _problems.AddRange(GraphDefiniteAssignment.Check(_document, _validation.Definitions, flow));

        var parameters = new List<IrParameter>();
        foreach (var pin in definition.DataOutputs.Where(p => p.Id.StartsWith(PinIds.ParameterPrefix, StringComparison.OrdinalIgnoreCase)))
        {
            var name = pin.Id[PinIds.ParameterPrefix.Length..];
            names.Reserve(name);
            var (typeName, isArray) = _types.TypeOf(pin.Type, Generics(entry.Id));
            parameters.Add(new IrParameter(name, typeName, isArray, pin.DeclaredDefault));
        }

        var returnType = entry.ConfigString("returns");
        var frame = new Frame
        {
            Names = names,
            Flow = flow,
            ReturnTypeName = string.IsNullOrWhiteSpace(returnType) ? null : BaseType(returnType),
            ReturnIsArray = IsArray(returnType),
        };


        foreach (var pin in definition.DataOutputs)
        {
            if (!pin.Id.StartsWith(PinIds.ParameterPrefix, StringComparison.OrdinalIgnoreCase)) continue;
            frame.ResultLocals[new PinRef(entry.Id, pin.Id).ToString()] = pin.Id[PinIds.ParameterPrefix.Length..];
        }

        var start = flow.TargetOf(entry.Id, PinIds.Exec);

        if (frame.ReturnTypeName != null)
        {
            var leaks = flow.PathsLeavingWithoutReturn(_validation.Definitions, start);
            if (leaks.Count > 0)
            {
                _problems.Add(new GraphDiagnostic
                {
                    Code = GraphDiagnosticCodes.NotAllPathsReturn,
                    Severity = GraphSeverity.Error,
                    Message = "This function declares a return value, but control can leave it "
                              + "without passing a Return node. Papyrus would hand back a zero value "
                              + "at runtime instead of refusing to compile.",
                    NodeId = entry.Id,
                    RelatedNodes = leaks,
                });
            }
        }

        var body = start == null
            ? new List<IrStatement>()
            : LowerRegion(start, stopAt: null, frame);

        CollapseTemporaries(body, frame);

        return new IrCallable
        {
            Name = GraphValidator.EntryNameOf(entry, definition),
            EntryNodeId = entry.Id,
            IsEvent = definition.Kind == GraphNodeKind.EventEntry,
            IsGlobal = entry.ConfigString("global") == "true",


            RemoteObjectType = definition.IsRemoteEvent ? definition.OwnerScript : null,
            StateName = entry.ConfigString("state"),
            ReturnTypeName = frame.ReturnTypeName,
            ReturnIsArray = frame.ReturnIsArray,
            Parameters = parameters,
            Locals = frame.Locals,
            Body = body,
        };
    }








    private List<IrStatement> LowerRegion(string? nodeId, string? stopAt, Frame frame)
    {
        var statements = new List<IrStatement>();
        var guard = 0;

        while (nodeId != null && !string.Equals(nodeId, stopAt, StringComparison.Ordinal))
        {
            if (++guard > 10_000)
            {
                _problems.Add(GraphDiagnostic.Error(
                    GraphDiagnosticCodes.InternalEmitterFault,
                    "Lowering did not terminate. This is a defect in the compiler, not the graph.",
                    nodeId));
                break;
            }

            var node = _document.Node(nodeId);
            if (node == null || !_validation.Definitions.TryGetValue(nodeId, out var definition)) break;

            switch (definition.Kind)
            {
                case GraphNodeKind.Branch:
                {
                    var merge = frame.Flow.ImmediatePostDominator.GetValueOrDefault(nodeId);
                    var condition = Pull(new PinRef(nodeId, PinIds.Condition), frame, statements);

                    var thenBody = LowerRegion(Step(nodeId, PinIds.Then, frame), merge, frame);
                    var elseBody = LowerRegion(Step(nodeId, PinIds.Else, frame), merge, frame);

                    statements.Add(Fold(condition, thenBody, elseBody, nodeId));
                    nodeId = merge;
                    continue;
                }

                case GraphNodeKind.While:
                {
                    var condition = Pull(new PinRef(nodeId, PinIds.Condition), frame, statements);

                    var loop = new LoopFrame { NodeId = nodeId };
                    frame.Loops.Add(loop);
                    var body = LowerRegion(frame.Flow.TargetOf(nodeId, PinIds.Body), nodeId, frame);
                    frame.Loops.RemoveAt(frame.Loops.Count - 1);

                    statements.Add(new IrWhile(GuardedBy(loop, condition, frame, statements), body)
                    {
                        NodeId = nodeId,
                    });
                    nodeId = Step(nodeId, PinIds.Completed, frame);
                    continue;
                }

                case GraphNodeKind.ForEach:
                {
                    statements.AddRange(LowerForEach(node, frame, statements));
                    nodeId = Step(nodeId, PinIds.Completed, frame);
                    continue;
                }

                case GraphNodeKind.Return:
                {



                    IrExpression? value = null;
                    var valuePin = new PinRef(nodeId, PinIds.Value);
                    if (_document.Into(valuePin).Any() || node.PinValues.ContainsKey(PinIds.Value))
                        value = Pull(valuePin, frame, statements);

                    if (value == null && frame.ReturnTypeName != null)
                    {
                        _problems.Add(GraphDiagnostic.Error(
                            GraphDiagnosticCodes.ReturnValueMissing,
                            "This function returns a value, so its Return node needs one.",
                            nodeId, PinIds.Value));
                    }
                    else if (value != null && frame.ReturnTypeName == null)
                    {
                        _problems.Add(GraphDiagnostic.Error(
                            GraphDiagnosticCodes.ReturnValueUnexpected,
                            "This function returns nothing, so its Return node cannot carry a value.",
                            nodeId, PinIds.Value));
                    }

                    statements.Add(new IrReturn(value) { NodeId = nodeId });
                    return statements;
                }

                case GraphNodeKind.Break:
                {
                    if (!InLoop(nodeId, "Break", frame)) return statements;

                    var loop = frame.Loops[^1];
                    loop.Sentinel ??= DeclareSentinel(frame);
                    statements.Add(new IrAssign(new IrName(loop.Sentinel), new IrLiteral("true"))
                    {
                        NodeId = nodeId,
                    });
                    return statements;
                }

                case GraphNodeKind.Continue:
                {



                    InLoop(nodeId, "Continue", frame);
                    return statements;
                }

                default:
                {
                    statements.AddRange(LowerSimple(node, definition, frame, statements));

                    var next = NextOf(nodeId, definition, frame);
                    nodeId = next != null && frame.Flow.BackEdges.Contains((nodeId, next)) ? null : next;
                    continue;
                }
            }
        }

        return statements;
    }








    private IEnumerable<IrStatement> LowerForEach(GraphNode node, Frame frame, List<IrStatement> into)
    {
        var array = Pull(new PinRef(node.Id, PinIds.Array), frame, into);
        var arrayLocal = Bind(node.Id, frame, "items", array.TypeName, array.IsArray, array, into);

        var indexName = frame.Names.Allocate("index");
        frame.Locals.Add(new IrLocal(indexName, "int", false));
        yield return new IrAssign(new IrName(indexName), new IrLiteral("0")) { NodeId = node.Id };

        var elementName = frame.Names.Allocate("item");
        frame.Locals.Add(new IrLocal(elementName, array.TypeName, false));

        frame.ResultLocals[new PinRef(node.Id, PinIds.Element).ToString()] = elementName;
        frame.ResultLocals[new PinRef(node.Id, PinIds.Index).ToString()] = indexName;

        var body = new List<IrStatement>
        {
            new IrAssign(
                new IrName(elementName),
                new IrIndex(new IrName(arrayLocal), new IrName(indexName))
                {
                    TypeName = array.TypeName,
                }) { NodeId = node.Id },
        };

        var loop = new LoopFrame { NodeId = node.Id };
        frame.Loops.Add(loop);
        body.AddRange(LowerRegion(frame.Flow.TargetOf(node.Id, PinIds.Body), node.Id, frame));
        frame.Loops.RemoveAt(frame.Loops.Count - 1);



        body.Add(new IrAssign(
            new IrName(indexName),
            new IrBinary("+", new IrName(indexName), new IrLiteral("1")) { TypeName = "int" })
        {
            NodeId = node.Id,
        });

        IrExpression bounds = new IrBinary(
            "<",
            new IrName(indexName),
            new IrMember(new IrName(arrayLocal), "Length") { TypeName = "int" })
        {
            TypeName = "bool",
        };

        var reset = new List<IrStatement>();
        bounds = GuardedBy(loop, bounds, frame, reset);
        foreach (var statement in reset) yield return statement;

        yield return new IrWhile(bounds, body) { NodeId = node.Id };
    }


    private IEnumerable<IrStatement> LowerSimple(
        GraphNode node, NodeDefinition definition, Frame frame, List<IrStatement> into)
    {
        switch (definition.Kind)
        {
            case GraphNodeKind.VariableSet:
            {
                var name = node.ConfigString("name") ?? "";
                var value = Pull(new PinRef(node.Id, PinIds.Value), frame, into);
                yield return new IrAssign(new IrName(name), value) { NodeId = node.Id };
                yield break;
            }

            case GraphNodeKind.LocalDeclare:
            {




                var name = node.ConfigString("name") ?? "";
                var written = node.ConfigString("type") ?? "var";

                if (!frame.DeclaredLocals.Add(name))
                {
                    _problems.Add(GraphDiagnostic.Error(
                        GraphDiagnosticCodes.DuplicateDeclaration,
                        $"'{name}' is already declared in this function.",
                        node.Id));
                    yield break;
                }

                frame.Names.Reserve(name);
                frame.Locals.Add(new IrLocal(name, BaseType(written), IsArray(written)));

                if (_document.Into(new PinRef(node.Id, PinIds.Value)).Any())
                {
                    var initial = Pull(new PinRef(node.Id, PinIds.Value), frame, into);
                    yield return new IrAssign(new IrName(name), initial) { NodeId = node.Id };
                }
                yield break;
            }

            case GraphNodeKind.PropertySet:
            {
                var target = PullReceiver(node, definition, frame, into);
                var value = Pull(new PinRef(node.Id, PinIds.Value), frame, into);
                IrExpression left = target == null
                    ? new IrName(definition.MemberName ?? "")
                    : new IrMember(target, definition.MemberName ?? "");
                yield return new IrAssign(left, value) { NodeId = node.Id };
                yield break;
            }

            case GraphNodeKind.IndexSet:
            {
                var array = Pull(new PinRef(node.Id, PinIds.Array), frame, into);
                var index = Pull(new PinRef(node.Id, PinIds.Index), frame, into);
                var value = Pull(new PinRef(node.Id, PinIds.Value), frame, into);
                yield return new IrAssign(new IrIndex(array, index), value) { NodeId = node.Id };
                yield break;
            }

            case GraphNodeKind.Call:
            case GraphNodeKind.ArrayOp:
            {
                var call = BuildCall(node, definition, frame, into);
                if (call == null) yield break;

                var consumed = _document.OutOf(new PinRef(node.Id, PinIds.Return)).Any();
                if (!consumed)
                {


                    yield return new IrExpressionStatement(call) { NodeId = node.Id };
                    yield break;
                }

                var name = Bind(
                    node.Id, frame, definition.LocalNameHint ?? "value",
                    call.TypeName, call.IsArray, call, into, declareOnly: true);
                yield return new IrAssign(new IrName(name), call) { NodeId = node.Id };
                yield break;
            }

            default:
                yield break;
        }
    }
















    private static void CollapseTemporaries(List<IrStatement> body, Frame frame) =>
        CollapseIn(body, body, frame);










    private static void CollapseIn(List<IrStatement> block, List<IrStatement> whole, Frame frame)
    {
        foreach (var statement in block)
        {
            switch (statement)
            {
                case IrIf branch:
                    foreach (var arm in branch.Branches)
                        if (arm.Body is List<IrStatement> armBody) CollapseIn(armBody, whole, frame);
                    if (branch.Else is List<IrStatement> elseBody) CollapseIn(elseBody, whole, frame);
                    break;
                case IrWhile loop:
                    if (loop.Body is List<IrStatement> loopBody) CollapseIn(loopBody, whole, frame);
                    break;
            }
        }

        for (int i = 0; i + 1 < block.Count; i++)
        {
            var body = block;
            if (body[i] is not IrAssign { Target: IrName produced } first) continue;
            if (first.Value is IrName) continue;
            if (body[i + 1] is not IrAssign { Target: IrName _, Value: IrName copied } second) continue;
            if (!string.Equals(produced.Name, copied.Name, StringComparison.Ordinal)) continue;
            if (frame.DeclaredLocals.Contains(produced.Name)) continue;

            var local = frame.Locals.FirstOrDefault(l =>
                string.Equals(l.Name, produced.Name, StringComparison.Ordinal));
            if (local == null) continue;
            if (CountReads(whole, produced.Name) != 1) continue;

            body[i] = new IrAssign(second.Target, first.Value)
            {
                NodeId = second.NodeId ?? first.NodeId,
                PinId = second.PinId,
            };
            body.RemoveAt(i + 1);
            frame.Locals.Remove(local);
        }
    }


    private static int CountReads(IEnumerable<IrStatement> body, string name)
    {
        var count = 0;
        foreach (var statement in body) Walk(statement);
        return count;

        void Walk(IrStatement statement)
        {
            switch (statement)
            {
                case IrAssign assign:
                    if (assign.Target is not IrName) Read(assign.Target);
                    Read(assign.Value);
                    break;
                case IrDefine define:
                    Read(define.Value);
                    break;
                case IrExpressionStatement expression:
                    Read(expression.Expression);
                    break;
                case IrReturn returned:
                    Read(returned.Value);
                    break;
                case IrIf branch:
                    foreach (var arm in branch.Branches)
                    {
                        Read(arm.Condition);
                        foreach (var inner in arm.Body) Walk(inner);
                    }
                    if (branch.Else != null) foreach (var inner in branch.Else) Walk(inner);
                    break;
                case IrWhile loop:
                    Read(loop.Condition);
                    foreach (var inner in loop.Body) Walk(inner);
                    break;
            }
        }

        void Read(IrExpression? expression)
        {
            switch (expression)
            {
                case null: return;
                case IrName named:
                    if (string.Equals(named.Name, name, StringComparison.Ordinal)) count++;
                    return;
                case IrMember member: Read(member.Target); return;
                case IrIndex index: Read(index.Target); Read(index.Index); return;
                case IrCall call:
                    Read(call.Receiver);
                    foreach (var argument in call.Arguments) Read(argument.Value);
                    return;
                case IrUnary unary: Read(unary.Operand); return;
                case IrBinary binary: Read(binary.Left); Read(binary.Right); return;
                case IrCast cast: Read(cast.Value); return;
                case IrTypeCheck check: Read(check.Value); return;
                case IrNewArray created: Read(created.Size); return;
            }
        }
    }












    private string? Step(string nodeId, string pinId, Frame frame)
    {
        var target = frame.Flow.TargetOf(nodeId, pinId);
        if (target == null) return null;
        return frame.Flow.BackEdges.Contains((nodeId, target)) ? null : target;
    }


    private bool InLoop(string nodeId, string what, Frame frame)
    {
        if (frame.Loops.Count > 0) return true;

        _problems.Add(GraphDiagnostic.Error(
            GraphDiagnosticCodes.LoopExitOutsideLoop,
            $"{what} has no loop to act on. Put it inside a While or ForEach.",
            nodeId));
        return false;
    }


    private static string DeclareSentinel(Frame frame)
    {
        var name = frame.Names.Allocate("broke");
        frame.Locals.Add(new IrLocal(name, "bool", false));
        return name;
    }














    private static IrExpression GuardedBy(
        LoopFrame loop, IrExpression condition, Frame frame, List<IrStatement> before)
    {
        if (loop.Sentinel == null) return condition;

        before.Add(new IrAssign(new IrName(loop.Sentinel), new IrLiteral("false"))
        {
            NodeId = loop.NodeId,
        });

        return new IrBinary(
            "&&",
            condition,
            new IrUnary("!", new IrName(loop.Sentinel) { TypeName = "bool" }) { TypeName = "bool" })
        {
            TypeName = "bool",
            NodeId = loop.NodeId,
        };
    }

    private string? NextOf(string nodeId, NodeDefinition definition, Frame frame)
    {
        foreach (var pin in definition.ExecOutputs)
        {
            var target = frame.Flow.TargetOf(nodeId, pin.Id);
            if (target != null) return target;
        }
        return null;
    }


    private static IrIf Fold(
        IrExpression condition, List<IrStatement> thenBody, List<IrStatement> elseBody, string nodeId)
    {



        if (thenBody.Count == 0 && elseBody.Count > 0
            && !(elseBody.Count == 1 && elseBody[0] is IrIf))
        {
            return new IrIf(
                new List<IrBranch> { new(Negate(condition), elseBody) },
                null) { NodeId = nodeId };
        }

        var branches = new List<IrBranch> { new(condition, thenBody) };

        if (elseBody.Count == 1 && elseBody[0] is IrIf nested)
        {
            branches.AddRange(nested.Branches);
            return new IrIf(branches, nested.Else) { NodeId = nodeId };
        }

        return new IrIf(branches, elseBody.Count == 0 ? null : elseBody) { NodeId = nodeId };
    }


    private static IrExpression Negate(IrExpression condition) =>
        condition is IrUnary { Operator: "!" } already
            ? already.Operand
            : new IrUnary("!", condition) { TypeName = "bool", NodeId = condition.NodeId };











    private IrExpression Pull(PinRef pin, Frame frame, List<IrStatement> into)
    {
        var wire = _document.Into(pin).FirstOrDefault();
        if (wire == null) return LiteralFor(pin);

        var sourceKey = wire.From.ToString();
        if (frame.ResultLocals.TryGetValue(sourceKey, out var local))
            return new IrName(local) { NodeId = wire.From.Node, PinId = wire.From.Pin };

        var sourceNode = _document.Node(wire.From.Node);
        if (sourceNode == null || !_validation.Definitions.TryGetValue(wire.From.Node, out var definition))
            return new IrLiteral("None");

        if (!definition.IsPure)
        {


            _problems.Add(new GraphDiagnostic
            {
                Code = GraphDiagnosticCodes.UseBeforeAssignment,
                Severity = GraphSeverity.Error,
                Message = "This value comes from a node that has not run on every path that reaches here.",
                NodeId = pin.Node,
                PinId = pin.Pin,
                RelatedNodes = new[] { wire.From.Node },
            });
            return new IrLiteral("None");
        }

        return BuildPure(sourceNode, definition, wire.From.Pin, frame, into);
    }

    private IrExpression BuildPure(
        GraphNode node, NodeDefinition definition, string outputPin, Frame frame, List<IrStatement> into)
    {
        var generics = Generics(node.Id);
        var pin = definition.PinsFor(node, _document)
            .FirstOrDefault(p => string.Equals(p.Id, outputPin, StringComparison.OrdinalIgnoreCase));
        var (typeName, isArray) = _types.TypeOf(pin?.Type, generics);

        IrExpression built = definition.Kind switch
        {
            GraphNodeKind.Literal => new IrLiteral(LiteralTextOf(node, definition)),
            GraphNodeKind.Self => new IrSelf(),
            GraphNodeKind.Parent => new IrParent(),
            GraphNodeKind.NoneValue => new IrLiteral("None"),

            GraphNodeKind.VariableGet => new IrName(node.ConfigString("name") ?? ""),

            GraphNodeKind.PropertyGet => Member(
                PullReceiver(node, definition, frame, into), definition.MemberName ?? ""),

            GraphNodeKind.Reroute => Pull(new PinRef(node.Id, PinIds.Value), frame, into),

            GraphNodeKind.Unary => new IrUnary(
                BuiltinNodeDefinitions.OperatorToken(definition.Id) ?? "!",
                Pull(new PinRef(node.Id, PinIds.Value), frame, into)),

            GraphNodeKind.Binary => new IrBinary(
                BuiltinNodeDefinitions.OperatorToken(definition.Id) ?? "+",
                Pull(new PinRef(node.Id, PinIds.Left), frame, into),
                Pull(new PinRef(node.Id, PinIds.Right), frame, into)),

            GraphNodeKind.Cast => new IrCast(
                Pull(new PinRef(node.Id, PinIds.Value), frame, into),
                BaseType(node.ConfigString("type")), IsArray(node.ConfigString("type"))),

            GraphNodeKind.TypeCheck => new IrTypeCheck(
                Pull(new PinRef(node.Id, PinIds.Value), frame, into),
                BaseType(node.ConfigString("type")), IsArray(node.ConfigString("type"))),

            GraphNodeKind.NewArray => new IrNewArray(
                BaseType(node.ConfigString("type")),
                Pull(new PinRef(node.Id, PinIds.Index), frame, into)),

            GraphNodeKind.StructNew => new IrNewStruct(node.ConfigString("type") ?? ""),

            GraphNodeKind.IndexGet => new IrIndex(
                Pull(new PinRef(node.Id, PinIds.Array), frame, into),
                Pull(new PinRef(node.Id, PinIds.Index), frame, into)),

            GraphNodeKind.StructGet => Member(
                Pull(new PinRef(node.Id, PinIds.Target), frame, into), node.ConfigString("member") ?? ""),

            GraphNodeKind.ArrayOp when definition.MemberName == "Length" => Member(
                Pull(new PinRef(node.Id, PinIds.Array), frame, into), "Length"),

            GraphNodeKind.Call => (IrExpression?)BuildCall(node, definition, frame, into)
                                  ?? new IrLiteral("None"),

            _ => new IrLiteral("None"),
        };

        return built with { NodeId = node.Id, PinId = outputPin, TypeName = typeName, IsArray = isArray };

        static IrExpression Member(IrExpression? target, string name) =>
            target == null ? new IrName(name) : new IrMember(target, name);
    }

    private IrCall? BuildCall(
        GraphNode node, NodeDefinition definition, Frame frame, List<IrStatement> into)
    {
        var receiver = PullReceiver(node, definition, frame, into);
        var arguments = new List<IrArgument>();
        bool skipped = false;

        foreach (var pin in definition.PinsFor(node, _document)
                     .Where(p => p.Kind == PinKind.Data && p.Direction == PinDirection.In)
                     .Where(p => PinIds.IsArgument(p.Id)))
        {
            var reference = new PinRef(node.Id, pin.Id);
            bool supplied = _document.Into(reference).Any() || node.PinValues.ContainsKey(pin.Id);

            if (!supplied && pin.IsOptional)
            {


                skipped = true;
                continue;
            }

            var value = Pull(reference, frame, into);
            arguments.Add(new IrArgument(value, skipped ? PinIds.ArgumentName(pin.Id) : null));
        }

        var generics = Generics(node.Id);
        var returnPin = definition.Pin(PinIds.Return);
        var (typeName, isArray) = _types.TypeOf(returnPin?.Type, generics);

        var name = definition.Kind == GraphNodeKind.ArrayOp
            ? definition.MemberName ?? definition.Title
            : definition.MemberName ?? definition.Title;


        if (definition.Kind == GraphNodeKind.ArrayOp)
            receiver = Pull(new PinRef(node.Id, PinIds.Array), frame, into);

        return new IrCall(receiver, name, arguments, definition.IsGlobal)
        {
            NodeId = node.Id,
            PinId = PinIds.Return,
            TypeName = typeName,
            IsArray = isArray,
        };
    }









    private IrExpression? PullReceiver(
        GraphNode node, NodeDefinition definition, Frame frame, List<IrStatement> into)
    {
        if (definition.IsGlobal)
            return new IrName(definition.OwnerScript ?? "") { NodeId = node.Id };

        var pin = new PinRef(node.Id, PinIds.Self);
        if (_document.Into(pin).Any()) return Pull(pin, frame, into);

        var owner = definition.OwnerScript;
        if (owner == null) return null;

        if (string.Equals(owner, _document.Header.ScriptName, StringComparison.OrdinalIgnoreCase)) return null;
        if (_owner != null && _types.InheritsFrom(_document.Header.ScriptName, owner)) return null;

        _problems.Add(new GraphDiagnostic
        {
            Code = GraphDiagnosticCodes.UnconnectedSelf,
            Severity = GraphSeverity.Error,
            Message = $"'{definition.Title}' belongs to {owner}, which this script is not, "
                      + "so its Target pin needs a value.",
            NodeId = node.Id,
            PinId = PinIds.Self,
        });
        return null;
    }


    private string Bind(
        string nodeId, Frame frame, string hint, string typeName, bool isArray,
        IrExpression value, List<IrStatement> into, bool declareOnly = false)
    {
        var key = new PinRef(nodeId, PinIds.Return).ToString();
        if (frame.ResultLocals.TryGetValue(key, out var existing)) return existing;

        var name = frame.Names.Allocate(hint);
        frame.Locals.Add(new IrLocal(name, string.IsNullOrEmpty(typeName) ? "var" : typeName, isArray));
        frame.ResultLocals[key] = name;

        if (!declareOnly) into.Add(new IrAssign(new IrName(name), value) { NodeId = nodeId });
        return name;
    }

    private GenericBinding Generics(string nodeId) =>
        _validation.Generics.GetValueOrDefault(nodeId) ?? new GenericBinding();

    private IrExpression LiteralFor(PinRef pin)
    {
        var node = _document.Node(pin.Node);
        if (node != null && node.PinValues.TryGetValue(pin.Pin, out var value))
            return new IrLiteral(value.Value) { NodeId = pin.Node, PinId = pin.Pin };

        if (node != null
            && _validation.Definitions.TryGetValue(pin.Node, out var definition)
            && definition.Pin(pin.Pin)?.DeclaredDefault is { } declared)
        {
            return new IrLiteral(declared) { NodeId = pin.Node, PinId = pin.Pin };
        }

        return new IrLiteral("None") { NodeId = pin.Node, PinId = pin.Pin };
    }

    private static string LiteralTextOf(GraphNode node, NodeDefinition definition)
    {
        if (node.PinValues.TryGetValue(PinIds.Value, out var value) && value.Value.Length > 0)
            return value.Value;

        return definition.Id switch
        {
            "literal.int" => "0",
            "literal.float" => "0.0",
            "literal.bool" => "false",
            "literal.string" => "\"\"",
            _ => "None",
        };
    }

    private static string BaseType(string? written) =>
        string.IsNullOrEmpty(written) ? ""
        : written.EndsWith("[]", StringComparison.Ordinal) ? written[..^2]
        : written;

    private static bool IsArray(string? written) =>
        written != null && written.EndsWith("[]", StringComparison.Ordinal);
}
