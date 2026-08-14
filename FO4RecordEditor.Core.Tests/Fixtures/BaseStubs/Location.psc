; Reduced stand-in for the Fallout 4 base script of the same name.
; Declarations only, hand authored: no bodies and no documentation from the shipped sources.
; Exists so the graph test gate can compile against a known member set on a bare checkout.
; BaseStubFidelityTests checks these signatures against the real tree when FO4RE_PSC_ROOTS is set.

Scriptname Location extends Form Native Hidden

bool Function IsChild(Location akOther) native
bool Function IsCleared() native
bool Function HasCommonParent(Location akOther, Keyword akFilter = None) native
