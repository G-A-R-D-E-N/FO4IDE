# Using FO4RecordEditor with Mod Organizer 2

## The workflow: launch through MO2 once, then work outside it

Launch the editor through MO2 a single time. That is what lets it see the modlist's virtual Data
folder and resolve the real load order. After that, run it standalone: the instance path is
remembered in `%APPDATA%\FO4RecordEditor\settings.json` (`Mo2InstancePath`), so plugins open fine
outside MO2.

A self-contained build cannot run through MO2 at all: its bundled .NET runtime cannot load through
usvfs's hooks, so the process dies before any managed code runs. The release build is
framework-dependent, loads the runtime from the shared install, and works fine.

The **Open MO2** workflow below reads your modlist directly off disk and runs standalone, no VFS.
It is the day-to-day path once the one-time MO2 launch above is done.

## 1. Get the exe

The release package ships a framework-dependent build. Requires the **.NET 9 Desktop Runtime**
(x64): https://dotnet.microsoft.com/download/dotnet/9.0

Unzip the package and keep the folder together (the exe loads `web\dist\` and `tools\` from beside
itself).

## 2. Load your modlist (Open MO2)

1. Run `FO4RecordEditor.exe`, through MO2 for the one-time launch above, standalone thereafter.
2. Click **Open MO2** in the Explorer toolbar.
3. Pick your **MO2 instance folder**, the one containing `mods\`, `profiles\`, and
   `ModOrganizer.ini`.
4. The editor reads the active profile's `plugins.txt` for the load order and resolves each
   plugin from `overwrite\` -> mods (by `modlist.txt` priority) -> the game `Data` folder, then
   builds the load order. Pick which plugins to open in the plugin picker.

The active profile and game path come from `ModOrganizer.ini` automatically. The chosen instance
folder is remembered for next time.

**Open ESP** still works for loading a single plugin file directly.

## Notes

- If **Open MO2** reports "no plugins loaded", confirm the profile's `plugins.txt` exists and
  that `gamePath` in `ModOrganizer.ini` points at your game install (its `Data` subfolder holds
  the vanilla/DLC/CC masters).
- A few plugins may report as "could not be resolved" if their file is not found in any enabled
  mod or the game Data folder; those are skipped and listed in the status line.
- **Data folder override** (Settings > Game Data Folder): an alternative to Open MO2. Point it
  at a single real Data folder (for example a fully deployed/merged modlist) and use **Load Env**.
  Leave it blank for normal auto-detect.
- Saving an authored/edited plugin writes to the editor's Output folder by default (configurable
  in Settings). To drop a result into your modlist, point the output at the appropriate MO2 mod's
  folder, or move the file in afterward.
