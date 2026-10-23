using System;
using System.Linq;
using FreeTheLights.Core;
using FreeTheLights.Engine;
using FreeTheLights.Settings;
using FreeTheLights.UI;
using Xunit;

namespace FreeTheLights.Tests
{
    /// <summary>Checking the wiring on the device page: the test pattern and reordering the groups on the LED strip.</summary>
    public class WiringTests
    {
        /// <summary>Shift lights (15) at 0, left column (3) at 15, right column (3) at 18, plus one dial at 21.</summary>
        private static DevicePart Wheel()
        {
            var part = DevicePart.CreateDefault("Wheel");
            part.ApplyFrontPlate("1", FrontPlatePresets.NoneId);
            return part;
        }

        private static LedColor Dim(LedColor c) => c.Scale(0.25);

        [Fact]
        public void Pattern_colors_each_group_and_runs_a_dot_along_the_wiring()
        {
            var part = Wheel();
            var frame = WiringPattern.Render(part, 0);

            Assert.Equal(22, frame.Length);
            Assert.Equal(WiringPattern.DotColor, frame[0]);
            Assert.All(frame.Skip(1).Take(14), c => Assert.Equal(Dim(WiringPattern.Colors[0]), c));
            Assert.Equal(WiringPattern.DotColor, frame[15]);
            Assert.Equal(Dim(WiringPattern.Colors[1]), frame[16]);
            Assert.Equal(WiringPattern.DotColor, frame[18]);
            Assert.Equal(Dim(WiringPattern.Colors[2]), frame[19]);
            Assert.Equal(Dim(LedColor.FromRgb(255, 255, 255)), frame[21]);

            // The dot moves to the next wired LED of each group, and wraps around shorter groups.
            var later = WiringPattern.Render(part, 4 * WiringPattern.StepMs);
            Assert.Equal(WiringPattern.DotColor, later[4]);
            Assert.Equal(WiringPattern.DotColor, later[15 + 1]);
            Assert.Equal(Dim(WiringPattern.Colors[0]), later[0]);
        }

        [Fact]
        public void Groups_start_with_plain_red_green_and_blue_and_all_colors_differ()
        {
            // Colors that look alike on a dim device (the old cyan and green) made the wiring hard to read.
            Assert.Equal(new[] { LedColor.FromRgb(255, 0, 0), LedColor.FromRgb(0, 255, 0), LedColor.FromRgb(0, 0, 255) }, WiringPattern.Colors.Take(3));
            Assert.Equal(WiringPattern.Colors.Length, WiringPattern.Colors.Distinct().Count());
            Assert.DoesNotContain(WiringPattern.DotColor, WiringPattern.Colors);
        }

        [Fact]
        public void Pattern_is_in_physical_order_whatever_the_direction()
        {
            // A reversed row still lights its first wired LED (physical index 0) first: that is what the device shows,
            // while the preview draws physical LED 0 at the right end of a reversed row.
            var part = Wheel();
            part.Groups[0].Reversed = true;
            Assert.Equal(WiringPattern.DotColor, WiringPattern.Render(part, 0)[0]);
        }

        [Fact]
        public void Pattern_follows_the_part_brightness()
        {
            var part = Wheel();
            part.Brightness = 50;
            Assert.Equal(WiringPattern.DotColor.Scale(0.5), WiringPattern.Render(part, 0)[0]);
            Assert.Equal(Dim(WiringPattern.Colors[0]).Scale(0.5), WiringPattern.Render(part, 0)[1]);
        }

        [Fact]
        public void Device_page_shows_the_wiring_the_dialog_edits()
        {
            var part = Wheel();
            var row = part.Groups[0];
            var left = part.Groups[1];
            var right = part.Groups[2];
            var dial = part.Elements.Single();
            var dialIndex = dial.StartIndex;

            using (var viewModel = new DeviceViewModel(part))
            {
                Assert.True(viewModel.HasWiring);
                Assert.Equal(new[] { row, left, right }, viewModel.WiringGroups);
                Assert.Equal("RPM LEDs → Left column → Right column", viewModel.WiringSummary);

                // The wiring dialog chains the left column first, then the row, then the right column.
                var editor = new WiringEditor(part, 0);
                editor.MoveCommand.Execute(new MoveRequest(1, 0));
                Assert.Equal(new[] { left, row, right }, viewModel.WiringGroups);
                Assert.Equal(0, left.StartIndex);
                Assert.Equal(3, row.StartIndex);
                Assert.Equal(18, right.StartIndex);
                Assert.Equal(dialIndex, dial.StartIndex);
                Assert.False(viewModel.HasOverlap);

                // Groups added, renamed or removed there show up too.
                editor.AddCommand.Execute("Row");
                editor.Groups[3].Name = "Extra";
                Assert.Equal("Left column → RPM LEDs → Right column → Extra", viewModel.WiringSummary);
                editor.RemoveCommand.Execute(editor.Groups[3]);
                Assert.Equal(new[] { left, row, right }, viewModel.WiringGroups);

                // The list follows start indices changed elsewhere (the inspector).
                right.StartIndex = 100;
                left.StartIndex = 200;
                Assert.Equal(new[] { row, right, left }, viewModel.WiringGroups);
            }
        }

        [Fact]
        public void Wiring_card_stays_for_a_part_without_groups_but_not_for_one_of_buttons_only()
        {
            // A matrix part whose only group was removed in the dialog: the card stays, to add groups again.
            var matrix = new DevicePart { Name = "Flag", Channel = LedChannel.Matrix };
            matrix.EnsureDefaults();
            matrix.AddGroup(new LedGroup { Name = "LED flag", Kind = LedGroupKind.Matrix, Columns = 8, Rows = 8 });
            using (var viewModel = new DeviceViewModel(matrix))
            {
                new WiringEditor(matrix, 64).RemoveCommand.Execute(matrix.Groups.Single());
                Assert.True(viewModel.HasWiring);
                Assert.Equal("No LED groups yet.", viewModel.WiringSummary);
            }

            // Front plate elements only: nothing to wire, they keep their LED numbers.
            var buttons = new DevicePart { Name = "Buttons", Channel = LedChannel.Buttons };
            buttons.EnsureDefaults();
            buttons.ApplyFrontPlate("1", "1x2");
            using (var viewModel = new DeviceViewModel(buttons))
            {
                Assert.False(viewModel.HasWiring);
            }
        }

        [Fact]
        public void Moving_a_group_on_the_part_renumbers_from_the_first_start_index()
        {
            var part = Wheel();
            var row = part.Groups[0];
            var left = part.Groups[1];
            var right = part.Groups[2];
            foreach (var group in new[] { row, left, right }) group.StartIndex += 5;

            Assert.True(part.MoveWiring(0, 2));
            Assert.Equal(new[] { left, right, row }, part.GroupsInWiringOrder());
            Assert.Equal(5, left.StartIndex);
            Assert.Equal(8, right.StartIndex);
            Assert.Equal(11, row.StartIndex);

            Assert.False(part.MoveWiring(1, 1));
            Assert.False(part.MoveWiring(0, 3));
            Assert.False(part.MoveWiring(-1, 0));
        }

        [Fact]
        public void Packing_chains_the_groups_again_after_a_change()
        {
            var part = Wheel();
            var row = part.Groups[0];
            var left = part.Groups[1];
            var right = part.Groups[2];
            var dial = part.Elements.Single();
            var dialIndex = dial.StartIndex;

            row.Count = 10;
            part.RemoveGroup(left);
            part.PackWiring();
            Assert.Equal(0, row.StartIndex);
            Assert.Equal(10, right.StartIndex);
            Assert.Equal(dialIndex, dial.StartIndex);

            // Without the first group the strip would begin with a gap; packing from where it started closes it.
            part.RemoveGroup(row);
            part.PackWiring(0);
            Assert.Equal(0, right.StartIndex);
        }

        [Fact]
        public void Wiring_editor_edits_the_part_and_reports_each_change()
        {
            // The editor the wizard and the device page's wiring dialog share.
            var part = Wheel();
            var editor = new WiringEditor(part, 0);
            var changes = 0;
            editor.Changed += (s, e) => changes++;
            Assert.Equal(part.GroupsInWiringOrder(), editor.Groups);
            Assert.Equal("21 LEDs in groups", editor.LedSummary);
            Assert.False(editor.HasTooManyLeds);

            editor.Groups[0].Reversed = true;
            Assert.Equal(1, changes);

            editor.MoveCommand.Execute(new MoveRequest(0, 2));
            Assert.Equal(new[] { 0, 3, 6 }, editor.Groups.Select(g => g.StartIndex));
            Assert.Equal(2, changes);

            editor.Groups[0].Count = 5;
            Assert.Equal(new[] { 0, 5, 8 }, editor.Groups.Select(g => g.StartIndex));
            Assert.Equal("23 LEDs in groups", editor.LedSummary);

            // Not shown any more: it stops following the groups.
            editor.Detach();
            var before = changes;
            editor.Groups[1].Reversed = true;
            Assert.Equal(before, changes);
        }

        [Fact]
        public void Confirmation_pattern_lights_every_led_green_and_breathes()
        {
            var part = Wheel();
            var dim = ConfirmationPattern.Render(part, 0);
            var bright = ConfirmationPattern.Render(part, ConfirmationPattern.PeriodMs / 2);

            Assert.Equal(22, dim.Length);
            Assert.All(dim, c => Assert.Equal(ConfirmationPattern.Color.Scale(ConfirmationPattern.Dim), c));
            Assert.All(bright, c => Assert.Equal(ConfirmationPattern.Color, c));

            part.Brightness = 50;
            Assert.Equal(ConfirmationPattern.Color.Scale(0.5), ConfirmationPattern.Render(part, ConfirmationPattern.PeriodMs / 2)[0]);
        }

        [Fact]
        public void Test_mode_shows_the_confirmation_on_one_device_until_cleared()
        {
            var test = new TestMode();
            var id = Guid.NewGuid();
            test.ConfirmationDeviceId = id;
            Assert.Equal(id, test.ConfirmationDeviceId);
            Assert.True(test.AnyActive);

            test.Clear();
            Assert.Equal(Guid.Empty, test.ConfirmationDeviceId);
            Assert.False(test.AnyActive);
        }

        [Fact]
        public void Test_mode_shows_the_pattern_on_one_part_until_cleared()
        {
            var test = new TestMode();
            Assert.False(test.AnyActive);
            var id = Guid.NewGuid();
            test.WiringPartId = id;
            Assert.Equal(id, test.WiringPartId);
            Assert.True(test.AnyActive);

            test.Clear();
            Assert.Equal(Guid.Empty, test.WiringPartId);
            Assert.False(test.AnyActive);
        }
    }
}
