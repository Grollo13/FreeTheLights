using System.Linq;
using FreeTheLights.Settings;
using Xunit;

namespace FreeTheLights.Tests
{
    public class LayoutTests
    {
        [Fact]
        public void Flag_patterns_offered_follow_the_parts_groups()
        {
            // Rows and columns only: Alternate and Halves; a flag set to Checker (which looks like Alternate there) becomes Alternate.
            var part = DevicePart.CreateDefault("Wheel");
            part.Flags.Rules[0].Pattern = FlagPattern.Checker;
            using (var viewModel = new FreeTheLights.UI.DeviceViewModel(part))
            {
                Assert.Equal(new[] { "Alternate", "Halves" }, viewModel.PatternChoices.Select(c => c.Name));
                Assert.Equal(FlagPattern.Alternate, part.Flags.Rules[0].Pattern);

                // With an LED flag, every pattern looks different.
                part.AddGroup(new LedGroup { Name = "LED flag", Kind = LedGroupKind.Matrix, Columns = 8, Rows = 8 });
                Assert.Equal(new[] { FlagPattern.Alternate, FlagPattern.Checker, FlagPattern.Diagonal }, viewModel.PatternChoices.Select(c => c.Value));
            }
        }

        [Fact]
        public void Row_maps_left_to_right_from_start_index()
        {
            var row = new LedGroup { Kind = LedGroupKind.HorizontalRow, Count = 5, StartIndex = 3 };
            Assert.Equal(new[] { 3, 4, 5, 6, 7 }, Enumerable.Range(0, 5).Select(i => row.PhysicalIndex(i)).ToArray());

            row.Reversed = true;
            Assert.Equal(new[] { 7, 6, 5, 4, 3 }, Enumerable.Range(0, 5).Select(i => row.PhysicalIndex(i)).ToArray());
        }

        [Fact]
        public void Column_index_zero_is_the_bottom_led()
        {
            var column = new LedGroup { Kind = LedGroupKind.VerticalColumn, Count = 3, StartIndex = 10 };
            Assert.Equal(10, column.PhysicalIndex(0));

            // Matrix-style coordinates: y = 0 is the top.
            Assert.Equal(12, column.PhysicalIndex(0, 0));
            Assert.Equal(10, column.PhysicalIndex(0, 2));

            column.Reversed = true; // wired top to bottom
            Assert.Equal(12, column.PhysicalIndex(0));
            Assert.Equal(10, column.PhysicalIndex(0, 0));
        }

        [Fact]
        public void Matrix_supports_serpentine_and_bottom_start()
        {
            var matrix = new LedGroup { Kind = LedGroupKind.Matrix, Columns = 3, Rows = 2, StartIndex = 100 };
            Assert.Equal(6, matrix.LedCount);
            Assert.Equal(100, matrix.PhysicalIndex(0, 0));
            Assert.Equal(105, matrix.PhysicalIndex(2, 1));

            matrix.Serpentine = true;
            Assert.Equal(105, matrix.PhysicalIndex(0, 1));
            Assert.Equal(103, matrix.PhysicalIndex(2, 1));

            matrix.Serpentine = false;
            matrix.StartsAtBottom = true;
            Assert.Equal(103, matrix.PhysicalIndex(0, 0));
            Assert.Equal(100, matrix.PhysicalIndex(0, 1));
        }

        [Fact]
        public void Every_matrix_pixel_maps_to_a_unique_led()
        {
            var matrix = new LedGroup { Kind = LedGroupKind.Matrix, Columns = 5, Rows = 4, Serpentine = true, Reversed = true, StartsAtBottom = true };
            var indices = (from y in Enumerable.Range(0, 4) from x in Enumerable.Range(0, 5) select matrix.PhysicalIndex(x, y)).ToList();
            Assert.Equal(Enumerable.Range(0, 20), indices.OrderBy(i => i));
        }

        [Fact]
        public void Adding_groups_appends_them_to_the_strip()
        {
            var device = DevicePart.CreateDefault("Wheel");
            Assert.Equal(new[] { 0, 15, 18 }, device.Groups.Select(g => g.StartIndex).ToArray());
            Assert.Equal(21, device.TotalLeds);

            device.Groups[1].Count = 4;
            device.PackStartIndices();
            Assert.Equal(new[] { 0, 15, 19 }, device.Groups.Select(g => g.StartIndex).ToArray());
        }

        [Fact]
        public void Overlapping_leds_are_counted_once_each()
        {
            var device = DevicePart.CreateDefault("Wheel");
            Assert.Equal(0, device.CountOverlappingLeds());

            // The left column (3 LEDs) moved from 15 to 13: LEDs 13 and 14 are also the row's.
            device.Groups[1].StartIndex = 13;
            Assert.Equal(2, device.CountOverlappingLeds());

            // The right column there too: 13 and 14 aren't counted again, 15 is now used twice.
            device.Groups[2].StartIndex = 13;
            Assert.Equal(3, device.CountOverlappingLeds());
        }
    }
}
