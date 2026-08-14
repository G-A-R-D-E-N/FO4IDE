; Reduced stand-in for the Fallout 4 base script of the same name.
; Declarations only, hand authored: no bodies and no documentation from the shipped sources.
; Exists so the graph test gate can compile against a known member set on a bare checkout.
; BaseStubFidelityTests checks these signatures against the real tree when FO4RE_PSC_ROOTS is set.

Scriptname FormList extends Form Native Hidden

int Function GetSize() native
Form Function GetAt(int aiIndex) native
bool Function HasForm(Form akForm) native
Function AddForm(Form apForm) native
Function RemoveAddedForm(Form apForm) native
