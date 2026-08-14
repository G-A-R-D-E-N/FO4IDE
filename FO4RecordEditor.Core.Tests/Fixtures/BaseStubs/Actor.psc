




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
