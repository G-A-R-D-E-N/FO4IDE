#!/usr/bin/env bash
#
# Builds the vanilla (.psc, .pex) corpus the Creation Kit differential compares against.
#
# The shipped compiled scripts are not loose on disk: they live inside "Fallout4 - Misc.ba2".
# This extracts them with the tool's own BA2 reader and stages the vanilla sources beside them in
# the layout PapyrusDifferentialTests pairs on:
#
#     <out>/scripts/X.pex   with   <out>/scripts/Source/X.psc
#
# Usage:
#   tools/build-vanilla-pex-corpus.sh <FalloutDataDir> <VanillaSourceDir> <OutDir>
#
# Then, and note the release flag, because the shipped binaries are release builds with DebugOnly
# and BetaOnly stripped. Without it every script that calls Debug.Trace reports a difference that is
# not a compiler defect, which measured about eleven points on this corpus:
#
#   FO4RE_PEX_RELEASE=1 \
#   FO4RE_PEX_CORPUS=<OutDir> \
#   FO4RE_PSC_ROOTS=<VanillaSourceDir> \
#     dotnet test FO4RecordEditor.Core.Tests/FO4RecordEditor.Core.Tests.csproj \
#       --filter "FullyQualifiedName~PapyrusDifferentialTests"
#
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

# The extractor accepts "glob" as the RPC alias for wildcard mode, splits archive separators with
# the host platform's directory separator, and merges case-only directory variants. A clean
# Fallout4 - Misc.ba2 extraction therefore produces one lowercase scripts tree directly.
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
