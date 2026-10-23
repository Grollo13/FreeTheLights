# Development tools

Not part of the solution and not shipped. Build the solution first, then `sh tools/build.sh`. Outputs go to `tools/bin/` (ignored by git).

## Build, test and deploy (`dev.sh`)

Builds the plugin in Debug from any folder. MSBuild is found with `vswhere` (override with `MSBUILD=...`), the xunit console in the NuGet cache (override with `XUNIT=...`), both through `msbuild.sh`.

```bash
sh tools/dev.sh build     # build without copying the plugin into SimHub
sh tools/dev.sh test      # build, then run all tests
sh tools/dev.sh deploy    # build and copy FreeTheLights.dll into SimHub (close SimHub first)
```

In cmd or PowerShell, where Git for Windows' `sh` is not on the PATH, use `toolsdev.cmd build|test|deploy`, which finds it through `git`. VS Code's tasks (`build`, `build and deploy`, `test`; Ctrl+Shift+B / the test task) call these scripts. The plugin only builds on Windows (WPF, SimHub's DLLs).

## UI harness (`harness/Harness.cs`, `harness.sh`)

Renders the plugin's WPF UI off-screen to PNG files, without SimHub running. It loads `src/FreeTheLights/bin/Debug/FreeTheLights.dll` and SimHub's DLLs by reflection (only its fake wizard host is compiled against the plugin), merges SimHub's theme dictionaries into an `Application`, fakes LED frames, and prints WPF binding errors (grep the output for `Error`).

```bash
sh tools/harness.sh tools/bin/out settings 1100 900          # settings page, one PNG per feature tab
sh tools/harness.sh tools/bin/out settings 1100 2000         # taller window: the whole page
sh tools/harness.sh tools/bin/out wizard                     # walks the device wizard step by step
```

Environment variables (set them for one run, e.g. `HARNESS_PRESET="CubeControls F-PRO" sh tools/harness.sh ...`):

| Variable | Effect |
|---|---|
| `HARNESS_PRESET` | Adds that SimHub device (name substring, e.g. `CubeControls F-PRO`, `Ascher Racing 4"/5"`) as the first device, built through the wizard; in `wizard` mode, picks it in the wizard |
| `HARNESS_KIND` | Device kind for the preset (`Wheel`, `Dashboard`, ...) |
| `HARNESS_HOST` | `1`: in `wizard` mode, a fake SimHub (`FakeWizardHost`): the device is added in SimHub, Connect succeeds (saves `*_connected.png`), the wiring step's first group is shortened to 15 LEDs and two columns are added (`*_edited.png`), two inputs are pressed on the buttons step (`*_pressed.png`; `HARNESS_PRESSES=n`: n inputs, a PNG after each). Without it the SimHub profile, wiring and buttons steps are skipped |
| `HARNESS_GENERIC` | `1`: the preset dashboard uses the generic drawing instead of its picture |
| `HARNESS_SELECT` | `element`, `multi`, `multi-mixed`, `group`, `matrix`, `none`: selects LEDs in the preview inspector before rendering; `press` (a button with a press color and its input) and `capture` (a dial waiting for its counter-clockwise input) also save the panel scrolled to the end (`card_*_end.png`) |
| `HARNESS_PART` | Selects the part with this name |
| `HARNESS_TWOPARTS` | `1`: first device gets a second part |
| `HARNESS_DEVICE` | Selects the device with this name |
| `HARNESS_CHIPS` | `1`: turns on some test chips |
| `HARNESS_EMPTY` | `1`: no devices (empty state) |
| `HARNESS_DIALOG` | `1`: renders the LED positions dialog of the first device instead; `wiring`: the wiring dialog of its first part (`wiring_dialog.png`) |
| `HARNESS_PREVIEW_SELECT` / `HARNESS_PREVIEW_CLICK` | Selection rings / real mouse clicks on the preview |
| `HARNESS_TRACETEST` | Checks that binding-error tracing works |
| `HARNESS_UPDATE` | The sidebar's update card in a state, with a fake release 0.2.0 of an installed 0.1.0 (nothing is downloaded): `available`, `installing`, `installed`, `failed`, `uptodate`, `checkfailed`, `off` |

Read the PNGs to check layout; `card_*.png` crops the preview card.

## Dashboard probe (`dashprobe/DashProbe.cs`, `dashprobe.sh`)

Runs `Settings/DashLedDetector` over every SimHub device with a display and LEDs that the kind guess calls a dashboard, prints the per-mode results, and renders contact sheets with the detected LEDs outlined (`tools/bin/dash/sheet*.png`). With a file argument it also writes the detected layouts (with the picture's SHA-256) in the format of `src/FreeTheLights/Settings/DashboardLayouts.json`:

```bash
sh tools/dashprobe.sh tools/bin/candidates.json
```

Review every overlay by eye before copying an entry into `DashboardLayouts.json`; the probe's built-in exclusion list (`exclude`) holds devices whose match was wrong.

## Installer (`installer.sh`)

Builds `FreeTheLights-Setup-X.Y.Z.exe` from `installer/FreeTheLights.iss` with Inno Setup 6 (`winget install JRSoftware.InnoSetup`). `ISCC.exe` is found in Inno Setup's per-user or all-users folder (override with `ISCC=...`). X.Y.Z is the version of the DLL it contains.

```bash
sh tools/installer.sh                    # the Release build's DLL, into tools/bin/installer
sh tools/installer.sh <dll> <outdir>     # what release.sh does
```

## Release script (`release.sh`)

Publishes the version in `src/FreeTheLights/Properties/AssemblyInfo.cs` as a GitHub release (see "Releases" in `CONTRIBUTING.md`). It needs the GitHub CLI (`gh`), logged in as an admin of the repository, Inno Setup 6, and a clean `main` that matches `origin/main`. It builds the solution in Release (warnings fail the build), runs the tests, builds the installer, tags `vX.Y.Z`, and creates the release with `FreeTheLights.dll`, `FreeTheLights-Setup-X.Y.Z.exe`, their `.sha256` files and notes generated by GitHub from the pull requests merged since the previous release (`.github/release.yml`).

```bash
sh tools/release.sh -d    # as a draft: review the notes, then publish it on GitHub
sh tools/release.sh       # publish at once
```

MSBuild is found with `vswhere` (override with `MSBUILD=...`), the xunit console in the NuGet cache (override with `XUNIT=...`).
