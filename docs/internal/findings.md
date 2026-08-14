# Branch feat/schematic-dedupe — Full Session Findings

## FO4RecordEditor Tool Fixes

### fix: type-collision guard in check_plugin + copy_as_override [5f53aa3c]
- `WriteService.ResolveConflict`: fetch BASE record from originating master via `GetRecordVersion` before creating override; refuse if types differ. Prior code used `TryResolve` (winning record) — if bad override already existed, guard silently passed.
- `MutagenLoader.CheckPlugin`: scan every foreign-mastered record vs master-plugin version; report `TYPE COLLISION` for COBJ-over-PERK / COBJ-over-ARMO mismatches.
- Root cause: FC_S7.esp had 7 COBJ records at 800000–800009 (post-renumber FormID mismatch); were COBJ-over-PERK collisions. Deleted + recreated at correct IDs 8005F6–8005FC with same S7 conditions. `check_plugin` → 0 issues.

### fix: deploy path correction + KNOWLEDGE.md launch exe doc [ef3ef548]
- `publish.bat` was deploying to `D:\Games\ModlistDownloads\Tools\FO4Editor` (broken path); fixed to `E:\Modlists\Fallen World Alpha 2\Tools\FO4Editor`
- KNOWLEDGE.md: documented canonical launch exe (`bin\Release\net9.0-windows\FO4RecordEditor.exe`); listed stale win-x64 builds to never launch

### fix: scan_broken_refs false positive [3d2e3779]
- Added loaded-plugin guard: skip refs to plugins not present in MO2 env
- Renamed "Missing masters:" label → "Broken refs by target plugin:"

---

## FallenWorldCrafting_Compat.esp

### fix: 216 phantom COBJ overrides deleted [b9edb101]
- Eli/Mercenary/CROSS/HotC COBJ overrides had unresolvable CNAM (created object FormKey pointed to deleted/renamed records)
- Root cause: these mods removed or renamed base records after FC_Compat was generated
- Deleted all 216 phantoms; confirmed 0 broken refs after

---

## FW_SkillGates.esp

### fix: 157 CO_Scrap overrides deleted [c2af64e0]
- `CO_Scrap_*` overrides used solo `UseOr` bypass with no following condition; when immersive mode ON this blocked all scrapping
- Scrap recipes need no gate — deleted overrides, revert to base/Compat version

### fix: 5,769 RunOn=Reference null-ref conditions [c2af64e0]
- `GetBaseValue` conditions had `RunOn=Reference` with null FK; S7 skill gate never evaluated against player
- Fixed all 5,769 → `RunOn=Subject`; gates now correctly evaluate at the workbench against the player character

### fix: 2 Groza.esp COBJ overrides with broken internal refs deleted [3d2e3779]
- Groza.esp was removed from modlist; 2 COBJ overrides in FW_SkillGates had broken internal FormID refs
- Deleted both; 0 broken refs remaining

### fix: 42 missing ammo schematic conditions [25a25966]
- Full audit of 8,941 COBJ records; discovered two-tier Unlock_ system:
  - FallenWorldCrafting.esp: 170 category-level perks
  - FallenWorldCrafting_Compat.esp: 840 individual per-recipe perks
- 42 ammo COBJs missing `HasPerk(Unlock_Unlockable_Ammo_<Caliber>_Perk)` conditions
- Fixed categories: DH/DX/DE .38 ammo/breakdowns/casings → SmallCal(82C); nuka-nuke → Explosive(831); small rifle primers → RifleCal(82D); large rifle primers → HeavyCal(82E); HandmadeSaiga → Shotgun(82F); HandmadeSaiga_Expl → Explosive(831)
- `C_research_*` (840 records): produce schematic books — correctly ungated (would be circular)
- `HT_co` throwables: intentionally ungated (design intent, only 2/27 gated and those use non-ammo schematics)
- Mutagen condition clone pattern (DeepCopy is explicit interface, inaccessible via dynamic):
  ```csharp
  var memberwiseClone = typeof(object).GetMethod("MemberwiseClone", BindingFlags.NonPublic|BindingFlags.Instance);
  dynamic CloneCondition(dynamic template) {
      var cc = memberwiseClone.Invoke((object)template, null);
      var dc = memberwiseClone.Invoke((object)template.Data, null);
      cc.GetType().GetProperty("Data").SetValue(cc, dc);
      return cc;
  }
  ```

### fix: 6 CWServRifleMarksmanCarbineAddon COBJ overrides deleted [1c22b025]
- M16 muzzle refs were broken (CWServRifle removed from modlist); 6 broken refs → 0

---

## FW_FinalGating.esp

### fix: 1,397 duplicate HasPerk conditions removed [1c22b025]
- 1,169/1,170 records had duplicate HasPerk conditions (identical FormKey appended twice at end of condition list)
- Merge artifact from session 10 when FinalGating was created; each duplicate was a verbatim copy
- Removed all 1,397 duplicates via run_script

### fix: 00UWCOBullpupCraft missing schematic [this session]
- FW_FinalGating.esp (LO 687) overwrites 1,162 FW_SkillGates.esp (LO 686) records completely
- Overwrite conflict check: 2,752 schematic-gated records cross-ref'd against all 1,170 FinalGating records
- 1 gap found: `00UWCOBullpupCraft` [00021E:PigsWastelandWeaponry.esl] missing `HasPerk(Unlock_00UWCOBullpupCraft_Perk)` from FC_Compat
- Fixed + saved

### fix: 92 type-collision COBJ overrides deleted [this session]
- check_plugin revealed 92 COBJ records at FormIDs that are no longer COBJ records in their source plugins
- Same root cause as the 6 CWServRifle overrides deleted from FW_SkillGates.esp [1c22b025]: stale FinalGating snapshot based on older mod versions with repurposed FormIDs
- Deleted iteratively via check_plugin → dry-run script enumerate → delete_record batch until 0 collisions
- Source breakdown: CWServRifle.esp(35), Backpacks(10), T6M_Abzats(11), Dank_LEO(15), DakFAL(3), DakLindaPistol(4), CombinedArmsNV(9), FWC_NonFWC_Schematics(1), VGThotel(3), Dak127mmPistol(1)
- Final: 1,078 records, 0 deleted, 0 undeclared masters, 0 type collisions

---

## FallenWorldCrafting.esp

### feat: 59 individual per-weapon schematics [afe0a36c]
- Supersedes 7-category weapon consolidation
- Repurposed 59 deleted perk records as `Unlock_Unlockable_Weapon_{Key}_Perk`
- Repurposed 59 deleted book records with Name + Teaches wired per-weapon
- Rewired 111 weapon COBJs in FW_SkillGates.esp to individual perks
- Ungated 16 basic weapon COBJs via DeleteOverride (board, pool cue, tire iron, pipe guns, molotov)
- Deleted 7 consolidated category perks (00083A–000840) + 7 books
- Cleaned 2 stale dual-consolidated refs (harpoongun, cryolator)

### fix: FC_S7 null-AV params [9cd34e7c]
- Mutagen writes GetBaseValue custom-AV params as null; fixed in FO4RecordEditor save path (MasterIsEsl + ParameterOneNumber encoding)
- Verify in xEdit not get_record (xEdit shows resolved values; get_record shows raw)

### fix: COBJ category assignment errors [fb9de13d]
- Several COBJs had wrong category perks; direct-edit corrected category assignments

### delete: wSW_SMR_Universal FURN + wSW_SMR_CraftingKey KYWRD [this session]
- Orphaned records; no COBJ referenced them; model was Workbench_Invisible.nif
- get_referenced_by confirmed only self-reference (KYWRD referenced by its own FURN)
- Safe to delete; removed both from FallenWorldCrafting.esp + saved

---

## IAF (Immersive Animation Framework) — Vanilla Food/Drink Animations

### fix: 18 Fallout4.esm RobCo patcher configs created [this session]
- Root cause: IAF ESP-less Patches shackleberry folder had NO `Fallout4.esm.ini` in any category
- All vanilla food/drink/chem had zero IAF keywords → engine fell back to stim animation on menu-close (Wheel Menu Remastered defers all IAF to after menu close, making that the only animation seen)
- Stimpak (023736) already covered by MAIM Redux with IAF_FakeChemKeyword [819]; skipped to avoid dual-trigger
- Created `Fallout4.esm.ini` in all 18 applicable shackleberry categories:
  - `nuka` [826]: NukaCola, NukaColaQuantum, NukaColaCherry + cold variants (6 items)
  - `beer` [827]: Gwinnett line, BeerBottleStandard01 + cold variants (14 items)
  - `bourbon` [828]: bourbon, MoonshineBobrov, DirtyWastelander (3 items)
  - `rum` [829]: Rum (1 item)
  - `vodka` [82A]: Vodka (1 item)
  - `whiskey` [82B]: Whiskey (1 item)
  - `wine` [82C]: Wine + 2 unique dungeon wines (3 items)
  - `water` [93F]: WaterDirty, WaterPurified, WaterInstitute, HC sippable variants, RefreshingBeverage, DeezerLemonade (7 items)
  - `stimpak` [821]: RadAway, MedX, Psycho, Bloodpack, BloodpackGlowing, CurieHealthpak (6 items)
  - `pill` [81D]: Mentats + variants, Buffout, Addictol, RadX, XCell, DaddyO, DayTripper, Fury, chem combos (16 items)
  - `jet` [81B]: Jet, UltraJet, JetFuel, Buffjet, PsychoJet (5 items)
  - `sweetroll` [82E]: SweetRoll, SweetRollBirthday (2 items)
  - `soup` [833]: IguanaSoup, VegetableSoup (2 items)
  - `stew` [834]: SquirrelStew (1 item)
  - `noodle` [831]: NoodleCup, BlamcoMacAndCheese + PreWar (3 items)
  - `cake` [832]: MirelurkCake, SouffleDeathclaw, PreservedPie01 (3 items)
  - `meat` [82F]: all steaks, raw meats, eggs, omelettes (39 items)
  - `generic` [82D]: packaged foods, snacks, produce, candy, forage (47 items)
- Location: `E:\Modlists\Fallen World Alpha 2\mods\Immersive Animation Framework (IAF) - ESP-less Patches\F4SE\Plugins\RobCo_Patcher\ingestible\shackleberry\{category}\Fallout4.esm.ini`
- REMEMBER: RobCo reads ALL .ini* files in the folder — do NOT rename to .disabled to test; move the file out

---

## DakRugerRifles.esp — .22 Carbine Iron Sights

### fix: iron sights camera alignment [this session]
- Root cause: iron sights OMOD (`000E17:DakRugerRifles.esp`, `00Dakmod_Ruger_Scope_SightsIron`) had NO zoom data properties — the ADS camera used default eye position with no Z compensation
- Comparison with dot sight OMOD (00030E) revealed it has `ZoomDataFOVMult=1.0(Set)` and `ZoomDataCameraOffsetZ=1.65(Add)` to lift camera to dot height; glow sights (000505) similarly has `AimModelConeIronSightsMultiplier` and `SightedTransitionSeconds` properties
- Fix: added 4 properties to `000E17:DakRugerRifles.esp` directly (run_script with DakRugerRifles.esp as patch_plugin; WeaponModification override not yet supported by copy_as_override tool):
  - `ZoomDataFOVMult = 1.0 (Set)` — enable FOV mult (required for zoom data to activate)
  - `ZoomDataCameraOffsetZ = 2.0 (Add)` — lift camera Z so it aligns with iron sight height; **starting value, needs in-game tuning** (dot sight uses 1.65 for its height)
  - `AimModelConeIronSightsMultiplier = -0.1 (MultAndAdd)` — improve ADS accuracy (matches glow sights)
  - `SightedTransitionSeconds = -0.05 (MultAndAdd)` — faster ADS transition (matches glow sights)
- Saved to: `E:\Modlists\Fallen World Alpha 2\mods\Ruger Rifle Pack (10-22 and .44 Carbines)\DakRugerRifles.esp`
- NOTE: `ZoomDataCameraOffsetZ = 2.0` is initial estimate. If sights still appear too low in ADS, increase toward 3.0. If camera overshoots above sights, decrease toward 1.5. A full NIF fix would be authoritative but plugin-level Z offset covers most misalignment cases.
- NOTE: FO4RecordEditor `copy_as_override` and `host.Override()` both block WeaponModification record type — used `WriteService.GetMutable` + `WriteService.FindMutableRecord` + direct `Properties.Add()` via reflection to bypass the tool restriction

---

## Other Plugins

### FallenWorld_Weapon_Patch.esp — 258 WEAP+WMOD records deleted [3d2e3779]
- All 258 Weapon + WeaponMod records had broken ONAM attachment refs from stripped CombinedArms/Colt/Remington mods
- Deleted all; both copies (LO copies) zeroed; 0 broken refs

### FallenWorld_GunRunners.esp — 2 StoryManagerQuestNode conditions cleared [1c22b025]
- 3 broken refs → 0 after clearing conditions on 2 SMQNode records

### FallenWorld_Frozen_Fixes.esp — HumanRace Race override deleted [1c22b025]
- 61 broken refs → 0 after deleting override

### Fallout Anomaly Overwrites.esp — backpack_MsgLLInjectSuccess script property nulled [1c22b025]
- 1 broken ref → 0; used reflection to null the property since normal set path failed

---

## FW_S7_Patch.esp — Session Fixes (2026-06-27)

### fix: Dialogue camera root cause diagnosed [usertask.md]
- `NoDialogueAutoRotAndForcedPos.esl` sets `bPlayDialogueRotations:Dialogue=False` on every load via `Actor.OnPlayerLoadGame` event
- Camera stays locked on NPC for entire conversation
- Fix (Option A): set `bDialogueCameraEnable=0` in `profiles\<profile>\Fallout4Prefs.ini`
- Fix recorded in `E:\Modlists\Fallen World Alpha 2\usertask.md`

### fix: S7 AP Trainer perk non-functional [FW_S7_Patch.esp]
- 3 independent failures in `S7_Athletics_ActionPointTrainerR1 [0007AE:S7 System.esp]`:
  1. Stage 155 fragment was empty (no code ran on perk take)
  2. Effects [1][2] used `ModSpellMagnitude` with impossible AND condition (`GetIsID` must match TWO different FormIDs simultaneously)
  3. No spell application path existed
- Fix: modified fragment `QF_S7_BladeMaster_ChanceChan_05000531.psc` Stage 155 to call `AddSpell(S7_PerkPointScalingEffects [0007D9:S7 System.esp])`
- Spell override in FW_S7_Patch.esp: `S7_PerkPointScalingEffects` magnitude changed from 0.5 → 10 (`S7_FortifyActionPointsBuff_ActionPointTrainer` MGEF is `PeakValueModifier` on ActionPoints)
- Result: taking AP Trainer perk now permanently grants +10 max AP via AddSpell
- Compiled .pex deployed: `E:\Modlists\Fallen World Alpha 2\mods\FW S7 Patch\Scripts\Fragments\Quests\QF_S7_BladeMaster_ChanceChan_05000531.pex`
- NOTE: `ModAV("ActionPoints", ...)` is Skyrim-only Papyrus — does NOT exist in FO4; use AddSpell with PeakValueModifier MGEF instead

### fix: FWC_HealingPowder not working [FW_S7_Patch.esp]
- `FWC_HealingPowder [000800:FWC_BasicMedical.esp]` used `Effect_RestoreHealth [000005:MAIM.esp]`
- MAIM effect has `CastType: ConstantEffect` — with `Duration=0` in the ALCH entry, a ConstantEffect lasting 0 seconds does nothing
- Fix: override in FW_S7_Patch.esp replacing `Effect_RestoreHealth [MAIM]` with `RestoreHealthStimpak [21DDB8:Fallout4.esm]` (magnitude 25, duration 10 = 25 HP over 10 seconds)
- `copy_as_override` to FW_S7_Patch.esp fails with "Could not find 00003E:Skb_MachinegunsRebirth.esl in ''" — tool bug, unrelated to FWC_BasicMedical.esp content; use `run_script` with `host.Override()` + `host.Set()` instead
- FW_S7_Patch.esp now has 3 records: Perk + Spell + ALCH healing powder override

### fix: ScrapBR (Makeshift Rifle) iron sights inaccuracy [PigsWastelandWeaponry.esl]
- Weapon: `00UWScrapBR [00019F:PigsWastelandWeaponry.esl]` — "Scrap Battle Rifle"
- Root cause: `00UWmod_ScrapBR_SightsScope_Iron [000436:PigsWastelandWeaponry.esl]` had `ZoomDataCameraOffsetZ = -0.1 (Add)` but NO `ZoomDataFOVMult` — without FOVMult, zoom data never activates; camera doesn't compensate for sight height
- Same root cause as DakRugerRifles iron sights fix (see above)
- OMOD records CANNOT be overridden via Mutagen or `host.Override()` — must edit source plugin directly (PigsWastelandWeaponry.esl)
- Fix applied directly via `set_field` on PigsWastelandWeaponry.esl:
  - `Properties[5].Value`: -0.1 → 2.5 (ZoomDataCameraOffsetZ lifted to compensate for iron sight height)
  - `Properties[6]` created: `ZoomDataFOVMult = 1.0 (Set)` — enables zoom data activation
- NOTE: `set_field` can CREATE new list entries by using an out-of-range index (e.g. `Properties[6]` when only 0-5 existed); integer format required for float values (`1` not `1.0`)
- NOTE: 2.5 is initial estimate based on sight mesh height; may need in-game tuning (DakRugerRifles used 2.0)
- Saved to: `E:\Modlists\Fallen World Alpha 2\mods\Pig's Wasteland Weaponry - A Best Of Pack\PigsWastelandWeaponry.esl`
- check_plugin: 1258 records, 0 issues

### fix: Heather companion carry weight broken [FW_S7_Patch.esp]
- NPC: `llamaCompanion [00AB33:llamaCompanionHeatherv2.esp]`
- Root cause: NPC ObjectProperty for `CarryWeight [0002DC:Fallout4.esm]` = -125. Engine floors to 0. Her carry weight was literally zero.
- Compound issue: `llamaEnchCarryWeight [249179:llamaCompanionHeatherv2.esp]` on `llamaHeatherBag [245C47:llamaCompanionHeatherv2.esp]` only had magnitude 25. Even if the bag was equipped and working, base (-125) + bag (+25) = -100, still floored to 0. Starting items already exceed 25.
- Fix: two overrides in FW_S7_Patch.esp:
  1. `llamaCompanion` NPC: CarryWeight property -125 -> 400
  2. `llamaEnchCarryWeight` ObjectEffect: magnitude 25 -> 150 (bag gives bonus on top; total with bag = 550)
- FW_S7_Patch.esp now 6 records; check_plugin: 0 issues.

### fix: Protectron nailgun creates impassable wall of nails [FW_S7_Patch.esp]
- Weapon: `DLC01BotWeapProtectron01ArmNailGunLeft/Right [010320/010321:DLCRobot.esm]`
- Ammo: `DLC01BotAmmoRRSpike [0009F8:DLCRobot.esm]`
- Projectile: `RailwayRifleProjectile [0FE26A:Fallout4.esm]`
- Root cause: projectile has `Flags = MuzzleFlash, PinsLimbs`. PinsLimbs makes each nail embed as a persistent physical collision object at the impact point. Shooting the same spot repeatedly stacks them into a solid wall.
- `DLC01BotAmmoRRSpike` is the ONLY ammo using `RailwayRifleProjectile` -- player Railway Rifle uses a completely separate ammo+projectile chain. Safe to override.
- Fix: override `RailwayRifleProjectile` in FW_S7_Patch.esp, set `Flags = MuzzleFlash` (removed PinsLimbs). Nails no longer embed as world objects.
- FW_S7_Patch.esp now has 4 records; check_plugin: 0 issues.

---

### fix: Arcjet - Deep Range Transmitter missing from Synth Boss [FallenWorldLoot DLL + FW_S7_Patch.esp]
- Quest: `BoS101 [06F5C1:Fallout4.esm]` (Tradecraft). `BoS101DRTAlias` (ID 6) uses `CreateReferenceToObject: Object=086874:Fallout4.esm, AliasID=7 (SynthBoss), Create=In` -- DRT is dynamically created inside the SynthBoss alias at quest fill.
- Root cause: `FallenWorldLoot.dll` corpse loot system fires `TESDeathEvent` on the Synth Boss death. The DRT is a `kMISC` item; `KeepFor(kMISC)` returns `iKeepMaterials=40%`, giving 60% chance of removal. No quest-item check existed in the corpse loop.
- `BoS101DRTAlias` flags include `QuestObject` but this marks the alias reference, not the base MISC form. The base form `MajorFlags=0` so `formFlags & 0x400` returns false.
- Fix (code): Added two guards in `FallenWorldLoot` corpse loop: `if (obj->formFlags & 0x400) continue` (QuestItem base-form flag) and `if (g_protectedForms.count(obj->formID)) continue` (FormList lookup).
- Fix (data): `LoadProtectedList()` at `kGameDataReady` reads FormList EditorID `FWLoot_ProtectedMisc` from load order. Created `FWLoot_ProtectedMisc [000801:FW_S7_Patch.esp]` FLST with `BoS101DeepRangeTransmitter [086874:Fallout4.esm]` as first entry.
- `GetFormByEditorID<T>` is the correct CommonLibF4 API (NOT `LookupByEditorID` which doesn't exist in this version).
- `host.Override()` THROWS for MiscItem; `copy_as_override` MCP tool also fails for it. Workaround: use FormList-based protection instead of flagging the base record directly.
- FW_S7_Patch.esp now has 7 records; check_plugin: 0 issues.

---

## run_script API Reference (PatchScriptHost)

run_script context provides `host` (instance of `PatchScriptHost`):
- `host.Records(string type, string plugin)` — all records of Mutagen type from a plugin
- `host.Override(IMajorRecordGetter getter)` — create override in patch; throws for WeaponModification (OMOD)
- `host.Set(IFallout4MajorRecord rec, string field, string value)` — set field (same syntax as set_field tool)
- `host.AddCondition(ConstructibleObject, ...)` — append condition to COBJ
- `host.Log(string)` — write to script output log
- `WriteService` is NOT an instance variable; ignore previous mentions of `WriteService.GetMutable`
- `host.Override()` works for: Ingestible, Spell, Perk, and other standard types
- `host.Override()` THROWS for: WeaponModification (OMOD)
- Script return value is NOT shown in tool output; write to temp file for diagnostics
- `open_plugin` tool uses `plugin` parameter (not `path`) — value is bare filename OR full path

## run_script Error Patterns (Avoid)
- `fixed` = reserved C# keyword; use `patchedCount`
- `FormKey` is struct; no null-conditional `?.` on it; use try/catch
- `DeepCopy` on Condition is explicit interface impl; inaccessible via `dynamic`; use MemberwiseClone via reflection
- `List<Condition>.Add(object)` fails; clone fn must return `dynamic` for dispatch to succeed
- `FallenWorldCrafting_Compat.esp` bare filename fails `open_plugin`; use full path: `E:\Modlists\Fallen World Alpha 2\mods\FallenWorldCraftingFramework\FallenWorldCrafting_Compat.esp`
- `scan_conflicts` returns 191k+ results if used on all record types; use targeted run_script COBJ FK overlap check instead
- CNAM xEdit errors during editing session are NEVER real issues; always xEdit session artifacts (Rule 0)

## Key Facts
- Live modlist: `E:\Modlists\Fallen World Alpha 2` (D:\Games\ModlistDownloads is BROKEN)
- LO positions: FallenWorldCrafting_Compat.esp=685, FW_SkillGates.esp=686, FW_FinalGating.esp=687
- Ammo schematic FKs in FallenWorldCrafting.esp: SmallCal=82C, RifleCal=82D, HeavyCal=82E, Shotgun=82F, Energy=830, Explosive=831, Special=832
- S7 System hooks PRKF::PerkTaken; does NOT auto-grant vanilla GunNut perks; dual-gate (HasPerk + GetBaseValue S7_Gunsmith) is intentional design
