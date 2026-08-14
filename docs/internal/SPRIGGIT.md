# Spriggit Reference

Lossless converter between a binary Bethesda plugin and a folder of per-record
text (JSON or YAML). Built on Mutagen. Pinned here to **0.40.1**, package
**Spriggit.Json.Fallout4**.

## Terminology (important - the words are backwards from intuition)
- **serialize** = plugin -> text folder (parse / extract for editing)
- **deserialize** = text folder -> plugin (compile). `create-plugin` is an alias.

## Executables

| Use | Path |
|---|---|
| CLI launcher | `spriggit-cli.bat` in the repo's `Spriggit\` folder |
| CLI exe | `Spriggit\Spriggit.CLI\bin\Release\net9.0\Spriggit.CLI.exe` |
| UI launcher | `spriggit-ui.bat` in the repo's `Spriggit\` folder |
| UI exe | `Spriggit\Spriggit.UI\bin\Release\net9.0\Spriggit.exe` |
| Global tool (wrapper) | `spriggit.exe` installed as a dotnet global tool |
| Cached translation exe | `%LOCALAPPDATA%\Temp\Spriggit\Translations\Spriggit.Json.Fallout4\0.40.1\Spriggit.Json.Fallout4.exe` |

## Packages (one per game x format)
`Spriggit.Json.Fallout4` (used here), `Spriggit.Yaml.Fallout4`, plus
`.Json/.Yaml` variants for `Skyrim`, `Oblivion`, `Starfield`. JSON is compact;
YAML is more readable. This workspace standardizes on JSON for FO4.

## Serialize (plugin -> JSON)

```powershell
cd <spriggit folder>
.\spriggit-cli.bat serialize `
  --InputPath  "E:\path\to\SomeMod.esp" `
  --OutputPath "E:\path\to\SomeMod_repo" `
  --GameRelease Fallout4 `
  --PackageName Spriggit.Json.Fallout4
```

Output layout: `<OutputPath>\<RecordType>\<EditorID> - <FormID>_<Master>.json`
plus a `RecordData.json` header and a `.spriggit` metadata file. See
[JSON_RECORD_FORMAT.md](JSON_RECORD_FORMAT.md).

The global tool form (used in the Rad Mod docs) is equivalent:
```powershell
spriggit serialize -i "SomeMod.esp" -o "out\SomeMod.esp" -p Spriggit.Json.Fallout4 -v 0.40.1
```

## Deserialize (JSON -> plugin)

```powershell
.\spriggit-cli.bat deserialize `
  --InputPath  "E:\path\to\SomeMod_repo" `
  --OutputPath "E:\path\to\SomeMod.esp"
```

## KNOWN GOTCHA 1 - the `spriggit` wrapper fails to compile some source trees

The global `spriggit` wrapper can fail deserialize with:
```
System.Data.DataException: Could not locate source info from ...
   at Spriggit.Engine.Services.Singletons.GetMetaToUse.Get(...)
```
This happens even with a valid `SpriggitSource` in `RecordData.json` and explicit
`-p`/`-v`. NuGet cache reset (`--Debug`), absolute paths, alternate output dirs,
and adding `spriggit.json` do NOT fix it.

### Bypass - call the cached translation exe directly
The wrapper is only a launcher around per-game translation packages cached under
`%LOCALAPPDATA%\Temp\Spriggit\Translations\`. That exe has its own working
`create-plugin` command:

```powershell
$exe = "$env:LOCALAPPDATA\Temp\Spriggit\Translations\Spriggit.Json.Fallout4\0.40.1\Spriggit.Json.Fallout4.exe"
& $exe create-plugin `
  -i "E:\...\esl_src\MyMod.esp" `
  -o "E:\...\esl_build\MyMod.esp"
```
Expected: `[INF] Starting deserialization ...` then `[INF] Finished deserializing`.

> If the cached exe is missing, run any `serialize` once - the wrapper downloads
> and caches the translation package on first use.

The repo also ships built translation exes (no temp cache needed):
`Spriggit\Translation Packages\Spriggit.Json.Fallout4\bin\Release\net9.0\Spriggit.Json.Fallout4.exe`.

## KNOWN GOTCHA 2 - ESL master FormID corruption (workspace memory)
Spriggit `create-plugin` can break ESL master FormID references on FO4. For
ESL-heavy patches, prefer the xEdit Pascal route instead. Plain-ESP trees (like
RadiationOverhaul) compile fine. Verify output size after every compile - a real
build is sized to its record count; a near-empty/failed one is suspiciously small
(< 10 KB for the Rad Mod tree).

## Verify a compile

```powershell
(Get-Item "E:\...\esl_build\MyMod.esp").Length
```

## UI link manager
`Settings.json` in the Spriggit folder stores serialize "Links" (ModPath <->
GitPath pairs) used by the UI for one-click re-serialize. Useful when mining many
research mods into a single output tree (see the Rad Mod `Research\Outpuit` setup).

## Git workflow
1. `git init` a repo for the mod text tree.
2. `serialize` the .esp into it, `git commit`.
3. Edit in Creation Kit OR by hand/script, re-`serialize`, `git diff` to review.
4. `deserialize` back to .esp to load in game.
The `.spriggit` file records which translation package/version produced the tree.
