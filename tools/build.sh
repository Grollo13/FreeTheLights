#!/bin/sh
# Builds the development tools into tools/bin (not part of the solution). Build the solution first:
# the dashboard probe references src/FreeTheLights/bin/Debug/FreeTheLights.dll.
T="$(cd "$(dirname "$0")" && pwd)"
R="$(dirname "$T")"
F="C:/Windows/Microsoft.NET/Framework64/v4.0.30319"
CSC="/c/Program Files/Microsoft Visual Studio/18/Community/MSBuild/Current/Bin/Roslyn/csc.exe"
WPF="-r:$F/WPF/PresentationFramework.dll -r:$F/WPF/PresentationCore.dll -r:$F/WPF/WindowsBase.dll -r:$F/System.Xaml.dll -r:$F/System.Core.dll"
mkdir -p "$T/bin"

# UI harness: loads the plugin by reflection; only its fake wizard host (FakeWizardHost) is compiled against it.
"$CSC" -nologo -nowarn:162 -target:exe -platform:anycpu32bitpreferred -out:"$T/bin/Harness.exe" \
  $WPF -r:"$F/Microsoft.CSharp.dll" -r:"$R/src/FreeTheLights/bin/Debug/FreeTheLights.dll" "$T/harness/Harness.cs" || exit 1

# Dashboard probe: x86 like SimHub; CS8012 (processor architecture mismatch) is expected.
"$CSC" -nologo -nowarn:8012 -target:exe -platform:x86 -out:"$T/bin/DashProbe.exe" \
  $WPF -r:"$R/src/FreeTheLights/bin/Debug/FreeTheLights.dll" "$T/dashprobe/DashProbe.cs" || exit 1
echo "built tools/bin/Harness.exe and tools/bin/DashProbe.exe"
