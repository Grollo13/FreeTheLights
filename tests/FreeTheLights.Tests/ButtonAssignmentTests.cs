using System;
using System.Collections.Generic;
using System.Linq;
using FreeTheLights.Core;
using FreeTheLights.Settings;
using FreeTheLights.UI;
using Xunit;

namespace FreeTheLights.Tests
{
    /// <summary>The wizard's button assignment: pressing the buttons and turning the dials one by one.</summary>
    public class ButtonAssignmentTests
    {
        private long now = 1000;
        private readonly InputState inputs = new InputState();
        private readonly List<Action> posted = new List<Action>();

        /// <summary>A wheel with two buttons per side and one dial in the center.</summary>
        private static DeviceConfig Wheel()
        {
            var part = DevicePart.CreateDefault("Wheel");
            part.ApplyFrontPlate("1", "1x2");
            return DeviceConfig.WithPart("Wheel", DeviceType.Wheel, part);
        }

        private ButtonAssignment Assign(DeviceConfig device, bool deferred = false)
        {
            var assignment = new ButtonAssignment(device, inputs, a => { if (deferred) posted.Add(a); else a(); }, () => now);
            assignment.Start();
            return assignment;
        }

        [Fact]
        public void Asks_for_the_left_side_then_both_dial_directions_then_the_right_side()
        {
            var device = Wheel();
            var assignment = Assign(device);
            var part = device.Parts.Single();
            var left = part.Elements.Where(e => e.Side == LedGroupSide.Left).OrderBy(e => e.ElementSlot).ToList();
            var right = part.Elements.Where(e => e.Side == LedGroupSide.Right).OrderBy(e => e.ElementSlot).ToList();
            var dial = part.Elements.Single(e => e.ElementType == ElementType.Dial);

            var order = ButtonAssignment.Order(device).Select(o => (o.Element, o.Slot)).ToList();
            Assert.Equal(
                left.Select(e => (e, InputSlot.Button))
                    .Concat(new[] { (dial, InputSlot.Clockwise), (dial, InputSlot.CounterClockwise) })
                    .Concat(right.Select(e => (e, InputSlot.Button))),
                order);
            Assert.Equal(order.Count, assignment.Count);
            Assert.Equal("Press " + left[0].Name, assignment.Prompt);
            Assert.Equal($"1 of {order.Count}", assignment.Progress);
        }

        [Fact]
        public void A_pressed_input_is_assigned_and_the_next_one_is_asked_for()
        {
            var device = Wheel();
            var assignment = Assign(device);
            var first = assignment.Current;

            inputs.Press("Wheel_B01");
            Assert.Equal("Wheel_B01", first.ButtonInput);
            Assert.Equal(1, assignment.Index);
            Assert.Contains("Wheel_B01", assignment.Note);

            now += ButtonAssignment.LockoutMs;
            inputs.Press("Wheel_B02");
            Assert.Equal(2, assignment.Index);
        }

        [Fact]
        public void Inputs_right_after_an_assigned_one_belong_to_the_same_click()
        {
            var assignment = Assign(Wheel());
            var first = assignment.Current;
            inputs.Press("Wheel_B01");
            var second = assignment.Current;

            // A bounce, or the same click reported under another name.
            now += ButtonAssignment.LockoutMs - 1;
            inputs.Press("Wheel_B01");
            inputs.Press("Wheel_Other_B01");
            Assert.Same(second, assignment.Current);
            Assert.Equal(string.Empty, second.ButtonInput);
            Assert.Equal("Wheel_B01", first.ButtonInput);
        }

        [Fact]
        public void The_input_just_assigned_is_not_taken_for_the_next_button()
        {
            var assignment = Assign(Wheel());
            var first = assignment.Current;
            inputs.Press("Wheel_B01");
            var second = assignment.Current;

            // Held, turned on, or pressed again by mistake: it stays with the first button.
            now += 2000;
            inputs.Press("Wheel_B01");
            Assert.Same(second, assignment.Current);
            Assert.Equal("Wheel_B01", first.ButtonInput);
            Assert.Contains("already " + first.Name, assignment.Note);

            // Previous asks for the first button again, and the same input can be pressed for it.
            assignment.Back();
            now += 2000;
            inputs.Press("Wheel_B01");
            Assert.Same(second, assignment.Current);
            Assert.Equal("Wheel_B01", first.ButtonInput);
        }

        [Fact]
        public void An_input_on_its_way_when_the_prompt_changes_is_dropped()
        {
            // Pressed for the first prompt, but handled on the UI thread only after Skip: it must not fill the second one.
            var assignment = Assign(Wheel(), deferred: true);
            var first = assignment.Current;
            inputs.Press("Wheel_B01");
            assignment.Skip();
            var second = assignment.Current;
            foreach (var action in posted) action();

            Assert.Equal(string.Empty, first.ButtonInput);
            Assert.Equal(string.Empty, second.ButtonInput);
            Assert.Same(second, assignment.Current);

            // Inputs pressed after the change still count.
            posted.Clear();
            now += 5000;
            inputs.Press("Wheel_B02");
            foreach (var action in posted) action();
            Assert.Equal("Wheel_B02", second.ButtonInput);
        }

        [Fact]
        public void Inputs_are_timed_when_simhub_reports_them()
        {
            // Two inputs reported together but handled later on the UI thread still count as one click.
            var assignment = Assign(Wheel(), deferred: true);
            var first = assignment.Current;
            inputs.Press("Wheel_B01");
            inputs.Press("Wheel_Other_B01");
            now += 5000;
            foreach (var action in posted) action();

            Assert.Equal(1, assignment.Index);
            Assert.Equal("Wheel_B01", first.ButtonInput);
        }

        [Fact]
        public void Turning_a_dial_asks_for_both_directions()
        {
            var device = Wheel();
            var assignment = Assign(device);
            var dial = device.Parts.Single().Elements.Single(e => e.ElementType == ElementType.Dial);
            while (assignment.Current != dial) assignment.Skip();

            Assert.Equal("Turn " + dial.Name + " clockwise", assignment.Prompt);
            inputs.Press("Wheel_Enc1_Cw");
            Assert.Equal("Turn " + dial.Name + " counter-clockwise", assignment.Prompt);
            now += ButtonAssignment.LockoutMs;
            inputs.Press("Wheel_Enc1_Ccw");
            Assert.Equal("Wheel_Enc1_Cw", dial.ClockwiseInput);
            Assert.Equal("Wheel_Enc1_Ccw", dial.CounterClockwiseInput);
        }

        [Fact]
        public void An_input_pressed_again_for_another_button_moves_there()
        {
            var device = Wheel();
            var assignment = Assign(device);
            var first = assignment.Current;
            inputs.Press("Wheel_B01");
            now += ButtonAssignment.LockoutMs;
            inputs.Press("Wheel_B02");
            var third = assignment.Current;
            var thirdSlot = assignment.CurrentSlot;

            now += ButtonAssignment.LockoutMs;
            inputs.Press("Wheel_B01");
            Assert.Equal(string.Empty, first.ButtonInput);
            Assert.Equal("Wheel_B01", third.GetInput(thirdSlot));
            Assert.Contains("moved from " + first.Name, assignment.Note);
        }

        [Fact]
        public void Skip_and_back_move_through_the_prompts()
        {
            var assignment = Assign(Wheel());
            Assert.False(assignment.CanGoBack);
            var first = assignment.Current;

            assignment.Skip();
            Assert.Equal(1, assignment.Index);
            Assert.Equal(string.Empty, first.ButtonInput);
            assignment.Back();
            Assert.Same(first, assignment.Current);

            for (var i = 0; i < assignment.Count; i++) assignment.Skip();
            Assert.True(assignment.IsDone);
            Assert.Null(assignment.Current);
            Assert.Equal($"0 of {assignment.Count} assigned.", assignment.Prompt);
            inputs.Press("Wheel_B01");
            Assert.Equal(string.Empty, first.ButtonInput);
        }

        [Fact]
        public void Inputs_after_stop_are_ignored_even_when_already_on_their_way()
        {
            var assignment = Assign(Wheel(), deferred: true);
            var first = assignment.Current;

            inputs.Press("Wheel_B01");
            assignment.Stop();
            foreach (var action in posted) action();
            Assert.Equal(string.Empty, first.ButtonInput);

            posted.Clear();
            inputs.Press("Wheel_B02");
            Assert.Empty(posted);
        }

        [Fact]
        public void The_device_shows_the_button_to_press_white_and_assigned_ones_green()
        {
            var device = Wheel();
            var assignment = Assign(device);
            var part = device.Parts.Single();
            var first = assignment.Current;
            inputs.Press("Wheel_B01");
            var second = assignment.Current;

            var frame = assignment.Frame(part);
            Assert.Equal(ButtonAssignment.AssignedColor, frame[first.StartIndex]);
            Assert.Equal(ButtonAssignment.CurrentColor, frame[second.StartIndex]);
            Assert.Equal(LedColor.Transparent, frame[part.Groups[0].StartIndex]);
            Assert.All(part.Elements.Where(e => e != first && e != second), e => Assert.Equal(LedColor.Off, frame[e.StartIndex]));

            // The copy for the game loop: same ids, only the static colors on, the device itself unchanged.
            var shown = assignment.DisplayDevice();
            var shownPart = shown.Parts.Single();
            Assert.Equal(device.Id, shown.Id);
            Assert.Equal(part.Id, shownPart.Id);
            Assert.All(shownPart.Features.Where(f => f != shownPart.StaticColors), f => Assert.False(f.Enabled));
            Assert.True(shownPart.StaticColors.Enabled);
            Assert.Equal(ButtonAssignment.CurrentColor.ToHex(), shownPart.Groups.Single(g => g.Id == second.Id).StaticColor);
            Assert.True(part.Rpm.Enabled);
        }
    }
}
