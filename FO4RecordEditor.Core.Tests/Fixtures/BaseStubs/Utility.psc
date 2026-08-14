; Reduced stand-in for the Fallout 4 base script of the same name.
; Declarations only, hand authored: no bodies and no documentation from the shipped sources.
; Exists so the graph test gate can compile against a known member set on a bare checkout.
; BaseStubFidelityTests checks these signatures against the real tree when FO4RE_PSC_ROOTS is set.

Scriptname Utility Native Hidden

Function Wait(float afSeconds) native global
Function WaitMenuMode(float afSeconds) native global
Function WaitGameTime(float afHours) native global
int Function RandomInt(int aiMin = 0, int aiMax = 100) native global
float Function RandomFloat(float afMin = 0.0, float afMax = 1.0) native global
float Function GetCurrentGameTime() native global
float Function GetCurrentRealTime() native global
