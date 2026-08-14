# Plugin Build Pipeline

The end-to-end round-trip for producing a plugin from text/scripts. Modeled on
a real plugin build pipeline.

```
 (1) PARSE      existing plugins  -> JSON   (Spriggit serialize)   research input
 (2) BUILD      scripts           -> JSON   (Python / Mutagen)     our records
 (3) MERGE      research JSON      -> JSON   (script, filtered)     safe overrides
 (4) COMPILE    JSON              -> .esp    (Spriggit deserialize) binary plugin
 (5) DEPLOY     .esp -> MO2 mod folder        (targeted copy)       in-game
```

## 1. PARSE - serialize sources to JSON (only when adding a new source)

Serialize Fallout4.esm, DLCs, or research mods so you can copy records verbatim
or mine overrides.

```powershell
spriggit serialize -i "SomeMod.esp" -o "Research\Outpuit\SomeMod.esp" `
  -p Spriggit.Json.Fallout4 -v 0.40.1
```
Output: `<out>\<RecordType>\<EditorID> - <FormID>_<Master>.json`. Copying a
vanilla record (e.g. RadAway, an IMAD) is "re-keying": load the parsed JSON,
mutate minimally, rewrite under your mod's FormKey. This guarantees correct field
structure instead of hand-building.

## 2. BUILD - generate the record tree

A generator (Python writing JSON, or a Mutagen C# project) produces the source
tree. The Rad Mod `gen_esl.py` pattern:
- wipes and rebuilds `esl_src\<Mod>.esp\` from scratch each run,
- writes a `.spriggit` file (`{PackageName, Version}`) and `RecordData.json`
  header (ModKey, GameRelease, masters, header flags),
- writes one JSON file per record into folders by record type.

```powershell
cd <generator project>
python gen_esl.py
```
Retune by editing constants at the top of the generator, then re-run.

## 3. MERGE - fold in vanilla/DLC overrides (optional)

A merge script mines the parsed research mods (step 1) and copies ONLY safe
override records on top of step 2's tree. Safety filters from `merge_overrides.py`:
- only **overrides of vanilla/DLC records**, never a mod's custom-owned FormKey
  (those carry meshes/scripts/dependencies you cannot ship);
- every referenced FormKey must point to an allowed master (Fallout4 + DLCs);
- theme filter on EditorID where appropriate;
- exclude leveled lists (broad side effects), deleted records, mojibake strings,
  and a hard exclude set;
- precedence: source-mod order, first to claim a FormKey wins.

```powershell
python merge_overrides.py            # dry run - prints plan, masters, overlaps
python merge_overrides.py --apply    # write into esl_src
```
> Order matters: run the generator BEFORE the merge `--apply` (the generator
> wipes the tree; the merge layers on top).

## 4. COMPILE - JSON tree to binary .esp

Use the cached translation exe, NOT the `spriggit` wrapper (see SPRIGGIT.md gotcha).

```powershell
$exe = "$env:LOCALAPPDATA\Temp\Spriggit\Translations\Spriggit.Json.Fallout4\0.40.1\Spriggit.Json.Fallout4.exe"
& $exe create-plugin -i "esl_src\MyMod.esp" -o "esl_build\MyMod.esp"
```
Verify size afterward; a real build matches its record count, a failed one is tiny.

## 5. DEPLOY - copy to MO2

Always targeted copies. NEVER delete the mod folder (it holds untracked runtime
files). Deploy to the modlist (`<modlist>\mods\...`).

```powershell
Copy-Item "esl_build\MyMod.esp" "<modlist>\mods\<ModFolder>\MyMod.esp" -Force
```

## Plain ESP vs ESL - load order consequence
- **Plain ESP** (`Flags: []`): loads LAST, WINS conflicts over hundreds of mods.
  Use for an overhaul that must override everything.
- **ESL / Light** (`Flags: ["Light"]`): force-loaded early, LOSES conflicts. Use
  only for additive content where winning is not required. Note: Spriggit can
  corrupt ESL master FormID refs on FO4 - prefer xEdit Pascal for ESL-heavy work.

## The FormID contract
When a DLL reads records by hardcoded FormID, the FormID map is a contract:
changing an ID in the generator requires a matching change in the DLL constants.
Keep an authoritative FormID table next to the generator.
