#!/bin/sh
# Builds the installer FreeTheLights-Setup-X.Y.Z.exe (installer/FreeTheLights.iss) with Inno Setup 6 around a
# FreeTheLights.dll; X.Y.Z is the DLL's version. Inno Setup's ISCC.exe is found in its default folders (per user or for
# all users; override with ISCC=...).
#
#   sh tools/installer.sh                  the Release build's DLL, into tools/bin/installer
#   sh tools/installer.sh <dll> <outdir>   (release.sh)
set -e
T="$(cd "$(dirname "$0")" && pwd)"
R="$(dirname "$T")"

fail() { echo "installer: $*" >&2; exit 1; }

DLL="${1:-$R/src/FreeTheLights/bin/Release/FreeTheLights.dll}"
OUT="${2:-$T/bin/installer}"
[ -f "$DLL" ] || fail "$DLL not found: build the Release configuration first (msbuild FreeTheLights.sln -restore -p:Configuration=Release -p:DeployToSimHub=false)"

if [ -z "$ISCC" ]; then
  for candidate in \
    "$(cygpath -u "${LOCALAPPDATA:-}")/Programs/Inno Setup 6/ISCC.exe" \
    "/c/Program Files (x86)/Inno Setup 6/ISCC.exe" \
    "/c/Program Files/Inno Setup 6/ISCC.exe"; do
    if [ -f "$candidate" ]; then ISCC="$candidate"; break; fi
  done
fi
[ -n "$ISCC" ] || fail "Inno Setup 6 is not installed (winget install JRSoftware.InnoSetup), or set ISCC=<path to ISCC.exe>"

mkdir -p "$OUT"
# ISCC's switches start with '/': keep Git Bash from turning them into paths.
MSYS2_ARG_CONV_EXCL='*' "$ISCC" /Q "/DSourceDll=$(cygpath -w "$DLL")" "/O$(cygpath -w "$OUT")" "$(cygpath -w "$R/installer/FreeTheLights.iss")"
ls "$OUT"/FreeTheLights-Setup-*.exe
