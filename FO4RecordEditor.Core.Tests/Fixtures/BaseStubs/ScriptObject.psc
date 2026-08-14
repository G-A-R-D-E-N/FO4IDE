




Scriptname ScriptObject Native Hidden

Function GotoState(string asNewState) native
string Function GetState() native
ScriptObject Function CastAs(string asScriptName) native
Var Function CallFunction(string asFuncName, Var[] aParams) native
Function CallFunctionNoWait(string asFuncName, Var[] aParams) native
Var Function GetPropertyValue(string asPropertyName) native
bool Function IsBoundGameObjectAvailable() native
Function StartTimer(float afInterval, int aiTimerID = 0) native
Function CancelTimer(int aiTimerID = 0) native
Function RegisterForCustomEvent(ScriptObject akSender, CustomEventName asEventName) native
Function UnregisterForCustomEvent(ScriptObject akSender, CustomEventName asEventName) native
Function SendCustomEvent(CustomEventName asEvent, Var[] akArgs = None) native
bool Function RegisterForRemoteEvent(ScriptObject akEventSource, ScriptEventName asEventName) native
Function UnregisterForRemoteEvent(ScriptObject akEventSource, ScriptEventName asEventName) native
bool Function RegisterForAnimationEvent(ObjectReference akSender, string asEventName) native
Function RegisterForHitEvent(ScriptObject akTarget, ScriptObject akAggressorFilter = None, Form akSourceFilter = None, Form akProjectileFilter = None, int aiPowerFilter = -1, int aiSneakFilter = -1, int aiBashFilter = -1, int aiBlockFilter = -1, bool abMatch = true) native

Event OnInit()
EndEvent

Event OnTimer(int aiTimerID)
EndEvent

Event OnBeginState(string asOldState)
EndEvent

Event OnEndState(string asNewState)
EndEvent
