using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FreeTheLights.Core;
using Xunit;

namespace FreeTheLights.Tests
{
    public class InputStateTests
    {
        private const string Button = "JoystickPlugin.Wheel_B05";
        private const string Other = "JoystickPlugin.Wheel_B06";

        [Fact]
        public void Press_and_release_track_the_held_state()
        {
            var inputs = new InputState();
            Assert.False(inputs.IsHeld(Button));

            inputs.Press(Button);
            Assert.True(inputs.IsHeld(Button));
            Assert.True(inputs.IsHeld(Button.ToUpperInvariant()));
            Assert.False(inputs.IsHeld(Other));

            inputs.Release(Button);
            Assert.False(inputs.IsHeld(Button));

            // Releasing an input that was never pressed, or null names, changes nothing.
            inputs.Release(Other);
            inputs.Press(null);
            Assert.False(inputs.IsHeld(Other));
            Assert.False(inputs.IsHeld(null));
        }

        [Fact]
        public void A_press_counts_for_the_minimum_on_time_even_when_released_at_once()
        {
            long now = 1000;
            var inputs = new InputState(() => now);
            Assert.False(inputs.IsHeldOrPressedWithin(Button, now, 50));

            inputs.Press(Button);
            inputs.Release(Button);
            Assert.False(inputs.IsHeld(Button));
            Assert.True(inputs.IsHeldOrPressedWithin(Button, 1050, 50));
            Assert.False(inputs.IsHeldOrPressedWithin(Button, 1051, 50));
            Assert.False(inputs.IsHeldOrPressedWithin(Other, 1000, 50));
            Assert.False(inputs.IsHeldOrPressedWithin(null, 1000, 50));

            // Held: active however long ago it was pressed.
            inputs.Press(Button);
            Assert.True(inputs.IsHeldOrPressedWithin(Button, 9000, 50));

            inputs.Clear();
            Assert.False(inputs.IsHeldOrPressedWithin(Button, 1000, 50));
        }

        [Fact]
        public void Clear_forgets_inputs_whose_release_was_lost()
        {
            var inputs = new InputState();
            inputs.Press(Button);
            inputs.Press(Other);

            inputs.Clear();
            Assert.False(inputs.IsHeld(Button));
            Assert.False(inputs.IsHeld(Other));

            // A late release of a forgotten input changes nothing; a new press counts again.
            inputs.Release(Button);
            inputs.Press(Button);
            Assert.True(inputs.IsHeld(Button));
        }

        [Fact]
        public void Pressed_reports_each_press_after_recording_it()
        {
            var inputs = new InputState();
            var seen = new List<string>();
            inputs.Pressed += name =>
            {
                Assert.True(inputs.IsHeld(name));
                seen.Add(name);
            };

            inputs.Press(Button);
            inputs.Release(Button);
            inputs.Press(Other);
            Assert.Equal(new[] { Button, Other }, seen);
        }

        [Fact]
        public void Presses_from_several_threads_are_all_recorded()
        {
            var inputs = new InputState();
            var names = Enumerable.Range(0, 200).Select(i => "Input" + i).ToArray();
            Parallel.ForEach(names, name =>
            {
                inputs.Press(name);
                inputs.IsHeld(names[0]);
            });

            Assert.All(names, name => Assert.True(inputs.IsHeld(name)));
        }
    }
}
