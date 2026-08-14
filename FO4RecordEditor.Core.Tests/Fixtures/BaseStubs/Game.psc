; Reduced stand-in for the Fallout 4 base script of the same name.
; Declarations only, hand authored: no bodies and no documentation from the shipped sources.
; Exists so the graph test gate can compile against a known member set on a bare checkout.
; BaseStubFidelityTests checks these signatures against the real tree when FO4RE_PSC_ROOTS is set.

Scriptname Game Native Hidden

Actor Function GetPlayer() native global
Form Function GetFormFromFile(int aiFormID, string asFilename) native global
Form Function GetForm(int aiFormID) native global
Function ShowFirstPersonGeometry(bool abShow = true) native global
int Function GetGameSettingInt(string asGameSetting) native global
float Function GetGameSettingFloat(string asGameSetting) native global
