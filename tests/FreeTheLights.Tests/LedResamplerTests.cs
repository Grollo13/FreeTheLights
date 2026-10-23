using System.Linq;
using FreeTheLights.Core;
using FreeTheLights.Engine;
using Xunit;

namespace FreeTheLights.Tests
{
    public class LedResamplerTests
    {
        private static readonly LedColor Green = LedColor.FromRgb(0, 255, 0);

        private static LedColor[] Lit(int count, params int[] lit)
        {
            var leds = new LedColor[count];
            foreach (var i in lit) leds[i] = Green;
            return leds;
        }

        private static int[] Map(int source, int target) => Enumerable.Range(0, target).Select(j => LedResampler.SourceIndex(j, source, target)).ToArray();

        [Theory]
        [InlineData(15)]
        [InlineData(10)]
        [InlineData(1)]
        public void Same_count_is_identity(int n)
        {
            Assert.Equal(Enumerable.Range(0, n).ToArray(), Map(n, n));
        }

        [Fact]
        public void Downsampling_15_to_10_skips_evenly_and_keeps_ends()
        {
            Assert.Equal(new[] { 0, 2, 3, 5, 6, 8, 9, 11, 12, 14 }, Map(15, 10));
        }

        [Fact]
        public void Upsampling_duplicates_and_keeps_ends()
        {
            var map = Map(15, 20);
            Assert.Equal(0, map.First());
            Assert.Equal(14, map.Last());
            Assert.Equal(map.OrderBy(i => i).ToArray(), map);
            Assert.Equal(15, map.Distinct().Count());
        }

        [Theory]
        [InlineData(10, 5)]
        [InlineData(15, 10)]
        [InlineData(10, 15)]
        [InlineData(16, 9)]
        [InlineData(9, 16)]
        [InlineData(15, 24)]
        public void Mapping_is_mirror_symmetric(int source, int target)
        {
            var map = Map(source, target);
            for (var j = 0; j < target; j++)
            {
                // The center LED of an odd device row can't mirror onto itself when the car has an even LED count.
                if (j == target - 1 - j && source % 2 == 0) continue;
                Assert.Equal(source - 1 - map[j], map[target - 1 - j]);
            }
        }

        [Fact]
        public void Single_led_device_shows_a_center_led()
        {
            Assert.Equal(new[] { 7 }, Map(15, 1));
        }

        [Fact]
        public void Downsampling_shows_lit_outer_leds()
        {
            // 10 → 5: the centers fall on car LEDs 1, 3, 4, 6, 8, so LEDs 0 and 9 are only reached through their spans.
            var leds = LedResampler.ResampleLeds(Lit(10, 0, 9), 5);
            Assert.Equal(new[] { Green, LedColor.Transparent, LedColor.Transparent, LedColor.Transparent, Green }, leds);
        }

        [Theory]
        [InlineData(10, 5)]
        [InlineData(15, 4)]
        [InlineData(16, 9)]
        [InlineData(12, 3)]
        public void Downsampling_never_skips_a_car_led(int source, int target)
        {
            for (var i = 0; i < source; i++)
            {
                Assert.Contains(LedResampler.ResampleLeds(Lit(source, i), target), c => !c.IsTransparent);
            }
        }

        [Theory]
        [InlineData(16, 9)]
        [InlineData(15, 4)]
        [InlineData(10, 5)]
        public void Downsampling_keeps_symmetric_patterns_symmetric(int source, int target)
        {
            var leds = LedResampler.ResampleLeds(Lit(source, 0, 1, source - 2, source - 1), target);
            for (var j = 0; j < target; j++) Assert.Equal(leds[j], leds[target - 1 - j]);
        }

        [Fact]
        public void Downsampling_lit_center_leds_is_unchanged()
        {
            var all = Enumerable.Range(0, 15).Select(i => LedColor.FromRgb((byte)i, 0, 0)).ToArray();
            Assert.Equal(LedResampler.Resample(all, 10), LedResampler.ResampleLeds(all, 10));
        }

        private static LedColor[] Distinct(int count) => Enumerable.Range(1, count).Select(i => LedColor.FromRgb((byte)i, 0, 0)).ToArray();

        private static readonly LedColor T = LedColor.Transparent;

        [Fact]
        public void Upsampling_centers_the_car_leds()
        {
            var six = Distinct(6);
            Assert.Equal(new[] { T, T, T, T, T, six[0], six[1], six[2], six[3], six[4], six[5], T, T, T, T, T },
                LedResampler.ResampleLeds(six, 16));

            var five = Distinct(5);
            Assert.Equal(new[] { T, T, T, T, T, five[0], five[1], five[2], five[3], five[4], T, T, T, T, T },
                LedResampler.ResampleLeds(five, 15));
        }

        [Fact]
        public void Upsampling_with_even_car_count_on_odd_row_adds_a_center_led()
        {
            // The Ferrari 296 GT3 case: 6 car LEDs on a 15-LED row use device LEDs 4..10, with LED 7 added in the center.
            var six = Distinct(6);
            Assert.Equal(new[] { T, T, T, T, six[0], six[1], six[2], six[3], six[3], six[4], six[5], T, T, T, T },
                LedResampler.ResampleLeds(six, 15));

            // Left-to-right fill: the center LED lights together with car LED 3, not before.
            for (var lit = 0; lit <= 6; lit++)
            {
                var leds = LedResampler.ResampleLeds(Lit(6, Enumerable.Range(0, lit).ToArray()), 15);
                Assert.Equal(lit >= 4 ? Green : T, leds[7]);
                Assert.Equal(lit >= 3 ? Green : T, leds[6]);
            }
        }

        [Fact]
        public void Upsampling_with_odd_car_count_on_even_row_doubles_the_center_led()
        {
            var five = Distinct(5);
            Assert.Equal(new[] { T, T, T, T, T, five[0], five[1], five[2], five[2], five[3], five[4], T, T, T, T, T },
                LedResampler.ResampleLeds(five, 16));
            Assert.Equal(Distinct(15).Take(8).Concat(Distinct(15).Skip(7)).ToArray(), LedResampler.ResampleLeds(Distinct(15), 16));
        }

        [Theory]
        [InlineData(6, 15)]
        [InlineData(6, 16)]
        [InlineData(5, 15)]
        [InlineData(5, 16)]
        [InlineData(15, 16)]
        [InlineData(1, 15)]
        [InlineData(1, 16)]
        public void Upsampling_keeps_symmetric_patterns_symmetric(int source, int target)
        {
            var patterns = new[]
            {
                Lit(source, Enumerable.Range(0, source).ToArray()),
                Lit(source, 0, source - 1),
                Lit(source, source / 2, (source - 1) / 2),
            };
            foreach (var pattern in patterns)
            {
                var leds = LedResampler.ResampleLeds(pattern, target);
                for (var j = 0; j < target; j++) Assert.Equal(leds[j], leds[target - 1 - j]);
                Assert.Equal(pattern.Count(c => !c.IsTransparent) > 0, leds.Any(c => !c.IsTransparent));
            }
        }

        [Fact]
        public void Upsampling_redline_off_phase_darkens_the_center_led()
        {
            var off = Enumerable.Repeat(LedColor.Off, 6).ToArray();
            var leds = LedResampler.ResampleLeds(off, 15);
            Assert.Equal(7, leds.Count(c => c == LedColor.Off));
            Assert.Equal(LedColor.Off, leds[7]);
        }
    }
}
