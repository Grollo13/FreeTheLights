# FreeTheLights

**Free & open-source LED control for your sim rig**

A [SimHub](https://www.simhubdash.com/) plugin that drives the LEDs of sim racing hardware (wheels, dashboards, stand-alone LED bars and LED flags) with:

1. **Car-accurate RPM / shift lights**: LED count, colors, per-gear RPM thresholds and redline blinking from [lovely-car-data](https://github.com/Lovely-Sim-Racing/lovely-car-data), scaled to the number of LEDs your hardware has.
2. **Race control flags**: yellow, blue, white and checkered, the debris flag (yellow/red stripes; iRacing only, since other games don't report it to SimHub), plus optional black, green and orange (meatball). Flags can have a second color (checkered, meatball, debris, ...).
3. **Car proximity**: lights up when a car is next to you. When the sim reports the distance, more LEDs light up as the other car gets closer.
4. **ABS indication**
5. **Traction control indication**

Wheels can also have illuminated **buttons, lights and dials** on the front plate. Each one has a static color and can show one of the effects above. Buttons and dials can also change color (or turn off) while you press the button or turn the dial.

## How it works

FreeTheLights does not talk to hardware itself. You describe each device's LED layout in the plugin, and the plugin computes a frame of colors for that device every game update. You then add the **FreeTheLights** effect to the device's LED profile in SimHub. SimHub's own drivers send the colors to any device it already supports.

LEDs that FreeTheLights does not drive stay transparent, so effects below it in the profile still show.

## Install and update

1. Close SimHub.
2. Download `FreeTheLights.dll` from the latest [release](https://github.com/Grollo13/FreeTheLights/releases/latest) and copy it into SimHub's folder (by default `C:\Program Files (x86)\SimHub\`).
3. Start SimHub and enable the plugin when it asks.

To update, use the plugin itself. It looks for a new release once a day, and the card at the bottom of its sidebar shows the version you have. When there's a new one, click **Update plugin**. If SimHub is installed under `Program Files`, Windows asks for administrator rights. Restart SimHub afterwards to use the new version. **What's new** opens the release notes. Turn off **Check for updates** to stop it from looking.

## Build

Requirements: Windows, SimHub, and MSBuild with the .NET Framework 4.8 build tools (Visual Studio or Build Tools; a .NET Framework 4.8 developer pack is not needed).

```
msbuild FreeTheLights.sln -restore -p:Configuration=Release
```

- SimHub assemblies are referenced from `%SIMHUB_INSTALL_PATH%`, or from `C:\Program Files (x86)\SimHub\` if that variable is not set.
- A successful build copies `FreeTheLights.dll` into the SimHub folder. Pass `-p:DeployToSimHub=false` to skip this. The copy fails while SimHub is running, and it may need write access to `Program Files`.
- To run the tests: `%USERPROFILE%\.nuget\packages\xunit.runner.console\2.9.3\tools\net48\xunit.console.exe tests\FreeTheLights.Tests\bin\Release\FreeTheLights.Tests.dll`

## Setup in SimHub

1. Start SimHub and enable the FreeTheLights plugin when it asks. It then appears in the left menu.
2. **Devices**: click **Add** to add one entry per LED device. A wizard asks for:

   - the device: pick it from SimHub's list of devices (wheels, dashboards, LED flags, ... with their product pictures; searchable), or a generic wheel, dashboard, LED flag, LED bar or custom device
   - for a SimHub device: what kind of device it is. SimHub doesn't say, so FreeTheLights suggests one and you confirm or change it
   - the number of LEDs: for generic devices, and for SimHub dashboards, where the split into RPM LEDs on top and LEDs on the sides is counted on SimHub's picture of the dashboard or suggested from SimHub's data
   - for SimHub dashboards: where the LEDs are on SimHub's picture of the dashboard, for the preview (see below)
   - for generic wheels: the front plate layout (dials and buttons/lights)
   - the features to show

   **Devices with several LED channels.** SimHub drives some devices' LEDs as separate channels, each with its own LED profile: for example a wheel's shift lights and its button LEDs. FreeTheLights shows such a device as one card with one **part** per channel. Each part has its own layout, features, colors, priority and brightness; switch between them above the feature tabs.

   **Wheels from SimHub** take the positions and LED numbers of their buttons from SimHub's button layout, and the preview shows them on SimHub's photo of the wheel. Names, button/light/dial type, colors and effects of the buttons can be changed (click a button in the preview). The layout is copied when the device is added; **Refresh from SimHub** (header) updates it after a SimHub update.

   **Dashboards from SimHub** are previewed on SimHub's picture of the dashboard, with each LED drawn where it is. SimHub doesn't say where the LEDs are on its pictures, so FreeTheLights uses its own list of reviewed positions for over 20 dashboards (as long as SimHub's picture is unchanged), else tries to find the LEDs on the picture. When neither works, drag the LEDs to their places, or use the generic dashboard drawing instead. Dragging the first or last LED of a group stretches the whole group. The picture button in the device header changes the positions later.
3. **Fine-tune in the preview**: the device's layout comes from the wizard (or from SimHub). Click any LED in the device preview to adjust it in the panel next to it:

   - **A row, column or matrix** (shift lights, side LEDs, LED flag): the whole group is selected. Set its name, LED count (or columns and rows), **start index** (the position of its first LED on the device's LED strip, as numbered in SimHub's LED editor), side, wiring direction, and which features show on it. A warning under the preview tells when two groups use the same LED index.
   - **A button, light or dial**: see [Front plate](#front-plate); Ctrl-click to select several.

   Hover over an LED to see its physical index. To change the layout itself (other groups, another front plate layout or another device type), add the device again with the wizard.
   **LED wiring** (card below the preview): once the device is connected in SimHub (step 5), turn on **Test pattern**. Each row, column or matrix lights in its own color and a bright dot runs from its first to its last wired LED, on the device and in the preview at the same time. Where they differ, drag the groups into the order they are chained on the LED strip, or flip a group's direction (starts on the right / at the top). **Stop all tests** turns the pattern off.
4. **Features**: below the test box, one tab per feature (RPM lights only for parts with a row of LEDs), plus the priority order.
5. In SimHub, open the device's LED editor and add the **FreeTheLights** effect (Effects group) to the profile of each channel. Put it on top, then pick the FreeTheLights device and, for devices with several parts, the part for that channel. For LED flag (matrix) devices, use the FreeTheLights matrix effect and choose the matrix group.

   For devices from SimHub's list, the **SimHub setup** card on the device page shows for each part whether it is connected, and **Connect** sets it up for you: it creates FreeTheLights' own LED profile for the part (named like "FreeTheLights: My dash") and makes it the active profile. Profiles of your own or of other plugins are never changed; you can switch back to them in SimHub's LED editor at any time. SimHub's device settings are backed up to `PluginsData\Common\FreeTheLights\backups` first.
6. Use the **Test** chips (RPM sweep, flags, car left/right, ABS, TC) to check the LEDs without driving. The live preview shows what the device displays.

Your settings are kept when you update FreeTheLights: settings saved by an older version are migrated to the new format. A copy of the old settings is kept in `PluginsData\Common\FreeTheLights\backups` first.

## Feature details

### RPM lights

- **Targets**: horizontal rows only.
- **Car data lookup**:
  - The car comes from SimHub's `DataCorePlugin.CurrentGame` and `CarId`.
  - The car is looked up in lovely-car-data's `manifest.json`. File names do not always match the normalized car id.
  - The data is downloaded from GitHub and cached in `SimHub\PluginsData\Common\FreeTheLights\cardata`, then refreshed every 24 h. Offline, the cached copy is used.
  - The **Car data** card in the sidebar shows which lovely-car-data is in use, for example `lovely-car-data v2.0.0 · main @ d5d6893 (2026-10-09)`. That is the file format version from the changelog, the branch, and the last commit that changed the data. It also shows when the current car's file was downloaded.
- **Device with more LEDs than the car**: the device shows the car's LEDs one to one in the middle of the row, so it looks like the real car. The RPM lights leave the outer LEDs transparent, so a feature lower in the priority list, or an effect below FreeTheLights in the SimHub profile, can still light them. For example, the Ferrari 296 GT3 has 6 LEDs, so a 16-LED dashboard uses its middle 6. If the car's LEDs can't sit exactly in the middle, one LED is added in the center to keep the row symmetric: a car with an odd LED count shows its center LED on two LEDs, and on a car with an even count the added LED lights once both car LEDs next to it are lit (6 car LEDs on a 15-LED row use the middle 7).
- **Device with fewer LEDs than the car**: each device LED shows the car LED under its center position. This keeps the car's pattern: left-to-right fill, fill from both ends, and color bands, and mirror-symmetric car patterns stay symmetric. A device LED whose center car LED is off shows the nearest lit car LED it covers, so no car LED, and no shift stage, is skipped.
- **Gaps**: some cars have gaps in their row of LEDs (in the car file, LEDs with a transparent color), for example the McLaren 720S GT3. The device keeps an LED in each gap's place that the RPM lights never light (it stays transparent, like the outer LEDs above).
- **Redline**: a redline color replaces all LEDs except the gaps, and `redlineBlinkInterval` makes them blink.
- **Car not in lovely-car-data**: a generic green/yellow/red bar is used, which can be switched off.

### Flags

- **Targets**: vertical columns and matrices by default; any group can be selected.
- **Per-flag settings**: color 1, an optional color 2, pattern, animation, and priority order. Drag a flag by its handle to reorder flags.
- **Two-color flags**: the LEDs alternate between the colors (LED 1 = color 1, LED 2 = color 2, LED 3 = color 1, ...).
  - Checkered: white + off.
  - Meatball: orange + off.
  - On LED flags (matrices), the pattern can also be a large checkerboard or a diagonal split.
- **Animation**:
  - *Steady*
  - *Blink*: on/off.
  - *Swap colors*: two-color flags only; every LED alternates between color 1 and color 2 over time.
- **Default priority**: black, checkered, orange, yellow, debris, blue, white, green.

### Car proximity

- **Default**: yellow on the vertical columns.
- **Columns**: Left columns react to a car on the left and Right columns to a car on the right; columns without a side react to both.
- **Rows and matrices**: these use their left and right halves.
- **Distance**: when the sim reports the distance, the number of lit LEDs grows as the gap shrinks. One LED is lit at the configured distance and all LEDs at 0 m.

### ABS / TC

- **Default**: ABS in yellow, traction control in blue, on the vertical columns; blinking optional. Both colors can be changed.
- **Hold**: activations are held for 100 ms, so single-frame reports don't flicker.

### Front plate

- **Elements**: buttons, non-button lights and dials, one LED each.
- **Layout** (chosen in the wizard for generic wheels; SimHub's wheels bring their own):
  - The layout is always mirror-symmetric. The right side mirrors the left side, and dials sit in the center.
  - One of 16 dial presets (0 to 6 dials, e.g. "3 dials: 1 on top, 2 below" or "2 on top, 1 below").
  - One of 14 presets for the buttons/lights on each side (up to 12 per side).
- **Per element**: click an element in the live preview, and the panel next to the preview shows its settings:
  - name
  - type (button or light; dials have none to choose)
  - LED index on the strip
  - static color, and a **Static colors** switch for the whole part (off: elements stay dark unless an effect is active)
  - one effect: race control flags, car proximity, ABS or traction control
  - buttons and dials only: **when pressed**, a color shown while the button is pressed or the dial turned (at least 50 ms per dial step, so quick turns are seen; black turns it off), and the element's **input**. A button has one input: click **Assign**, then press the button on the wheel. A dial has one input per direction: assign **Turn clockwise** and **Turn counter-clockwise** by turning the dial one step that way.
  - a **Press colors** switch for the whole part
- **Several elements at once**: Ctrl-click (or Shift-click) more elements in the preview, or use the **Select all** buttons (buttons, lights, dials, all) while nothing is selected. Changes apply to every selected element; a field shows "different colors" or "different effects" where they differ, until you change it. Clicking empty space clears the selection.
- **How effects and static colors combine**:
  - While the effect is active, it replaces the static color.
  - While the element is pressed, its press color replaces both (with the default priority).
  - Car proximity on a left element reacts to a car on the left, and on a right element to a car on the right. Dials react to both.
- **Wiring**: the wizard numbers the elements after the other LEDs, in the order left side, dials, right side.

### Priority

- When features overlap on the same LED, the order on the Priority tab decides which one wins. Reorder it by drag & drop.
- **Default order**: button press > Flags > Proximity > ABS > TC > RPM > static colors.

## SimHub properties

- **Status**: `FreeTheLights.CarDataStatus`, `FreeTheLights.CarDataVersion`, `FreeTheLights.CarName`, `FreeTheLights.CarLedCount`, `FreeTheLights.FlagName`.
- **Action**: `FreeTheLights.ReloadCarData`.

## License

FreeTheLights is free software: you can redistribute it and/or modify it under the terms of the [GNU General Public License](LICENSE.md), version 3 or (at your option) any later version. Modified versions you share must be published under the same license, with their source code.

Because FreeTheLights is a plugin for SimHub, which is not free software, it comes with an additional permission to combine it with SimHub and the libraries distributed with it. See [LICENSE-EXCEPTION.md](LICENSE-EXCEPTION.md).
