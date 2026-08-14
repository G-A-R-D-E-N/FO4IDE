# Spriggit JSON Record Format (Fallout 4)

How a serialized plugin is laid out on disk, and the shape of individual record
files. All examples are real, taken from the RadiationOverhaul source tree
(`Spriggit.Json.Fallout4`, v0.40.1).

## Folder layout

```
<ModName>.esp\
├── .spriggit                 # {"PackageName":"Spriggit.Json.Fallout4","Version":"0.40.1"}
├── RecordData.json           # mod header: ModKey, masters, flags, author
├── Globals\
│   └── <EditorID> - <FormID>_<Master>.json
├── MagicEffects\
├── Spells\
├── Perks\
├── Ingestibles\
├── ImageSpaceAdapters\
├── Weather\
├── ConstructibleObjects\
├── GameSettings\
├── Quests\
└── ... (one folder per record type present)
```

## File naming convention (must match exactly)

```
<EditorID> - <FormID>_<Master>.json
```
- `<FormID>` is 6 hex digits, uppercase (e.g. `000850`).
- `<Master>` is the plugin that OWNS the FormKey. For records you author it is
  your own mod (`RadiationOverhaul.esp`); for overrides it is the original owner
  (`Fallout4.esm`).

Examples:
```
RadOverhaul_TissueDamage - 000850_RadiationOverhaul.esp.json   # custom
ServiceCostCureRads - 0C03D2_Fallout4.esm.json                 # vanilla override
```
Generators must reproduce this naming exactly or records will not deserialize.

## RecordData.json (the header)

```json
{
  "SpriggitSource": { "PackageName": "Spriggit.Json.Fallout4", "Version": "0.40.1" },
  "ModKey": "RadiationOverhaul.esp",
  "GameRelease": "Fallout4",
  "ModHeader": {
    "Flags": [],
    "Stats": { "Version": 0.95 },
    "Author": "NomadsReach",
    "MasterReferences": [
      { "Master": "Fallout4.esm", "FileSize": 0 },
      { "Master": "DLCCoast.esm", "FileSize": 0 },
      { "Master": "DLCNukaWorld.esm", "FileSize": 0 }
    ],
    "INTV": "0x01000000"
  }
}
```
- `Flags: []` = plain ESP (loads last, wins). `["Light"]` = ESL.
- Declare every master whose forms you reference or override. DLC masters are
  required if any merged override touches a DLC form.

## FormKey format
`"XXXXXX:Plugin.esp"` - 6 hex FormID, colon, owning master. Examples:
`"000850:RadiationOverhaul.esp"`, `"0002C4:Fallout4.esm"` (vanilla Endurance AV).
The FormID prefix in the field and in the filename must agree.

## Record examples

### Global (float)
```json
{
  "MutagenObjectType": "GlobalFloat",
  "FormKey": "000850:RadiationOverhaul.esp",
  "EditorID": "RadOverhaul_TissueDamage",
  "Data": 0.0
}
```

### Magic Effect (peak-value-mod archetype, detrimental)
```json
{
  "FormKey": "000800:RadiationOverhaul.esp",
  "EditorID": "RadOverhaul_ReduceEND",
  "Name": { "TargetLanguage": "English", "Value": "Radiation Sickness" },
  "Flags": ["Detrimental", "NoArea", "NoHitEffect"],
  "Archetype": {
    "MutagenObjectType": "MagicEffectPeakValueModArchetype",
    "ActorValue": "0002C4:Fallout4.esm"
  },
  "CastType": "FireAndForget",
  "TargetType": "Self",
  "DualCastScale": 1.0,
  "CastingSoundLevel": "Silent",
  "Sounds": [],
  "Description": { "TargetLanguage": "English",
    "Value": "Endurance reduced by <mag> from radiation poisoning." }
}
```

## Field conventions
- **Localized strings** are objects: `{ "TargetLanguage": "English", "Value": "..." }`.
- **Polymorphic subrecords** carry a `MutagenObjectType` discriminator
  (e.g. `GlobalFloat` vs `GlobalInt`, the various `MagicEffect*Archetype` types).
  Set it correctly or deserialization picks the wrong type.
- **Enums** serialize as strings (`"FireAndForget"`, `"Self"`); flags as string arrays.
- **Empty collections** are `[]`, not omitted, in generated records.
- Defaults/zeros are written explicitly by generators for stability.

## Override vs custom records
- **Custom**: FormKey + filename use YOUR mod as master, FormID in your reserved
  range (this workspace uses the `0x800+` low range for custom forms).
- **Override**: keep the ORIGINAL `XXXXXX:Fallout4.esm` FormKey/filename so your
  copy replaces the vanilla record at load. Re-key a parsed vanilla record rather
  than building it by hand to keep field structure correct.

## Gotchas
- Filename FormID/master must match the in-file `FormKey`.
- Missing a master reference for a FormKey you reference = broken record.
- ESL trees risk master FormID corruption through Spriggit on FO4 (use xEdit Pascal).
- Non-ASCII / mojibake string values cause failures; filter them when mining mods.
