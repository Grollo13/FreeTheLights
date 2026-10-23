using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FreeTheLights.Core;
using Newtonsoft.Json.Linq;

namespace FreeTheLights.CarData
{
    /// <summary>
    /// Car specific shift light definition from lovely-car-data (file format v2).
    /// In <c>ledColor</c> and in every per-gear <c>ledRpm</c> array, index 0 is the redline and
    /// indices 1..N are the LEDs from left to right. An RPM of 0 or a transparent color marks an unused LED.
    /// </summary>
    public class CarLedProfile
    {
        private readonly Dictionary<string, int[]> gearThresholds;
        private readonly int[] defaultThresholds;
        private readonly KeyValuePair<int, int[]>[] numericGears;

        public CarLedProfile(string carName, string carId, string carClass, int ledCount, int redlineBlinkIntervalMs, LedColor[] colors, Dictionary<string, int[]> gearThresholds)
        {
            if (ledCount <= 0) throw new ArgumentOutOfRangeException(nameof(ledCount));
            if (gearThresholds == null || gearThresholds.Count == 0) throw new ArgumentException("No RPM data", nameof(gearThresholds));

            // Downloaded data: the LED arrays are sized by this count every frame.
            ledCount = Math.Min(ledCount, LedLimits.MaxLeds);

            CarName = carName;
            CarId = carId;
            CarClass = carClass;
            LedCount = ledCount;
            RedlineBlinkIntervalMs = Math.Max(0, redlineBlinkIntervalMs);
            Colors = Pad(colors, ledCount + 1, LedColor.Transparent);
            this.gearThresholds = gearThresholds.ToDictionary(kv => kv.Key.Trim().ToUpperInvariant(), kv => Pad(kv.Value, ledCount + 1, 0), StringComparer.OrdinalIgnoreCase);
            defaultThresholds = this.gearThresholds.TryGetValue("1", out var first) ? first : this.gearThresholds.Values.First();
            numericGears = this.gearThresholds
                .Where(kv => int.TryParse(kv.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                .Select(kv => new KeyValuePair<int, int[]>(int.Parse(kv.Key, NumberStyles.Integer, CultureInfo.InvariantCulture), kv.Value))
                .ToArray();
        }

        public string CarName { get; }
        public string CarId { get; }
        public string CarClass { get; }

        /// <summary>Number of shift light LEDs of the real car.</summary>
        public int LedCount { get; }

        public int RedlineBlinkIntervalMs { get; }

        /// <summary>Index 0: redline color, 1..N: LED colors.</summary>
        public LedColor[] Colors { get; }

        public IEnumerable<string> Gears => gearThresholds.Keys;

        /// <summary>
        /// RPM thresholds for <paramref name="gear"/>: exact gear, otherwise the nearest numeric gear,
        /// otherwise gear 1 (or the first entry).
        /// </summary>
        public int[] GetThresholds(string gear)
        {
            // Runs every frame: no LINQ and no string allocations (the dictionary ignores case).
            var key = (gear ?? string.Empty).Trim();
            if (gearThresholds.TryGetValue(key, out var exact)) return exact;

            if (int.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
            {
                int[] nearest = null;
                var nearestDistance = int.MaxValue;
                foreach (var candidate in numericGears)
                {
                    // Strictly closer only, so ties keep the first gear in file order.
                    var distance = Math.Abs(candidate.Key - number);
                    if (distance < nearestDistance)
                    {
                        nearest = candidate.Value;
                        nearestDistance = distance;
                    }
                }

                if (nearest != null) return nearest;
            }

            return defaultThresholds;
        }

        /// <summary>
        /// Computes the state of the car's own LEDs.
        /// </summary>
        /// <param name="rpm">Current engine RPM.</param>
        /// <param name="gear">Current gear as reported by SimHub ("R", "N", "1", ...).</param>
        /// <param name="nowMs">Monotonic time in milliseconds, used for redline blinking.</param>
        /// <returns>N colors: transparent where the LED is off, <see cref="LedColor.Off"/> for lit LEDs in the redline blink off-phase.</returns>
        public LedColor[] ComputeLeds(double rpm, string gear, long nowMs)
        {
            var thresholds = GetThresholds(gear);
            var result = new LedColor[LedCount];
            var redlineRpm = thresholds[0];
            var atRedline = redlineRpm > 0 && rpm >= redlineRpm;

            var redlineColor = atRedline && !Colors[0].IsTransparent;
            for (var i = 0; i < LedCount; i++)
            {
                // Unused LEDs are gaps in the car's row (no LED there): they are never lit (stay transparent), also not in the redline color.
                var threshold = thresholds[i + 1];
                var color = Colors[i + 1];
                if (threshold <= 0 || color.IsTransparent) continue;
                if (redlineColor) result[i] = Colors[0];
                else if (rpm >= threshold) result[i] = color;
            }

            // Blink off-phase: LEDs lit in the on-phase turn opaque black, so lower layers don't flicker through.
            if (atRedline && RedlineBlinkIntervalMs > 0 && Blink.IsOffPhase(nowMs, RedlineBlinkIntervalMs))
            {
                for (var i = 0; i < LedCount; i++)
                {
                    if (!result[i].IsTransparent) result[i] = LedColor.Off;
                }
            }

            return result;
        }

        public static CarLedProfile Parse(string json)
        {
            var root = JObject.Parse(json);
            var ledCount = root.Value<int?>("ledNumber") ?? 0;

            var colors = (root["ledColor"] as JArray)?.Select(t => LedColor.Parse(t.Type == JTokenType.String ? (string)t : null)).ToArray() ?? new LedColor[0];

            var gears = new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase);
            var rpmToken = root["ledRpm"];
            var rpmObjects = rpmToken is JArray array ? array.OfType<JObject>() : rpmToken is JObject single ? new[] { single } : Enumerable.Empty<JObject>();
            foreach (var gearObject in rpmObjects)
            {
                foreach (var property in gearObject.Properties())
                {
                    if (!(property.Value is JArray values)) continue;
                    gears[property.Name] = values.Select(ToInt).ToArray();
                }
            }

            if (ledCount <= 0)
            {
                ledCount = Math.Max(colors.Length, gears.Values.Select(v => v.Length).DefaultIfEmpty(0).Max()) - 1;
            }

            return new CarLedProfile(
                root.Value<string>("carName"),
                root.Value<string>("carId"),
                root.Value<string>("carClass"),
                ledCount,
                root.Value<int?>("redlineBlinkInterval") ?? 0,
                colors,
                gears);
        }

        private static int ToInt(JToken token)
        {
            switch (token.Type)
            {
                case JTokenType.Integer:
                case JTokenType.Float:
                    return (int)Math.Round((double)token);
                case JTokenType.String:
                    return double.TryParse((string)token, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? (int)Math.Round(d) : 0;
                default:
                    return 0;
            }
        }

        private static T[] Pad<T>(T[] source, int length, T fill)
        {
            var result = new T[length];
            for (var i = 0; i < length; i++) result[i] = source != null && i < source.Length ? source[i] : fill;
            return result;
        }
    }
}
