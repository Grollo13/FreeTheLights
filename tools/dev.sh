#!/bin/sh
# Builds, tests and deploys the plugin (Debug), from anywhere.
#
#   sh tools/dev.sh build     build the solution without copying the plugin into SimHub
#   sh tools/dev.sh test      build, then run all tests
#   sh tools/dev.sh deploy    build and copy FreeTheLights.dll into SimHub's folder (close SimHub first; the folder
#                             may need administrator rights)
set -e
T="$(cd "$(dirname "$0")" && pwd)"
cd "$(dirname "$T")"
. "$T/msbuild.sh"

build() { "$MSBUILD" FreeTheLights.sln -restore -nologo -v:m -p:DeployToSimHub="$1"; }
test_all() { "$XUNIT" tests/FreeTheLights.Tests/bin/Debug/FreeTheLights.Tests.dll -nologo -noshadow; }

case "$1" in
  build) build false ;;
  test) build false; test_all ;;
  deploy) build true ;;
  *) echo "usage: sh tools/dev.sh build|test|deploy" >&2; exit 2 ;;
esac
