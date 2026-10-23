using System;
using System.Collections.Generic;
using System.Linq;
using FreeTheLights.Core;
using FreeTheLights.Engine;
using FreeTheLights.Settings;
using FreeTheLights.UI;
using Newtonsoft.Json;
using Xunit;

namespace FreeTheLights.Tests
{
    public class FrontPlateTests
    {
        private static readonly LedColor Yellow = LedColor.Parse("#FFFFD000");
        private static readonly LedColor Purple = LedColor.Parse("#FF8000FF");

        /// <summary>The default wheel (21 LEDs) with 3 dials (1 on top, 2 below) and 2 × 3 buttons per side.</summary>
        private static DevicePart Wheel()
        {
            var device = DevicePart.CreateDefault("Wheel");
            device.ApplyFrontPlate("1-2", "2x3");
            return device;
        }

        private static LedGroup Element(DevicePart device, LedGroupSide side, int slot) => device.Elements.Single(e => e.Side == side && e.ElementSlot == slot);

        [Fact]
        public void Presets_respect_the_limits_and_have_unique_ids()
        {
            Assert.All(FrontPlatePresets.Dials, p => Assert.InRange(p.Count, 0, FrontPlatePresets.MaxDials));
            Assert.All(FrontPlatePresets.Sides, p => Assert.InRange(p.Count, 0, FrontPlatePresets.MaxPerSide));
            Assert.Equal(FrontPlatePresets.Dials.Length, FrontPlatePresets.Dials.Select(p => p.Id).Distinct().Count());
            Assert.Equal(FrontPlatePresets.Sides.Length, FrontPlatePresets.Sides.Select(p => p.Id).Distinct().Count());

            // Every dial count from 0 to 6 is offered; 2 dials only side by side.
            Assert.Equal(Enumerable.Range(0, 7), FrontPlatePresets.Dials.Select(p => p.Count).Distinct().OrderBy(c => c));
            Assert.Single(FrontPlatePresets.Dials, p => p.Count == 2);
            Assert.Contains(FrontPlatePresets.Dials, p => p.Rows.SequenceEqual(new[] { 1, 2 }));
            Assert.Contains(FrontPlatePresets.Dials, p => p.Rows.SequenceEqual(new[] { 2, 1 }));
        }

        [Fact]
        public void Unknown_preset_ids_mean_none()
        {
            Assert.Equal(0, FrontPlatePresets.FindDials("from-a-newer-version").Count);
            Assert.Equal(0, FrontPlatePresets.FindSides(null).Count);
        }

        [Fact]
        public void Layout_is_mirror_symmetric()
        {
            foreach (var dials in FrontPlatePresets.Dials)
            {
                foreach (var sides in FrontPlatePresets.Sides)
                {
                    var layout = new FrontPlateLayout { DialPresetId = dials.Id, SidePresetId = sides.Id };
                    for (var slot = 0; slot < sides.Count; slot++)
                    {
                        var left = FrontPlatePresets.Position(layout, LedGroupSide.Left, slot);
                        var right = FrontPlatePresets.Position(layout, LedGroupSide.Right, slot);
                        Assert.Equal(-left.x, right.x, 6);
                        Assert.Equal(left.y, right.y, 6);
                        Assert.True(left.x < 0);
                    }

                    // The dial pattern itself is symmetric around the axis.
                    var xs = Enumerable.Range(0, dials.Count).Select(slot => FrontPlatePresets.Position(layout, LedGroupSide.None, slot).x).ToList();
                    Assert.Equal(0, xs.Sum(), 6);
                }
            }
        }

        [Fact]
        public void Dials_are_arranged_by_rows()
        {
            var layout = new FrontPlateLayout { DialPresetId = "1-2", SidePresetId = FrontPlatePresets.NoneId };
            var top = FrontPlatePresets.Position(layout, LedGroupSide.None, 0);
            var bottomLeft = FrontPlatePresets.Position(layout, LedGroupSide.None, 1);
            var bottomRight = FrontPlatePresets.Position(layout, LedGroupSide.None, 2);

            Assert.Equal(0, top.x, 6);
            Assert.True(top.y < bottomLeft.y);
            Assert.Equal(bottomLeft.y, bottomRight.y, 6);
            Assert.True(bottomLeft.x < 0 && bottomRight.x > 0);
        }

        [Fact]
        public void Applying_a_layout_appends_elements_left_dials_right()
        {
            var device = Wheel();
            var elements = device.ElementsInWiringOrder().ToList();

            Assert.Equal(6 + 3 + 6, elements.Count);
            Assert.Equal(Enumerable.Range(21, 15), elements.Select(e => e.StartIndex));
            Assert.Equal(36, device.TotalLeds);
            Assert.All(elements.Take(6), e => Assert.Equal(LedGroupSide.Left, e.Side));
            Assert.All(elements.Skip(6).Take(3), e => Assert.Equal(ElementType.Dial, e.ElementType));
            Assert.All(elements.Skip(9), e => Assert.Equal(LedGroupSide.Right, e.Side));
            Assert.Equal("Left 1", elements[0].Name);
            Assert.Equal("Dial 3", elements[8].Name);
            Assert.All(elements, e => Assert.Contains(e.Id, device.StaticColors.TargetGroupIds));
            Assert.All(elements, e => Assert.Null(device.GetElementEffect(e)));
        }

        [Fact]
        public void Changing_the_layout_keeps_settings_of_remaining_elements()
        {
            var device = Wheel();
            var kept = Element(device, LedGroupSide.Left, 1);
            kept.StaticColor = "#FFFF0000";
            kept.ElementType = ElementType.Light;
            kept.StartIndex = 99;
            device.SetElementEffect(kept, LayerKind.Abs);
            var removed = Element(device, LedGroupSide.Right, 5);
            device.SetElementEffect(removed, LayerKind.Flags);

            Assert.Equal(2 + 2 * 2, device.CountElementsRemovedBy("1", "2x2")); // 2 dials and 2 elements per side
            device.ApplyFrontPlate("1", "2x2");

            Assert.Equal(1 + 4 + 4, device.Elements.Count());
            Assert.Same(kept, Element(device, LedGroupSide.Left, 1));
            Assert.Equal("#FFFF0000", kept.StaticColor);
            Assert.Equal(ElementType.Light, kept.ElementType);
            Assert.Equal(99, kept.StartIndex);
            Assert.Equal(LayerKind.Abs, device.GetElementEffect(kept));
            Assert.DoesNotContain(removed.Id, device.Flags.TargetGroupIds);
            Assert.Equal("1", device.FrontPlate.DialPresetId);
            Assert.Equal("2x2", device.FrontPlate.SidePresetId);
        }

        [Fact]
        public void An_element_has_exactly_one_effect()
        {
            var device = Wheel();
            var element = Element(device, LedGroupSide.Left, 0);

            device.SetElementEffect(element, LayerKind.Flags);
            device.SetElementEffect(element, LayerKind.TractionControl);
            Assert.Equal(LayerKind.TractionControl, device.GetElementEffect(element));
            Assert.DoesNotContain(element.Id, device.Flags.TargetGroupIds);

            device.SetElementEffect(element, null);
            Assert.Null(device.GetElementEffect(element));
            Assert.All(DevicePart.ElementEffects, k => Assert.DoesNotContain(element.Id, device.GetFeature(k).TargetGroupIds));

            Assert.Throws<ArgumentOutOfRangeException>(() => device.SetElementEffect(element, LayerKind.Rpm));
        }

        [Fact]
        public void Loading_repairs_elements_with_several_effects_and_duplicates()
        {
            var device = Wheel();
            var element = Element(device, LedGroupSide.Left, 0);
            device.Flags.TargetGroupIds.Add(element.Id);
            device.Abs.TargetGroupIds.Add(element.Id);
            var duplicate = element.Clone();
            device.Groups.Add(duplicate);

            var loaded = JsonConvert.DeserializeObject<DevicePart>(JsonConvert.SerializeObject(device));
            loaded.EnsureDefaults();

            var repaired = loaded.Elements.Single(e => e.Side == LedGroupSide.Left && e.ElementSlot == 0);
            Assert.Equal(element.Id, repaired.Id);
            Assert.Equal(LayerKind.Flags, loaded.GetElementEffect(repaired));
            Assert.DoesNotContain(repaired.Id, loaded.Abs.TargetGroupIds);
        }

        [Fact]
        public void Auto_numbering_follows_the_other_groups()
        {
            var device = Wheel();
            foreach (var element in device.Elements) element.StartIndex = 0;
            device.Groups[0].Count = 10; // shorter shift lights; the columns keep their LEDs 15-20

            device.PackFrontPlateIndices();
            Assert.Equal(Enumerable.Range(21, 15), device.ElementsInWiringOrder().Select(e => e.StartIndex));

            device.PackStartIndices();
            Assert.Equal(16, device.ElementsInWiringOrder().First().StartIndex);
        }

        [Fact]
        public void Partial_parts_get_the_static_color_layer_last_and_no_front_plate()
        {
            var device = JsonConvert.DeserializeObject<DevicePart>(@"{ ""LayerPriority"": [0, 1, 2, 3, 4], ""Groups"": [ { ""Kind"": 0, ""Count"": 5 } ] }");
            device.EnsureDefaults();

            Assert.Equal(LayerKind.StaticColor, device.LayerPriority.Last());
            Assert.NotNull(device.FrontPlate);
            Assert.Equal(FrontPlatePresets.NoneId, device.FrontPlate.DialPresetId);
            Assert.Empty(device.Elements);
        }

        // ---------- Rendering ----------

        [Fact]
        public void Static_color_shows_when_idle_and_effects_override_it()
        {
            var device = Wheel();
            var element = Element(device, LedGroupSide.Left, 0);
            element.StaticColor = "#FF8000FF";
            device.SetElementEffect(element, LayerKind.Abs);

            var idle = FrameRenderer.Render(device, new TelemetrySnapshot(), null, 0);
            Assert.Equal(Purple, idle[element.StartIndex]);

            var abs = FrameRenderer.Render(device, new TelemetrySnapshot { AbsActive = true }, null, 0);
            Assert.Equal(Yellow, abs[element.StartIndex]);

            // Blink off-phase is dark, not the static color.
            var off = FrameRenderer.Render(device, new TelemetrySnapshot { AbsActive = true }, null, device.Abs.BlinkIntervalMs);
            Assert.Equal(LedColor.Off, off[element.StartIndex]);

            device.StaticColors.Enabled = false;
            Assert.True(FrameRenderer.Render(device, new TelemetrySnapshot(), null, 0)[element.StartIndex].IsTransparent);
        }

        [Fact]
        public void Press_color_shows_while_the_button_is_held_and_wins_over_effects()
        {
            var device = Wheel();
            var element = Element(device, LedGroupSide.Left, 0);
            element.StaticColor = "#FFFFD000";
            element.PressColor = "#FF8000FF";
            element.ButtonInput = "JoystickPlugin.Wheel_B05";
            device.SetElementEffect(element, LayerKind.Abs);

            var inputs = new InputState();
            var t = new TelemetrySnapshot { Inputs = inputs };
            Assert.Equal(Yellow, FrameRenderer.Render(device, t, null, 0)[element.StartIndex]);

            inputs.Press("JoystickPlugin.Wheel_B05");
            Assert.Equal(Purple, FrameRenderer.Render(device, t, null, 5000)[element.StartIndex]);

            // Wins over an active effect, even in its blink off-phase.
            var abs = new TelemetrySnapshot { Inputs = inputs, AbsActive = true };
            Assert.Equal(Purple, FrameRenderer.Render(device, abs, null, device.Abs.BlinkIntervalMs)[element.StartIndex]);

            // Released: the static color returns at once; a button has no minimum on-time, not even for a quick tap.
            inputs.Release("JoystickPlugin.Wheel_B05");
            Assert.Equal(Yellow, FrameRenderer.Render(device, t, null, 5000)[element.StartIndex]);
            inputs.Press("JoystickPlugin.Wheel_B05");
            inputs.Release("JoystickPlugin.Wheel_B05");
            Assert.Equal(Yellow, FrameRenderer.Render(device, t, null, 0)[element.StartIndex]);

            // Other elements don't react to this input.
            inputs.Press("JoystickPlugin.Wheel_B05");
            var other = Element(device, LedGroupSide.Left, 1);
            other.PressColor = "#FF8000FF";
            Assert.True(FrameRenderer.Render(device, t, null, 5000)[other.StartIndex].IsTransparent);

            device.Press.Enabled = false;
            Assert.Equal(Yellow, FrameRenderer.Render(device, t, null, 5000)[element.StartIndex]);
        }

        [Fact]
        public void A_dial_step_shows_the_press_color_for_at_least_50_ms_and_black_turns_it_off()
        {
            var device = Wheel();
            var dial = Element(device, LedGroupSide.None, 0);
            dial.StaticColor = "#FFFFD000";
            dial.PressColor = LedColor.Off.ToString();
            dial.ClockwiseInput = "JoystickPlugin.Wheel_B20";
            dial.CounterClockwiseInput = "JoystickPlugin.Wheel_B21";

            long now = 1000;
            var inputs = new InputState(() => now);
            var t = new TelemetrySnapshot { Inputs = inputs };
            Assert.Equal(50, PressSettings.DialMinimumOnMs);

            // A step the other way, pressed and released within one frame: shown for 50 ms after the press.
            inputs.Press("JoystickPlugin.Wheel_B21");
            inputs.Release("JoystickPlugin.Wheel_B21");
            Assert.Equal(LedColor.Off, FrameRenderer.Render(device, t, null, 1000)[dial.StartIndex]);
            Assert.Equal(LedColor.Off, FrameRenderer.Render(device, t, null, 1050)[dial.StartIndex]);
            Assert.Equal(Yellow, FrameRenderer.Render(device, t, null, 1051)[dial.StartIndex]);

            // A step held longer shows until its release.
            now = 2000;
            inputs.Press("JoystickPlugin.Wheel_B20");
            Assert.Equal(LedColor.Off, FrameRenderer.Render(device, t, null, 2200)[dial.StartIndex]);
            inputs.Release("JoystickPlugin.Wheel_B20");
            Assert.Equal(Yellow, FrameRenderer.Render(device, t, null, 2200)[dial.StartIndex]);
        }

        [Fact]
        public void Each_type_reacts_only_to_its_own_inputs_and_lights_never()
        {
            var device = Wheel();
            var element = Element(device, LedGroupSide.Right, 0);
            element.PressColor = "#FF8000FF";
            element.ButtonInput = "B1";
            element.ClockwiseInput = "B2";

            var inputs = new InputState();
            var t = new TelemetrySnapshot { Inputs = inputs };

            // A button ignores dial inputs it may still have stored.
            inputs.Press("B2");
            Assert.True(FrameRenderer.Render(device, t, null, 0)[element.StartIndex].IsTransparent);
            inputs.Press("B1");
            Assert.Equal(Purple, FrameRenderer.Render(device, t, null, 0)[element.StartIndex]);

            // A light is never pressed, not even in test mode.
            element.ElementType = ElementType.Light;
            Assert.True(FrameRenderer.Render(device, t, null, 0)[element.StartIndex].IsTransparent);
            Assert.True(FrameRenderer.Render(device, new TelemetrySnapshot { SimulatePress = true }, null, 0)[element.StartIndex].IsTransparent);
        }

        [Fact]
        public void Elements_without_press_color_or_inputs_do_not_react()
        {
            var device = Wheel();
            var noColor = Element(device, LedGroupSide.Left, 0);
            noColor.ButtonInput = "B1";
            var noInput = Element(device, LedGroupSide.Left, 1);
            noInput.PressColor = "#FF8000FF";

            var inputs = new InputState();
            inputs.Press("B1");
            var frame = FrameRenderer.Render(device, new TelemetrySnapshot { Inputs = inputs }, null, 0);
            Assert.True(frame[noColor.StartIndex].IsTransparent);
            Assert.True(frame[noInput.StartIndex].IsTransparent);

            // Without any input state (no SimHub), nothing reacts either.
            Assert.True(FrameRenderer.Render(device, new TelemetrySnapshot(), null, 0)[noInput.StartIndex].IsTransparent);
        }

        [Fact]
        public void Test_mode_press_shows_every_press_color()
        {
            var device = Wheel();
            var withColor = Element(device, LedGroupSide.Right, 0);
            withColor.PressColor = "#FF8000FF";
            var withoutColor = Element(device, LedGroupSide.Right, 1);

            var test = new TestMode { Press = true };
            Assert.True(test.AnyActive);
            var t = new TelemetrySnapshot();
            test.Apply(t, null, 0);

            var frame = FrameRenderer.Render(device, t, null, 0);
            Assert.Equal(Purple, frame[withColor.StartIndex]);
            Assert.True(frame[withoutColor.StartIndex].IsTransparent);

            test.Clear();
            Assert.False(test.Press);
        }

        [Fact]
        public void Elements_without_color_or_effect_stay_transparent()
        {
            var device = Wheel();
            var frame = FrameRenderer.Render(device, new TelemetrySnapshot { AbsActive = true, FlagYellow = true, Rpm = 9000, Redline = 9000 }, null, 0);
            Assert.All(device.Elements, e => Assert.True(frame[e.StartIndex].IsTransparent));
        }

        [Fact]
        public void Proximity_on_elements_follows_their_side()
        {
            var device = Wheel();
            var left = Element(device, LedGroupSide.Left, 0);
            var right = Element(device, LedGroupSide.Right, 0);
            var dial = Element(device, LedGroupSide.None, 0);
            foreach (var e in new[] { left, right, dial }) device.SetElementEffect(e, LayerKind.Proximity);

            var carLeft = FrameRenderer.Render(device, new TelemetrySnapshot { SpotterCarLeft = true }, null, 0);
            Assert.Equal(Yellow, carLeft[left.StartIndex]);
            Assert.True(carLeft[right.StartIndex].IsTransparent);
            Assert.Equal(Yellow, carLeft[dial.StartIndex]);

            var carRight = FrameRenderer.Render(device, new TelemetrySnapshot { SpotterCarRight = true }, null, 0);
            Assert.True(carRight[left.StartIndex].IsTransparent);
            Assert.Equal(Yellow, carRight[right.StartIndex]);
            Assert.Equal(Yellow, carRight[dial.StartIndex]);
        }

        [Fact]
        public void Two_color_flag_on_an_element_swaps_over_time()
        {
            var device = Wheel();
            var element = Element(device, LedGroupSide.None, 1);
            device.SetElementEffect(element, LayerKind.Flags);
            device.Flags.Rules.Single(r => r.Kind == FlagKind.Checkered).Animation = FlagAnimation.SwapColors;
            var white = LedColor.Parse("#FFFFFFFF");

            var even = FrameRenderer.Render(device, new TelemetrySnapshot { FlagCheckered = true }, null, 0);
            var odd = FrameRenderer.Render(device, new TelemetrySnapshot { FlagCheckered = true }, null, device.Flags.BlinkIntervalMs);
            Assert.Equal(white, even[element.StartIndex]);
            Assert.Equal(LedColor.Off, odd[element.StartIndex]);
        }

        [Fact]
        public void Rpm_never_drives_elements()
        {
            var device = Wheel();
            var element = Element(device, LedGroupSide.Left, 0);
            device.Rpm.TargetGroupIds.Add(element.Id);
            var frame = FrameRenderer.Render(device, new TelemetrySnapshot { Rpm = 9500, Redline = 9000 }, null, 0);
            Assert.True(frame[element.StartIndex].IsTransparent);
        }

        // ---------- UI view models ----------

        [Fact]
        public void Wheel_wizard_builds_the_front_plate()
        {
            var wizard = new DeviceWizardViewModel
            {
                SelectedDialPreset = FrontPlatePresets.FindDials("2-1"),
                SelectedSidePreset = FrontPlatePresets.FindSides("1x4"),
            };
            Assert.True(wizard.HasFrontPlateStep);
            Assert.Equal(3 + 8, wizard.ElementCount);

            var device = wizard.Build().Parts.Single();
            Assert.Equal(21 + 11, device.TotalLeds);
            Assert.Equal("2-1", device.FrontPlate.DialPresetId);
            Assert.Equal(11, device.Elements.Count());
            Assert.Contains("32 LEDs", wizard.Summary);
        }

        [Fact]
        public void Led_bar_wizard_skips_the_front_plate_step()
        {
            var wizard = new DeviceWizardViewModel
            {
                SelectedSidePreset = FrontPlatePresets.FindSides("1x2"),
                Template = DeviceTemplate.LedBar,
            };
            Assert.False(wizard.HasFrontPlateStep);
            Assert.Equal(3, wizard.StepHeaders.Length);

            wizard.Step = DeviceWizardViewModel.StepLayout;
            wizard.NextCommand.Execute(null);
            Assert.Equal(DeviceWizardViewModel.StepFinish, wizard.Step);
            wizard.BackCommand.Execute(null);
            Assert.Equal(DeviceWizardViewModel.StepLayout, wizard.Step);

            Assert.Empty(wizard.Build().Parts.Single().Elements);
        }

        [Fact]
        public void Rpm_settings_are_only_offered_for_parts_with_a_row()
        {
            var buttons = new DevicePart { Name = "Buttons", Channel = LedChannel.Buttons };
            buttons.EnsureDefaults();
            buttons.ApplyFrontPlate("2-1", "1x4");
            using (var viewModel = new DeviceViewModel(buttons))
            {
                Assert.False(viewModel.HasRpmTargets);

                var changed = new List<string>();
                viewModel.PropertyChanged += (s, e) => changed.Add(e.PropertyName);
                buttons.AddGroup(new LedGroup { Name = "RPM LEDs", Kind = LedGroupKind.HorizontalRow, Count = 10 });
                Assert.True(viewModel.HasRpmTargets);
                Assert.Contains(nameof(DeviceViewModel.HasRpmTargets), changed);
            }
        }

        [Fact]
        public void Elements_get_their_effect_from_the_inspector_and_stay_out_of_the_feature_lists()
        {
            var device = Wheel();
            using (var viewModel = new DeviceViewModel(device))
            {
                var left = device.Elements.Where(e => e.Side == LedGroupSide.Left).OrderBy(e => e.ElementSlot).ToList();
                var center = device.Elements.Where(e => e.Side == LedGroupSide.None).OrderBy(e => e.ElementSlot).ToList();
                Assert.Equal(6, left.Count);
                Assert.Equal(3, center.Count);
                Assert.Equal("outer column, row 1", LedInspector.ElementPosition(device, left[0]));
                Assert.Equal("row 1", LedInspector.ElementPosition(device, center[0]));

                var element = Element(device, LedGroupSide.Right, 2);
                using (var inspector = new LedInspector())
                {
                    inspector.Select(device, element, 0, false);
                    inspector.Effect = EffectChoice.For(LayerKind.Proximity);
                }

                Assert.Equal(LayerKind.Proximity, device.GetElementEffect(element));

                // Elements are not offered in the feature target lists.
                Assert.DoesNotContain(viewModel.FlagTargets, t => t.Group.IsElement);
                Assert.DoesNotContain(viewModel.ProximityTargets, t => t.Group.IsElement);
            }
        }

        [Fact]
        public void Every_feature_for_rows_columns_or_matrices_has_target_chips()
        {
            var part = DevicePart.CreateDefault("Wheel");
            part.AddGroup(new LedGroup { Kind = LedGroupKind.Matrix, Columns = 4, Rows = 4 });
            using (var viewModel = new DeviceViewModel(part))
            {
                foreach (var feature in DevicePart.AllFeatures)
                {
                    var settings = feature.SettingsOf(part);
                    var targets = viewModel.TargetsOf(feature.Kind);
                    if (settings.AllowedKinds.All(k => k == LedGroupKind.Element))
                    {
                        // Front plate only: set up in the inspector.
                        Assert.Null(targets);
                        continue;
                    }

                    Assert.True(targets != null, $"{feature.DisplayName} has no target chips on the device page");
                    Assert.Equal(part.Groups.Count(g => settings.AllowedKinds.Contains(g.Kind)), targets.Count);
                }
            }
        }
    }
}
