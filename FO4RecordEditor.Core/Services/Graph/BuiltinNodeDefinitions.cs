using System;
using System.Collections.Generic;
using System.Linq;

namespace FO4RecordEditor.Services.Graph;

/// <summary>
/// The node types that do not come from a script: control flow, operators, literals, array members.
/// </summary>
/// <remarks>
/// Small and fixed, unlike the generated palette. The array member list has to stay in step with
/// what <c>PapyrusResolver</c> accepts after a dot on an array, and a test asserts that by resolving
/// each one rather than by reading a list, since the resolver's own table is private.
/// </remarks>
public static class BuiltinNodeDefinitions
{
    public const string Branch = "branch";
    public const string While = "while";
    public const string ForEach = "foreach";
    public const string Return = "return";
    public const string Break = "break";
    public const string Continue = "continue";
    public const string Reroute = "reroute";
    public const string Self = "self";
    public const string Parent = "parent";
    public const string NoneValue = "none";
    public const string Cast = "cast";
    public const string TypeCheck = "is";
    public const string NewArray = "new.array";
    public const string IndexGet = "index.get";
    public const string IndexSet = "index.set";
    public const string VariableGet = "var.get";
    public const string VariableSet = "var.set";
    public const string LocalDeclare = "local.declare";
    public const string FunctionEntry = "function.entry";

    public const string LiteralPrefix = "literal.";
    public const string OperatorPrefix = "op.";
    public const string ArrayPrefix = "array.";

    /// <summary>
    /// The members Papyrus allows after a dot on an array.
    /// </summary>
    /// <remarks>
    /// Names, arity and result type taken from what the resolver binds. <c>Length</c> is the only
    /// one that is pure: every other member either mutates the array or is a search whose cost the
    /// author should see sequenced.
    /// </remarks>
    private static readonly (string Name, string Result, int Args, bool Pure)[] ArrayMembers =
    {
        ("Length", "int", 0, true),
        ("Find", "int", 2, false),
        ("RFind", "int", 2, false),
        ("FindStruct", "int", 3, false),
        ("RFindStruct", "int", 3, false),
        ("Add", "None", 2, false),
        ("Insert", "None", 2, false),
        ("Remove", "None", 2, false),
        ("RemoveLast", "None", 0, false),
        ("Clear", "None", 0, false),
        ("GetMatchingStructs", "array", 3, false),
    };

    private static readonly (string Id, string Title, string Token, string Result)[] BinaryOperators =
    {
        ("add", "Add", "+", ""),
        ("sub", "Subtract", "-", ""),
        ("mul", "Multiply", "*", ""),
        ("div", "Divide", "/", ""),
        ("mod", "Modulo", "%", ""),
        ("eq", "Equal", "==", "bool"),
        ("ne", "Not Equal", "!=", "bool"),
        ("lt", "Less Than", "<", "bool"),
        ("le", "Less Or Equal", "<=", "bool"),
        ("gt", "Greater Than", ">", "bool"),
        ("ge", "Greater Or Equal", ">=", "bool"),
        ("and", "And", "&&", "bool"),
        ("or", "Or", "||", "bool"),
    };

    private static readonly (string Id, string Title, string Token, string Type)[] UnaryOperators =
    {
        ("not", "Not", "!", "bool"),
        ("neg", "Negate", "-", ""),
    };

    private static readonly (string Suffix, string Type, string Default)[] Literals =
    {
        ("int", "int", "0"),
        ("float", "float", "0.0"),
        ("bool", "bool", "false"),
        ("string", "string", "\"\""),
    };

    private static IReadOnlyList<NodeDefinition>? _all;

    /// <summary>Every built-in definition.</summary>
    public static IReadOnlyList<NodeDefinition> All => _all ??= Build();

    public static NodeDefinition? Find(string? id) =>
        id == null ? null : All.FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>The operator token a binary or unary definition emits.</summary>
    public static string? OperatorToken(string definitionId)
    {
        if (!definitionId.StartsWith(OperatorPrefix, StringComparison.OrdinalIgnoreCase)) return null;
        var suffix = definitionId[OperatorPrefix.Length..];

        foreach (var (id, _, token, _) in BinaryOperators)
            if (string.Equals(id, suffix, StringComparison.OrdinalIgnoreCase)) return token;
        foreach (var (id, _, token, _) in UnaryOperators)
            if (string.Equals(id, suffix, StringComparison.OrdinalIgnoreCase)) return token;
        return null;
    }

    /// <summary>The array member name a definition calls, or null.</summary>
    public static string? ArrayMemberName(string definitionId)
    {
        if (!definitionId.StartsWith(ArrayPrefix, StringComparison.OrdinalIgnoreCase)) return null;
        var suffix = definitionId[ArrayPrefix.Length..];
        foreach (var (name, _, _, _) in ArrayMembers)
            if (string.Equals(name, suffix, StringComparison.OrdinalIgnoreCase)) return name;
        return null;
    }

    /// <summary>The array member names the palette offers, for the parity test.</summary>
    public static IReadOnlyList<string> ArrayMemberNames =>
        ArrayMembers.Select(m => m.Name).ToList();

    private static PinDefinition ExecIn() => new()
    {
        Id = PinIds.Exec, Label = "", Direction = PinDirection.In, Kind = PinKind.Exec,
    };

    private static PinDefinition ExecOut(string id, string label) => new()
    {
        Id = id, Label = label, Direction = PinDirection.Out, Kind = PinKind.Exec,
    };

    private static PinDefinition DataIn(string id, string label, PinTypeExpr type, bool optional = false) => new()
    {
        Id = id, Label = label, Direction = PinDirection.In, Kind = PinKind.Data,
        Type = type, IsOptional = optional,
    };

    private static PinDefinition DataOut(string id, string label, PinTypeExpr type) => new()
    {
        Id = id, Label = label, Direction = PinDirection.Out, Kind = PinKind.Data, Type = type,
    };

    private static List<NodeDefinition> Build()
    {
        var all = new List<NodeDefinition>
        {
            new()
            {
                Id = Branch, Kind = GraphNodeKind.Branch, Title = "Branch", Category = "Flow",
                Summary = "Runs one of two paths depending on a condition.",
                Pins = new[]
                {
                    ExecIn(),
                    DataIn(PinIds.Condition, "Condition", PinTypeExpr.Concrete("bool")),
                    ExecOut(PinIds.Then, "True"),
                    ExecOut(PinIds.Else, "False"),
                },
            },
            new()
            {
                Id = While, Kind = GraphNodeKind.While, Title = "While", Category = "Flow",
                Summary = "Repeats the body while the condition holds.",
                Pins = new[]
                {
                    ExecIn(),
                    DataIn(PinIds.Condition, "Condition", PinTypeExpr.Concrete("bool")),
                    ExecOut(PinIds.Body, "Body"),
                    ExecOut(PinIds.Completed, "Completed"),
                },
            },
            new()
            {
                Id = ForEach, Kind = GraphNodeKind.ForEach, Title = "For Each", Category = "Flow",
                // Sugar. Papyrus has no foreach, so this lowers to a While over an index.
                Summary = "Walks an array. Lowered to a While loop over an index.",
                Pins = new[]
                {
                    ExecIn(),
                    DataIn(PinIds.Array, "Array", PinTypeExpr.ArrayOfGeneric()),
                    ExecOut(PinIds.Body, "Body"),
                    DataOut(PinIds.Element, "Element", PinTypeExpr.ElementOfGeneric()),
                    DataOut(PinIds.Index, "Index", PinTypeExpr.Concrete("int")),
                    ExecOut(PinIds.Completed, "Completed"),
                },
            },
            new()
            {
                Id = Return, Kind = GraphNodeKind.Return, Title = "Return", Category = "Flow",
                Summary = "Leaves the function, optionally with a value.",
                Pins = new[]
                {
                    ExecIn(),
                    DataIn(PinIds.Value, "Value", PinTypeExpr.Any, optional: true),
                },
            },
            new()
            {
                Id = LocalDeclare, Kind = GraphNodeKind.LocalDeclare, Title = "Local", Category = "Flow",
                Summary = "Declares a variable that lives for one call of this function.",
                Pins = new[]
                {
                    ExecIn(),
                    DataIn(PinIds.Value, "Value", PinTypeExpr.Any, optional: true),
                    ExecOut(PinIds.Then, ""),
                },
            },
            new()
            {
                Id = Break, Kind = GraphNodeKind.Break, Title = "Break", Category = "Flow",
                Summary = "Leaves the enclosing loop.",
                Pins = new[] { ExecIn() },
            },
            new()
            {
                Id = Continue, Kind = GraphNodeKind.Continue, Title = "Continue", Category = "Flow",
                Summary = "Skips the rest of this pass and tests the loop again.",
                Pins = new[] { ExecIn() },
            },
            new()
            {
                Id = Reroute, Kind = GraphNodeKind.Reroute, Title = "Reroute", Category = "Flow",
                IsPure = true,
                Summary = "Tidies a wire. Emits nothing.",
                Pins = new[]
                {
                    DataIn(PinIds.Value, "", PinTypeExpr.Generic()),
                    DataOut(PinIds.Value + ".out", "", PinTypeExpr.Generic()),
                },
            },
            new()
            {
                Id = Self, Kind = GraphNodeKind.Self, Title = "Self", Category = "Values",
                IsPure = true, LocalNameHint = "self",
                Pins = new[] { DataOut(PinIds.Value, "Self", PinTypeExpr.SelfType) },
            },
            new()
            {
                Id = Parent, Kind = GraphNodeKind.Parent, Title = "Parent", Category = "Values",
                IsPure = true,
                Pins = new[] { DataOut(PinIds.Value, "Parent", PinTypeExpr.SelfType) },
            },
            new()
            {
                Id = NoneValue, Kind = GraphNodeKind.NoneValue, Title = "None", Category = "Values",
                IsPure = true,
                Pins = new[] { DataOut(PinIds.Value, "None", PinTypeExpr.Concrete("None")) },
            },
            new()
            {
                Id = Cast, Kind = GraphNodeKind.Cast, Title = "Cast", Category = "Values",
                IsPure = true, LocalNameHint = "cast",
                // Required rather than implicit: a downcast can yield None at run time, and the
                // author owns that decision.
                Summary = "Converts a value to another type with 'as'.",
                Pins = new[]
                {
                    DataIn(PinIds.Value, "Value", PinTypeExpr.Any),
                    DataOut(PinIds.Return, "Result", PinTypeExpr.Any),
                },
            },
            new()
            {
                Id = TypeCheck, Kind = GraphNodeKind.TypeCheck, Title = "Is", Category = "Values",
                IsPure = true, LocalNameHint = "isType",
                Summary = "Tests whether a value is of a type.",
                Pins = new[]
                {
                    DataIn(PinIds.Value, "Value", PinTypeExpr.Any),
                    DataOut(PinIds.Return, "Result", PinTypeExpr.Concrete("bool")),
                },
            },
            new()
            {
                Id = NewArray, Kind = GraphNodeKind.NewArray, Title = "New Array", Category = "Array",
                IsPure = true, LocalNameHint = "array",
                Pins = new[]
                {
                    DataIn(PinIds.Index, "Size", PinTypeExpr.Concrete("int")),
                    DataOut(PinIds.Return, "Array", PinTypeExpr.ArrayOfGeneric()),
                },
            },
            new()
            {
                Id = IndexGet, Kind = GraphNodeKind.IndexGet, Title = "Get At", Category = "Array",
                IsPure = true, LocalNameHint = "item",
                Pins = new[]
                {
                    DataIn(PinIds.Array, "Array", PinTypeExpr.ArrayOfGeneric()),
                    DataIn(PinIds.Index, "Index", PinTypeExpr.Concrete("int")),
                    DataOut(PinIds.Return, "Value", PinTypeExpr.ElementOfGeneric()),
                },
            },
            new()
            {
                Id = IndexSet, Kind = GraphNodeKind.IndexSet, Title = "Set At", Category = "Array",
                Pins = new[]
                {
                    ExecIn(),
                    DataIn(PinIds.Array, "Array", PinTypeExpr.ArrayOfGeneric()),
                    DataIn(PinIds.Index, "Index", PinTypeExpr.Concrete("int")),
                    DataIn(PinIds.Value, "Value", PinTypeExpr.ElementOfGeneric()),
                    ExecOut(PinIds.Then, ""),
                },
            },
            new()
            {
                Id = VariableGet, Kind = GraphNodeKind.VariableGet, Title = "Get Variable",
                Category = "Variables", IsPure = true,
                Pins = new[] { DataOut(PinIds.Value, "Value", PinTypeExpr.Any) },
            },
            new()
            {
                Id = VariableSet, Kind = GraphNodeKind.VariableSet, Title = "Set Variable",
                Category = "Variables",
                Pins = new[]
                {
                    ExecIn(),
                    DataIn(PinIds.Value, "Value", PinTypeExpr.Any),
                    ExecOut(PinIds.Then, ""),
                },
            },
            new()
            {
                Id = FunctionEntry, Kind = GraphNodeKind.FunctionEntry, Title = "Function",
                Category = "Flow",
                Summary = "Declares a function this script provides.",
                Pins = new[] { ExecOut(PinIds.Exec, "") },
            },
        };

        all.AddRange(Literals.Select(literal => new NodeDefinition
        {
            Id = LiteralPrefix + literal.Type,
            Kind = GraphNodeKind.Literal,
            Title = literal.Type,
            Category = "Values",
            IsPure = true,
            LocalNameHint = literal.Type,
            Pins = new[]
            {
                DataOut(PinIds.Value, "Value", PinTypeExpr.Concrete(literal.Type)),
            },
        }));

        all.AddRange(BinaryOperators.Select(op => new NodeDefinition
        {
            Id = OperatorPrefix + op.Id,
            Kind = GraphNodeKind.Binary,
            Title = op.Title,
            Category = "Math",
            IsPure = true,
            LocalNameHint = op.Id,
            Pins = new[]
            {
                DataIn(PinIds.Left, "A", PinTypeExpr.Generic()),
                DataIn(PinIds.Right, "B", PinTypeExpr.Generic()),
                // An arithmetic result is whatever its operands are, so it is solved rather than
                // declared var: var would refuse to flow into a float parameter.
                DataOut(
                    PinIds.Return, "Result",
                    op.Result.Length > 0 ? PinTypeExpr.Concrete(op.Result) : PinTypeExpr.Generic()),
            },
        }));

        all.AddRange(UnaryOperators.Select(op => new NodeDefinition
        {
            Id = OperatorPrefix + op.Id,
            Kind = GraphNodeKind.Unary,
            Title = op.Title,
            Category = "Math",
            IsPure = true,
            LocalNameHint = op.Id,
            Pins = new[]
            {
                DataIn(
                    PinIds.Value, "Value",
                    op.Type.Length > 0 ? PinTypeExpr.Concrete(op.Type) : PinTypeExpr.Generic()),
                DataOut(
                    PinIds.Return, "Result",
                    op.Type.Length > 0 ? PinTypeExpr.Concrete(op.Type) : PinTypeExpr.Generic()),
            },
        }));

        all.AddRange(ArrayMembers.Select(member =>
        {
            var pins = new List<PinDefinition>();
            if (!member.Pure) pins.Add(ExecIn());
            pins.Add(DataIn(PinIds.Array, "Array", PinTypeExpr.ArrayOfGeneric()));

            // Argument shapes follow what the resolver accepts: a searched-for value plus a start
            // index, a member name for the struct searches, and an element for the mutators.
            switch (member.Name)
            {
                case "Find":
                case "RFind":
                    pins.Add(DataIn(PinIds.Value, "Value", PinTypeExpr.ElementOfGeneric()));
                    pins.Add(DataIn(PinIds.Index, "Start Index", PinTypeExpr.Concrete("int"), optional: true));
                    break;
                case "FindStruct":
                case "RFindStruct":
                case "GetMatchingStructs":
                    pins.Add(DataIn("member", "Member Name", PinTypeExpr.Concrete("string")));
                    pins.Add(DataIn(PinIds.Value, "Value", PinTypeExpr.Any));
                    pins.Add(DataIn(PinIds.Index, "Start Index", PinTypeExpr.Concrete("int"), optional: true));
                    break;
                case "Add":
                case "Insert":
                    pins.Add(DataIn(PinIds.Element, "Element", PinTypeExpr.ElementOfGeneric()));
                    pins.Add(DataIn(PinIds.Index, "Index", PinTypeExpr.Concrete("int"), optional: true));
                    break;
                case "Remove":
                    pins.Add(DataIn(PinIds.Index, "Index", PinTypeExpr.Concrete("int")));
                    pins.Add(DataIn("count", "Count", PinTypeExpr.Concrete("int"), optional: true));
                    break;
            }

            if (member.Result == "array")
                pins.Add(DataOut(PinIds.Return, "Result", PinTypeExpr.ArrayOfGeneric()));
            else if (member.Result != "None")
                pins.Add(DataOut(PinIds.Return, "Result", PinTypeExpr.Concrete(member.Result)));

            if (!member.Pure) pins.Add(ExecOut(PinIds.Then, ""));

            return new NodeDefinition
            {
                Id = ArrayPrefix + member.Name.ToLowerInvariant(),
                Kind = GraphNodeKind.ArrayOp,
                Title = member.Name,
                Category = "Array",
                IsPure = member.Pure,
                MemberName = member.Name,
                LocalNameHint = member.Name.ToLowerInvariant(),
                Pins = pins,
            };
        }));

        return all;
    }
}
