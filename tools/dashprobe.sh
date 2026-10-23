#!/bin/sh
# Runs the dashboard LED detector over every SimHub dashboard picture (see tools/README.md). Build the solution and
# tools/build.sh first. Contact sheets go to tools/bin/dash/. With a file argument, the reviewed-candidate list is also
# written as JSON in the format of src/FreeTheLights/Settings/DashboardLayouts.json.
#   tools/dashprobe.sh [layouts.json]
T="$(cd "$(dirname "$0")" && pwd)"
rm -rf "$T/bin/dash"
"$T/bin/DashProbe.exe" "$@" | grep -v "NO PICTURE"
