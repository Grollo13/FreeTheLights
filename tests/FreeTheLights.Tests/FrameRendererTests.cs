using System;
using System.Linq;
using FreeTheLights.CarData;
using FreeTheLights.Core;
using FreeTheLights.Engine;
using FreeTheLights.Settings;
using Xunit;

namespace FreeTheLights.Tests
{
    public class FrameRendererTests
    {
        private static readonly LedColor Yellow = LedColor.Parse("#FFFFD000");

        [Fact]
        public void Frames_that_are_too_old_release_the_leds()
        {
            var store = new FrameStore();
            var part = Guid.NewGuid();
            var frame = new[] { Yellow };
            Assert.Null(store.Get(part, 0));

            store.Put(part, frame, 1000);
            Assert.Same(frame, store.Get(part, 1000));
            Assert.Same(frame, store.Get(part, 1000 + FrameStore.MaxAgeMs));

            // The game loop stopped, or this part keeps failing to render: no frame, so the LEDs are released.
            Assert.Null(store.Get(part, 1001 + FrameStore.MaxAgeMs));

            // Rendered again: shown again.
            var next = new[] { LedColor.Off };
            store.Put(part, next, 5000);
            Assert.Same(next, store.Get(part, 5000));
        }

        [Fact]
        public void Frames_with_the_same_colors_keep_their_array()
        {
            var store = new FrameStore();
            var part = Guid.NewGuid();
            var frame = new[] { Yellow, LedColor.Transparent };
            store.Put(part, frame, 0);

            // The same colors in a new array (the game loop renders a new one every frame): the preview sees no change.
            store.Put(part, new[] { Yellow, LedColor.Transparent }, 20);
            Assert.Same(frame, store.Get(part, 20));

            // The time is still the new frame's, so the frame doesn't age out while the colors stay the same.
            Assert.Same(frame, store.Get(part, 20 + FrameStore.MaxAgeMs));

            var changed = new[] { Yellow, Yellow };
            store.Put(part, changed, 40);
            Assert.Same(changed, store.Get(part, 40));

            var longer = new[] { Yellow, Yellow, LedColor.Transparent };
            store.Put(part, longer, 60);
            Assert.Same(longer, store.Get(part, 60));
        }

        [Fact]
        public void Every_feature_has_a_renderer()
        {
            foreach (var feature in DevicePart.AllFeatures)
            {
                Assert.True(FrameRenderer.HasRenderer(feature.Kind), $"{feature.DisplayName} ({feature.Kind}) is never drawn");
            }
        }

        private static DevicePart Wheel()
        {
            // Row 0..14, left column 15..17, right column 18..20.
            return DevicePart.CreateDefault("Wheel");
        }

        private static CarLedProfile ThreeLedCar() => CarLedProfile.Parse(
            @"{ ""ledNumber"": 3, ""ledColor"": [""#00000000"", ""#FF00FF00"", ""#FFFFFF00"", ""#FFFF0000""],
                ""ledRpm"": [ { ""1"": [9000, 6000, 7000, 8000] } ] }");

        private static LedColor[] Slice(LedColor[] frame, int start, int count) => frame.Skip(start).Take(count).ToArray();

        [Fact]
        public void Nothing_is_lit_without_input()
        {
            var frame = FrameRenderer.Render(Wheel(), new TelemetrySnapshot(), null, 0);
            Assert.Equal(21, frame.Length);
            Assert.All(frame, c => Assert.True(c.IsTransparent));
        }

        [Fact]
        public void Car_leds_are_centered_on_a_longer_device_row()
        {
            var frame = FrameRenderer.Render(Wheel(), new TelemetrySnapshot { Rpm = 7500, Gear = "1" }, ThreeLedCar(), 0);
            var row = Slice(frame, 0, 15);

            // 3 car LEDs on 15 device LEDs: device LEDs 6..8, like on the car.
            Assert.All(row.Take(6), c => Assert.True(c.IsTransparent));
            Assert.Equal(LedColor.Parse("#FF00FF00"), row[6]);
            Assert.Equal(LedColor.Parse("#FFFFFF00"), row[7]);
            Assert.All(row.Skip(8), c => Assert.True(c.IsTransparent));
            Assert.All(Slice(frame, 15, 6), c => Assert.True(c.IsTransparent));
        }

        [Fact]
        public void Generic_fallback_without_car_data()
        {
            var device = Wheel();
            var telemetry = new TelemetrySnapshot { Rpm = 8500, Redline = 10000 };

            var row = Slice(FrameRenderer.Render(device, telemetry, null, 0), 0, 15);
            Assert.Equal(8, row.Count(c => !c.IsTransparent));

            device.Rpm.FallbackMode = RpmFallbackMode.Off;
            Assert.All(FrameRenderer.Render(device, telemetry, null, 0), c => Assert.True(c.IsTransparent));
        }

        [Fact]
        public void Generic_redline_blink_off_phase_is_opaque_black()
        {
            var device = Wheel();
            var telemetry = new TelemetrySnapshot { Rpm = 10500, Redline = 10000 };

            Assert.All(Slice(FrameRenderer.Render(device, telemetry, null, 0), 0, 15), c => Assert.Equal(LedColor.FromRgb(255, 0, 0), c));
            Assert.All(Slice(FrameRenderer.Render(device, telemetry, null, device.Rpm.GenericBlinkIntervalMs), 0, 15), c => Assert.Equal(LedColor.Off, c));
        }

        [Fact]
        public void Flags_use_the_columns_by_default()
        {
            var frame = FrameRenderer.Render(Wheel(), new TelemetrySnapshot { FlagYellow = true }, null, 0);
            Assert.All(Slice(frame, 0, 15), c => Assert.True(c.IsTransparent));
            Assert.All(Slice(frame, 15, 6), c => Assert.Equal(Yellow, c));

            // Blink off-phase keeps the LEDs owned (opaque black) instead of letting lower layers through.
            var off = FrameRenderer.Render(Wheel(), new TelemetrySnapshot { FlagYellow = true }, null, 400);
            Assert.All(Slice(off, 15, 6), c => Assert.Equal(LedColor.Off, c));
        }

        [Fact]
        public void Each_flag_reacts_to_its_own_telemetry()
        {
            var settings = new FlagSettings();
            settings.EnsureDefaults();
            foreach (var rule in settings.Rules) rule.Enabled = true;

            Assert.Equal(FlagKind.Checkered, FrameRenderer.ActiveFlag(settings, new TelemetrySnapshot { FlagCheckered = true })?.Kind);
            Assert.Equal(FlagKind.Orange, FrameRenderer.ActiveFlag(settings, new TelemetrySnapshot { FlagOrange = true })?.Kind);
            Assert.Equal(FlagKind.White, FrameRenderer.ActiveFlag(settings, new TelemetrySnapshot { FlagWhite = true })?.Kind);
            Assert.Null(FrameRenderer.ActiveFlag(settings, new TelemetrySnapshot { FlagName = "Green" }));
        }

        [Fact]
        public void Black_green_and_debris_flags_react_to_their_telemetry()
        {
            var settings = new FlagSettings();
            settings.EnsureDefaults();
            foreach (var rule in settings.Rules) rule.Enabled = true;

            Assert.Equal(FlagKind.Black, FrameRenderer.ActiveFlag(settings, new TelemetrySnapshot { FlagBlack = true })?.Kind);
            Assert.Equal(FlagKind.Green, FrameRenderer.ActiveFlag(settings, new TelemetrySnapshot { FlagGreen = true })?.Kind);
            Assert.Equal(FlagKind.Debris, FrameRenderer.ActiveFlag(settings, new TelemetrySnapshot { FlagDebris = true })?.Kind);

            // The yellow flag outranks debris by default.
            Assert.Equal(FlagKind.Yellow, FrameRenderer.ActiveFlag(settings, new TelemetrySnapshot { FlagYellow = true, FlagDebris = true })?.Kind);
        }

        [Fact]
        public void Debris_flag_shows_yellow_and_red_stripes_by_default()
        {
            var frame = FrameRenderer.Render(Wheel(), new TelemetrySnapshot { FlagDebris = true }, null, 0);
            var red = LedColor.Parse("#FFFF0000");
            Assert.Equal(new[] { Yellow, red, Yellow }, Slice(frame, 15, 3));

            // Steady: the same stripes later.
            Assert.Equal(new[] { Yellow, red, Yellow }, Slice(FrameRenderer.Render(Wheel(), new TelemetrySnapshot { FlagDebris = true }, null, 400), 15, 3));
        }

        [Theory]
        [InlineData(0x40, true)]
        [InlineData(0x10044, true)]
        [InlineData(0x8, false)]
        [InlineData(0, false)]
        public void IRacing_session_flags_tell_the_debris_flag(long flags, bool debris)
        {
            Assert.Equal(debris, IRacingSessionFlags.HasDebris(flags));
            Assert.Equal(debris, IRacingSessionFlags.HasDebris((int)flags));
            Assert.Equal(debris, IRacingSessionFlags.HasDebris((System.IO.FileAttributes)flags));
        }

        [Fact]
        public void IRacing_session_flags_ignore_missing_or_odd_values()
        {
            Assert.False(IRacingSessionFlags.HasDebris(null));
            Assert.False(IRacingSessionFlags.HasDebris("not a number"));
        }

        [Fact]
        public void Test_mode_simulates_black_green_and_debris()
        {
            var test = new TestMode { Black = true, Green = true, Debris = true };
            Assert.True(test.AnyActive);
            var t = new TelemetrySnapshot();
            test.Apply(t, null, 0);
            Assert.True(t.FlagBlack && t.FlagGreen && t.FlagDebris);

            test.Clear();
            Assert.False(test.AnyActive);
        }

        [Fact]
        public void Flag_priority_follows_rule_order()
        {
            var settings = new FlagSettings();
            settings.EnsureDefaults();
            var both = new TelemetrySnapshot { FlagYellow = true, FlagBlue = true };
            Assert.Equal(FlagKind.Yellow, FrameRenderer.ActiveFlag(settings, both).Kind);

            var blue = settings.Rules.Single(r => r.Kind == FlagKind.Blue);
            var yellow = settings.Rules.Single(r => r.Kind == FlagKind.Yellow);
            settings.Rules.Move(settings.Rules.IndexOf(blue), settings.Rules.IndexOf(yellow));
            Assert.Equal(FlagKind.Blue, FrameRenderer.ActiveFlag(settings, both).Kind);
        }

        [Fact]
        public void Checkered_flag_alternates_colors_from_the_first_led()
        {
            var white = LedColor.Parse("#FFFFFFFF");
            var frame = FrameRenderer.Render(Wheel(), new TelemetrySnapshot { FlagCheckered = true }, null, 0);

            // Left column is wired bottom-up: LED 0 (bottom) = color 1, LED 1 = color 2, LED 2 = color 1.
            Assert.Equal(new[] { white, LedColor.Off, white }, Slice(frame, 15, 3));
        }

        [Fact]
        public void Two_color_flag_uses_both_colors()
        {
            var device = Wheel();
            var orange = device.Flags.Rules.Single(r => r.Kind == FlagKind.Orange);
            orange.Enabled = true;
            orange.Color2 = "#FF0000FF";

            var frame = FrameRenderer.Render(device, new TelemetrySnapshot { FlagOrange = true }, null, 0);
            var expected1 = LedColor.Parse(orange.Color);
            var expected2 = LedColor.Parse("#FF0000FF");
            Assert.Equal(new[] { expected1, expected2, expected1 }, Slice(frame, 15, 3));
        }

        [Fact]
        public void Swap_animation_alternates_each_led_between_the_colors()
        {
            var device = Wheel();
            var checkered = device.Flags.Rules.Single(r => r.Kind == FlagKind.Checkered);
            checkered.Animation = FlagAnimation.SwapColors;
            var white = LedColor.Parse("#FFFFFFFF");

            var even = FrameRenderer.Render(device, new TelemetrySnapshot { FlagCheckered = true }, null, 0);
            var odd = FrameRenderer.Render(device, new TelemetrySnapshot { FlagCheckered = true }, null, device.Flags.BlinkIntervalMs);

            Assert.Equal(new[] { white, LedColor.Off, white }, Slice(even, 15, 3));
            Assert.Equal(new[] { LedColor.Off, white, LedColor.Off }, Slice(odd, 15, 3));
        }

        [Fact]
        public void Single_color_flag_is_solid_and_cannot_swap()
        {
            var device = Wheel();
            var white = device.Flags.Rules.Single(r => r.Kind == FlagKind.White);
            Assert.False(white.HasSecondColor);
            Assert.DoesNotContain(FlagAnimation.SwapColors, white.AvailableAnimations);

            var frame = FrameRenderer.Render(device, new TelemetrySnapshot { FlagWhite = true }, null, 0);
            Assert.All(Slice(frame, 15, 6), c => Assert.Equal(LedColor.Parse("#FFFFFFFF"), c));
        }

        [Fact]
        public void Removing_the_second_color_turns_swap_into_blink()
        {
            var rule = FlagRule.CreateDefault(FlagKind.Checkered);
            rule.Animation = FlagAnimation.SwapColors;
            rule.Color2 = string.Empty;
            Assert.Equal(FlagAnimation.Blink, rule.Animation);
        }

        [Fact]
        public void Matrix_patterns()
        {
            var matrix = new LedGroup { Kind = LedGroupKind.Matrix, Columns = 4, Rows = 4 };
            Assert.True(FrameRenderer.IsFirstColor(FlagPattern.Diagonal, matrix, 0, 0));
            Assert.True(FrameRenderer.IsFirstColor(FlagPattern.Diagonal, matrix, 2, 0));
            Assert.False(FrameRenderer.IsFirstColor(FlagPattern.Diagonal, matrix, 3, 3));
            Assert.False(FrameRenderer.IsFirstColor(FlagPattern.Diagonal, matrix, 1, 3));

            Assert.True(FrameRenderer.IsFirstColor(FlagPattern.Alternate, matrix, 0, 0));
            Assert.False(FrameRenderer.IsFirstColor(FlagPattern.Alternate, matrix, 1, 0));
            Assert.False(FrameRenderer.IsFirstColor(FlagPattern.Alternate, matrix, 0, 1));

            var big = new LedGroup { Kind = LedGroupKind.Matrix, Columns = 8, Rows = 8 };
            Assert.True(FrameRenderer.IsFirstColor(FlagPattern.Checker, big, 1, 1));
            Assert.False(FrameRenderer.IsFirstColor(FlagPattern.Checker, big, 2, 0));
        }

        [Theory]
        [InlineData(0.0, 3)]
        [InlineData(double.NaN, 3)]
        [InlineData(0.5, 3)]
        [InlineData(2.0, 2)]
        [InlineData(4.0, 1)]
        [InlineData(20.0, 1)]
        public void Proximity_lights_more_leds_when_closer(double distance, int expected)
        {
            var settings = new ProximitySettings { MaxDistance = 5 };
            Assert.Equal(expected, FrameRenderer.ProximityLitCount(settings, 3, distance));
        }

        [Fact]
        public void Proximity_without_progressive_lights_all()
        {
            var settings = new ProximitySettings { Progressive = false };
            Assert.Equal(3, FrameRenderer.ProximityLitCount(settings, 3, 4.0));
        }

        [Fact]
        public void Car_left_lights_only_the_left_column_from_the_bottom()
        {
            var frame = FrameRenderer.Render(Wheel(), new TelemetrySnapshot { SpotterCarLeft = true, SpotterCarLeftDistance = 3 }, null, 0);
            Assert.Equal(new[] { Yellow, Yellow, LedColor.Transparent }, Slice(frame, 15, 3));
            Assert.All(Slice(frame, 18, 3), c => Assert.True(c.IsTransparent));
        }

        [Fact]
        public void Car_on_both_sides_on_a_row_fills_from_both_edges()
        {
            var device = Wheel();
            device.Proximity.TargetGroupIds.Clear();
            device.Proximity.TargetGroupIds.Add(device.Groups[0].Id);
            var frame = FrameRenderer.Render(device, new TelemetrySnapshot { SpotterCarLeft = true, SpotterCarRight = true }, null, 0);

            Assert.All(Slice(frame, 0, 15), c => Assert.Equal(Yellow, c));

            var leftOnly = FrameRenderer.Render(device, new TelemetrySnapshot { SpotterCarLeft = true, SpotterCarLeftDistance = 4 }, null, 0);
            var row = Slice(leftOnly, 0, 15);
            Assert.Equal(Yellow, row[0]);
            Assert.True(row[14].IsTransparent);
        }

        [Fact]
        public void Abs_and_tc_light_the_columns_with_abs_winning_by_default()
        {
            var device = Wheel();
            device.Abs.Color = "#FFFF0000";
            device.TractionControl.Color = "#FF0000FF";

            var tcOnly = FrameRenderer.Render(device, new TelemetrySnapshot { TcActive = true }, null, 0);
            Assert.All(Slice(tcOnly, 15, 6), c => Assert.Equal(LedColor.Parse("#FF0000FF"), c));

            var both = FrameRenderer.Render(device, new TelemetrySnapshot { TcActive = true, AbsActive = true }, null, 0);
            Assert.All(Slice(both, 15, 6), c => Assert.Equal(LedColor.Parse("#FFFF0000"), c));
        }

        [Fact]
        public void Layer_priority_decides_overlaps()
        {
            var device = Wheel();
            device.Flags.TargetGroupIds.Add(device.Groups[0].Id); // flags also on the shift light row
            var telemetry = new TelemetrySnapshot { FlagBlue = true, Rpm = 9500, Gear = "1" };
            var car = ThreeLedCar();

            var flagsFirst = FrameRenderer.Render(device, telemetry, car, 0);
            Assert.Equal(LedColor.Parse("#FF0040FF"), flagsFirst[6]);

            device.LayerPriority.Move(device.LayerPriority.IndexOf(LayerKind.Rpm), 0);
            var rpmFirst = FrameRenderer.Render(device, telemetry, car, 0);
            Assert.Equal(LedColor.Parse("#FF00FF00"), rpmFirst[6]);
        }

        [Fact]
        public void Disabled_feature_and_brightness()
        {
            var device = Wheel();
            device.Flags.Enabled = false;
            Assert.All(FrameRenderer.Render(device, new TelemetrySnapshot { FlagYellow = true }, null, 0), c => Assert.True(c.IsTransparent));

            device.Flags.Enabled = true;
            device.Brightness = 50;
            var frame = FrameRenderer.Render(device, new TelemetrySnapshot { FlagWhite = true }, null, 0);
            Assert.Equal(new LedColor(255, 128, 128, 128), frame[15]);
        }

        [Fact]
        public void Rpm_never_drives_columns()
        {
            var device = Wheel();
            device.Rpm.TargetGroupIds.Add(device.Groups[1].Id);
            var frame = FrameRenderer.Render(device, new TelemetrySnapshot { Rpm = 9500, Gear = "1" }, ThreeLedCar(), 0);
            Assert.All(Slice(frame, 15, 6), c => Assert.True(c.IsTransparent));
        }

        // ---------- Idle animation ----------

        private static readonly LedColor Red = LedColor.FromRgb(255, 0, 0);

        /// <summary>A part with one row of ten LEDs and only the idle animation on.</summary>
        private static DevicePart IdleRow(IdleAnimation animation)
        {
            var part = new DevicePart();
            part.EnsureDefaults();
            part.AddGroup(new LedGroup { Kind = LedGroupKind.HorizontalRow, Count = 10 });
            foreach (var feature in part.Features) feature.Enabled = feature == part.Idle;
            part.Idle.Animation = animation;
            part.Idle.Color = "#FFFF0000";
            part.Idle.PeriodMs = 1000;
            part.Idle.TrailPercent = 30;
            return part;
        }

        [Fact]
        public void Idle_animation_plays_only_while_idle()
        {
            var part = IdleRow(IdleAnimation.Breathing);
            Assert.All(FrameRenderer.Render(part, new TelemetrySnapshot(), null, 500), c => Assert.True(c.IsTransparent));

            // Full at the middle of a breath, dim at its start.
            var idle = new TelemetrySnapshot { Idle = true };
            Assert.All(FrameRenderer.Render(part, idle, null, 500), c => Assert.Equal(Red, c));
            Assert.All(FrameRenderer.Render(part, idle, null, 0), c => Assert.Equal(Red.Scale(FrameRenderer.BreathDim), c));
        }

        [Fact]
        public void Running_light_has_a_fading_tail()
        {
            var part = IdleRow(IdleAnimation.Running);

            // Halfway through the run the light is on LED 7 (index 6); the 30 % tail covers the two LEDs behind it.
            var frame = FrameRenderer.Render(part, new TelemetrySnapshot { Idle = true }, null, 500);
            Assert.Equal(new[] { LedColor.Off, LedColor.Off, LedColor.Off, LedColor.Off, Red.Scale(1 / 3.0), Red.Scale(2 / 3.0), Red, LedColor.Off, LedColor.Off, LedColor.Off }, frame);
        }

        [Fact]
        public void Oscillating_light_turns_around()
        {
            var part = IdleRow(IdleAnimation.Oscillating);

            // On the way back (second period) the tail follows on the right.
            var frame = FrameRenderer.Render(part, new TelemetrySnapshot { Idle = true }, null, 1550);
            Assert.Equal(LedColor.Off, frame[3]);
            Assert.Equal(Red, frame[4]);
            Assert.Equal(Red.Scale(2 / 3.0), frame[5]);
            Assert.Equal(Red.Scale(1 / 3.0), frame[6]);
            Assert.Equal(LedColor.Off, frame[7]);
        }

        [Fact]
        public void Mirrored_animations_are_symmetric()
        {
            var part = IdleRow(IdleAnimation.Running);
            part.Idle.Mirror = true;
            for (var now = 0; now < 1000; now += 50)
            {
                var frame = FrameRenderer.Render(part, new TelemetrySnapshot { Idle = true }, null, now);
                Assert.Equal(frame, frame.Reverse());
            }

            // Early in the run only the center is lit.
            var start = FrameRenderer.Render(part, new TelemetrySnapshot { Idle = true }, null, 100);
            Assert.False(start[4].Equals(LedColor.Off));
            Assert.Equal(LedColor.Off, start[0]);
        }

        [Fact]
        public void Changing_colors_cycle_together_or_spread_as_a_rainbow()
        {
            var part = IdleRow(IdleAnimation.Breathing);
            var idle = new TelemetrySnapshot { Idle = true };

            part.Idle.ColorMode = AnimationColorMode.Cycle;
            var cycle = FrameRenderer.Render(part, idle, null, 500);
            Assert.All(cycle, c => Assert.Equal(LedColor.FromHue(500.0 / Animations.HueCycleMs), c));

            part.Idle.ColorMode = AnimationColorMode.Rainbow;
            var rainbow = FrameRenderer.Render(part, idle, null, 500);
            Assert.Equal(10, rainbow.Distinct().Count());
        }

        [Fact]
        public void Front_plate_elements_play_along_from_left_to_right()
        {
            var part = new DevicePart();
            part.EnsureDefaults();
            part.ApplyFrontPlate("1", "1x2");
            foreach (var feature in part.Features) feature.Enabled = feature == part.Idle;
            part.Idle.Animation = IdleAnimation.Running;
            part.Idle.PeriodMs = 1000;
            part.Idle.TrailPercent = 1;
            var idle = new TelemetrySnapshot { Idle = true };
            var left = part.Elements.Where(e => e.Side == LedGroupSide.Left).Select(e => e.StartIndex).ToList();
            var right = part.Elements.Where(e => e.Side == LedGroupSide.Right).Select(e => e.StartIndex).ToList();
            Assert.NotEmpty(left);
            Assert.NotEmpty(right);

            var early = FrameRenderer.Render(part, idle, null, 150);
            Assert.All(left, i => Assert.NotEqual(LedColor.Off, early[i]));
            Assert.All(right, i => Assert.Equal(LedColor.Off, early[i]));

            var late = FrameRenderer.Render(part, idle, null, 750);
            Assert.All(left, i => Assert.Equal(LedColor.Off, late[i]));
            Assert.All(right, i => Assert.NotEqual(LedColor.Off, late[i]));

            part.Idle.IncludeElements = false;
            Assert.All(FrameRenderer.Render(part, idle, null, 150), c => Assert.True(c.IsTransparent));
        }

        [Fact]
        public void Idle_is_when_no_game_runs_and_no_other_test_is_on()
        {
            var tests = new TestMode();
            var noGame = new TelemetrySnapshot();
            tests.Apply(noGame, null, 0);
            Assert.True(noGame.Idle);

            var game = new TelemetrySnapshot { GameRunning = true };
            tests.Apply(game, null, 0);
            Assert.False(game.Idle);

            tests.Yellow = true;
            var testing = new TelemetrySnapshot();
            tests.Apply(testing, null, 0);
            Assert.False(testing.Idle);

            tests.Clear();
            tests.Idle = true;
            Assert.True(tests.AnyActive);
            var forced = new TelemetrySnapshot { GameRunning = true };
            tests.Apply(forced, null, 0);
            Assert.True(forced.Idle);
        }
    }
}
