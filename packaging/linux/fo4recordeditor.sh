#!/bin/sh
# FO4RecordEditor launcher.
#
# Starts the local server and opens the UI. Every argument is passed straight through, so the
# headless MCP mode works the same way it does on Windows:
#   fo4recordeditor --mcp --mo2 "/path/to/MO2 instance"
set -e
APP_DIR=/opt/fo4recordeditor

# DOTNET_ROOT set for a Linux SDK confuses the wine shim that runs the Windows-only helper tools.
unset DOTNET_ROOT

exec "$APP_DIR/FO4RecordEditor.Server" "$@"
