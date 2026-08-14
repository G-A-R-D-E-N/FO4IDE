# FO4RecordEditor on Linux

There is a native Linux build. Install the .deb and run it; no Wine, no WebView2.

Verified on Linux Mint 22.3 against a real 651-plugin load order.

## Install

```bash
sudo apt install ./packaging/linux/out/fo4recordeditor_1.0.0_amd64.deb
fo4recordeditor
```

It opens in its own window, not a browser tab: the UI is hosted in an embedded WebKitGTK view, the
same way the Windows build hosts it in WebView2. Real titlebar, own taskbar entry, own icon.

The package is self-contained (it carries its own .NET runtime), so the only things it needs from
the system are `libwebkit2gtk-4.1-0` and `libgtk-3-0`, which apt pulls in. It puts the app in
`/opt/fo4recordeditor`, a launcher at `/usr/bin/fo4recordeditor`, and a menu entry under
Development.

## Building the .deb

```bash
packaging/linux/build-deb.sh [version]     # default 1.0.0
```

Needs `dotnet` (9 SDK), `npm`, `dpkg-deb` and `fakeroot`. It builds the React bundle, publishes the
server self-contained for `linux-x64`, stages the tree and writes the package to
`packaging/linux/out/`.

Two things that will bite:

- **Staging happens in `/tmp`, deliberately.** The repo often lives on an exfat/ntfs mount, which
  reports every file as mode 777, and `dpkg-deb` refuses a `DEBIAN/` directory with those
  permissions.
- **`node_modules` is platform-specific.** Vite 8 uses rolldown, whose native binding is per-OS. A
  `node_modules` installed on Windows has only `@rolldown/binding-win32-x64-msvc` and the build dies
  with `Cannot find module '../rolldown-binding.linux-x64-gnu.node'`. Run `npm ci` on the machine
  you are building on. Installing the Linux binding prunes the Windows one, so a Windows build
  afterwards needs its own `npm ci`.
- **`npm install` needs symlinks, which the ntfs3 kernel driver cannot create.** If the repo sits
  on a mount reported as `type ntfs3` (check `mount`; `ntfs-3g` userspace supports symlinks, the
  kernel `ntfs3` driver does not), npm dies on the `node_modules/.bin` links with
  `ENOENT: no such file or directory, symlink '../@babel/parser/bin/babel-parser.js' -> ...`. Work
  around it with `--no-bin-links` and call the tools by their real paths:
  `npm ci --no-bin-links`, then `node node_modules/typescript/bin/tsc -b`,
  `node node_modules/vite/bin/vite.js build` (dev: `node node_modules/vite/bin/vite.js`),
  `node node_modules/vitest/vitest.mjs run` for the web tests. On an ext4 mount, or on Windows,
  where npm writes `.bin` shims instead of symlinks, the plain `npm ci && npm run build` works as
  written.

## How it works

The UI is a React app. On Windows the WPF shell loads it into a WebView2 control and exposes the C#
interop objects with `AddHostObjectToScript`. On Linux `FO4RecordEditor.Server` serves the identical
bundle over Kestrel, exposes the identical interop objects over `POST /rpc`, and loads it into an
embedded WebKitGTK window (via Photino). Kestrel is an implementation detail of the bridge, not a
"run it in your browser" design: the loopback port is how the page reaches C#, in place of
WebView2's COM marshalling.

**Both hosts compile the same `Services/`, `Models/` and `ViewModels/` sources.** They are linked
into `FO4RecordEditor.Server.csproj` rather than copied, so there is no porting step: an edit to an
interop method reaches both. The only Windows-only code left is `Views/` and the `.xaml` code-behind.

Anything a UI toolkit has to provide goes through `FO4RecordEditor.Services.HostServices`, which
each host installs at startup:

| | Windows | Linux |
|---|---|---|
| File/folder pickers | `Microsoft.Win32.*Dialog` | zenity, else kdialog, else "" |
| Message box | `MessageBox.Show` | zenity/kdialog, else stderr |
| UI-thread marshalling | `Dispatcher.Invoke` | inline under a lock |

Endpoints, if you need to drive it directly:

| | |
|---|---|
| `POST /rpc` | `{"target":"backend","method":"GetConflicts","args":[]}` -> `{"ok":true,"value":...}` |
| `GET /events` | server-sent events; the SSE stand-in for `PostWebMessageAsJson` |
| `GET /api/health` | liveness plus the registered host-object names |

Flags:

| Flag | Meaning |
|---|---|
| `--browser` | use an app-mode browser window instead of the native one (needs no WebKitGTK; gives real devtools) |
| `--headless` | serve the UI but open nothing |
| `--port N` / `--host ADDR` | pin the loopback endpoint instead of taking a free port |

`WEBKIT_DISABLE_DMABUF_RENDERER=1` is set automatically unless you already set it; without it
WebKitGTK draws a blank or black window on some drivers and compositors. Plus the MCP flags below.

## MCP mode, natively

The same `--mcp` contract as the Windows build, so this replaces the old Wine setup entirely:

```bash
fo4recordeditor --mcp --mo2 "/media/ricky/Games-Storage/Modlists/Fallen World Alpha 2"
```

| Flag | Meaning |
|---|---|
| `--mcp` | headless mode; JSON-RPC (MCP `2024-11-05`) on stdin/stdout, one message per line |
| `--mo2 <instance>` | load an MO2 instance (the folder holding `mods/` and `profiles/`) |
| `--data <folder>` | load a plain game Data folder instead |
| `--ck-wiki <folder>` | override the bundled CK wiki mirror |

Paths are ordinary Linux paths. The process exits when stdin closes, so feed a one-shot invocation
every request up front:

```bash
python3 - <<'PY' > /tmp/in.jsonl
import json
for m in [
 {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05",
   "capabilities":{},"clientInfo":{"name":"probe","version":"1"}}},
 {"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"list_plugins","arguments":{}}},
]: print(json.dumps(m))
PY
fo4recordeditor --mcp --mo2 "/path/to/instance" < /tmp/in.jsonl > /tmp/out.jsonl
```

## What needs Wine, and what does not

Reading, editing, conflict resolution, saving, cells, archives and Papyrus *decompiling* are all
native. A handful of helper tools only ever shipped as Windows binaries and have no Linux build:

- `niftool.exe` (NIF authoring/inspection)
- `Texconvx64.exe` (texture conversion, used by the Cell Viewer)
- `Archive2.exe` (BA2 packing)
- `PapyrusCompiler.exe` (Papyrus compiling)
- `xWMAEncode.exe` (audio conversion)

`ProcessRunner` transparently runs any `.exe` through `wine` on non-Windows, so installing wine
makes those panels work. Set `FO4RE_WINE` to name a different wine binary, or to an empty value to
turn the shim off. Untested against the compiler's path handling; if a Windows-style path is
required, that is where it will show up first.

## Gotchas

- **"Load Env" (auto-detect) does not work on Linux, and now says so.** It builds a Mutagen
  environment from the game's managed load order file (`Plugins.txt`), which has no Linux equivalent
  outside a Proton prefix, so it fails with an actionable `EnvironmentLoadException` ("...Use Open
  MO2...") instead of hanging at "Initializing Game Environment...". Load a modlist with **Open MO2**
  (`--mo2`) instead; it reads the profile's own plugin list directly. The translation lives in
  `MutagenLoader.TranslateEnvironmentError`, and the raw Mutagen cause is kept as the inner exception.
- **Look up FormKeys, do not guess them.** A guessed FormID just returns "Could not resolve". Use
  `scan_conflicts` or `search_all` to get a real one first.
- **`resolve_editorid` takes `id`, not `editor_id`.** Several tools use a bare `id`; check the
  schema in `PluginToolExecutor.cs` rather than assuming.
- Many EditorIDs will not resolve at all: Fallout 4 strips them from plugins.
- The app log is at `~/.config/FO4RecordEditor/debug.log`.

## Building from source

### You need the .NET 10 SDK, not just 9

The vendored Mutagen projects multi-target `net8.0;net9.0;net10.0`
(`Mutagen/Mutagen.Bethesda.Core/Mutagen.Bethesda.Core.csproj`), and building a project that
references them builds every one of those target frameworks. So the SDK has to be able to target the
highest of them, even though `FO4RecordEditor.Core` itself is `net8.0`.

With only the 8 SDK installed this fails as `NETSDK1045: The current .NET SDK does not support
targeting .NET 9.0`; install the 9 SDK and the same error reappears naming .NET 10.0. That message
names the *SDK's* ceiling, not anything wrong with the project -- it is easy to misread as "this
project cannot be built on Linux", which it can.

```bash
curl -sSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh && chmod +x /tmp/dotnet-install.sh
/tmp/dotnet-install.sh --channel 10.0 --install-dir "$HOME/.dotnet"    # side by side, keeps 8 and 9
dotnet --list-sdks
```

If the install prints `cp: cannot create regular file '.../dotnet': Text file busy`, a dotnet process
is running; the SDK itself still installs correctly and only the shared host binary is skipped.

```bash
cd web && npm ci && npm run build          # the UI bundle
dotnet build FO4RecordEditor.Server/FO4RecordEditor.Server.csproj -c Release
```

If this repo is on an `ntfs3` mount, `npm ci`/`npm run build` fail on symlinks, see the workaround
under “Two things that will bite” above (`npm ci --no-bin-links` + direct `node .../bin/...`
invocations).

The Windows shell also builds here, which is worth running after touching any shared source, since
that source is compiled by both:

```bash
dotnet build FO4RecordEditor/FO4RecordEditor.csproj -c Release -p:EnableWindowsTargeting=true
```

`dotnet test` builds but cannot **run**: the test host needs `Microsoft.WindowsDesktop.App` as a
Linux runtime, which does not exist, because the test project references the WPF project. Move a
test to a project referencing only `FO4RecordEditor.Core` / `FO4RecordEditor.Server` to run it here.

A throwaway project that `<Compile Include>`s the specific test files and references only
`FO4RecordEditor.Core` runs them on Linux without touching the real suite -- useful for proving a
Core-only change before handing it to a Windows box. Note the repo path contains an `&`, which must
be written `&amp;` inside the csproj or MSBuild fails with `MSB4025: An error occurred while parsing
EntityName`.

Set `FO4RE_REQUIRE_FIXTURES=1` when doing this. Several tests resolve a real `Data` folder through
`TestDataRoots` and otherwise **skip silently while still reporting green**, so a pass without it
does not mean the code was exercised.
