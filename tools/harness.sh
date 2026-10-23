#!/bin/sh
# Renders the plugin's UI off-screen to PNG files (see tools/README.md). Build the solution and tools/build.sh first.
#   tools/harness.sh <outdir> settings <width> <height> [tabs|all] [tab|drag|popup]
#   tools/harness.sh <outdir> wizard
T="$(cd "$(dirname "$0")" && pwd)"
R="$(dirname "$T")"
OUT="$1"; shift
mkdir -p "$OUT"
"$T/bin/Harness.exe" "$R/src/FreeTheLights/bin/Debug" "$OUT" "$@"
