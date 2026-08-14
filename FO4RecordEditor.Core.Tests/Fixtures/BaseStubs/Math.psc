; Reduced stand-in for the Fallout 4 base script of the same name.
; Declarations only, hand authored: no bodies and no documentation from the shipped sources.
; Exists so the graph test gate can compile against a known member set on a bare checkout.
; BaseStubFidelityTests checks these signatures against the real tree when FO4RE_PSC_ROOTS is set.

Scriptname Math Native Hidden

float Function abs(float afValue) global native
int Function Floor(float afValue) global native
int Function Ceiling(float afValue) global native
float Function sqrt(float afValue) global native
float Function pow(float x, float y) global native
float Function Min(float afValue1, float afValue2) global native
float Function Max(float afValue1, float afValue2) global native
