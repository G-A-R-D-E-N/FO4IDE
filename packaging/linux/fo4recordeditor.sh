#!/bin/sh

set -e
APP_DIR=/opt/fo4ide

unset DOTNET_ROOT

exec "$APP_DIR/FO4RecordEditor.Server" "$@"
