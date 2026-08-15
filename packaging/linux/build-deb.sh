#!/usr/bin/env bash
set -euo pipefail

VERSION="${1:-1.0.0}"
HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"
OUT="$HERE/out"
STAGE_ROOT="$(mktemp -d)"
STAGE="$STAGE_ROOT/fo4recordeditor_${VERSION}_amd64"
trap 'rm -rf "$STAGE_ROOT"' EXIT

echo ">> building the React bundle"
cd "$ROOT/web"
[ -d node_modules ] || npm ci
npm run build

echo ">> publishing the server (self-contained linux-x64)"
cd "$ROOT"
rm -rf "$OUT"
dotnet publish FO4RecordEditor.Server/FO4RecordEditor.Server.csproj \
  -c Release -r linux-x64 --self-contained true \
  -p:PublishSingleFile=false -p:DebugType=none \
  -o "$OUT/publish"

echo ">> staging the package tree"
install -d "$STAGE/DEBIAN" \
           "$STAGE/opt/fo4recordeditor" \
           "$STAGE/usr/bin" \
           "$STAGE/usr/share/applications" \
           "$STAGE/usr/share/icons/hicolor/scalable/apps" \
           "$STAGE/usr/share/doc/fo4recordeditor"

cp -r "$OUT/publish/." "$STAGE/opt/fo4recordeditor/"
chmod +x "$STAGE/opt/fo4recordeditor/FO4RecordEditor.Server"

install -m 0755 "$HERE/fo4recordeditor.sh" "$STAGE/usr/bin/fo4recordeditor"
install -m 0644 "$HERE/fo4recordeditor.desktop" "$STAGE/usr/share/applications/fo4recordeditor.desktop"
install -m 0644 "$ROOT/web/public/favicon.svg" \
                "$STAGE/usr/share/icons/hicolor/scalable/apps/fo4recordeditor.svg"
install -m 0644 "$ROOT/LICENSE" "$STAGE/usr/share/doc/fo4recordeditor/copyright"
install -m 0644 "$ROOT/THIRD_PARTY_NOTICES.md" "$STAGE/usr/share/doc/fo4recordeditor/"

INSTALLED_KB="$(du -ks "$STAGE" | cut -f1)"

cat > "$STAGE/DEBIAN/control" <<EOF
Package: fo4recordeditor
Version: ${VERSION}
Section: devel
Priority: optional
Architecture: amd64
Maintainer: PRISMA User Interface Framework <noreply@users.noreply.github.com>
Installed-Size: ${INSTALLED_KB}
Depends: libc6 (>= 2.34), libgcc-s1, libstdc++6, zlib1g,
         libwebkit2gtk-4.1-0, libgtk-3-0
Recommends: zenity, wine
Homepage: https://github.com/PRISMA-USER-INTERFACE-FRAMEWORK/PluginEditTool
Description: Fallout 4 plugin editor and conflict resolver
 A Mutagen-based editor for Fallout 4 plugins: browse and edit records, resolve
 conflicts across a full Mod Organizer 2 load order, inspect cells in 3D, read
 BA2/BSA archives, compile and decompile Papyrus, and drive all of it from an AI
 assistant over MCP.
 .
 The interface is a WebKitGTK window driven by a local process; nothing is sent
 anywhere. Run with --browser to use a browser window instead, or --headless to
 serve the UI without opening anything.
 .
 A few bundled helpers (niftool, texconv, Archive2, PapyrusCompiler, xWMAEncode)
 exist only as Windows binaries; install wine to use the panels that drive them.
 Everything else is native.
EOF

cat > "$STAGE/DEBIAN/postinst" <<'EOF'
#!/bin/sh
set -e
if [ "$1" = "configure" ]; then
    command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database -q || true
    command -v gtk-update-icon-cache >/dev/null 2>&1 && \
        gtk-update-icon-cache -q /usr/share/icons/hicolor 2>/dev/null || true
fi
EOF
chmod 0755 "$STAGE/DEBIAN/postinst"

echo ">> building the .deb"
DEB="$OUT/fo4recordeditor_${VERSION}_amd64.deb"
fakeroot dpkg-deb --build --root-owner-group "$STAGE" "$DEB" > /dev/null

echo
echo "Built: $DEB"
ls -lh "$DEB" | awk '{print "Size:  " $5}'
echo
echo "Install with:  sudo apt install \"$DEB\""
