; Reduced stand-in for the Fallout 4 base script of the same name.
; Declarations only, hand authored: no bodies and no documentation from the shipped sources.
; Exists so the graph test gate can compile against a known member set on a bare checkout.
; BaseStubFidelityTests checks these signatures against the real tree when FO4RE_PSC_ROOTS is set.

Scriptname Actor extends ObjectReference Native Hidden

bool Function IsDead() native
int Function GetLevel() native
bool Function IsInCombat() native
ActorBase Function GetActorBase() native
Function EquipItem(Form akItem, bool abPreventRemoval = false, bool abSilent = false) native
Function UnequipItem(Form akItem, bool abPreventEquip = false, bool abSilent = false) native
Function AddPerk(Perk akPerk, bool abNotify = false) native
bool Function HasPerk(Perk akPerk) native
Function Kill(Actor akKiller = None) native
bool Function IsPlayerTeammate() native
Function StartCombat(Actor akTarget, bool abPreferredTarget = false) native
Function StopCombat() native
bool Function IsSneaking() native
