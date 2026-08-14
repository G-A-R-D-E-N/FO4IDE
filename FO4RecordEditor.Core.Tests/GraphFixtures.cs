using System;
using System.Collections.Generic;
using FO4RecordEditor.Services.Graph;

namespace FO4RecordEditor.Core.Tests;







internal sealed record GraphFixture(string Name, Func<GraphDocument> Build, string Reference)
{
    public override string ToString() => Name;
}








internal static class GraphFixtures
{
    public static IReadOnlyList<GraphFixture> All => Build();

    private static IReadOnlyList<GraphFixture> Build()
    {
        var palette = GraphTestEnvironment.Palette();
        var fixtures = new List<GraphFixture>();

        void Add(string name, Func<GraphBuilder, GraphDocument> build, string reference) =>
            fixtures.Add(new GraphFixture(
                name,
                () => build(new GraphBuilder("Fixture", "ObjectReference")),
                reference));


        Add("01_OnActivateNotify", graph =>
        {
            var entry = graph.Node(palette, "event:ObjectReference.OnActivate");
            var notify = graph.Node(palette, "global:Debug.Notification");
            graph.Value(notify, "arg:asNotificationText", "string", "\"opened\"");
            graph.Wire(entry, PinIds.Exec, notify, PinIds.Exec);
            return graph.Document;
        }, """
            Scriptname Fixture extends ObjectReference

            Event OnActivate(ObjectReference akActionRef)
                Debug.Notification("opened")
            EndEvent
            """);


        Add("02_EventParameterFlows", graph =>
        {
            var entry = graph.Node(palette, "event:ObjectReference.OnActivate");
            var distance = graph.Node(palette, "call:ObjectReference.GetDistance");
            graph.Wire(entry, PinIds.Exec, distance, PinIds.Exec);
            graph.Wire(entry, "param:akActionRef", distance, "arg:akOther");
            return graph.Document;
        }, """
            Scriptname Fixture extends ObjectReference

            Event OnActivate(ObjectReference akActionRef)
                GetDistance(akActionRef)
            EndEvent
            """);


        Add("03_BranchRejoins", graph =>
        {
            var entry = graph.Node(palette, "event:ObjectReference.OnLoad");
            var enabled = graph.Node(palette, "call:ObjectReference.IsEnabled");
            var branch = graph.Node(palette, BuiltinNodeDefinitions.Branch);
            var yes = graph.Node(palette, "global:Debug.Notification");
            var no = graph.Node(palette, "global:Debug.Notification");
            var after = graph.Node(palette, "global:Debug.Notification");

            graph.Value(yes, "arg:asNotificationText", "string", "\"on\"");
            graph.Value(no, "arg:asNotificationText", "string", "\"off\"");
            graph.Value(after, "arg:asNotificationText", "string", "\"done\"");

            graph.Wire(entry, PinIds.Exec, enabled, PinIds.Exec);
            graph.Wire(enabled, PinIds.Then, branch, PinIds.Exec);
            graph.Wire(enabled, PinIds.Return, branch, PinIds.Condition);
            graph.Wire(branch, PinIds.Then, yes, PinIds.Exec);
            graph.Wire(branch, PinIds.Else, no, PinIds.Exec);
            graph.Wire(yes, PinIds.Then, after, PinIds.Exec);
            graph.Wire(no, PinIds.Then, after, PinIds.Exec);
            return graph.Document;
        }, """
            Scriptname Fixture extends ObjectReference

            Event OnLoad()
                bool enabled
                enabled = IsEnabled()
                If (enabled)
                    Debug.Notification("on")
                Else
                    Debug.Notification("off")
                EndIf
                Debug.Notification("done")
            EndEvent
            """);


        Add("04_BranchNoElse", graph =>
        {
            var entry = graph.Node(palette, "event:ObjectReference.OnLoad");
            var enabled = graph.Node(palette, "call:ObjectReference.IsEnabled");
            var branch = graph.Node(palette, BuiltinNodeDefinitions.Branch);
            var yes = graph.Node(palette, "call:ObjectReference.Disable");

            graph.Wire(entry, PinIds.Exec, enabled, PinIds.Exec);
            graph.Wire(enabled, PinIds.Then, branch, PinIds.Exec);
            graph.Wire(enabled, PinIds.Return, branch, PinIds.Condition);
            graph.Wire(branch, PinIds.Then, yes, PinIds.Exec);
            return graph.Document;
        }, """
            Scriptname Fixture extends ObjectReference

            Event OnLoad()
                bool enabled
                enabled = IsEnabled()
                If (enabled)
                    Disable()
                EndIf
            EndEvent
            """);


        Add("05_WhileLoop", graph =>
        {
            var entry = graph.Node(palette, "event:ObjectReference.OnLoad");
            var enabled = graph.Node(palette, "call:ObjectReference.IsEnabled");
            var loop = graph.Node(palette, BuiltinNodeDefinitions.While);
            var body = graph.Node(palette, "global:Utility.Wait");
            var after = graph.Node(palette, "global:Debug.Notification");

            graph.Value(body, "arg:afSeconds", "float", "1.0");
            graph.Value(after, "arg:asNotificationText", "string", "\"done\"");

            graph.Wire(entry, PinIds.Exec, enabled, PinIds.Exec);
            graph.Wire(enabled, PinIds.Then, loop, PinIds.Exec);
            graph.Wire(enabled, PinIds.Return, loop, PinIds.Condition);
            graph.Wire(loop, PinIds.Body, body, PinIds.Exec);
            graph.Wire(body, PinIds.Then, loop, PinIds.Exec);
            graph.Wire(loop, PinIds.Completed, after, PinIds.Exec);
            return graph.Document;
        }, """
            Scriptname Fixture extends ObjectReference

            Event OnLoad()
                bool enabled
                enabled = IsEnabled()
                While (enabled)
                    Utility.Wait(1.0)
                EndWhile
                Debug.Notification("done")
            EndEvent
            """);


        Add("06_TrailingOptionalOmitted", graph =>
        {
            var entry = graph.Node(palette, "event:ObjectReference.OnLoad");
            var add = graph.Node(palette, "call:ObjectReference.AddItem");
            var none = graph.Node(palette, BuiltinNodeDefinitions.NoneValue);
            graph.Wire(entry, PinIds.Exec, add, PinIds.Exec);
            graph.Wire(none, PinIds.Value, add, "arg:akItemToAdd");
            return graph.Document;
        }, """
            Scriptname Fixture extends ObjectReference

            Event OnLoad()
                AddItem(None)
            EndEvent
            """);


        Add("07_NamedArgumentAfterSkip", graph =>
        {
            var entry = graph.Node(palette, "event:ObjectReference.OnLoad");
            var add = graph.Node(palette, "call:ObjectReference.AddItem");
            var none = graph.Node(palette, BuiltinNodeDefinitions.NoneValue);
            graph.Wire(entry, PinIds.Exec, add, PinIds.Exec);
            graph.Wire(none, PinIds.Value, add, "arg:akItemToAdd");
            graph.Value(add, "arg:abSilent", "bool", "true");
            return graph.Document;
        }, """
            Scriptname Fixture extends ObjectReference

            Event OnLoad()
                AddItem(None, abSilent = true)
            EndEvent
            """);


        Add("08_SharedCallBindsOneLocal", graph =>
        {
            var entry = graph.Node(palette, "event:ObjectReference.OnLoad");
            var player = graph.Node(palette, "global:Game.GetPlayer");
            var move = graph.Node(palette, "call:ObjectReference.MoveTo");
            var distance = graph.Node(palette, "call:ObjectReference.GetDistance");

            graph.Wire(entry, PinIds.Exec, player, PinIds.Exec);
            graph.Wire(player, PinIds.Then, move, PinIds.Exec);
            graph.Wire(move, PinIds.Then, distance, PinIds.Exec);
            graph.Wire(player, PinIds.Return, move, "arg:akTarget");
            graph.Wire(player, PinIds.Return, distance, "arg:akOther");
            return graph.Document;
        }, """
            Scriptname Fixture extends ObjectReference

            Event OnLoad()
                Actor player
                player = Game.GetPlayer()
                MoveTo(player)
                GetDistance(player)
            EndEvent
            """);


        Add("09_ArithmeticInlines", graph =>
        {
            var entry = graph.Node(palette, "event:ObjectReference.OnLoad");
            var wait = graph.Node(palette, "global:Utility.Wait");
            var add = graph.Node(palette, "op.add");
            var left = graph.Node(palette, "literal.float");
            var right = graph.Node(palette, "literal.float");

            graph.Value(left, PinIds.Value, "float", "1.5");
            graph.Value(right, PinIds.Value, "float", "2.5");
            graph.Wire(entry, PinIds.Exec, wait, PinIds.Exec);
            graph.Wire(left, PinIds.Value, add, PinIds.Left);
            graph.Wire(right, PinIds.Value, add, PinIds.Right);
            graph.Wire(add, PinIds.Return, wait, "arg:afSeconds");
            return graph.Document;
        }, """
            Scriptname Fixture extends ObjectReference

            Event OnLoad()
                Utility.Wait(1.5 + 2.5)
            EndEvent
            """);


        Add("10_ComparisonBranch", graph =>
        {
            var entry = graph.Node(palette, "event:ObjectReference.OnLoad");
            var count = graph.Node(palette, "call:ObjectReference.GetItemCount");
            var compare = graph.Node(palette, "op.gt");
            var zero = graph.Node(palette, "literal.int");
            var branch = graph.Node(palette, BuiltinNodeDefinitions.Branch);
            var notify = graph.Node(palette, "global:Debug.Notification");

            graph.Value(zero, PinIds.Value, "int", "0");
            graph.Value(notify, "arg:asNotificationText", "string", "\"has items\"");
            graph.Wire(entry, PinIds.Exec, count, PinIds.Exec);
            graph.Wire(count, PinIds.Then, branch, PinIds.Exec);
            graph.Wire(count, PinIds.Return, compare, PinIds.Left);
            graph.Wire(zero, PinIds.Value, compare, PinIds.Right);
            graph.Wire(compare, PinIds.Return, branch, PinIds.Condition);
            graph.Wire(branch, PinIds.Then, notify, PinIds.Exec);
            return graph.Document;
        }, """
            Scriptname Fixture extends ObjectReference

            Event OnLoad()
                int itemCount
                itemCount = GetItemCount(None)
                If (itemCount > 0)
                    Debug.Notification("has items")
                EndIf
            EndEvent
            """);


        Add("11_VariableRoundTrip", graph =>
        {
            graph.Variable("Counter", "int");
            var entry = graph.Node(palette, "event:ObjectReference.OnLoad");
            var set = graph.Node(palette, BuiltinNodeDefinitions.VariableSet, ("name", "Counter"));
            var literal = graph.Node(palette, "literal.int");
            var trace = graph.Node(palette, "global:Debug.Trace");
            var get = graph.Node(palette, BuiltinNodeDefinitions.VariableGet, ("name", "Counter"));

            graph.Value(literal, PinIds.Value, "int", "7");
            graph.Wire(entry, PinIds.Exec, set, PinIds.Exec);
            graph.Wire(literal, PinIds.Value, set, PinIds.Value);
            graph.Wire(set, PinIds.Then, trace, PinIds.Exec);
            graph.Wire(get, PinIds.Value, trace, "arg:asTextToPrint");
            return graph.Document;
        }, """
            Scriptname Fixture extends ObjectReference

            int Counter

            Event OnLoad()
                Counter = 7
                Debug.Trace(Counter)
            EndEvent
            """);


        Add("12_AutoProperty", graph =>
        {
            graph.Variable("Target", "ObjectReference", isProperty: true);
            var entry = graph.Node(palette, "event:ObjectReference.OnLoad");
            var notify = graph.Node(palette, "global:Debug.Notification");
            graph.Value(notify, "arg:asNotificationText", "string", "\"ready\"");
            graph.Wire(entry, PinIds.Exec, notify, PinIds.Exec);
            return graph.Document;
        }, """
            Scriptname Fixture extends ObjectReference

            ObjectReference Property Target Auto

            Event OnLoad()
                Debug.Notification("ready")
            EndEvent
            """);


        Add("13_ReceiverFromPin", graph =>
        {
            var entry = graph.Node(palette, "event:ObjectReference.OnLoad");
            var player = graph.Node(palette, "global:Game.GetPlayer");
            var level = graph.Node(palette, "call:Actor.GetLevel");
            var trace = graph.Node(palette, "global:Debug.Trace");

            graph.Wire(entry, PinIds.Exec, player, PinIds.Exec);
            graph.Wire(player, PinIds.Then, level, PinIds.Exec);
            graph.Wire(level, PinIds.Then, trace, PinIds.Exec);
            graph.Wire(player, PinIds.Return, level, PinIds.Self);
            graph.Wire(level, PinIds.Return, trace, "arg:asTextToPrint");
            return graph.Document;
        }, """
            Scriptname Fixture extends ObjectReference

            Event OnLoad()
                Actor player
                int level
                player = Game.GetPlayer()
                level = player.GetLevel()
                Debug.Trace(level)
            EndEvent
            """);


        Add("14_InheritedCallNoReceiver", graph =>
        {
            var entry = graph.Node(palette, "event:ObjectReference.OnLoad");
            var disable = graph.Node(palette, "call:ObjectReference.Disable");
            graph.Wire(entry, PinIds.Exec, disable, PinIds.Exec);
            return graph.Document;
        }, """
            Scriptname Fixture extends ObjectReference

            Event OnLoad()
                Disable()
            EndEvent
            """);


        Add("15_ExplicitCast", graph =>
        {
            var entry = graph.Node(palette, "event:ObjectReference.OnActivate");
            var cast = graph.Node(palette, BuiltinNodeDefinitions.Cast, ("type", "Actor"));
            var level = graph.Node(palette, "call:Actor.GetLevel");
            var trace = graph.Node(palette, "global:Debug.Trace");

            graph.Wire(entry, PinIds.Exec, level, PinIds.Exec);
            graph.Wire(entry, "param:akActionRef", cast, PinIds.Value);
            graph.Wire(cast, PinIds.Return, level, PinIds.Self);
            graph.Wire(level, PinIds.Then, trace, PinIds.Exec);
            graph.Wire(level, PinIds.Return, trace, "arg:asTextToPrint");
            return graph.Document;
        }, """
            Scriptname Fixture extends ObjectReference

            Event OnActivate(ObjectReference akActionRef)
                int level
                level = (akActionRef as Actor).GetLevel()
                Debug.Trace(level)
            EndEvent
            """);


        Add("16_NotOperator", graph =>
        {
            var entry = graph.Node(palette, "event:ObjectReference.OnLoad");
            var enabled = graph.Node(palette, "call:ObjectReference.IsEnabled");
            var not = graph.Node(palette, "op.not");
            var branch = graph.Node(palette, BuiltinNodeDefinitions.Branch);
            var enable = graph.Node(palette, "call:ObjectReference.Enable");

            graph.Wire(entry, PinIds.Exec, enabled, PinIds.Exec);
            graph.Wire(enabled, PinIds.Then, branch, PinIds.Exec);
            graph.Wire(enabled, PinIds.Return, not, PinIds.Value);
            graph.Wire(not, PinIds.Return, branch, PinIds.Condition);
            graph.Wire(branch, PinIds.Then, enable, PinIds.Exec);
            return graph.Document;
        }, """
            Scriptname Fixture extends ObjectReference

            Event OnLoad()
                bool enabled
                enabled = IsEnabled()
                If (!enabled)
                    Enable()
                EndIf
            EndEvent
            """);


        Add("17_ShortCircuit", graph =>
        {
            var entry = graph.Node(palette, "event:ObjectReference.OnLoad");
            var enabled = graph.Node(palette, "call:ObjectReference.IsEnabled");
            var count = graph.Node(palette, "call:ObjectReference.GetItemCount");
            var compare = graph.Node(palette, "op.gt");
            var zero = graph.Node(palette, "literal.int");
            var and = graph.Node(palette, "op.and");
            var branch = graph.Node(palette, BuiltinNodeDefinitions.Branch);
            var disable = graph.Node(palette, "call:ObjectReference.Disable");

            graph.Value(zero, PinIds.Value, "int", "0");
            graph.Wire(entry, PinIds.Exec, enabled, PinIds.Exec);
            graph.Wire(enabled, PinIds.Then, count, PinIds.Exec);
            graph.Wire(count, PinIds.Then, branch, PinIds.Exec);
            graph.Wire(count, PinIds.Return, compare, PinIds.Left);
            graph.Wire(zero, PinIds.Value, compare, PinIds.Right);
            graph.Wire(enabled, PinIds.Return, and, PinIds.Left);
            graph.Wire(compare, PinIds.Return, and, PinIds.Right);
            graph.Wire(and, PinIds.Return, branch, PinIds.Condition);
            graph.Wire(branch, PinIds.Then, disable, PinIds.Exec);
            return graph.Document;
        }, """
            Scriptname Fixture extends ObjectReference

            Event OnLoad()
                bool enabled
                int itemCount
                enabled = IsEnabled()
                itemCount = GetItemCount(None)
                If (enabled && itemCount > 0)
                    Disable()
                EndIf
            EndEvent
            """);


        Add("18_FunctionReturnsOnBothArms", graph =>
        {
            var entry = graph.Node(palette, BuiltinNodeDefinitions.FunctionEntry,
                ("name", "Score"), ("returns", "int"));
            var enabled = graph.Node(palette, "call:ObjectReference.IsEnabled");
            var branch = graph.Node(palette, BuiltinNodeDefinitions.Branch);
            var yes = graph.Node(palette, BuiltinNodeDefinitions.Return);
            var no = graph.Node(palette, BuiltinNodeDefinitions.Return);
            var one = graph.Node(palette, "literal.int");
            var zero = graph.Node(palette, "literal.int");

            graph.Value(one, PinIds.Value, "int", "1");
            graph.Value(zero, PinIds.Value, "int", "0");
            graph.Wire(entry, PinIds.Exec, enabled, PinIds.Exec);
            graph.Wire(enabled, PinIds.Then, branch, PinIds.Exec);
            graph.Wire(enabled, PinIds.Return, branch, PinIds.Condition);
            graph.Wire(branch, PinIds.Then, yes, PinIds.Exec);
            graph.Wire(branch, PinIds.Else, no, PinIds.Exec);
            graph.Wire(one, PinIds.Value, yes, PinIds.Value);
            graph.Wire(zero, PinIds.Value, no, PinIds.Value);
            return graph.Document;
        }, """
            Scriptname Fixture extends ObjectReference

            int Function Score()
                bool enabled
                enabled = IsEnabled()
                If (enabled)
                    Return 1
                Else
                    Return 0
                EndIf
            EndFunction
            """);


        Add("19_VoidFunction", graph =>
        {
            var entry = graph.Node(palette, BuiltinNodeDefinitions.FunctionEntry, ("name", "Reset"));
            var disable = graph.Node(palette, "call:ObjectReference.Disable");
            var enable = graph.Node(palette, "call:ObjectReference.Enable");
            graph.Wire(entry, PinIds.Exec, disable, PinIds.Exec);
            graph.Wire(disable, PinIds.Then, enable, PinIds.Exec);
            return graph.Document;
        }, """
            Scriptname Fixture extends ObjectReference

            Function Reset()
                Disable()
                Enable()
            EndFunction
            """);


        Add("20_TwoEntries", graph =>
        {
            var load = graph.Node(palette, "event:ObjectReference.OnLoad");
            var unload = graph.Node(palette, "event:ObjectReference.OnUnload");
            var first = graph.Node(palette, "global:Debug.Notification");
            var second = graph.Node(palette, "global:Debug.Notification");

            graph.Value(first, "arg:asNotificationText", "string", "\"in\"");
            graph.Value(second, "arg:asNotificationText", "string", "\"out\"");
            graph.Wire(load, PinIds.Exec, first, PinIds.Exec);
            graph.Wire(unload, PinIds.Exec, second, PinIds.Exec);
            return graph.Document;
        }, """
            Scriptname Fixture extends ObjectReference

            Event OnLoad()
                Debug.Notification("in")
            EndEvent

            Event OnUnload()
                Debug.Notification("out")
            EndEvent
            """);


        Add("21_SelfAsArgument", graph =>
        {
            var entry = graph.Node(palette, "event:ObjectReference.OnLoad");
            var self = graph.Node(palette, BuiltinNodeDefinitions.Self);
            var activate = graph.Node(palette, "call:ObjectReference.Activate");
            graph.Wire(entry, PinIds.Exec, activate, PinIds.Exec);
            graph.Wire(self, PinIds.Value, activate, "arg:akActivator");
            return graph.Document;
        }, """
            Scriptname Fixture extends ObjectReference

            Event OnLoad()
                Activate(Self)
            EndEvent
            """);


        fixtures.Add(new GraphFixture("22_GlobalUtilityScript", () =>
        {
            var graph = new GraphBuilder("Fixture");
            var entry = graph.Node(palette, BuiltinNodeDefinitions.FunctionEntry,
                ("name", "Twice"), ("returns", "int"), ("global", "true"));
            var ret = graph.Node(palette, BuiltinNodeDefinitions.Return);
            var literal = graph.Node(palette, "literal.int");

            graph.Value(literal, PinIds.Value, "int", "84");
            graph.Wire(entry, PinIds.Exec, ret, PinIds.Exec);
            graph.Wire(literal, PinIds.Value, ret, PinIds.Value);
            return graph.Document;
        }, """
            Scriptname Fixture

            int Function Twice() global
                Return 84
            EndFunction
            """));


        Add("23_NestedBranch", graph =>
        {
            var entry = graph.Node(palette, "event:ObjectReference.OnLoad");
            var outer = graph.Node(palette, "call:ObjectReference.IsEnabled");
            var branch = graph.Node(palette, BuiltinNodeDefinitions.Branch);
            var inner = graph.Node(palette, "call:ObjectReference.GetItemCount");
            var innerCompare = graph.Node(palette, "op.gt");
            var zero = graph.Node(palette, "literal.int");
            var innerBranch = graph.Node(palette, BuiltinNodeDefinitions.Branch);
            var deep = graph.Node(palette, "call:ObjectReference.Disable");

            graph.Value(zero, PinIds.Value, "int", "0");
            graph.Wire(entry, PinIds.Exec, outer, PinIds.Exec);
            graph.Wire(outer, PinIds.Then, branch, PinIds.Exec);
            graph.Wire(outer, PinIds.Return, branch, PinIds.Condition);
            graph.Wire(branch, PinIds.Then, inner, PinIds.Exec);
            graph.Wire(inner, PinIds.Then, innerBranch, PinIds.Exec);
            graph.Wire(inner, PinIds.Return, innerCompare, PinIds.Left);
            graph.Wire(zero, PinIds.Value, innerCompare, PinIds.Right);
            graph.Wire(innerCompare, PinIds.Return, innerBranch, PinIds.Condition);
            graph.Wire(innerBranch, PinIds.Then, deep, PinIds.Exec);
            return graph.Document;
        }, """
            Scriptname Fixture extends ObjectReference

            Event OnLoad()
                bool enabled
                int itemCount
                enabled = IsEnabled()
                If (enabled)
                    itemCount = GetItemCount(None)
                    If (itemCount > 0)
                        Disable()
                    EndIf
                EndIf
            EndEvent
            """);


        Add("24_HexFormId", graph =>
        {
            var entry = graph.Node(palette, "event:ObjectReference.OnLoad");
            var getForm = graph.Node(palette, "global:Game.GetFormFromFile");
            var add = graph.Node(palette, "call:ObjectReference.AddItem");

            graph.Value(getForm, "arg:aiFormID", "int", "0x0000000F");
            graph.Value(getForm, "arg:asFilename", "string", "\"Fallout4.esm\"");
            graph.Wire(entry, PinIds.Exec, getForm, PinIds.Exec);
            graph.Wire(getForm, PinIds.Then, add, PinIds.Exec);
            graph.Wire(getForm, PinIds.Return, add, "arg:akItemToAdd");
            return graph.Document;
        }, """
            Scriptname Fixture extends ObjectReference

            Event OnLoad()
                Form formFromFile
                formFromFile = Game.GetFormFromFile(0x0000000F, "Fallout4.esm")
                AddItem(formFromFile)
            EndEvent
            """);



        Add("25_AutoStateMachine", graph =>
        {
            graph.AutoState("Waiting");

            var waiting = graph.Node(palette, "event:ObjectReference.OnActivate", ("state", "Waiting"));
            var goTo = graph.Node(palette, "call:ScriptObject.GotoState");
            var starting = graph.Node(palette, "global:Debug.Notification");

            graph.Value(goTo, "arg:asNewState", "string", "\"Busy\"");
            graph.Value(starting, "arg:asNotificationText", "string", "\"starting\"");
            graph.Wire(waiting, PinIds.Exec, goTo, PinIds.Exec);
            graph.Wire(goTo, PinIds.Then, starting, PinIds.Exec);

            var busy = graph.Node(palette, "event:ObjectReference.OnActivate", ("state", "Busy"));
            var already = graph.Node(palette, "global:Debug.Notification");

            graph.Value(already, "arg:asNotificationText", "string", "\"busy\"");
            graph.Wire(busy, PinIds.Exec, already, PinIds.Exec);
            return graph.Document;
        }, """
            Scriptname Fixture extends ObjectReference

            Auto State Waiting
                Event OnActivate(ObjectReference akActionRef)
                    GotoState("Busy")
                    Debug.Notification("starting")
                EndEvent
            EndState

            State Busy
                Event OnActivate(ObjectReference akActionRef)
                    Debug.Notification("busy")
                EndEvent
            EndState
            """);




        Add("26_RemoteAndCustomEvents", graph =>
        {
            graph.CustomEvent("Ready");

            var remote = graph.Node(palette, NodePalette.RemoteEventId("ObjectReference", "OnLoad"));
            var loaded = graph.Node(palette, "global:Debug.Notification");
            graph.Value(loaded, "arg:asNotificationText", "string", "\"loaded\"");
            graph.Wire(remote, PinIds.Exec, loaded, PinIds.Exec);

            var custom = graph.Node(palette, NodePalette.RemoteEventId("FixtureEventSource", "AffinityChanged"));
            var affinity = graph.Node(palette, "global:Debug.Notification");
            graph.Value(affinity, "arg:asNotificationText", "string", "\"affinity\"");
            graph.Wire(custom, PinIds.Exec, affinity, PinIds.Exec);
            return graph.Document;
        }, """
            Scriptname Fixture extends ObjectReference

            CustomEvent Ready

            Event ObjectReference.OnLoad(ObjectReference akSender)
                Debug.Notification("loaded")
            EndEvent

            Event FixtureEventSource.AffinityChanged(FixtureEventSource akSender, Var[] akArgs)
                Debug.Notification("affinity")
            EndEvent
            """);


        Add("27_BreakLeavesTheLoop", graph =>
        {
            var entry = graph.Node(palette, "event:ObjectReference.OnLoad");
            var enabled = graph.Node(palette, "call:ObjectReference.IsEnabled");
            var loop = graph.Node(palette, BuiltinNodeDefinitions.While);
            var first = graph.Node(palette, "global:Debug.Notification");
            var branch = graph.Node(palette, BuiltinNodeDefinitions.Branch);
            var leave = graph.Node(palette, BuiltinNodeDefinitions.Break);
            var rest = graph.Node(palette, "global:Debug.Notification");
            var after = graph.Node(palette, "global:Debug.Notification");

            graph.Value(first, "arg:asNotificationText", "string", "\"A\"");
            graph.Value(rest, "arg:asNotificationText", "string", "\"B\"");
            graph.Value(after, "arg:asNotificationText", "string", "\"after\"");

            graph.Wire(entry, PinIds.Exec, enabled, PinIds.Exec);
            graph.Wire(enabled, PinIds.Then, loop, PinIds.Exec);
            graph.Wire(enabled, PinIds.Return, loop, PinIds.Condition);
            graph.Wire(loop, PinIds.Body, first, PinIds.Exec);
            graph.Wire(first, PinIds.Then, branch, PinIds.Exec);
            graph.Wire(enabled, PinIds.Return, branch, PinIds.Condition);
            graph.Wire(branch, PinIds.Then, leave, PinIds.Exec);
            graph.Wire(branch, PinIds.Else, rest, PinIds.Exec);
            graph.Wire(rest, PinIds.Then, loop, PinIds.Exec);
            graph.Wire(loop, PinIds.Completed, after, PinIds.Exec);
            return graph.Document;
        }, """
            Scriptname Fixture extends ObjectReference

            Event OnLoad()
                bool enabled
                bool broke
                enabled = IsEnabled()
                broke = false
                While (enabled && !broke)
                    Debug.Notification("A")
                    If (enabled)
                        broke = true
                    Else
                        Debug.Notification("B")
                    EndIf
                EndWhile
                Debug.Notification("after")
            EndEvent
            """);



        Add("28_ContinueSkipsThePass", graph =>
        {
            graph.Variable("Items", "Form[]", isProperty: true);

            var entry = graph.Node(palette, "event:ObjectReference.OnLoad");
            var enabled = graph.Node(palette, "call:ObjectReference.IsEnabled");
            var loop = graph.Node(palette, BuiltinNodeDefinitions.ForEach);
            var items = graph.Node(palette, BuiltinNodeDefinitions.VariableGet, ("name", "Items"));
            var branch = graph.Node(palette, BuiltinNodeDefinitions.Branch);
            var skip = graph.Node(palette, BuiltinNodeDefinitions.Continue);
            var work = graph.Node(palette, "global:Debug.Notification");

            graph.Value(work, "arg:asNotificationText", "string", "\"work\"");

            graph.Wire(entry, PinIds.Exec, enabled, PinIds.Exec);
            graph.Wire(enabled, PinIds.Then, loop, PinIds.Exec);
            graph.Wire(items, PinIds.Value, loop, PinIds.Array);
            graph.Wire(loop, PinIds.Body, branch, PinIds.Exec);
            graph.Wire(enabled, PinIds.Return, branch, PinIds.Condition);
            graph.Wire(branch, PinIds.Then, skip, PinIds.Exec);
            graph.Wire(branch, PinIds.Else, work, PinIds.Exec);
            return graph.Document;
        }, """
            Scriptname Fixture extends ObjectReference

            Form[] Property Items Auto

            Event OnLoad()
                bool enabled
                Form[] items
                int index
                Form item
                enabled = IsEnabled()
                items = Items
                index = 0
                While (index < items.Length)
                    item = items[index]
                    If (!enabled)
                        Debug.Notification("work")
                    EndIf
                    index = index + 1
                EndWhile
            EndEvent
            """);

        return fixtures;
    }
}
