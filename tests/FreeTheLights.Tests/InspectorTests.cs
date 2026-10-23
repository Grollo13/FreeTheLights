using System;
using System.Collections.Generic;
using System.Linq;
using FreeTheLights.Core;
using FreeTheLights.Engine;
using FreeTheLights.Settings;
using FreeTheLights.UI;
using Xunit;

namespace FreeTheLights.Tests
{
    public class InspectorTests
    {
        /// <summary>The default wheel (rows and columns on LEDs 1-21) with 3 dials (1 on top, 2 below) and 2 × 3 buttons per side.</summary>
        private static DevicePart Wheel()
        {
            var part = DevicePart.CreateDefault("Wheel");
            part.ApplyFrontPlate("1-2", "2x3");
            return part;
        }

        private static LedGroup Element(DevicePart part, LedGroupSide side, int slot) => part.Elements.Single(e => e.Side == side && e.ElementSlot == slot);

        private static LedGroup Named(DevicePart part, string name) => part.Groups.Single(g => g.Name == name);

        private static List<string> Record(LedInspector inspector)
        {
            var names = new List<string>();
            inspector.PropertyChanged += (s, e) => names.Add(e.PropertyName);
            return names;
        }

        [Fact]
        public void A_detached_inspector_ignores_the_settings_and_catches_up_when_attached()
        {
            var part = Wheel();
            var inspector = new LedInspector();
            var row = Named(part, "RPM LEDs");
            inspector.Select(part, row, 2, false);
            var names = Record(inspector);

            inspector.Detach();
            row.Name = "Top row";
            part.RemoveGroup(row);
            Assert.Empty(names);
            Assert.Same(row, inspector.Group);

            // The row was removed meanwhile: it leaves the selection.
            inspector.Attach();
            Assert.True(inspector.IsNone);
            Assert.Contains(nameof(LedInspector.IsNone), names);

            // Attached again: follows the part as before.
            var element = Element(part, LedGroupSide.Left, 0);
            inspector.Select(part, element, 0, false);
            part.RemoveGroup(element);
            Assert.True(inspector.IsNone);
        }

        [Fact]
        public void A_detached_part_view_model_ignores_the_settings_and_catches_up_when_attached()
        {
            var part = DevicePart.CreateDefault("Wheel");
            using (var viewModel = new DeviceViewModel(part))
            {
                var changed = new List<string>();
                viewModel.PropertyChanged += (s, e) => changed.Add(e.PropertyName);

                viewModel.Detach();
                part.AddGroup(new LedGroup { Name = "Extra", Kind = LedGroupKind.VerticalColumn, Count = 2 });
                part.Groups[0].StartIndex = 40;
                Assert.Empty(changed);
                Assert.DoesNotContain(viewModel.FlagTargets, t => t.Group.Name == "Extra");

                viewModel.Attach();
                Assert.Contains(viewModel.FlagTargets, t => t.Group.Name == "Extra");
                Assert.Equal(WiringPattern.GroupsInWiringOrder(part), viewModel.WiringGroups);
                Assert.Contains(nameof(DeviceViewModel.HeaderSummary), changed);

                // Attached again: follows the part as before, including the group added meanwhile.
                changed.Clear();
                part.Groups.Single(g => g.Name == "Extra").Count = 4;
                Assert.Contains(nameof(DeviceViewModel.HeaderSummary), changed);
            }
        }

        [Fact]
        public void Clicking_an_element_selects_it()
        {
            var part = Wheel();
            var inspector = new LedInspector();
            var element = Element(part, LedGroupSide.Left, 0);

            inspector.Select(part, element, 0, false);

            Assert.Equal(InspectorMode.Elements, inspector.Mode);
            Assert.True(inspector.IsElements);
            Assert.False(inspector.IsNone);
            Assert.False(inspector.IsGroup);
            Assert.Same(part, inspector.Part);
            Assert.Same(element, Assert.Single(inspector.Elements));
            Assert.Null(inspector.Group);
            Assert.Equal(-1, inspector.ClickedLogical);
            Assert.True(inspector.IsSingle);
            Assert.Equal("Left 1", inspector.Title);
            Assert.Equal("Left 1", inspector.Name);
            Assert.Equal("button · left side · LED #22", inspector.Subtitle);
            Assert.Equal(21, inspector.LedIndex);
            Assert.True(inspector.CanChangeIndex);
            Assert.Equal("outer column, row 1", inspector.Position);
            Assert.Equal(ElementType.Button, inspector.Type);
            Assert.True(inspector.CanChangeType);
            Assert.Equal(new[] { ElementType.Button, ElementType.Light }, inspector.TypeChoices);
            Assert.Same(EffectChoice.All, inspector.EffectChoices);
        }

        [Fact]
        public void Subtitle_names_the_side_and_type()
        {
            var part = Wheel();
            var inspector = new LedInspector();

            inspector.Select(part, Element(part, LedGroupSide.Right, 1), 0, false);
            Assert.Equal("button · right side · LED #" + (Element(part, LedGroupSide.Right, 1).StartIndex + 1), inspector.Subtitle);

            var dial = Element(part, LedGroupSide.None, 2);
            inspector.Select(part, dial, 0, false);
            Assert.Equal("dial · center · LED #" + (dial.StartIndex + 1), inspector.Subtitle);
            Assert.Equal("Dial 3", inspector.Title);
        }

        [Fact]
        public void Ctrl_click_adds_and_removes_elements()
        {
            var part = Wheel();
            var inspector = new LedInspector();
            var a = Element(part, LedGroupSide.Left, 0);
            var b = Element(part, LedGroupSide.Left, 1);
            var c = Element(part, LedGroupSide.Right, 0);

            inspector.Select(part, a, 0, false);
            inspector.Select(part, b, 0, true);
            inspector.Select(part, c, 0, true);

            Assert.Equal(new[] { a, b, c }, inspector.Elements);
            Assert.False(inspector.IsSingle);
            Assert.Equal("3 elements", inspector.Title);
            Assert.Equal("3 buttons", inspector.Subtitle);
            Assert.Null(inspector.Name);
            Assert.Equal(string.Empty, inspector.Position);

            inspector.Select(part, b, 0, true);
            Assert.Equal(new[] { a, c }, inspector.Elements);

            inspector.Select(part, a, 0, true);
            inspector.Select(part, c, 0, true);
            Assert.Equal(InspectorMode.None, inspector.Mode);
            Assert.Empty(inspector.Elements);
            Assert.Same(part, inspector.Part);
        }

        [Fact]
        public void Ctrl_click_on_a_first_element_starts_a_selection()
        {
            var part = Wheel();
            var inspector = new LedInspector();
            var a = Element(part, LedGroupSide.Left, 0);

            inspector.Select(part, a, 0, true);

            Assert.Same(a, Assert.Single(inspector.Elements));
        }

        [Fact]
        public void A_plain_click_replaces_the_selection()
        {
            var part = Wheel();
            var inspector = new LedInspector();
            var a = Element(part, LedGroupSide.Left, 0);
            var b = Element(part, LedGroupSide.Left, 1);
            var row = Named(part, "RPM LEDs");

            inspector.Select(part, a, 0, false);
            inspector.Select(part, b, 0, true);
            inspector.Select(part, a, 0, false);
            Assert.Same(a, Assert.Single(inspector.Elements));

            // A group replaces elements, even with Ctrl held, and an element replaces a group.
            inspector.Select(part, row, 3, true);
            Assert.Equal(InspectorMode.Group, inspector.Mode);
            Assert.Empty(inspector.Elements);
            inspector.Select(part, b, 0, true);
            Assert.Equal(InspectorMode.Elements, inspector.Mode);
            Assert.Same(b, Assert.Single(inspector.Elements));
            Assert.Null(inspector.Group);
        }

        [Fact]
        public void Clicking_empty_space_clears_unless_adding()
        {
            var part = Wheel();
            var inspector = new LedInspector();
            var a = Element(part, LedGroupSide.Left, 0);
            var raised = 0;
            inspector.SelectionChanged += (s, e) => raised++;

            inspector.Clear();
            inspector.Select(part, null, -1, false);
            Assert.Equal(0, raised);

            inspector.Select(part, a, 0, false);
            Assert.Equal(1, raised);

            inspector.Select(part, null, -1, true);
            Assert.Equal(InspectorMode.Elements, inspector.Mode);
            Assert.Equal(1, raised);

            inspector.Select(part, null, -1, false);
            Assert.Equal(InspectorMode.None, inspector.Mode);
            Assert.Empty(inspector.Elements);
            Assert.Equal(string.Empty, inspector.Title);
            Assert.Equal(string.Empty, inspector.Subtitle);
            Assert.Equal(2, raised);

            inspector.Select(part, a, 0, false);
            inspector.Clear();
            Assert.Equal(InspectorMode.None, inspector.Mode);
            Assert.Equal(4, raised);
        }

        [Fact]
        public void The_selection_changed_event_and_property_changes_are_raised()
        {
            var part = Wheel();
            var inspector = new LedInspector();
            var names = Record(inspector);
            var raised = 0;
            inspector.SelectionChanged += (s, e) => raised++;

            inspector.Select(part, Element(part, LedGroupSide.Left, 0), 0, false);

            Assert.Equal(1, raised);
            Assert.Contains(nameof(LedInspector.Mode), names);
            Assert.Contains(nameof(LedInspector.IsElements), names);
            Assert.Contains(nameof(LedInspector.Elements), names);
            Assert.Contains(nameof(LedInspector.Title), names);
            Assert.Contains(nameof(LedInspector.Subtitle), names);
            Assert.Contains(nameof(LedInspector.Effect), names);

            // Selecting what is already selected is not a change.
            inspector.Select(part, Element(part, LedGroupSide.Left, 0), 0, false);
            Assert.Equal(1, raised);
        }

        [Fact]
        public void Static_color_shows_mixed_values_and_writes_to_all_selected_elements()
        {
            var part = Wheel();
            var inspector = new LedInspector();
            var a = Element(part, LedGroupSide.Left, 0);
            var b = Element(part, LedGroupSide.Left, 1);
            a.StaticColor = "#FFFF0000";

            inspector.Select(part, a, 0, false);
            Assert.Equal("#FFFF0000", inspector.StaticColor);
            Assert.False(inspector.IsColorMixed);

            inspector.Select(part, b, 0, true);
            Assert.Null(inspector.StaticColor);
            Assert.True(inspector.IsColorMixed);

            var names = Record(inspector);
            inspector.StaticColor = "#FF00FF00";
            Assert.Equal("#FF00FF00", a.StaticColor);
            Assert.Equal("#FF00FF00", b.StaticColor);
            Assert.Equal("#FF00FF00", inspector.StaticColor);
            Assert.False(inspector.IsColorMixed);
            Assert.Contains(nameof(LedInspector.StaticColor), names);
            Assert.Contains(nameof(LedInspector.IsColorMixed), names);

            // "No color" is an empty string, and it is the same for every element that has none.
            inspector.StaticColor = string.Empty;
            Assert.Equal(string.Empty, inspector.StaticColor);
            Assert.Equal(string.Empty, a.StaticColor);
            Assert.False(inspector.IsColorMixed);

            // Setting null (the mixed value) changes nothing; the case of a color does not matter.
            inspector.StaticColor = null;
            Assert.Equal(string.Empty, a.StaticColor);
            a.StaticColor = "#FFAABBCC";
            b.StaticColor = "#ffaabbcc";
            Assert.False(inspector.IsColorMixed);
        }

        [Fact]
        public void Press_color_shows_mixed_values_and_writes_to_all_selected_elements()
        {
            var part = Wheel();
            var inspector = new LedInspector();
            var a = Element(part, LedGroupSide.Left, 0);
            var b = Element(part, LedGroupSide.Left, 1);
            a.PressColor = "#FF000000";

            inspector.Select(part, a, 0, false);
            Assert.Equal("#FF000000", inspector.PressColor);
            inspector.Select(part, b, 0, true);
            Assert.Null(inspector.PressColor);
            Assert.True(inspector.IsPressColorMixed);

            var names = Record(inspector);
            inspector.PressColor = "#FF00FF00";
            Assert.Equal("#FF00FF00", a.PressColor);
            Assert.Equal("#FF00FF00", b.PressColor);
            Assert.False(inspector.IsPressColorMixed);
            Assert.Contains(nameof(LedInspector.PressColor), names);

            inspector.PressColor = null;
            Assert.Equal("#FF00FF00", a.PressColor);
        }

        [Fact]
        public void Press_settings_show_only_for_buttons_and_dials()
        {
            var part = Wheel();
            var inspector = new LedInspector { InputSource = new InputState() };
            var button = Element(part, LedGroupSide.Left, 0);
            var light = Element(part, LedGroupSide.Left, 1);
            light.ElementType = ElementType.Light;
            var dial = Element(part, LedGroupSide.None, 0);

            inspector.Select(part, button, 0, false);
            Assert.True(inspector.CanPress);
            Assert.Equal(new[] { InputSlot.Button }, inspector.InputSlots.Select(i => i.Slot));

            inspector.Select(part, dial, 0, false);
            Assert.True(inspector.CanPress);
            Assert.Equal(new[] { InputSlot.Clockwise, InputSlot.CounterClockwise }, inspector.InputSlots.Select(i => i.Slot));

            inspector.Select(part, light, 0, false);
            Assert.False(inspector.CanPress);
            Assert.False(inspector.HasInputSlots);
            Assert.Null(inspector.PressColor);
            inspector.PressColor = "#FF00FF00";
            Assert.Equal(string.Empty, light.PressColor);

            // Several elements: the press color only when all of them can be pressed; inputs one element at a time.
            inspector.Select(part, button, 0, false);
            inspector.Select(part, dial, 0, true);
            Assert.True(inspector.CanPress);
            Assert.False(inspector.HasInputSlots);
            inspector.Select(part, light, 0, true);
            Assert.False(inspector.CanPress);
        }

        [Fact]
        public void A_button_has_one_input_and_assigning_replaces_it()
        {
            var part = Wheel();
            var source = new InputState();
            var inspector = new LedInspector();
            var button = Element(part, LedGroupSide.Left, 0);

            // Without SimHub's inputs nothing can be assigned.
            inspector.Select(part, button, 0, false);
            Assert.False(inspector.CanAssignInput);
            Assert.False(inspector.InputSlots.Single().CanAssign);
            inspector.InputSource = source;
            Assert.True(inspector.CanAssignInput);

            var slot = inspector.InputSlots.Single();
            Assert.False(slot.IsAssigned);
            Assert.Equal("Not assigned", slot.Text);
            Assert.Equal("Assign", slot.AssignText);

            var names = Record(inspector);
            inspector.StartCapture(InputSlot.Button);
            Assert.True(inspector.IsCapturing);
            Assert.True(inspector.InputSlots.Single().IsCapturing);
            Assert.Equal("Press the button now…", inspector.InputSlots.Single().Text);
            source.Press("JoystickPlugin.Wheel_B05");
            Assert.False(inspector.IsCapturing);
            Assert.Equal("JoystickPlugin.Wheel_B05", button.ButtonInput);
            Assert.Contains(nameof(LedInspector.InputSlots), names);

            slot = inspector.InputSlots.Single();
            Assert.Equal("Wheel_B05", slot.Text);
            Assert.Equal("Change", slot.AssignText);

            // Only the first press counts; assigning again replaces the input.
            source.Press("JoystickPlugin.Wheel_B30");
            Assert.Equal("JoystickPlugin.Wheel_B05", button.ButtonInput);
            inspector.StartCapture(InputSlot.Button);
            source.Press("JoystickPlugin.Wheel_B06");
            Assert.Equal("JoystickPlugin.Wheel_B06", button.ButtonInput);

            // A dial's slots don't exist on a button.
            inspector.StartCapture(InputSlot.Clockwise);
            Assert.False(inspector.IsCapturing);

            inspector.ClearInput(InputSlot.Button);
            Assert.Equal(string.Empty, button.ButtonInput);
        }

        [Fact]
        public void A_dial_has_one_input_per_direction()
        {
            var part = Wheel();
            var source = new InputState();
            var inspector = new LedInspector { InputSource = source };
            var dial = Element(part, LedGroupSide.None, 0);
            inspector.Select(part, dial, 0, false);

            inspector.StartCapture(InputSlot.Clockwise);
            Assert.Equal("Turn the dial one step clockwise now…", inspector.InputSlots[0].Text);
            source.Press("JoystickPlugin.Wheel_B20");
            inspector.StartCapture(InputSlot.CounterClockwise);
            source.Press("JoystickPlugin.Wheel_B21");
            Assert.Equal("JoystickPlugin.Wheel_B20", dial.ClockwiseInput);
            Assert.Equal("JoystickPlugin.Wheel_B21", dial.CounterClockwiseInput);
            Assert.Equal(new[] { "Wheel_B20", "Wheel_B21" }, inspector.InputSlots.Select(i => i.Text));

            // One input turns the dial one way only: assigning it to the other direction moves it.
            inspector.StartCapture(InputSlot.CounterClockwise);
            source.Press("JoystickPlugin.Wheel_B20");
            Assert.Equal(string.Empty, dial.ClockwiseInput);
            Assert.Equal("JoystickPlugin.Wheel_B20", dial.CounterClockwiseInput);

            inspector.ClearInput(InputSlot.CounterClockwise);
            Assert.Equal(string.Empty, dial.CounterClockwiseInput);
        }

        [Fact]
        public void Changing_the_type_ends_the_capture()
        {
            var part = Wheel();
            var source = new InputState();
            var inspector = new LedInspector { InputSource = source };
            var button = Element(part, LedGroupSide.Left, 0);
            inspector.Select(part, button, 0, false);

            inspector.StartCapture(InputSlot.Button);
            inspector.Type = ElementType.Light;
            Assert.False(inspector.IsCapturing);
            source.Press("B1");

            inspector.Type = ElementType.Button;
            Assert.False(inspector.IsCapturing);
            source.Press("B2");
            Assert.Equal(string.Empty, button.ButtonInput);
        }

        [Fact]
        public void The_type_field_is_hidden_only_when_only_dials_are_selected()
        {
            var part = Wheel();
            var inspector = new LedInspector();
            var button = Element(part, LedGroupSide.Left, 0);
            var dial = Element(part, LedGroupSide.None, 0);

            inspector.Select(part, dial, 0, false);
            Assert.False(inspector.ShowsType);
            inspector.SelectAll(part, ElementType.Dial);
            Assert.False(inspector.ShowsType);

            // With a dial among them the field shows, read-only.
            inspector.Select(part, button, 0, false);
            inspector.Select(part, dial, 0, true);
            Assert.True(inspector.ShowsType);
            Assert.False(inspector.CanChangeType);

            inspector.SelectAll(part, null);
            Assert.True(inspector.ShowsType);
            inspector.Select(part, button, 0, false);
            Assert.True(inspector.ShowsType);
            Assert.True(inspector.CanChangeType);
        }

        [Fact]
        public void A_new_selection_or_cancel_ends_the_capture_without_assigning()
        {
            var part = Wheel();
            var source = new InputState();
            var inspector = new LedInspector { InputSource = source };
            var a = Element(part, LedGroupSide.Left, 0);
            var b = Element(part, LedGroupSide.Left, 1);

            inspector.Select(part, a, 0, false);
            inspector.StartCapture(InputSlot.Button);
            inspector.Select(part, b, 0, false);
            Assert.False(inspector.IsCapturing);
            source.Press("B1");
            Assert.Equal(string.Empty, a.ButtonInput);
            Assert.Equal(string.Empty, b.ButtonInput);

            inspector.StartCapture(InputSlot.Button);
            inspector.CancelCapture();
            source.Press("B1");
            Assert.Equal(string.Empty, b.ButtonInput);

            // Captured inputs go through Post (the settings page's dispatcher); one arriving after the capture ended is dropped.
            var posted = new List<Action>();
            inspector.Post = posted.Add;
            inspector.StartCapture(InputSlot.Button);
            source.Press("B2");
            inspector.CancelCapture();
            posted.ForEach(p => p());
            Assert.Equal(string.Empty, b.ButtonInput);

            // A button that became a light meanwhile gets no input.
            inspector.StartCapture(InputSlot.Button);
            source.Press("B3");
            b.ElementType = ElementType.Light;
            posted.Last()();
            Assert.Equal(string.Empty, b.ButtonInput);

            b.ElementType = ElementType.Button;
            inspector.StartCapture(InputSlot.Button);
            source.Press("B4");
            posted.Last()();
            Assert.Equal("B4", b.ButtonInput);
        }

        [Fact]
        public void An_input_queued_for_an_ended_capture_does_not_go_to_the_next_one()
        {
            var part = Wheel();
            var source = new InputState();
            var posted = new List<Action>();
            var inspector = new LedInspector { InputSource = source, Post = posted.Add };
            var dial = Element(part, LedGroupSide.None, 0);
            inspector.Select(part, dial, 0, false);

            // An input reaches the UI thread only after the user cancelled and started capturing the other direction.
            inspector.StartCapture(InputSlot.Clockwise);
            source.Press("B1");
            inspector.CancelCapture();
            inspector.StartCapture(InputSlot.CounterClockwise);
            posted[0]();
            Assert.True(inspector.IsCapturing);
            Assert.Equal(string.Empty, dial.ClockwiseInput);
            Assert.Equal(string.Empty, dial.CounterClockwiseInput);

            // The new capture still takes its own input.
            source.Press("B2");
            posted.Last()();
            Assert.False(inspector.IsCapturing);
            Assert.Equal("B2", dial.CounterClockwiseInput);
        }

        [Fact]
        public void Effect_shows_mixed_values_and_gives_every_selected_element_exactly_that_effect()
        {
            var part = Wheel();
            var inspector = new LedInspector();
            var a = Element(part, LedGroupSide.Left, 0);
            var b = Element(part, LedGroupSide.Left, 1);
            var c = Element(part, LedGroupSide.Right, 0);
            part.SetElementEffect(a, LayerKind.Abs);
            part.SetElementEffect(b, LayerKind.Flags);

            inspector.Select(part, a, 0, false);
            Assert.Same(EffectChoice.For(LayerKind.Abs), inspector.Effect);
            Assert.False(inspector.IsEffectMixed);

            inspector.Select(part, b, 0, true);
            Assert.Null(inspector.Effect);
            Assert.True(inspector.IsEffectMixed);

            inspector.Select(part, c, 0, true);
            inspector.Effect = EffectChoice.For(LayerKind.Proximity);
            foreach (var element in new[] { a, b, c })
            {
                Assert.Equal(LayerKind.Proximity, part.GetElementEffect(element));
                Assert.DoesNotContain(element.Id, part.Abs.TargetGroupIds);
                Assert.DoesNotContain(element.Id, part.Flags.TargetGroupIds);
                Assert.Contains(element.Id, part.Proximity.TargetGroupIds);
            }

            Assert.Same(EffectChoice.For(LayerKind.Proximity), inspector.Effect);
            Assert.False(inspector.IsEffectMixed);

            inspector.Effect = EffectChoice.For(null);
            Assert.All(new[] { a, b, c }, e => Assert.Null(part.GetElementEffect(e)));
            Assert.Same(EffectChoice.For(null), inspector.Effect);

            // Null is the mixed value; setting it changes nothing.
            part.SetElementEffect(a, LayerKind.TractionControl);
            inspector.Effect = null;
            Assert.Equal(LayerKind.TractionControl, part.GetElementEffect(a));
            Assert.Null(part.GetElementEffect(b));
        }

        [Fact]
        public void Type_shows_mixed_values_and_dials_stay_dials()
        {
            var part = Wheel();
            var inspector = new LedInspector();
            var a = Element(part, LedGroupSide.Left, 0);
            var b = Element(part, LedGroupSide.Left, 1);
            var dial = Element(part, LedGroupSide.None, 0);
            b.ElementType = ElementType.Light;

            inspector.Select(part, a, 0, false);
            inspector.Select(part, b, 0, true);
            Assert.Null(inspector.Type);
            Assert.True(inspector.CanChangeType);
            Assert.Equal("1 button, 1 light", inspector.Subtitle);

            inspector.Type = ElementType.Light;
            Assert.Equal(ElementType.Light, a.ElementType);
            Assert.Equal(ElementType.Light, b.ElementType);
            Assert.Equal(ElementType.Light, inspector.Type);
            Assert.Equal("2 lights", inspector.Subtitle);

            inspector.Select(part, dial, 0, true);
            Assert.False(inspector.CanChangeType);
            Assert.Null(inspector.Type);
            Assert.Equal("2 lights, 1 dial", inspector.Subtitle);

            inspector.Type = ElementType.Button;
            Assert.Equal(ElementType.Dial, dial.ElementType);
            Assert.Equal(ElementType.Button, a.ElementType);
            Assert.Equal(ElementType.Button, b.ElementType);

            // A dial can't be turned into anything, and nothing can be turned into a dial.
            inspector.Select(part, dial, 0, false);
            Assert.False(inspector.CanChangeType);
            inspector.Type = ElementType.Light;
            Assert.Equal(ElementType.Dial, dial.ElementType);
            inspector.Select(part, a, 0, false);
            inspector.Type = ElementType.Dial;
            Assert.Equal(ElementType.Button, a.ElementType);
        }

        [Fact]
        public void Name_and_led_index_edit_the_single_element()
        {
            var part = Wheel();
            var inspector = new LedInspector();
            var a = Element(part, LedGroupSide.Left, 0);
            var b = Element(part, LedGroupSide.Left, 1);

            inspector.Select(part, a, 0, false);
            var names = Record(inspector);

            inspector.Name = "Pit limiter";
            Assert.Equal("Pit limiter", a.Name);
            Assert.Equal("Pit limiter", inspector.Title);
            Assert.Contains(nameof(LedInspector.Title), names);
            Assert.Contains(nameof(LedInspector.Subtitle), names);

            inspector.LedIndex = 30;
            Assert.Equal(30, a.StartIndex);
            Assert.Equal(30, inspector.LedIndex);
            Assert.Equal("button · left side · LED #31", inspector.Subtitle);

            // Several elements have neither a shared name nor a shared LED number.
            inspector.Select(part, b, 0, true);
            var bIndex = b.StartIndex;
            inspector.Name = "Both";
            inspector.LedIndex = 5;
            Assert.Equal("Pit limiter", a.Name);
            Assert.Equal("Left 2", b.Name);
            Assert.Equal(30, a.StartIndex);
            Assert.Equal(bIndex, b.StartIndex);
            Assert.False(inspector.CanChangeIndex);
        }

        [Fact]
        public void Elements_from_a_simhub_layout_have_a_fixed_led_number()
        {
            var part = Wheel();
            var inspector = new LedInspector();
            var a = Element(part, LedGroupSide.Right, 0);
            a.PhotoX = 100;
            a.PhotoY = 100;
            a.PhotoSize = 30;
            var start = a.StartIndex;

            inspector.Select(part, a, 0, false);

            Assert.False(inspector.CanChangeIndex);
            Assert.Equal("right side", inspector.Position);
            inspector.LedIndex = 3;
            Assert.Equal(start, a.StartIndex);
        }

        [Fact]
        public void Changes_made_elsewhere_refresh_the_title_and_subtitle()
        {
            var part = Wheel();
            var inspector = new LedInspector();
            var a = Element(part, LedGroupSide.Left, 0);
            inspector.Select(part, a, 0, false);
            var names = Record(inspector);

            a.Name = "Renamed";
            Assert.Contains(nameof(LedInspector.Title), names);
            Assert.Equal("Renamed", inspector.Title);

            names.Clear();
            a.ElementType = ElementType.Light;
            Assert.Contains(nameof(LedInspector.Subtitle), names);
            Assert.StartsWith("light", inspector.Subtitle);

            names.Clear();
            a.StartIndex = 40;
            Assert.Contains(nameof(LedInspector.Subtitle), names);
            Assert.EndsWith("LED #41", inspector.Subtitle);
        }

        [Fact]
        public void Clicking_a_group_selects_the_whole_group()
        {
            var part = Wheel();
            var inspector = new LedInspector();
            var row = Named(part, "RPM LEDs");
            var column = Named(part, "Left column");

            inspector.Select(part, row, 4, false);

            Assert.Equal(InspectorMode.Group, inspector.Mode);
            Assert.True(inspector.IsGroup);
            Assert.Same(row, inspector.Group);
            Assert.Empty(inspector.Elements);
            Assert.Equal(4, inspector.ClickedLogical);
            Assert.Equal("RPM LEDs", inspector.Title);
            Assert.Equal("LED 5 of 15 · device LED #5", inspector.Subtitle);
            Assert.False(inspector.IsSingle);
            Assert.False(inspector.CanChangeType);

            // The strip number follows the wiring.
            row.Reversed = true;
            Assert.Equal("LED 5 of 15 · device LED #11", inspector.Subtitle);
            row.Reversed = false;

            inspector.Select(part, column, 1, false);
            Assert.Equal("LED 2 of 3 · device LED #17", inspector.Subtitle);

            // Without a clicked LED the subtitle describes the whole group; adding never extends a group selection.
            inspector.Select(part, row, -1, true);
            Assert.Same(row, inspector.Group);
            Assert.Equal(-1, inspector.ClickedLogical);
            Assert.Equal("15 LEDs · LEDs #1-#15", inspector.Subtitle);

            inspector.Select(part, row, 99, false);
            Assert.Equal(-1, inspector.ClickedLogical);
        }

        [Fact]
        public void Group_edits_refresh_the_title_and_subtitle()
        {
            var part = Wheel();
            var inspector = new LedInspector();
            var row = Named(part, "RPM LEDs");
            inspector.Select(part, row, 0, false);
            var names = Record(inspector);

            row.Name = "RPM";
            Assert.Equal("RPM", inspector.Title);
            Assert.Contains(nameof(LedInspector.Title), names);

            row.Count = 10;
            Assert.Equal("LED 1 of 10 · device LED #1", inspector.Subtitle);

            inspector.Select(part, row, -1, false);
            row.StartIndex = 2;
            Assert.Equal("10 LEDs · LEDs #3-#12", inspector.Subtitle);
        }

        [Fact]
        public void Matrix_clicks_use_the_logical_index_of_the_pixel()
        {
            var part = new DevicePart { Name = "Flag" };
            part.EnsureDefaults();
            var matrix = new LedGroup { Name = "LED flag", Kind = LedGroupKind.Matrix, Rows = 4, Columns = 8 };
            part.AddGroup(matrix);
            var inspector = new LedInspector();

            // Pixel (2, 1) is logical index 1 * 8 + 2.
            inspector.Select(part, matrix, 10, false);

            Assert.Equal("LED 11 of 32 · device LED #11", inspector.Subtitle);
            Assert.Equal(new[] { LayerKind.Flags, LayerKind.Proximity, LayerKind.Abs, LayerKind.TractionControl, LayerKind.Idle }, inspector.Features.Select(f => f.Kind));
        }

        [Fact]
        public void Group_features_are_the_features_that_can_drive_the_group()
        {
            var part = Wheel();
            var inspector = new LedInspector();
            var row = Named(part, "RPM LEDs");
            var column = Named(part, "Left column");

            Assert.Empty(inspector.Features);

            inspector.Select(part, row, 0, false);
            Assert.Equal(
                new[] { LayerKind.Flags, LayerKind.Proximity, LayerKind.Abs, LayerKind.TractionControl, LayerKind.Rpm, LayerKind.Idle },
                inspector.Features.Select(f => f.Kind));
            Assert.Equal(
                new[] { "Race control flags", "Car proximity", "ABS", "Traction control", "RPM", "Idle" },
                inspector.Features.Select(f => f.Name));
            Assert.Equal(
                new[] { false, false, false, false, true, true },
                inspector.Features.Select(f => f.IsChecked));

            // RPM only drives rows.
            inspector.Select(part, column, 0, false);
            Assert.Equal(
                new[] { LayerKind.Flags, LayerKind.Proximity, LayerKind.Abs, LayerKind.TractionControl, LayerKind.Idle },
                inspector.Features.Select(f => f.Kind));
            Assert.All(inspector.Features, f => Assert.True(f.IsChecked));

            // Elements have no feature list.
            inspector.Select(part, Element(part, LedGroupSide.Left, 0), 0, false);
            Assert.Empty(inspector.Features);
        }

        [Fact]
        public void Checking_a_group_feature_toggles_the_target_group()
        {
            var part = Wheel();
            var inspector = new LedInspector();
            var row = Named(part, "RPM LEDs");
            inspector.Select(part, row, 0, false);
            var flags = inspector.Features.Single(f => f.Kind == LayerKind.Flags);
            var rpm = inspector.Features.Single(f => f.Kind == LayerKind.Rpm);
            var names = new List<string>();
            flags.PropertyChanged += (s, e) => names.Add(e.PropertyName);

            flags.IsChecked = true;
            Assert.Contains(row.Id, part.Flags.TargetGroupIds);
            Assert.True(flags.IsChecked);
            Assert.Contains(nameof(GroupFeatureOption.IsChecked), names);

            flags.IsChecked = true;
            Assert.Equal(1, part.Flags.TargetGroupIds.Count(id => id == row.Id));

            rpm.IsChecked = false;
            Assert.DoesNotContain(row.Id, part.Rpm.TargetGroupIds);
            flags.IsChecked = false;
            Assert.DoesNotContain(row.Id, part.Flags.TargetGroupIds);

            // The options are rebuilt on every selection and read the current targets.
            inspector.Select(part, row, 1, false);
            Assert.False(inspector.Features.Single(f => f.Kind == LayerKind.Flags).IsChecked);
            Assert.False(inspector.Features.Single(f => f.Kind == LayerKind.Rpm).IsChecked);
        }

        [Fact]
        public void A_removed_group_leaves_the_selection()
        {
            var part = Wheel();
            var inspector = new LedInspector();
            var a = Element(part, LedGroupSide.Left, 0);
            var b = Element(part, LedGroupSide.Left, 1);
            var c = Element(part, LedGroupSide.Right, 0);
            var raised = 0;
            inspector.SelectionChanged += (s, e) => raised++;

            inspector.Select(part, a, 0, false);
            inspector.Select(part, b, 0, true);
            inspector.Select(part, c, 0, true);
            Assert.Equal(3, raised);

            part.RemoveGroup(b);
            Assert.Equal(new[] { a, c }, inspector.Elements);
            Assert.Equal(4, raised);

            // Groups that are not selected don't touch the selection.
            part.RemoveGroup(Element(part, LedGroupSide.Right, 5));
            Assert.Equal(4, raised);

            part.RemoveGroup(a);
            part.RemoveGroup(c);
            Assert.Equal(InspectorMode.None, inspector.Mode);
            Assert.Empty(inspector.Elements);
            Assert.Equal(6, raised);

            var row = Named(part, "RPM LEDs");
            inspector.Select(part, row, 2, false);
            Assert.NotEmpty(inspector.Features);
            part.RemoveGroup(row);
            Assert.Equal(InspectorMode.None, inspector.Mode);
            Assert.Null(inspector.Group);
            Assert.Equal(-1, inspector.ClickedLogical);
            Assert.Empty(inspector.Features);
        }

        [Fact]
        public void Select_all_selects_every_element_or_every_element_of_a_type()
        {
            var part = Wheel();
            var inspector = new LedInspector();
            Element(part, LedGroupSide.Left, 0).ElementType = ElementType.Light;

            inspector.SelectAll(part, null);
            Assert.Equal(InspectorMode.Elements, inspector.Mode);
            Assert.Equal(15, inspector.Elements.Count);
            Assert.Equal("15 elements", inspector.Title);
            Assert.Equal("11 buttons, 1 light, 3 dials", inspector.Subtitle);

            inspector.SelectAll(part, ElementType.Button);
            Assert.Equal(11, inspector.Elements.Count);
            Assert.All(inspector.Elements, e => Assert.Equal(ElementType.Button, e.ElementType));
            Assert.Equal("11 buttons", inspector.Subtitle);

            inspector.SelectAll(part, ElementType.Dial);
            Assert.Equal(3, inspector.Elements.Count);
            Assert.False(inspector.CanChangeType);

            inspector.SelectAll(part, ElementType.Light);
            Assert.Same(Element(part, LedGroupSide.Left, 0), Assert.Single(inspector.Elements));
            Assert.True(inspector.IsSingle);

            // Nothing of that type: the selection is cleared.
            Element(part, LedGroupSide.Left, 0).ElementType = ElementType.Button;
            inspector.SelectAll(part, ElementType.Light);
            Assert.Equal(InspectorMode.None, inspector.Mode);
        }

        [Fact]
        public void Select_all_then_set_works_on_the_whole_selection()
        {
            var part = Wheel();
            var inspector = new LedInspector();

            inspector.SelectAll(part, ElementType.Button);
            inspector.Effect = EffectChoice.For(LayerKind.Flags);
            inspector.StaticColor = "#FF0040FF";

            foreach (var button in part.Elements.Where(e => e.ElementType == ElementType.Button))
            {
                Assert.Equal(LayerKind.Flags, part.GetElementEffect(button));
                Assert.Equal("#FF0040FF", button.StaticColor);
            }

            Assert.All(part.Elements.Where(e => e.ElementType == ElementType.Dial), d => Assert.Null(part.GetElementEffect(d)));
        }

        [Fact]
        public void Quick_select_flags_follow_the_part()
        {
            var part = Wheel();
            var inspector = new LedInspector();
            var names = Record(inspector);

            Assert.False(inspector.HasElements);
            Assert.False(inspector.HasButtons);

            inspector.Select(part, Named(part, "RPM LEDs"), 0, false);
            Assert.True(inspector.HasElements);
            Assert.True(inspector.HasButtons);
            Assert.False(inspector.HasLights);
            Assert.True(inspector.HasDials);

            // They stay valid while nothing is selected.
            inspector.Clear();
            Assert.True(inspector.HasButtons);

            inspector.Select(part, Element(part, LedGroupSide.Left, 0), 0, false);
            names.Clear();
            inspector.Type = ElementType.Light;
            Assert.True(inspector.HasLights);
            Assert.Contains(nameof(LedInspector.HasLights), names);

            part.ApplyFrontPlate("none", "none");
            Assert.False(inspector.HasElements);
            Assert.False(inspector.HasButtons);
            Assert.False(inspector.HasDials);

            var plain = DevicePart.CreateDefault("Strip");
            inspector.SelectAll(plain, null);
            Assert.Same(plain, inspector.Part);
            Assert.False(inspector.HasElements);
        }

        [Fact]
        public void Selecting_in_another_part_replaces_the_selection_and_follows_the_part()
        {
            var first = Wheel();
            var second = Wheel();
            var inspector = new LedInspector();
            var a = Element(first, LedGroupSide.Left, 0);
            var b = Element(second, LedGroupSide.Left, 0);

            inspector.Select(first, a, 0, false);
            inspector.Select(second, b, 0, true);

            Assert.Same(second, inspector.Part);
            Assert.Same(b, Assert.Single(inspector.Elements));

            // The first part is no longer watched, the second one is.
            first.RemoveGroup(a);
            Assert.Same(b, Assert.Single(inspector.Elements));
            second.RemoveGroup(b);
            Assert.Equal(InspectorMode.None, inspector.Mode);

            inspector.Select(second, Element(second, LedGroupSide.Left, 1), 0, false);
            inspector.SetPart(first);
            Assert.Equal(InspectorMode.None, inspector.Mode);
            Assert.Same(first, inspector.Part);
        }

        [Fact]
        public void A_group_that_is_not_in_the_part_is_ignored()
        {
            var part = Wheel();
            var other = Wheel();
            var inspector = new LedInspector();
            var a = Element(part, LedGroupSide.Left, 0);
            inspector.Select(part, a, 0, false);

            inspector.Select(part, Element(other, LedGroupSide.Left, 0), 0, false);

            Assert.Same(a, Assert.Single(inspector.Elements));
        }

        [Fact]
        public void Dispose_stops_watching_the_part()
        {
            var part = Wheel();
            var inspector = new LedInspector();
            var a = Element(part, LedGroupSide.Left, 0);
            inspector.Select(part, a, 0, false);
            var names = Record(inspector);

            inspector.Dispose();
            a.Name = "Renamed";
            part.RemoveGroup(a);

            Assert.Empty(names);
            Assert.Same(a, Assert.Single(inspector.Elements));
        }

        [Fact]
        public void Element_position_texts_come_from_the_front_plate_presets()
        {
            var part = Wheel();

            Assert.Equal("outer column, row 1", LedInspector.ElementPosition(part, Element(part, LedGroupSide.Left, 0)));
            Assert.Equal("inner column, row 3", LedInspector.ElementPosition(part, Element(part, LedGroupSide.Right, 5)));
            Assert.Equal("row 1", LedInspector.ElementPosition(part, Element(part, LedGroupSide.None, 0)));
            Assert.Equal("row 2, 2 of 2", LedInspector.ElementPosition(part, Element(part, LedGroupSide.None, 2)));

            var single = Wheel();
            single.ApplyFrontPlate("none", "1x3");
            Assert.Equal("column, row 2", LedInspector.ElementPosition(single, Element(single, LedGroupSide.Left, 1)));

            var wide = Wheel();
            wide.ApplyFrontPlate("none", "3x2");
            Assert.Equal("column 2, row 1", LedInspector.ElementPosition(wide, Element(wide, LedGroupSide.Left, 2)));

            // SimHub layouts fix the position by the photo; only the side is known.
            var photo = Element(part, LedGroupSide.Left, 1);
            photo.PhotoSize = 20;
            Assert.Equal("left side", LedInspector.ElementPosition(part, photo));
            var right = Element(part, LedGroupSide.Right, 1);
            right.PhotoSize = 20;
            Assert.Equal("right side", LedInspector.ElementPosition(part, right));
            var dial = Element(part, LedGroupSide.None, 1);
            dial.PhotoSize = 20;
            Assert.Equal("center", LedInspector.ElementPosition(part, dial));

            // An element that doesn't fit the presets (for example after a preset change elsewhere) is shown as unknown.
            var stray = new LedGroup { Kind = LedGroupKind.Element, Side = LedGroupSide.Left, ElementSlot = 50 };
            Assert.Equal("-", LedInspector.ElementPosition(part, stray));
        }

        [Fact]
        public void Every_preset_element_has_a_known_position()
        {
            foreach (var sides in new[] { "1x2", "2x3", "3x3", "2x6" })
            {
                foreach (var dials in new[] { "none", "1-2", "3-3", "1-2-2-1" })
                {
                    var part = Wheel();
                    part.ApplyFrontPlate(dials, sides);
                    Assert.All(part.Elements, element => Assert.NotEqual("-", LedInspector.ElementPosition(part, element)));

                    // The inspector shows the same text for a selected element.
                    var inspector = new LedInspector();
                    foreach (var element in part.Elements)
                    {
                        inspector.Select(part, element, 0, false);
                        Assert.Equal(LedInspector.ElementPosition(part, element), inspector.Position);
                    }
                }
            }
        }
    }
}
