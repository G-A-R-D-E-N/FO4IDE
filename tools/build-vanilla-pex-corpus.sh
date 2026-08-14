#!/usr/bin/env bash

set -euo pipefail

DATA=${1:-}
SOURCES=${2:-}
OUT=${3:-}

if [[ -z $DATA || -z $SOURCES || -z $OUT ]]; then
    echo "usage: $0 <FalloutDataDir> <VanillaSourceDir> <OutDir>" >&2
    exit 2
fi

BA2="$DATA/Fallout4 - Misc.ba2"
[[ -f $BA2 ]] || { echo "no archive at $BA2" >&2; exit 1; }
[[ -d $SOURCES ]] || { echo "no source tree at $SOURCES" >&2; exit 1; }

HERE=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
SERVER="$HERE/FO4RecordEditor.Server/bin/Release/net9.0/FO4RecordEditor.Server.dll"
[[ -f $SERVER ]] || {
    echo "build the server first: dotnet build FO4RecordEditor.Server/FO4RecordEditor.Server.csproj -c Release" >&2
    exit 1
}

rm -rf "$OUT"
mkdir -p "$OUT"

PORT=${FO4RE_CORPUS_PORT:-44987}
dotnet "$SERVER" --host 127.0.0.1 --port "$PORT" --headless >"$OUT/.server.log" 2>&1 &
SERVER_PID=$!
trap 'kill $SERVER_PID 2>/dev/null || true' EXIT

for _ in $(seq 1 60); do
    curl -sf --max-time 2 "http://127.0.0.1:$PORT/api/health" >/dev/null && break
    sleep 1
done
curl -sf --max-time 2 "http://127.0.0.1:$PORT/api/health" >/dev/null || { echo "server did not start" >&2; exit 1; }

echo "extracting..."
jq -n --arg a "$BA2" --arg o "$OUT" \
    '{target:"archive",method:"ExtractAll",args:[$a,$o,"*.pex",20000,"glob"]}' \
    | curl -sf --max-time 900 -X POST "http://127.0.0.1:$PORT/rpc" \
        -H 'Content-Type: application/json' --data @- \
    | sed 's/\\"/"/g'

echo "staging sources..."
cp -r "$SOURCES" "$OUT/scripts/Source"

echo
echo "corpus at $OUT"
echo "  .pex: $(find "$OUT/scripts" -name '*.pex' | wc -l)"
echo "  .psc: $(find "$OUT/scripts/Source" -name '*.psc' | wc -l)"
