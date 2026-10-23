# Sourced by the other scripts: sets MSBUILD (Visual Studio's or the Build Tools', found with vswhere; override with
# MSBUILD=...) and XUNIT (the xunit console in the NuGet cache; override with XUNIT=...).
if [ -z "$MSBUILD" ]; then
  VSWHERE="/c/Program Files (x86)/Microsoft Visual Studio/Installer/vswhere.exe"
  if [ -x "$VSWHERE" ]; then
    MSBUILD="$("$VSWHERE" -latest -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | head -n 1 | tr -d '\r')"
  fi
  [ -n "$MSBUILD" ] || MSBUILD=msbuild
fi
XUNIT="${XUNIT:-$HOME/.nuget/packages/xunit.runner.console/2.9.3/tools/net48/xunit.console.exe}"
