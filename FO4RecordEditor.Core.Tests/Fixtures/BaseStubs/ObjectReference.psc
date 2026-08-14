




Scriptname ObjectReference extends Form Native Hidden

bool Function Activate(ObjectReference akActivator, bool abDefaultProcessingOnly = false) native
Function AddItem(Form akItemToAdd, int aiCount = 1, bool abSilent = false) native
Function RemoveItem(Form akItemToRemove, int aiCount = 1, bool abSilent = false, ObjectReference akOtherContainer = None) native
int Function GetItemCount(Form akItem = None) native
Form Function GetBaseObject() native
ObjectReference Function GetLinkedRef(Keyword apKeyword = None) native
Function SetLinkedRef(ObjectReference akLinkedRef, Keyword apKeyword = None) native
Function Disable(bool abFadeOut = false) native
Function Enable(bool abFadeIn = false) native
bool Function IsEnabled() native
Function Delete() native
float Function GetDistance(ObjectReference akOther) native
Function MoveTo(ObjectReference akTarget, float afXOffset = 0.0, float afYOffset = 0.0, float afZOffset = 0.0, bool abMatchRotation = true) native
float Function GetPositionX() native
float Function GetPositionY() native
float Function GetPositionZ() native
Cell Function GetParentCell() native
Function SetOpen(bool abOpen = true) native
bool Function PlayAnimation(string asAnimation) native
Function SetMotionType(int aeMotionType, bool abAllowActivate = true) native
Function SetValue(ActorValue akAV, float afValue) native
float Function GetValue(ActorValue akAV) native
Function DamageValue(ActorValue akAV, float afDamage) native
Function SetPosition(float afX, float afY, float afZ) native
Function SetAngle(float afXAngle, float afYAngle, float afZAngle) native
bool Function PlayGamebryoAnimation(string asAnimation, bool abStartOver = false, float afEaseInTime = 0.0) native
Function SetScale(float afScale) native
float Function GetScale() native

Event OnActivate(ObjectReference akActionRef)
EndEvent

Event OnLoad()
EndEvent

Event OnUnload()
EndEvent

Event OnOpen(ObjectReference akActionRef)
EndEvent

Event OnClose(ObjectReference akActionRef)
EndEvent

Event OnTriggerEnter(ObjectReference akActionRef)
EndEvent

Event OnTriggerLeave(ObjectReference akActionRef)
EndEvent

Event OnEquipped(Actor akActor)
EndEvent

Event OnUnequipped(Actor akActor)
EndEvent

Event OnPowerOn(ObjectReference akPowerGenerator)
EndEvent

Event OnPowerOff()
EndEvent

Event OnDestructionStageChanged(int aiOldStage, int aiCurrentStage)
EndEvent

