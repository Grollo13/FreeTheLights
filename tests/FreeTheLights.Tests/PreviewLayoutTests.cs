using System.Linq;
using FreeTheLights.Settings;
using FreeTheLights.UI;
using Xunit;

namespace FreeTheLights.Tests
{
    public class PreviewLayoutTests
    {
        [Theory]
        [InlineData(DeviceType.Custom)]
        [InlineData(DeviceType.LedBar)]
        [InlineData(DeviceType.Wheel)]
        [InlineData(DeviceType.Dashboard)]
        public void Every_led_is_drawn_once_where_its_color_is(DeviceType type)
        {
            var device = Device(type);
            var layout = PreviewLayout.Build(device);

            foreach (var part in device.Parts)
            {
                foreach (var group in part.Groups)
                {
                    var spots = layout.Spots.Where(s => s.Group == group).ToList();
                    Assert.Equal(group.LedCount, spots.Count);
                    Assert.All(spots, s => Assert.Same(part, s.Part));

                    // Each logical LED once, at the frame index the renderer writes its color to.
                    Assert.Equal(Enumerable.Range(0, group.LedCount), spots.Select(s => s.Logical).OrderBy(i => i));
                    Assert.All(spots, s => Assert.Equal(group.PhysicalIndex(s.Logical), s.Physical));
                }
            }

            // No LED hides another, and everything is inside the layout's size.
            Assert.Equal(layout.Spots.Count, layout.Spots.Select(s => s.Center).Distinct().Count());
            Assert.All(layout.Spots, s => Assert.InRange(s.Center.X, 0, layout.Size.Width));
            Assert.All(layout.Spots, s => Assert.InRange(s.Center.Y, 0, layout.Size.Height));
        }

        [Fact]
        public void Only_the_schematic_has_labels_and_no_backdrop()
        {
            var schematic = PreviewLayout.Build(Device(DeviceType.LedBar));
            Assert.Null(schematic.Backdrop);
            Assert.Contains(schematic.Labels, l => l.Text == "Shift lights");
            Assert.Contains(schematic.Labels, l => l.Text == "Front plate");

            var wheel = PreviewLayout.Build(Device(DeviceType.Wheel));
            Assert.NotNull(wheel.Backdrop?.Body);
            Assert.Empty(wheel.Labels);
        }

        [Fact]
        public void A_device_without_leds_has_an_empty_layout()
        {
            Assert.Empty(PreviewLayout.Build(null).Spots);

            var empty = DeviceConfig.WithPart("Empty", DeviceType.Custom, new DevicePart());
            var layout = PreviewLayout.Build(empty);
            Assert.Empty(layout.Spots);
            Assert.Equal(0, layout.Size.Width);
        }

        /// <summary>A row, both columns, a matrix and a front plate, in two parts (LEDs and buttons).</summary>
        private static DeviceConfig Device(DeviceType type)
        {
            var leds = new DevicePart { Name = "LEDs" };
            leds.EnsureDefaults();
            leds.AddGroup(new LedGroup { Name = "Shift lights", Kind = LedGroupKind.HorizontalRow, Count = 12, Reversed = true });
            leds.AddGroup(new LedGroup { Name = "Left", Kind = LedGroupKind.VerticalColumn, Count = 3, Side = LedGroupSide.Left });
            leds.AddGroup(new LedGroup { Name = "Right", Kind = LedGroupKind.VerticalColumn, Count = 3, Side = LedGroupSide.Right });
            leds.AddGroup(new LedGroup { Name = "Flag", Kind = LedGroupKind.Matrix, Columns = 4, Rows = 3, Serpentine = true });

            var buttons = new DevicePart { Name = "Buttons", Channel = LedChannel.Buttons };
            buttons.EnsureDefaults();
            buttons.ApplyFrontPlate("2-1", "1x4");

            var device = DeviceConfig.WithPart("Test", type, leds);
            device.Parts.Add(buttons);
            return device;
        }
    }
}
