; Reduced stand-in for the Fallout 4 base script of the same name.
; Declarations only, hand authored: no bodies and no documentation from the shipped sources.
; Exists so the graph test gate can compile against a known member set on a bare checkout.
; BaseStubFidelityTests checks these signatures against the real tree when FO4RE_PSC_ROOTS is set.

Scriptname Form extends ScriptObject Native Hidden

int Function GetFormID() native
bool Function HasKeyword(Keyword akKeyword) native
bool Function HasKeywordInFormList(FormList akKeywordList) native
bool Function PlayerKnows() native
int Function GetGoldValue() native
