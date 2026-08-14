#!/bin/sh

set -e
APP_DIR=/opt/fo4recordeditor

unset DOTNET_ROOT

exec "$APP_DIR/FO4RecordEditor.Server" "$@"
