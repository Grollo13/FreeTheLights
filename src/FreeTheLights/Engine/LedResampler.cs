using System;
using FreeTheLights.Core;

namespace FreeTheLights.Engine
{
    /// <summary>
    /// Maps the car's N shift light LEDs onto a device row of M LEDs.
    ///
    /// <see cref="Resample{T}"/> (through <see cref="SourceIndex"/>) gives each device LED j the state of the car LED
    /// under its center, x = (j + 0.5) * N / M. This keeps the spatial pattern of the real car (left-to-right fill,
    /// fill from both ends, color bands) for both M &gt; N (LEDs are duplicated) and M &lt; N (LEDs are skipped evenly).
    /// M == N is the identity. When a center falls exactly on the border between two car LEDs, the left half rounds
    /// down and the right half rounds up, so the mapping is mirror-symmetric and symmetric car patterns stay symmetric.
    ///
    /// <see cref="ResampleLeds"/>, which maps the car's LED colors, differs from it:
    /// - M &lt; N: as <see cref="Resample{T}"/>, but a device LED whose center car LED is off shows the nearest lit car
    ///   LED it covers, so no car LED (and no shift stage) is ever skipped.
    /// - M &gt; N: no stretching. The car's LEDs are shown one to one in the middle of the row, as on the real car,
    ///   and the outer LEDs are left transparent (<see cref="Center"/>).
    /// </summary>
    public static class LedResampler
    {
        public static int SourceIndex(int targetIndex, int sourceCount, int targetCount)
        {
            if (sourceCount <= 0 || targetCount <= 0) return 0;
            var x = (2.0 * targetIndex + 1) * sourceCount / (2.0 * targetCount);
            var index = x < sourceCount / 2.0 ? (int)Math.Floor(x) : (int)Math.Ceiling(x) - 1;
            return Math.Max(0, Math.Min(sourceCount - 1, index));
        }

        public static T[] Resample<T>(T[] source, int targetCount)
        {
            var result = new T[targetCount];
            if (source == null || source.Length == 0) return result;
            for (var j = 0; j < targetCount; j++)
            {
                result[j] = source[SourceIndex(j, source.Length, targetCount)];
            }

            return result;
        }

        /// <summary>
        /// <see cref="Resample{T}"/> for LED colors. With fewer device LEDs than car LEDs, a device LED whose center car LED
        /// is transparent shows the nearest non-transparent car LED within its span (ties go to the LED nearer the row's
        /// end on the device LED's side), so a car LED can never be skipped. With more device LEDs than car LEDs, the car's
        /// LEDs are centered on the row (<see cref="Center"/>).
        /// </summary>
        public static LedColor[] ResampleLeds(LedColor[] source, int targetCount)
        {
            var n = source?.Length ?? 0;
            if (targetCount > n) return Center(source, targetCount);

            var result = Resample(source, targetCount);
            if (targetCount == n) return result;

            for (var j = 0; j < targetCount; j++)
            {
                if (!result[j].IsTransparent) continue;

                // Car LEDs that overlap device LED j: [j * n / M, (j + 1) * n / M) in car LED units.
                var first = j * n / targetCount;
                var last = ((j + 1) * n + targetCount - 1) / targetCount - 1;
                var leftHalf = 2 * j + 1 < targetCount;
                var best = -1;
                var bestDistance = int.MaxValue;
                for (var i = first; i <= last; i++)
                {
                    if (source[i].IsTransparent) continue;

                    // Distance between the centers of car LED i and device LED j, in units of 1 / (2 * M) car LEDs.
                    var distance = Math.Abs((2 * i + 1) * targetCount - (2 * j + 1) * n);
                    if (distance < bestDistance || (distance == bestDistance && (leftHalf ? i < best : i > best)))
                    {
                        best = i;
                        bestDistance = distance;
                    }
                }

                if (best >= 0) result[j] = source[best];
            }

            return result;
        }

        /// <summary>
        /// Shows the car's N LEDs one to one in the middle of a row of M &gt; N LEDs; the outer LEDs stay transparent.
        /// When M - N is odd, the car's LEDs can't sit exactly in the middle, so one LED is added in the center: an odd
        /// car row shows its center LED twice, an even one gets an extra center LED that lights once both car LEDs next
        /// to it are lit, in the right one's color.
        /// </summary>
        private static LedColor[] Center(LedColor[] source, int targetCount)
        {
            var result = new LedColor[targetCount];
            var n = source?.Length ?? 0;
            if (n == 0) return result;

            var extra = (targetCount - n) % 2;
            var offset = (targetCount - n - extra) / 2;
            var half = n / 2;
            for (var i = 0; i < n; i++)
            {
                result[offset + i + (i >= half ? extra : 0)] = source[i];
            }

            if (extra == 1 && (n % 2 == 1 || (!source[half - 1].IsTransparent && !source[half].IsTransparent)))
            {
                result[offset + half] = source[half];
            }

            return result;
        }
    }
}
