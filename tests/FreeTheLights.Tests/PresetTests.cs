using System;
using System.IO;
using System.Linq;
using FreeTheLights.Core;
using FreeTheLights.Settings;
using FreeTheLights.UI;
using Xunit;

namespace FreeTheLights.Tests
{
    /// <summary>SimHub device presets: reading .shdevice data, guessing the kind, building and refreshing devices.</summary>
    public class PresetTests
    {
        /// <summary>A dashboard like SimHub's Ascher 4"/5" dash: 21 LEDs, the RPM effect split into three segments from LED 4.</summary>
        internal const string Dashboard = @"{ ""DeviceTypeID"": ""3799fbb8-51f3-4329-a759-13c3976d6612"", ""DeviceTypeName"": ""Ascher Racing 4\"" Dashboard"",
            ""Settings"": { ""LCD"": { ""display"": {} }, ""LEDS"": { ""leds"": { ""ForcedLedCount"": 21, ""activeProfileId"": ""p1"", ""Profiles"": [ { ""ProfileId"": ""p1"", ""LedContainers"": [
                { ""ContainerType"": ""Groups.GameRunningGroupContainer"", ""LedContainers"": [
                    { ""ContainerType"": ""Flags.YellowFlagContainer"", ""StartPosition"": 1, ""LedCount"": 2 },
                    { ""ContainerType"": ""RPMSegmentsContainer"", ""StartPosition"": 14, ""LedCount"": 5 },
                    { ""ContainerType"": ""RPMSegmentsContainer"", ""StartPosition"": 9, ""LedCount"": 5 },
                    { ""ContainerType"": ""RPMSegmentsContainer"", ""StartPosition"": 4, ""LedCount"": 5 } ] } ] } ] },
                ""buttons"": null, ""matrix"": null } } }";

        /// <summary>A wheel without display whose channels sit directly in the settings, like SimHub's Artura GT4.</summary>
        internal const string Wheel = @"{ ""DeviceTypeID"": ""BB8A5358-5AC4-4941-9410-23CA28635F4A"", ""DeviceTypeName"": ""Ascher Racing Artura GT4"",
            ""Settings"": { ""ledModuleSettings"": {}, ""leds"": null, ""buttons"": { ""ForcedLedCount"": 12, ""Profiles"": [] } } }";

        internal static SimHubButtonLayout GT4Layout()
        {
            var layout = new SimHubButtonLayout { PhotoFile = @"DevicesUI\gt4.jpg", CanvasWidth = 850, CanvasHeight = 455 };
            for (var n = 1; n <= 12; n++)
            {
                layout.Markers.Add(new ButtonMarker
                {
                    LedNumber = n,
                    X = n <= 6 ? 100 + n * 10 : 600 + n * 10,
                    Y = 40 + n,
                    Size = n == 3 ? 85 : n == 4 ? 40 : 45,
                    Visible = n != 6 && n != 7,
                });
            }

            return layout;
        }

        // ---------- .shdevice ----------

        [Fact]
        public void Parser_reads_name_id_display_and_channels()
        {
            var dash = ShDeviceParser.Parse(Dashboard);
            Assert.Equal("3799FBB8-51F3-4329-A759-13C3976D6612", dash.DeviceTypeId);
            Assert.Equal("Ascher Racing 4\" Dashboard", dash.Name);
            Assert.Equal("Ascher", dash.Brand);
            Assert.True(dash.HasDisplay);
            Assert.Equal(new[] { LedChannel.Leds }, dash.Channels.Keys);
            Assert.Equal(21, dash.CountOf(LedChannel.Leds));

            var wheel = ShDeviceParser.Parse(Wheel);
            Assert.False(wheel.HasDisplay);
            Assert.Equal(new[] { LedChannel.Buttons }, wheel.Channels.Keys);
            Assert.Equal(12, wheel.CountOf(LedChannel.Buttons));
            Assert.Null(wheel.SuggestedSplit);
        }

        [Fact]
        public void Parser_rejects_files_without_a_device()
        {
            Assert.Null(ShDeviceParser.Parse("not json"));
            Assert.Null(ShDeviceParser.Parse(@"{ ""DeviceTypeName"": ""no id"" }"));
        }

        [Fact]
        public void Rpm_segments_split_the_leds_into_sides_and_top()
        {
            var split = ShDeviceParser.Parse(Dashboard).SuggestedSplit;
            Assert.Equal("3/15/3", split.ToString());
            Assert.True(split.Confident);
        }

        [Fact]
        public void Split_without_counts_assumes_symmetric_sides_and_without_rpm_uses_one_row()
        {
            var noCounts = Newtonsoft.Json.Linq.JObject.Parse(@"{ ""LedContainers"": [ { ""ContainerType"": ""RPMContainer"", ""StartPosition"": 6 } ] }");
            var split = ShDeviceParser.SuggestSplit(23, noCounts);
            Assert.Equal("5/13/5", split.ToString());
            Assert.False(split.Confident);

            Assert.Equal("0/20/0", ShDeviceParser.SuggestSplit(20, null).ToString());
        }

        [Fact]
        public void Missing_led_count_is_estimated_from_the_default_profile()
        {
            var info = ShDeviceParser.Parse(@"{ ""DeviceTypeID"": ""x"", ""DeviceTypeName"": ""Some dash"", ""Settings"": { ""LEDS"": { ""leds"": { ""Profiles"": [ { ""LedContainers"": [
                { ""ContainerType"": ""RPMContainer"", ""StartPosition"": 3, ""LedCount"": 10 }, { ""ContainerType"": ""Flag"", ""StartPosition"": 14 } ] } ] } } } }");
            Assert.Equal(14, info.CountOf(LedChannel.Leds));
        }

        [Theory]
        [InlineData("Ascher Racing Artura GT4", "DefaultColorsEditorAscherRacingMcLarenArturaGT4")]
        [InlineData("Ascher Racing Artura PRO", "DefaultColorsEditorAscherRacingMcLarenArturaPro")]
        [InlineData("Cube Controls F-PRO", "DefaultColorsEditorCubeControlsFPRO")]
        [InlineData("Unknown Wheel", null)]
        public void Layouts_are_matched_by_the_words_of_the_name(string device, string expected)
        {
            var layouts = new[] { "DefaultColorsEditorAscherRacingMcLarenArturaGT4", "DefaultColorsEditorAscherRacingMcLarenArturaPro", "DefaultColorsEditorAscherRacingMcLarenArturaProXL", "DefaultColorsEditorCubeControlsFPRO" };
            Assert.Equal(expected, ShDeviceParser.MatchLayoutName(device, layouts));
        }

        // ---------- Kind guess ----------

        [Fact]
        public void Kind_guess_prefers_the_name_then_the_button_layout_then_capabilities()
        {
            SimHubDeviceInfo Info(string name, bool display, bool layout, params LedChannel[] channels)
            {
                var info = new SimHubDeviceInfo { DeviceTypeId = "x", Name = name, HasDisplay = display, ButtonLayoutName = layout ? "DefaultColorsEditorX" : null };
                foreach (var channel in channels) info.Channels[channel] = 10;
                return info;
            }

            // A wheel with a display, RPM LEDs and button LEDs, like a dashboard; SimHub's button layout gives it away.
            Assert.Equal(DeviceType.Wheel, DeviceKindGuess.Guess(Info("BavarianSimTec ΩPRO", true, true, LedChannel.Leds, LedChannel.Buttons)).type);
            Assert.Equal(DeviceType.Dashboard, DeviceKindGuess.Guess(Info("Ascher Racing 4\" Dashboard", true, false, LedChannel.Leds)).type);
            Assert.Equal(DeviceType.Wheel, DeviceKindGuess.Guess(Info("Ascher Racing Artura GT4", false, true, LedChannel.Buttons)).type);
            Assert.Equal(DeviceType.LedFlag, DeviceKindGuess.Guess(Info("3D-Simgear LED-Flag", false, false, LedChannel.Matrix)).type);
            Assert.Equal(DeviceType.Dashboard, DeviceKindGuess.Guess(Info("SimCore UD68", true, false, LedChannel.Leds)).type);
            Assert.Equal(DeviceType.LedBar, DeviceKindGuess.Guess(Info("Some strip", false, false, LedChannel.Leds)).type);
            Assert.Equal(DeviceType.Custom, DeviceKindGuess.Guess(Info("Nitro Concepts Immersion LED Box", false, false, LedChannel.Leds)).type);
            Assert.Equal(DeviceType.LedFlag, DeviceKindGuess.Guess(Info("Barrel matrix", false, false, LedChannel.Matrix)).type);
            Assert.Contains("button layout", DeviceKindGuess.Guess(Info("BavarianSimTec ΩPRO", true, true, LedChannel.Leds)).reason);
        }

        // ---------- Building ----------

        [Fact]
        public void Wheel_buttons_come_from_simhubs_layout()
        {
            var info = ShDeviceParser.Parse(Wheel);
            info.ButtonLayoutName = "DefaultColorsEditorAscherRacingMcLarenArturaGT4";
            var device = PresetBuilder.Build(info, DeviceType.Wheel, GT4Layout());

            Assert.Equal(DeviceType.Wheel, device.Type);
            Assert.Equal(info.DeviceTypeId, device.Preset.DeviceTypeId);
            Assert.Equal(@"DevicesUI\gt4.jpg", device.Preset.PhotoFile);
            var buttons = device.Parts.Single();
            Assert.Equal(LedChannel.Buttons, buttons.Channel);

            // LEDs 6 and 7 are hidden in SimHub's layout: no element, but their LED numbers stay unused.
            Assert.Equal(new[] { 1, 2, 3, 4, 5, 8, 9, 10, 11, 12 }, buttons.Elements.Select(e => e.StartIndex + 1).OrderBy(n => n));
            Assert.Equal(12, buttons.TotalLeds);
            Assert.Equal(ElementType.Dial, buttons.Elements.Single(e => e.StartIndex == 2).ElementType);
            Assert.Equal(ElementType.Light, buttons.Elements.Single(e => e.StartIndex == 3).ElementType);
            Assert.Equal(LedGroupSide.Left, buttons.Elements.Single(e => e.StartIndex == 0).Side);
            Assert.Equal(LedGroupSide.Right, buttons.Elements.Single(e => e.StartIndex == 11).Side);
            Assert.All(buttons.Elements, e => Assert.True(e.HasPhotoPosition));
            Assert.All(buttons.Elements, e => Assert.Contains(e.Id, buttons.StaticColors.TargetGroupIds));

            // SimHub's LED numbers are fixed: auto-numbering leaves them alone.
            buttons.PackStartIndices();
            buttons.PackFrontPlateIndices();
            Assert.Equal(new[] { 1, 2, 3, 4, 5, 8, 9, 10, 11, 12 }, buttons.Elements.Select(e => e.StartIndex + 1).OrderBy(n => n));
        }

        [Fact]
        public void Wheel_shift_lights_and_dashboards_use_the_suggested_split()
        {
            var dash = ShDeviceParser.Parse(Dashboard);
            var asDash = PresetBuilder.Build(dash, DeviceType.Dashboard).Parts.Single();
            Assert.Equal("RPM and side LEDs", asDash.Name);
            Assert.Equal(new[] { "Left side", "RPM LEDs", "Right side" }, asDash.Groups.Select(g => g.Name));
            Assert.Equal(new[] { 0, 3, 18 }, asDash.Groups.Select(g => g.StartIndex));
            // Wired as one chain: up the left side, along the top, down the right side.
            Assert.Equal(new[] { false, false, true }, asDash.Groups.Select(g => g.Reversed));
            Assert.Equal(18, asDash.Groups[2].PhysicalIndex(2));

            var asWheel = PresetBuilder.Build(dash, DeviceType.Wheel).Parts.Single();
            Assert.Equal("RPM LEDs", asWheel.Name);
            Assert.Equal(15, asWheel.Groups.Single(g => g.Kind == LedGroupKind.HorizontalRow).Count);

            var asBar = PresetBuilder.Build(dash, DeviceType.LedBar).Parts.Single();
            Assert.Equal(21, asBar.Groups.Single().Count);
        }

        [Fact]
        public void Refresh_keeps_element_settings_and_follows_simhubs_layout()
        {
            var info = ShDeviceParser.Parse(Wheel);
            var device = PresetBuilder.Build(info, DeviceType.Wheel, GT4Layout());
            var part = device.Parts.Single();
            var first = part.Elements.Single(e => e.StartIndex == 0);
            first.Name = "Pit limiter";
            first.StaticColor = "#FFFF0000";
            part.SetElementEffect(first, LayerKind.Flags);

            // SimHub moved LED 1, dropped LED 12 and shows LED 6.
            var layout = GT4Layout();
            layout.Markers.Single(m => m.LedNumber == 1).X = 77;
            layout.Markers.RemoveAll(m => m.LedNumber == 12);
            layout.Markers.Single(m => m.LedNumber == 6).Visible = true;
            var fresh = PresetBuilder.Build(info, DeviceType.Wheel, layout);

            Assert.Equal(1, PresetBuilder.CountRemovedByRefresh(device, fresh));
            var result = PresetBuilder.Refresh(device, fresh);
            Assert.Contains("1 added, 1 removed, 1 moved", result);

            Assert.Equal("Pit limiter", first.Name);
            Assert.Equal("#FFFF0000", first.StaticColor);
            Assert.Equal(LayerKind.Flags, part.GetElementEffect(first));
            Assert.Equal(77, first.PhotoX);
            Assert.DoesNotContain(part.Elements, e => e.StartIndex == 11);
            Assert.Contains(part.Elements, e => e.StartIndex == 5);
            Assert.Equal("Already up to date with SimHub.", PresetBuilder.Refresh(device, fresh));
        }

        // ---------- SimHub LED profiles ----------

        [Fact]
        public void Effects_in_simhubs_profiles_are_found_and_added_per_part()
        {
            var device = DeviceConfig.CreateDefault("Wheel");
            var buttons = new DevicePart { Name = "Buttons", Channel = LedChannel.Buttons };
            device.Parts.Add(buttons);

            // As SimHub stores it: an effect saved before parts existed has no PartId and shows the first part.
            var settings = Newtonsoft.Json.Linq.JObject.Parse(@"{ ""Settings"": { ""LEDS"": {
                ""leds"": { ""activeProfileId"": ""b"", ""Profiles"": [ { ""ProfileId"": ""a"", ""Name"": ""Old"", ""LedContainers"": [] },
                    { ""ProfileId"": ""b"", ""Name"": ""Default Profile"", ""LedContainers"": [ { ""ContainerType"": ""Groups.GameRunningGroupContainer"", ""LedContainers"": [
                        { ""DeviceId"": """ + device.Id + @""", ""ContainerType"": ""FreeTheLights.Effect.FreeTheLightsEffect"" } ] } ] } ] },
                ""buttons"": { ""Profiles"": [ { ""ProfileId"": ""c"", ""Name"": ""Buttons profile"", ""LedContainers"": [ { ""ContainerType"": ""Flags.YellowFlagContainer"" } ] } ] } } } }");

            var leds = SimHubProfiles.ActiveProfile(settings, LedChannel.Leds);
            Assert.Equal("Default Profile", (string)leds["Name"]);
            Assert.True(SimHubProfiles.Shows(leds, device, device.Parts[0]));
            Assert.False(SimHubProfiles.Shows(leds, device, buttons));

            var buttonProfile = SimHubProfiles.ActiveProfile(settings, LedChannel.Buttons);
            Assert.False(SimHubProfiles.Shows(buttonProfile, device, buttons));
            Assert.True(SimHubProfiles.AddEffect(buttonProfile, device, buttons));
            Assert.True(SimHubProfiles.Shows(buttonProfile, device, buttons));
            Assert.False(SimHubProfiles.AddEffect(buttonProfile, device, buttons));
            Assert.Equal(SimHubProfiles.StripEffectType, (string)buttonProfile["LedContainers"][0]["ContainerType"]);
            Assert.Equal(2, ((Newtonsoft.Json.Linq.JArray)buttonProfile["LedContainers"]).Count);

            // Another FreeTheLights device's effect doesn't count; a device without the channel has no profile.
            Assert.False(SimHubProfiles.Shows(leds, DeviceConfig.CreateDefault("Other"), device.Parts[0]));
            Assert.Null(SimHubProfiles.ActiveProfile(settings, LedChannel.Encoders));
        }

        [Fact]
        public void Effects_of_another_device_are_found_and_retargeted()
        {
            var wheel = PresetBuilder.Build(ShDeviceParser.Parse(Wheel), DeviceType.Wheel, GT4Layout());
            var dash = PresetBuilder.Build(ShDeviceParser.Parse(Dashboard), DeviceType.Dashboard);

            // The dashboard's LED profile shows the wheel's buttons (a new effect defaulted to the first device).
            var settings = Newtonsoft.Json.Linq.JObject.Parse(@"{ ""Settings"": { ""LEDS"": { ""leds"": { ""Profiles"": [ { ""ProfileId"": ""5d0e3f4c-8d43-4a59-8c51-2f4c1f2a9d10"", ""Name"": ""Default"",
                ""LedContainers"": [ { ""ContainerType"": ""Flags.YellowFlagContainer"" }, { ""DeviceId"": """ + wheel.Id + @""", ""ContainerType"": ""FreeTheLights.Effect.FreeTheLightsEffect"" } ] } ] } } } }");
            var profile = SimHubProfiles.ActiveProfile(settings, LedChannel.Leds);
            var dashPart = dash.Parts.Single();
            Assert.True(SimHubProfiles.ShowsOther(profile, dash, dashPart));
            Assert.False(SimHubProfiles.Shows(profile, dash, dashPart));

            Assert.True(SimHubProfiles.RetargetEffect(profile, dash, dashPart));
            Assert.True(SimHubProfiles.Shows(profile, dash, dashPart));
            Assert.False(SimHubProfiles.ShowsOther(profile, dash, dashPart));
            Assert.Equal(2, ((Newtonsoft.Json.Linq.JArray)profile["LedContainers"]).Count);

            // The profile tells which SimHub device and channel it belongs to, and so which part to show.
            Assert.Equal(LedChannel.Leds, SimHubProfiles.ChannelOfProfile(settings, System.Guid.Parse("5d0e3f4c-8d43-4a59-8c51-2f4c1f2a9d10")));
            Assert.Null(SimHubProfiles.ChannelOfProfile(settings, System.Guid.NewGuid()));
            var devices = new[] { wheel, dash };
            Assert.Same(dashPart, SimHubProfiles.FindPartFor(devices, dash.Preset.DeviceTypeId, LedChannel.Leds).part);
            Assert.Same(wheel.Parts.Single(), SimHubProfiles.FindPartFor(devices, wheel.Preset.DeviceTypeId.ToLowerInvariant(), LedChannel.Buttons).part);
            Assert.Null(SimHubProfiles.FindPartFor(devices, wheel.Preset.DeviceTypeId, LedChannel.Leds).part);
            Assert.Null(SimHubProfiles.FindPartFor(devices, "unknown", LedChannel.Leds).device);
        }

        [Fact]
        public void Profiles_are_found_in_the_settings_simhub_returns_at_runtime()
        {
            // DeviceInstance.GetSettings returns what a saved device file holds under "Settings", without that key:
            // a composite device (a dashboard: LEDs + display) one key per component, a wheel its channels directly.
            var wheel = PresetBuilder.Build(ShDeviceParser.Parse(Wheel), DeviceType.Wheel, GT4Layout());
            var dash = PresetBuilder.Build(ShDeviceParser.Parse(Dashboard), DeviceType.Dashboard);
            var dashRuntime = Newtonsoft.Json.Linq.JObject.Parse(@"{ ""LEDS"": { ""leds"": { ""activeProfileId"": ""e941c572-55a8-456a-90f0-994af6228ab8"", ""Profiles"": [
                    { ""ProfileId"": ""21a3d50e-830f-428d-91b9-0e03fbb21473"", ""Name"": ""ATSR"", ""LedContainers"": [] },
                    { ""ProfileId"": ""e941c572-55a8-456a-90f0-994af6228ab8"", ""Name"": ""New profile"", ""LedContainers"": [
                        { ""ContainerType"": ""FreeTheLights.Effect.FreeTheLightsEffect"", ""DeviceId"": """ + dash.Id + @""", ""PartId"": ""00000000-0000-0000-0000-000000000000"" } ] } ] } },
                ""LCD"": { ""display"": {} } }");
            var wheelRuntime = Newtonsoft.Json.Linq.JObject.Parse(@"{ ""buttons"": { ""activeProfileId"": ""6fc8f975-b566-4be0-bfc2-2eb894e78d26"", ""Profiles"": [
                    { ""ProfileId"": ""6fc8f975-b566-4be0-bfc2-2eb894e78d26"", ""Name"": ""Default Profile"", ""LedContainers"": [
                        { ""ContainerType"": ""FreeTheLights.Effect.FreeTheLightsEffect"", ""DeviceId"": """ + wheel.Id + @""" } ] } ] } }");

            var dashProfile = SimHubProfiles.ActiveProfile(dashRuntime, LedChannel.Leds);
            Assert.Equal("New profile", (string)dashProfile["Name"]);
            Assert.True(SimHubProfiles.Shows(dashProfile, dash, dash.Parts.Single()));
            Assert.Equal(LedChannel.Leds, SimHubProfiles.ChannelOfProfile(dashRuntime, System.Guid.Parse("21a3d50e-830f-428d-91b9-0e03fbb21473")));

            var wheelProfile = SimHubProfiles.ActiveProfile(wheelRuntime, LedChannel.Buttons);
            Assert.True(SimHubProfiles.Shows(wheelProfile, wheel, wheel.Parts.Single()));
            Assert.Equal(LedChannel.Buttons, SimHubProfiles.ChannelOfProfile(wheelRuntime, System.Guid.Parse("6fc8f975-b566-4be0-bfc2-2eb894e78d26")));
            Assert.Null(SimHubProfiles.ActiveProfile(wheelRuntime, LedChannel.Leds));
        }

        /// <summary>Runtime settings of a dashboard whose active profile belongs to someone else (another plugin's effects, a game binding).</summary>
        private static Newtonsoft.Json.Linq.JObject ForeignProfileSettings(string activeId = "21a3d50e-830f-428d-91b9-0e03fbb21473") => Newtonsoft.Json.Linq.JObject.Parse(
            @"{ ""LEDS"": { ""leds"": { ""activeProfileId"": " + (activeId == null ? "null" : @"""" + activeId + @"""") + @", ""Profiles"": [
                { ""ProfileId"": ""21a3d50e-830f-428d-91b9-0e03fbb21473"", ""Name"": ""ATSR : Dashboard : Profile"", ""GameCode"": ""IRacing"", ""GlobalBrightness"": 80.0,
                  ""EmbeddedJavascript"": ""// other plugin"", ""LedContainers"": [ { ""ContainerType"": ""Base.Group"", ""LedContainers"": [] } ] } ] } } }");

        [Fact]
        public void Connect_creates_its_own_profile_and_leaves_other_profiles_alone()
        {
            var dash = PresetBuilder.Build(ShDeviceParser.Parse(Dashboard), DeviceType.Dashboard);
            dash.Name = "Dash";
            var part = dash.Parts.Single();
            var settings = ForeignProfileSettings();
            var foreign = settings["LEDS"]["leds"]["Profiles"][0].DeepClone();

            Assert.Equal(ConnectAction.CreateOwn, SimHubProfiles.PlanConnect(settings, dash, part).action);
            var profile = SimHubProfiles.Connect(settings, dash, part);

            var profiles = (Newtonsoft.Json.Linq.JArray)settings["LEDS"]["leds"]["Profiles"];
            Assert.Equal(2, profiles.Count);
            Assert.True(Newtonsoft.Json.Linq.JToken.DeepEquals(foreign, profiles[0]), "another profile was changed");
            Assert.Equal("FreeTheLights: Dash", (string)profile["Name"]);
            Assert.Equal((string)profile["ProfileId"], (string)settings["LEDS"]["leds"]["activeProfileId"]);
            Assert.Equal(80.0, (double)profile["GlobalBrightness"]);
            Assert.Equal(Newtonsoft.Json.Linq.JTokenType.Null, profile["GameCode"].Type);
            Assert.Equal(Newtonsoft.Json.Linq.JTokenType.Null, profile["EmbeddedJavascript"].Type);
            Assert.True(SimHubProfiles.Shows(profile, dash, part));
            Assert.Single(profile["LedContainers"]);
            Assert.Equal(System.Guid.Parse((string)profile["ProfileId"]), part.SimHubProfileIds.Single());
            Assert.Equal(profile, SimHubProfiles.ActiveProfile(settings, LedChannel.Leds));

            // Connecting again changes nothing.
            var snapshot = settings.DeepClone();
            Assert.Equal(ConnectAction.UseActive, SimHubProfiles.PlanConnect(settings, dash, part).action);
            SimHubProfiles.Connect(settings, dash, part);
            Assert.True(Newtonsoft.Json.Linq.JToken.DeepEquals(snapshot, settings));
        }

        [Fact]
        public void Connect_switches_back_to_its_own_profile_and_only_edits_that_one()
        {
            var dash = PresetBuilder.Build(ShDeviceParser.Parse(Dashboard), DeviceType.Dashboard);
            var part = dash.Parts.Single();
            var settings = ForeignProfileSettings();
            var own = SimHubProfiles.Connect(settings, dash, part);

            // The user switched back to the other profile, and FreeTheLights' profile shows a deleted device.
            settings["LEDS"]["leds"]["activeProfileId"] = "21a3d50e-830f-428d-91b9-0e03fbb21473";
            own["LedContainers"][0]["DeviceId"] = System.Guid.NewGuid().ToString();
            var foreign = settings["LEDS"]["leds"]["Profiles"][0].DeepClone();

            Assert.Equal(ConnectAction.SwitchToOwn, SimHubProfiles.PlanConnect(settings, dash, part).action);
            Assert.Same(own, SimHubProfiles.Connect(settings, dash, part));
            Assert.Equal(2, ((Newtonsoft.Json.Linq.JArray)settings["LEDS"]["leds"]["Profiles"]).Count);
            Assert.True(Newtonsoft.Json.Linq.JToken.DeepEquals(foreign, settings["LEDS"]["leds"]["Profiles"][0]));
            Assert.True(SimHubProfiles.Shows(own, dash, part));
            Assert.Single(own["LedContainers"]);

            // Found by name too (e.g. the settings were reset), and a FreeTheLights effect in a foreign profile isn't touched.
            var reset = PresetBuilder.Build(ShDeviceParser.Parse(Dashboard), DeviceType.Dashboard);
            reset.Name = dash.Name;
            Assert.True(SimHubProfiles.IsOwnProfile(own, reset, reset.Parts.Single()));
            Assert.False(SimHubProfiles.IsOwnProfile((Newtonsoft.Json.Linq.JObject)foreign, dash, part));
        }

        [Fact]
        public void Connect_creates_a_profile_when_none_is_active_and_skips_missing_channels()
        {
            var dash = PresetBuilder.Build(ShDeviceParser.Parse(Dashboard), DeviceType.Dashboard);
            var part = dash.Parts.Single();
            var settings = ForeignProfileSettings(activeId: null);

            Assert.Equal(ConnectAction.CreateOwn, SimHubProfiles.PlanConnect(settings, dash, part).action);
            SimHubProfiles.Connect(settings, dash, part);
            Assert.Equal(2, ((Newtonsoft.Json.Linq.JArray)settings["LEDS"]["leds"]["Profiles"]).Count);

            var noChannel = Newtonsoft.Json.Linq.JObject.Parse(@"{ ""buttons"": { ""Profiles"": [] } }");
            Assert.Equal(ConnectAction.Impossible, SimHubProfiles.PlanConnect(noChannel, dash, part).action);
            Assert.Null(SimHubProfiles.Connect(noChannel, dash, part));

            // A copied device doesn't claim the original's profiles.
            Assert.Empty(part.Clone().SimHubProfileIds);
        }

        [Fact]
        public void Backups_of_simhubs_settings_never_replace_an_earlier_one()
        {
            // Connecting several channels backs up SimHub's settings several times within one second.
            var folder = Path.Combine(Path.GetTempPath(), "FreeTheLightsTests-" + Guid.NewGuid());
            Directory.CreateDirectory(folder);
            try
            {
                var time = new DateTime(2026, 10, 17, 14, 30, 5);
                var first = BackupFile.NewPath(folder, "device", time);
                Assert.Equal(Path.Combine(folder, "device-20261017-143005.json"), first);
                File.WriteAllText(first, "original");

                var second = BackupFile.NewPath(folder, "device", time);
                Assert.Equal(Path.Combine(folder, "device-20261017-143005-2.json"), second);
                File.WriteAllText(second, "modified");
                Assert.Equal(Path.Combine(folder, "device-20261017-143005-3.json"), BackupFile.NewPath(folder, "device", time));
                Assert.Equal("original", File.ReadAllText(first));
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }

        // ---------- Wizard ----------

        [Fact]
        public void Generic_devices_skip_the_kind_step_and_simhub_devices_get_the_suggestion()
        {
            var wizard = new DeviceWizardViewModel();
            Assert.False(wizard.IsSimHubDevice);
            Assert.DoesNotContain(wizard.StepHeaders, h => h.Title == "Kind");

            wizard.SetSimHubDevices(new[] { ShDeviceParser.Parse(Dashboard) });
            wizard.SelectedChoice = wizard.Choices.Single(c => c.Preset != null);
            Assert.Equal(DeviceType.Dashboard, wizard.SelectedKind);
            Assert.Equal(DeviceType.Dashboard, wizard.SuggestedKind);
            Assert.Equal("Ascher Racing 4\" Dashboard", wizard.Name);
            Assert.Equal(new[] { "Device", "Kind", "LED layout", "Finish" }, wizard.StepHeaders.Select(h => h.Title));
            Assert.Single(wizard.KindOptions, o => o.IsSuggested && o.IsSelected);

            // The layout step starts with the suggested split; what the user checks there is what gets built.
            Assert.Equal((3, 15, 3), (wizard.LeftCount, wizard.RowCount, wizard.RightCount));
            wizard.LeftCount = 4;
            var part = wizard.Build().Parts.Single();
            Assert.Equal(LedChannel.Leds, part.Channel);
            Assert.Equal(new[] { 4, 15, 3 }, part.Groups.Select(g => g.LedCount));
            Assert.Equal(new[] { false, false, true }, part.Groups.Select(g => g.Reversed));

            // As a wheel, SimHub's data decides and the layout step is skipped.
            wizard.SelectedKind = DeviceType.Wheel;
            Assert.DoesNotContain(wizard.StepHeaders, h => h.Title == "LED layout");
            Assert.Equal(DeviceType.Wheel, wizard.Build().Type);
            Assert.False(wizard.KindOptions.Single(o => o.Type == DeviceType.Wheel).IsSuggested);

            wizard.SelectedChoice = wizard.Choices.First();
            Assert.False(wizard.IsSimHubDevice);
            Assert.Equal("Wheel", wizard.Name);
            Assert.All(wizard.Build().Parts.Single().Groups, g => Assert.False(g.Reversed));
        }

        [Fact]
        public void Search_filters_the_device_list_by_brand_and_name()
        {
            var wizard = new DeviceWizardViewModel();
            wizard.SetSimHubDevices(new[] { ShDeviceParser.Parse(Dashboard), ShDeviceParser.Parse(Wheel) });
            wizard.SearchText = "ascher gt4";
            Assert.Equal(new[] { "Ascher Racing Artura GT4" }, wizard.ChoicesView.Cast<DeviceChoice>().Select(c => c.Title));
            wizard.SearchText = string.Empty;
            Assert.Equal(DeviceChoice.Generic.Length + 2, wizard.ChoicesView.Count);
        }
    }
}
