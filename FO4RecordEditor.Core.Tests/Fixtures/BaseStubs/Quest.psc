; Reduced stand-in for the Fallout 4 base script of the same name.
; Declarations only, hand authored: no bodies and no documentation from the shipped sources.
; Exists so the graph test gate can compile against a known member set on a bare checkout.
; BaseStubFidelityTests checks these signatures against the real tree when FO4RE_PSC_ROOTS is set.

Scriptname Quest extends Form Native Hidden

int Function GetStage() native
bool Function SetStage(int aiStage) native
bool Function IsRunning() native
bool Function Start() native
Function Stop() native
Alias Function GetAlias(int aiAliasID) native
bool Function GetStageDone(int aiStage) native
bool Function IsObjectiveCompleted(int aiObjective) native
Function SetObjectiveCompleted(int aiObjective, bool abCompleted = true) native
Function SetObjectiveDisplayed(int aiObjective, bool abDisplayed = true, bool abForce = false) native
