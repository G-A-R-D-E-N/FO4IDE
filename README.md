# FO4RecordEditor

A Fallout 4 plugin IDE that reads and writes real `.esp`/`.esm`/`.esl` files. Two things live in
one executable:

- a **desktop editor** for browsing records across your load order, diffing and editing them,
  scanning conflicts, compiling and decompiling Papyrus, and inspecting NIFs
- an **MCP server** that gives an AI assistant the same operations: you describe the edit, the
  assistant makes it, and the plugin is written by
  [Mutagen](https://github.com/Mutagen-Modding/Mutagen) rather than by hand

It's built on a patched Mutagen, so it doesn't go through xEdit and doesn't need xEdit installed.
One codebase runs natively on Windows and Linux.

![The record editor, with the Claude assistant panel on the right](images/editor.png)

## Install (Windows)

1. Install the **.NET 9 Desktop Runtime (x64)**. That's the only prerequisite.
2. Unzip the package anywhere, keeping the folder together (the exe loads `web\dist\` and `tools\`
   from beside itself).
3. Run `FO4RecordEditor.exe`.

If you're editing a plugin that overrides mods, launch it **through Mod Organizer 2**
(Settings → Executables → Add from file). That's what lets it see your modlist's virtual Data
folder instead of the vanilla one.

Why no self-contained exe: MO2's virtual file system (usvfs) hooks file access, and a
self-contained .NET host can't load its bundled runtime through those hooks: the process dies
before any managed code runs. A framework-dependent build loads the shared runtime, which usvfs
handles fine.

## Linux

A native build exists: install the `.deb` (`packaging/linux/out/fo4recordeditor_1.0.0_amd64.deb`)
or build it with `packaging/linux/build-deb.sh`. Same code as Windows; the UI runs in a WebKitGTK
window instead of WebView2.

A few helper tools (niftool, texconv, Archive2, PapyrusCompiler, xWMAEncode) exist only as Windows
binaries, so installing Wine lets the panels that drive them work. Everything else is native:
Papyrus compiling has its own engine and doesn't need `PapyrusCompiler.exe`.

## Point an AI at it (MCP)

The editor speaks [MCP](https://modelcontextprotocol.io) over stdio. The full picture, covering
every tool and the rules that keep an assistant from corrupting plugins, is in
**[docs/MCP_SETUP.md](docs/MCP_SETUP.md)**. The short version: drop a `.mcp.json` in your project
folder (a sample sits next to the exe as `mcp.sample.json`):

```jsonc
{
  "mcpServers": {
    "fo4editor": {
      "type": "stdio",
      "command": "C:\\Tools\\FO4RecordEditor\\FO4RecordEditor.exe",
      "args": ["--mcp", "--mo2", "C:\\Modlists\\My Modlist"]
    }
  }
}
```

- `--mcp` runs it headless as a server; the GUI never opens.
- `--mo2 <instance>` points it at an MO2 instance, so it reads the active profile's `plugins.txt`
  and reconstructs that exact load order.
- `--data <folder>` is the alternative: a plain `Data` folder, for a vanilla setup.

Use one or the other, then restart your AI client (or `/mcp reconnect` in Claude Code).

## Visual scripting

Papyrus scripts edit as node graphs: events, branches, calls and variables wired together instead
of text. Scripts open from compiled `.pex` files and compile back to them.

![The blueprint editor: a Papyrus script as a node graph](images/blueprint.png)

## The panels

The editor ships with a set of focused panels for game-asset work:

![The audio panel: convert to XWM, and make or extract FUZ files](images/audio-panel.png)

![The archive panel: list, compare, and pack BA2/BSA archives](images/archive-panel.png)

![The NIF viewer: view, edit, and verify meshes in a live 3D viewport](images/nif-viewer.png)

![The Papyrus panel: decompile .pex back to source, or compile .psc](images/papyrus-decompile.png)

![The cell viewer: inspect placed references in a 3D viewport](images/cell-viewer.png)

## Configuration

Settings live in `%APPDATA%\FO4RecordEditor\settings.json` and are all optional: every path is
auto-detected when blank. The GUI's settings panel covers them all, and each also honors an
environment variable that wins over the file: `NIFTOOL_PATH`, `TEXCONV_PATH`,
`PAPYRUS_COMPILER_PATH`, `PAPYRUS_BASE_IMPORTS`, `CK_WIKI_PATH`, `FFMPEG_PATH`,
`XWMAENCODE_PATH`, `ARCHIVE2_PATH`.

One caveat about Papyrus: `compile_papyrus` defaults to the Creation Kit's `PapyrusCompiler.exe`
when one is installed (it's not redistributable, so it never ships here) and falls back to a
built-in compiler otherwise. The built-in engine still needs the vanilla base script sources on its
import path. Those are plain `.psc` text; point `PAPYRUS_BASE_IMPORTS` at them. The
`papyrus_check` / `papyrus_outline` / `papyrus_definition` tools need nothing installed at all.

## What's in the box

```
FO4RecordEditor.exe     the editor + MCP server
web\dist\               the UI (WebView2 on Windows, WebKitGTK on Linux)
tools\niftool\          NIF authoring/repair CLI
tools\texconv\          DDS conversion fallback
tools\ckwiki\           offline Creation Kit Wiki mirror
tools\audio\            ffmpeg, xWMAEncode, BmlFuz (the audio_* tools)
mcp.sample.json         copy-paste MCP config
docs\MCP_SETUP.md       the MCP wiring and tool surface
```

## Building it

Working on the tool rather than with it? Start at
[docs/internal/README.md](docs/internal/README.md) and read
[docs/internal/KNOWLEDGE.md](docs/internal/KNOWLEDGE.md) before touching plugin or script logic.
Nothing under `docs\internal\` ships in a release; `package.ps1` copies an explicit allowlist.

## License

GPL-3.0, because it's built directly against Mutagen and embeds nifly, both GPL-3.0. See
[LICENSE](LICENSE), and [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) for the component list.
The package contains no Bethesda assets; it reads the game files you already own.
