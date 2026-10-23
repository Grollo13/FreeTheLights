using System;
using System.IO;
using System.Linq;
using FreeTheLights.Core;
using FreeTheLights.Settings;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace FreeTheLights.Tests
{
    public class SettingsTests
    {
        [Fact]
        public void Default_device_targets_follow_the_spec()
        {
            var device = DevicePart.CreateDefault("Wheel");
            var row = device.Groups[0];
            var left = device.Groups[1];
            var right = device.Groups[2];

            Assert.Equal(new[] { row.Id }, device.Rpm.TargetGroupIds.ToArray());
            foreach (var feature in new FeatureSettingsBase[] { device.Flags, device.Proximity, device.Abs, device.TractionControl })
            {
                Assert.Equal(new[] { left.Id, right.Id }, feature.TargetGroupIds.ToArray());
            }

            Assert.Equal("#FFFFD000", device.Proximity.Color);
            Assert.Equal("#FFFFD000", device.Abs.Color);
            Assert.Equal("#FF0060FF", device.TractionControl.Color);
        }

        [Fact]
        public void Abs_and_tc_default_to_yellow_and_blue_and_keep_saved_colors()
        {
            var missing = JsonConvert.DeserializeObject<DevicePart>(@"{ ""Abs"": null }");
            missing.EnsureDefaults();
            Assert.Equal(IndicatorSettings.AbsDefaultColor, missing.Abs.Color);
            Assert.Equal(IndicatorSettings.TractionControlDefaultColor, missing.TractionControl.Color);

            var saved = JsonConvert.DeserializeObject<DevicePart>(@"{ ""TractionControl"": { ""Color"": ""#FF00FF00"" } }");
            saved.EnsureDefaults();
            Assert.Equal("#FF00FF00", saved.TractionControl.Color);
            Assert.Equal(IndicatorSettings.AbsDefaultColor, saved.Abs.Color);
        }

        [Fact]
        public void Second_row_does_not_become_an_rpm_target_but_a_matrix_becomes_a_flag_target()
        {
            var device = DevicePart.CreateDefault("Wheel");
            var row2 = new LedGroup { Kind = LedGroupKind.HorizontalRow, Count = 5 };
            var flag = new LedGroup { Kind = LedGroupKind.Matrix };
            device.AddGroup(row2);
            device.AddGroup(flag);

            Assert.DoesNotContain(row2.Id, device.Rpm.TargetGroupIds);
            Assert.Contains(flag.Id, device.Flags.TargetGroupIds);
            Assert.DoesNotContain(flag.Id, device.Abs.TargetGroupIds);
        }

        [Fact]
        public void Json_roundtrip_does_not_duplicate_defaults()
        {
            var settings = new PluginSettings();
            settings.Devices.Add(DeviceConfig.CreateDefault("Wheel"));

            var json = JsonConvert.SerializeObject(settings);
            var loaded = JsonConvert.DeserializeObject<PluginSettings>(json);
            loaded.EnsureDefaults();

            var device = loaded.Devices.Single().Parts.Single();
            Assert.Equal(Enum.GetValues(typeof(FlagKind)).Length, device.Flags.Rules.Count);
            Assert.Equal(Enum.GetValues(typeof(LayerKind)).Length, device.LayerPriority.Count);
            Assert.Equal(3, device.Groups.Count);
            Assert.Equal(2, device.Flags.TargetGroupIds.Count);
        }

        [Fact]
        public void Missing_sections_are_repaired()
        {
            var device = JsonConvert.DeserializeObject<DevicePart>(@"{ ""Name"": ""Old"", ""Flags"": null, ""LayerPriority"": [4, 4] }");
            device.EnsureDefaults();

            Assert.NotNull(device.Flags);
            Assert.Equal(FlagSettings.DefaultOrder.Length, device.Flags.Rules.Count);
            Assert.Equal(DevicePart.AllFeatures.Select(f => f.Kind), device.LayerPriority);
        }

        [Fact]
        public void Missing_layers_are_inserted_at_their_default_position()
        {
            // The user moved RPM first and the static colors above ABS; the missing layers go after their predecessor.
            var device = JsonConvert.DeserializeObject<DevicePart>(@"{ ""LayerPriority"": [4, 0, 1, 5, 2, 3] }");
            device.EnsureDefaults();

            Assert.Equal(new[] { LayerKind.Press, LayerKind.Rpm, LayerKind.Idle, LayerKind.Flags, LayerKind.Proximity, LayerKind.StaticColor, LayerKind.Abs, LayerKind.TractionControl, LayerKind.Engine }, device.LayerPriority.ToArray());
        }

        [Fact]
        public void Settings_without_the_press_layer_get_it_first_with_every_element_targeted()
        {
            var saved = JObject.Parse(File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Fixtures", "settings-v4.json")));
            var settings = SettingsMigrator.Load(saved).Settings;

            foreach (var part in settings.Devices.SelectMany(d => d.Parts))
            {
                Assert.Equal(LayerKind.Press, part.LayerPriority[0]);
                Assert.Equal(DevicePart.AllFeatures.Length, part.LayerPriority.Count);
                Assert.True(part.Press.Enabled);
                Assert.All(part.Elements, e => Assert.True(part.Press.Targets(e)));
                Assert.All(part.Elements, e => Assert.Equal(string.Empty, e.PressColor));
                Assert.All(part.Elements, e => Assert.Equal(string.Empty, e.ButtonInput));
                Assert.All(part.Elements, e => Assert.Equal(string.Empty, e.ClockwiseInput));
                Assert.All(part.Elements, e => Assert.Equal(string.Empty, e.CounterClockwiseInput));
            }
        }

        [Fact]
        public void Input_slots_follow_the_element_type()
        {
            var device = DevicePart.CreateDefault("Wheel");
            device.ApplyFrontPlate("1", "1x2");
            var element = device.Elements.First(e => e.Side == LedGroupSide.Left);
            Assert.Equal(new[] { InputSlot.Button }, element.InputSlots);
            element.ElementType = ElementType.Light;
            Assert.Empty(element.InputSlots);
            Assert.False(element.CanBePressed);
            Assert.Equal(new[] { InputSlot.Clockwise, InputSlot.CounterClockwise }, device.Elements.First(e => e.ElementType == ElementType.Dial).InputSlots);
            Assert.Empty(device.Groups.First(g => !g.IsElement).InputSlots);

            element.SetInput(InputSlot.Button, "B1");
            element.SetInput(InputSlot.Clockwise, "B2");
            element.SetInput(InputSlot.CounterClockwise, "B3");
            Assert.Equal(new[] { "B1", "B2", "B3" }, new[] { element.ButtonInput, element.ClockwiseInput, element.CounterClockwiseInput });
            Assert.Equal("B2", element.GetInput(InputSlot.Clockwise));
        }

        [Fact]
        public void Press_color_and_inputs_survive_saving_and_copying()
        {
            var part = DevicePart.CreateDefault("Wheel");
            part.ApplyFrontPlate("1", "1x2");
            var element = part.Elements.First();
            element.PressColor = "#FF000000";
            element.ButtonInput = "JoystickPlugin.Wheel_B05";
            element.ClockwiseInput = "JoystickPlugin.Wheel_B20";
            element.CounterClockwiseInput = "JoystickPlugin.Wheel_B21";

            var loaded = JsonConvert.DeserializeObject<DevicePart>(JsonConvert.SerializeObject(part));
            loaded.EnsureDefaults();
            foreach (var copy in new[] { loaded.Elements.First(), element.Clone() })
            {
                Assert.Equal("#FF000000", copy.PressColor);
                Assert.Equal("JoystickPlugin.Wheel_B05", copy.ButtonInput);
                Assert.Equal("JoystickPlugin.Wheel_B20", copy.ClockwiseInput);
                Assert.Equal("JoystickPlugin.Wheel_B21", copy.CounterClockwiseInput);
            }

            var partCopy = part.Clone();
            Assert.True(partCopy.Press.Targets(partCopy.Elements.First()));
        }

        [Fact]
        public void Clone_gets_new_ids_and_remaps_targets()
        {
            var device = DevicePart.CreateDefault("Wheel");
            var copy = device.Clone();

            Assert.NotEqual(device.Id, copy.Id);
            Assert.Empty(copy.Groups.Select(g => g.Id).Intersect(device.Groups.Select(g => g.Id)));
            Assert.Equal(new[] { copy.Groups[0].Id }, copy.Rpm.TargetGroupIds.ToArray());
            Assert.Equal(new[] { copy.Groups[1].Id, copy.Groups[2].Id }, copy.Flags.TargetGroupIds.ToArray());
        }

        [Fact]
        public void Removing_a_group_removes_it_from_all_targets()
        {
            var device = DevicePart.CreateDefault("Wheel");
            var left = device.Groups[1];
            device.RemoveGroup(left);

            Assert.All(device.Features, f => Assert.DoesNotContain(left.Id, f.TargetGroupIds));
        }

        [Theory]
        [InlineData(typeof(LedChannel), "Leds", 0)]
        [InlineData(typeof(LedChannel), "Buttons", 1)]
        [InlineData(typeof(LedChannel), "Encoders", 2)]
        [InlineData(typeof(LedChannel), "Matrix", 3)]
        [InlineData(typeof(DeviceType), "Custom", 0)]
        [InlineData(typeof(DeviceType), "Wheel", 1)]
        [InlineData(typeof(DeviceType), "Dashboard", 2)]
        [InlineData(typeof(DeviceType), "LedBar", 3)]
        [InlineData(typeof(DeviceType), "LedFlag", 4)]
        [InlineData(typeof(PhotoLedSource), "None", 0)]
        [InlineData(typeof(PhotoLedSource), "BuiltIn", 1)]
        [InlineData(typeof(PhotoLedSource), "Found", 2)]
        [InlineData(typeof(PhotoLedSource), "Manual", 3)]
        [InlineData(typeof(PhotoLedSource), "Generic", 4)]
        [InlineData(typeof(LayerKind), "Flags", 0)]
        [InlineData(typeof(LayerKind), "Proximity", 1)]
        [InlineData(typeof(LayerKind), "Abs", 2)]
        [InlineData(typeof(LayerKind), "TractionControl", 3)]
        [InlineData(typeof(LayerKind), "Rpm", 4)]
        [InlineData(typeof(LayerKind), "StaticColor", 5)]
        [InlineData(typeof(LayerKind), "Press", 6)]
        [InlineData(typeof(LayerKind), "Idle", 7)]
        [InlineData(typeof(LayerKind), "Engine", 8)]
        [InlineData(typeof(EngineAnimation), "Off", 0)]
        [InlineData(typeof(EngineAnimation), "Sweep", 1)]
        [InlineData(typeof(EngineAnimation), "Fill", 2)]
        [InlineData(typeof(EngineAnimation), "Flash", 3)]
        [InlineData(typeof(EngineAnimation), "Drain", 4)]
        [InlineData(typeof(EngineAnimation), "Fade", 5)]
        [InlineData(typeof(IdleAnimation), "Breathing", 0)]
        [InlineData(typeof(IdleAnimation), "Running", 1)]
        [InlineData(typeof(IdleAnimation), "Oscillating", 2)]
        [InlineData(typeof(AnimationColorMode), "Fixed", 0)]
        [InlineData(typeof(AnimationColorMode), "Cycle", 1)]
        [InlineData(typeof(AnimationColorMode), "Rainbow", 2)]
        [InlineData(typeof(LedGroupKind), "HorizontalRow", 0)]
        [InlineData(typeof(LedGroupKind), "VerticalColumn", 1)]
        [InlineData(typeof(LedGroupKind), "Matrix", 2)]
        [InlineData(typeof(LedGroupKind), "Element", 3)]
        [InlineData(typeof(ElementType), "Button", 0)]
        [InlineData(typeof(ElementType), "Light", 1)]
        [InlineData(typeof(ElementType), "Dial", 2)]
        [InlineData(typeof(LedGroupSide), "None", 0)]
        [InlineData(typeof(LedGroupSide), "Left", 1)]
        [InlineData(typeof(LedGroupSide), "Right", 2)]
        [InlineData(typeof(RpmFallbackMode), "Generic", 0)]
        [InlineData(typeof(RpmFallbackMode), "Off", 1)]
        [InlineData(typeof(FlagKind), "Yellow", 0)]
        [InlineData(typeof(FlagKind), "Blue", 1)]
        [InlineData(typeof(FlagKind), "White", 2)]
        [InlineData(typeof(FlagKind), "Black", 4)]
        [InlineData(typeof(FlagKind), "Checkered", 5)]
        [InlineData(typeof(FlagKind), "Green", 6)]
        [InlineData(typeof(FlagKind), "Orange", 7)]
        [InlineData(typeof(FlagKind), "Debris", 8)]
        [InlineData(typeof(FlagPattern), "Alternate", 1)]
        [InlineData(typeof(FlagPattern), "Checker", 2)]
        [InlineData(typeof(FlagPattern), "Diagonal", 3)]
        [InlineData(typeof(FlagAnimation), "Steady", 0)]
        [InlineData(typeof(FlagAnimation), "Blink", 1)]
        [InlineData(typeof(FlagAnimation), "SwapColors", 2)]
        [InlineData(typeof(FillOrigin), "Start", 0)]
        [InlineData(typeof(FillOrigin), "End", 1)]
        public void Persisted_enum_values_never_change(Type enumType, string name, int value)
        {
            // Settings store enums as numbers: renumbering a member silently changes every saved setting that uses it.
            Assert.Equal(value, Convert.ToInt32(Enum.Parse(enumType, name)));
        }

        [Fact]
        public void Every_persisted_enum_member_is_pinned()
        {
            // A new member needs a row in Persisted_enum_values_never_change, with a new number.
            var types = new[] { typeof(LayerKind), typeof(LedGroupKind), typeof(ElementType), typeof(LedGroupSide), typeof(RpmFallbackMode), typeof(FlagKind), typeof(FlagPattern), typeof(FlagAnimation), typeof(FillOrigin), typeof(LedChannel), typeof(DeviceType), typeof(PhotoLedSource), typeof(IdleAnimation), typeof(AnimationColorMode), typeof(EngineAnimation) };
            Assert.Equal(63, types.Sum(t => Enum.GetValues(t).Length));
        }

        [Fact]
        public void Null_devices_and_groups_are_removed()
        {
            var loaded = JsonConvert.DeserializeObject<PluginSettings>(@"{ ""SettingsVersion"": 4, ""Devices"": [ null, { ""Name"": ""Wheel"", ""Parts"": [ null, { ""Groups"": [ null, { ""Kind"": 0, ""Count"": 5 } ] } ] } ] }");
            loaded.EnsureDefaults();

            var device = loaded.Devices.Single().Parts.Single();
            Assert.Equal(5, device.Groups.Single().Count);
            Assert.Equal(5, device.TotalLeds);
        }

        [Fact]
        public void Every_layer_has_exactly_one_feature()
        {
            var device = DevicePart.CreateDefault("Wheel");
            foreach (LayerKind layer in Enum.GetValues(typeof(LayerKind)))
            {
                Assert.Single(DevicePart.AllFeatures, f => f.Kind == layer);
                Assert.NotNull(device.GetFeature(layer));
                Assert.False(string.IsNullOrWhiteSpace(DevicePart.FindFeature(layer).DisplayName));
            }

            Assert.Equal(DevicePart.AllFeatures.Length, device.Features.Distinct().Count());
        }

        [Fact]
        public void Unknown_layers_are_dropped()
        {
            var loaded = JsonConvert.DeserializeObject<DevicePart>(@"{ ""LayerPriority"": [99, 4] }");
            loaded.EnsureDefaults();

            Assert.Equal(DevicePart.AllFeatures.Select(f => f.Kind), loaded.LayerPriority);
            Assert.Null(new DevicePart().GetFeature((LayerKind)99));
        }

        [Fact]
        public void Missing_matrix_group_falls_back_to_the_first_matrix()
        {
            var device = DevicePart.CreateDefault("Wheel");
            var flag1 = new LedGroup { Kind = LedGroupKind.Matrix };
            var flag2 = new LedGroup { Kind = LedGroupKind.Matrix };
            device.AddGroup(flag1);
            device.AddGroup(flag2);

            Assert.Same(flag2, device.FindMatrixGroup(flag2.Id));
            Assert.Same(flag1, device.FindMatrixGroup(Guid.Empty));
            Assert.Same(flag1, device.FindMatrixGroup(Guid.NewGuid()));
            Assert.Null(DevicePart.CreateDefault("Wheel").FindMatrixGroup(Guid.Empty));
        }

        [Fact]
        public void Snapshot_publishes_independent_copies_only_when_settings_change()
        {
            var live = new[] { DeviceConfig.CreateDefault("Wheel") };
            var snapshot = new DeviceSnapshot();

            Assert.True(snapshot.Update(live));
            var copy = snapshot.Devices.Single();
            Assert.NotSame(live[0], copy);
            Assert.Equal(live[0].Id, copy.Id);
            Assert.Same(copy, snapshot.Find(live[0].Id));
            Assert.Null(snapshot.Find(Guid.NewGuid()));

            Assert.False(snapshot.Update(live));
            Assert.Same(copy, snapshot.Devices.Single());

            live[0].Parts[0].Groups[0].Count = 10;
            Assert.Equal(15, copy.Parts[0].Groups[0].Count);
            Assert.True(snapshot.Update(live));
            Assert.Equal(10, snapshot.Devices.Single().Parts[0].Groups[0].Count);
        }

        [Fact]
        public void Snapshot_skips_null_devices()
        {
            var snapshot = new DeviceSnapshot();
            snapshot.Update(new DeviceConfig[] { null, DeviceConfig.CreateDefault("Wheel") });
            Assert.Single(snapshot.Devices);
        }

        // ---------- Devices and parts ----------

        [Fact]
        public void Every_device_has_at_least_one_part()
        {
            var loaded = JsonConvert.DeserializeObject<PluginSettings>(@"{ ""SettingsVersion"": 4, ""Devices"": [ { ""Name"": ""Empty"", ""Parts"": [] } ] }");
            loaded.EnsureDefaults();
            Assert.Single(loaded.Devices.Single().Parts);
        }

        [Fact]
        public void Unknown_or_empty_part_ids_find_the_first_part()
        {
            var device = DeviceConfig.CreateDefault("Wheel");
            var buttons = new DevicePart { Name = "Buttons", Channel = LedChannel.Buttons };
            device.Parts.Add(buttons);

            Assert.Same(buttons, device.FindPart(buttons.Id));
            Assert.Same(device.Parts[0], device.FindPart(Guid.Empty));
            Assert.Same(device.Parts[0], device.FindPart(Guid.NewGuid()));

            var snapshot = new DeviceSnapshot();
            snapshot.Update(new[] { device });
            Assert.Equal(buttons.Id, snapshot.FindPart(device.Id, buttons.Id).Id);
            Assert.Null(snapshot.FindPart(Guid.NewGuid(), buttons.Id));
        }

        [Fact]
        public void Cloning_a_device_gives_its_parts_and_groups_new_ids()
        {
            var device = DeviceConfig.CreateDefault("Wheel");
            device.Parts.Add(new DevicePart { Name = "Buttons", Channel = LedChannel.Buttons });
            var copy = device.Clone();

            Assert.NotEqual(device.Id, copy.Id);
            Assert.Equal(new[] { "LEDs", "Buttons" }, copy.Parts.Select(p => p.Name));
            Assert.Empty(copy.Parts.Select(p => p.Id).Intersect(device.Parts.Select(p => p.Id)));
            Assert.Empty(copy.Parts[0].Groups.Select(g => g.Id).Intersect(device.Parts[0].Groups.Select(g => g.Id)));
            Assert.Equal(copy.Parts[0].Groups.Select(g => g.Id).Take(1), copy.Parts[0].Rpm.TargetGroupIds);
        }

        [Fact]
        public void Settings_are_saved_once_the_editing_pauses()
        {
            var autoSave = new Debouncer(2000);
            Assert.False(autoSave.TakeDue(0));

            autoSave.Changed(1000);
            Assert.False(autoSave.TakeDue(2500));

            // Another change starts the wait again.
            autoSave.Changed(2500);
            Assert.False(autoSave.TakeDue(4000));
            Assert.True(autoSave.TakeDue(4500));
            Assert.False(autoSave.TakeDue(9000));

            // Saved some other way (the page was closed): nothing left to do.
            autoSave.Changed(10000);
            autoSave.Reset();
            Assert.False(autoSave.TakeDue(20000));
        }

        [Fact]
        public void Start_index_is_limited_so_frames_stay_small()
        {
            Assert.Equal(LedLimits.MaxLeds, JsonConvert.DeserializeObject<LedGroup>("{ \"StartIndex\": 100000000 }").StartIndex);
            Assert.Equal(0, new LedGroup { StartIndex = -5 }.StartIndex);
        }
    }
}
