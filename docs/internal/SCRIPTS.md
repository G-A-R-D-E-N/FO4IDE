# Helper Scripts

Three PowerShell helpers wrap the serialize -> edit -> compile -> deploy loop so paths don't have
to be retyped. They live in the repository's `scripts` folder and all share `_config.ps1`
(Spriggit 0.40.1 / `Spriggit.Json.Fallout4`), which points at your modlist's `mods` folder.

Run them from the repository root.

## 1. serialize.ps1 - plugin -> JSON tree
```powershell
.\serialize.ps1 -Plugin "MyMod.esp"            # finds it under the modlist -> repos\MyMod\
.\serialize.ps1 -Plugin "E:\path\Some.esp" -Out "repos\Some"
```
`-Plugin` accepts an exact filename, a partial name (searched across the load order), or a full
path. Output defaults to `repos\<name>\`.

## 2. compile.ps1 - JSON tree -> .esp
```powershell
.\compile.ps1 -Src "repos\MyMod"               # -> repos\MyMod.esp
.\compile.ps1 -Src "repos\MyMod" -Out "build\MyMod.esp"
```
Calls the cached translation exe `create-plugin` directly. Auto-detects whether `RecordData.json`
is at the tree root or inside an inner `<ModName>.esp\` folder. Warns if output is under 5 KB
(likely failed).

## 3. deploy.ps1 - .esp -> MO2 mod folder
```powershell
.\deploy.ps1 -Esp "repos\MyMod.esp" -ModFolder "MyMod"
.\deploy.ps1 -Esp "repos\MyMod.esp" -ModFolder "<modlist>\mods\Some Mod"
```
Targeted copy only. Never deletes the mod folder. The mod folder must already exist.

## Typical loop
```powershell
.\serialize.ps1 -Plugin "MyMod.esp"
#   ... edit or script the JSON under repos\MyMod\ ...
.\compile.ps1   -Src "repos\MyMod"
.\deploy.ps1    -Esp "repos\MyMod.esp" -ModFolder "MyMod"
#   ... test in game ...
```

## Notes
- `repos\` is the working area (one subfolder per plugin); git-init it if you want diffs.
- ESL plugins risk master-FormID corruption through Spriggit on FO4; for ESL-heavy patches use
  the xEdit route instead (see [SPRIGGIT](SPRIGGIT.md)).
- If the cached compile exe is ever missing, `compile.ps1` falls back to the repo-built exe
  automatically; running `serialize.ps1` once also repopulates the cache.
