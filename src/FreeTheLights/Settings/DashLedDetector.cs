using System;
using System.Collections.Generic;
using System.Linq;

namespace FreeTheLights.Settings
{
    /// <summary>One LED on a product picture: center and size in picture pixels.</summary>
    public class PhotoLed
    {
        public PhotoLed()
        {
        }

        public PhotoLed(double x, double y, double width, double height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        public double X { get; set; }

        public double Y { get; set; }

        public double Width { get; set; }

        public double Height { get; set; }

        public PhotoLed Clone() => new PhotoLed(X, Y, Width, Height);
    }

    /// <summary>
    /// Where a dashboard's LEDs are on its product picture, in logical order: the top row from left to right, the
    /// side columns from bottom to top.
    /// </summary>
    public class DashPhotoLayout
    {
        public int PictureWidth { get; set; }

        public int PictureHeight { get; set; }

        public List<PhotoLed> Left { get; set; } = new List<PhotoLed>();

        public List<PhotoLed> Top { get; set; } = new List<PhotoLed>();

        public List<PhotoLed> Right { get; set; } = new List<PhotoLed>();

        public int Total => Left.Count + Top.Count + Right.Count;

        public LedSplit Split => new LedSplit(Left.Count, Top.Count, Right.Count, true);
    }

    /// <summary>
    /// Finds a dashboard's LEDs on its product picture (SimHub's DevicesLogos pictures have no LED coordinates).
    /// LEDs are drawn as bright, equally sized dots or bars in a line: a row along the top edge and a column along
    /// each side. The result only counts when it finds exactly the device's LED count; otherwise it is null and the
    /// user places the LEDs.
    /// </summary>
    public static class DashLedDetector
    {
        /// <summary>
        /// Ways to tell LED pixels from the rest, tried in turn: a positive value is a brightness threshold, a negative
        /// one how much brighter than its surroundings a pixel must be. Product pictures differ a lot: lit LEDs on a
        /// dark bezel, grey "off" LEDs on a black one, dark dots on carbon.
        /// </summary>
        internal static readonly int[] Modes = { 150, 120, 180, 95, 210, 75, -50, -35, -25, -18 };

        /// <summary>
        /// The LED positions on a picture of <paramref name="width"/> × <paramref name="height"/> pixels
        /// (<paramref name="argb"/>: one 0xAARRGGBB value per pixel, row by row), or null when no layout with exactly
        /// <paramref name="expectedTotal"/> LEDs was found. <paramref name="suggested"/> (SimHub's suggested split, may
        /// be null) picks between several layouts with the right total.
        /// </summary>
        public static DashPhotoLayout Detect(int[] argb, int width, int height, int expectedTotal, LedSplit suggested = null)
        {
            if (argb == null || width <= 0 || height <= 0 || argb.Length < width * height || expectedTotal <= 0) return null;

            var value = Brightness(argb, width, height);
            var surroundings = LocalMean(value, width, height, Radius(width));
            DashPhotoLayout best = null;
            var bestScore = double.MinValue;
            foreach (var mode in Modes)
            {
                var layout = DetectAt(value, surroundings, width, height, mode);
                if (layout == null || layout.Total != expectedTotal) continue;
                var score = Score(layout, suggested);
                if (score > bestScore)
                {
                    best = layout;
                    bestScore = score;
                }
            }

            return best;
        }

        /// <summary>The layout found with one <see cref="Modes"/> entry, whatever its LED count; null without a top row.</summary>
        internal static DashPhotoLayout DetectAt(int[] argb, int width, int height, int mode)
        {
            var value = Brightness(argb, width, height);
            return DetectAt(value, LocalMean(value, width, height, Radius(width)), width, height, mode);
        }

        private static int Radius(int width) => Math.Max(4, width / 32);

        /// <summary>Prefers the suggested split, then symmetric sides, then evenly spaced LEDs.</summary>
        private static double Score(DashPhotoLayout layout, LedSplit suggested)
        {
            var score = -Math.Abs(layout.Left.Count - layout.Right.Count)
                - 5 * (Unevenness(layout.Top, true) + Unevenness(layout.Left, false) + Unevenness(layout.Right, false));
            if (suggested != null && suggested.Left == layout.Left.Count && suggested.Top == layout.Top.Count && suggested.Right == layout.Right.Count) score += 10;
            return score;
        }

        /// <summary>How much the gaps between neighbouring LEDs vary (0 = evenly spaced), ignoring larger gaps between blocks.</summary>
        private static double Unevenness(List<PhotoLed> leds, bool horizontal)
        {
            if (leds.Count < 3) return 0;
            var gaps = new List<double>();
            for (var i = 1; i < leds.Count; i++) gaps.Add(Math.Abs(horizontal ? leds[i].X - leds[i - 1].X : leds[i].Y - leds[i - 1].Y));
            gaps.Sort();
            var median = gaps[gaps.Count / 2];
            if (median <= 0) return 1;
            return gaps.Where(g => g < 1.6 * median).Select(g => Math.Abs(g - median) / median).Average();
        }

        private static DashPhotoLayout DetectAt(byte[] value, byte[] surroundings, int width, int height, int mode)
        {
            var spots = FindSpots(value, surroundings, width, height, mode);

            // The top row: the highest horizontal line of at least 3 equally sized LEDs in the upper part.
            List<Spot> top = null;
            foreach (var seed in spots.Where(s => s.Y < height * 0.3).OrderBy(s => s.Y))
            {
                var line = Line(spots, seed, s => s.Y, s => s.X, s => s.Height);
                if (line.Count >= 3 && Spread(line, s => s.X) > 4 * line.Average(s => s.Size))
                {
                    top = line;
                    break;
                }
            }

            if (top == null) return null;
            var rowY = top.Max(s => s.Y);
            var rest = spots.Except(top).Where(s => s.Y > rowY).ToList();

            var layout = new DashPhotoLayout
            {
                PictureWidth = width,
                PictureHeight = height,
                Top = top.OrderBy(s => s.X).Select(s => s.ToLed()).ToList(),
                Left = Column(rest.Where(s => s.X < width * 0.2).OrderBy(s => s.X), rest),
                Right = Column(rest.Where(s => s.X > width * 0.8).OrderByDescending(s => s.X), rest),
            };
            return IsPlausible(layout) ? layout : null;
        }

        /// <summary>
        /// Dashboards are mirror-symmetric: the top row is centered, and there are either no side columns or one on
        /// each side at mirrored positions. Rules out buttons on wheel pictures that happen to line up.
        /// </summary>
        private static bool IsPlausible(DashPhotoLayout layout)
        {
            int width = layout.PictureWidth, height = layout.PictureHeight;
            var center = (layout.Top.Min(l => l.X) + layout.Top.Max(l => l.X)) / 2;
            if (Math.Abs(center - width / 2.0) > 0.08 * width) return false;
            if (layout.Left.Count == 0 && layout.Right.Count == 0) return true;
            if (layout.Left.Count == 0 || layout.Right.Count == 0) return false;
            if (Math.Abs(layout.Left.Average(l => l.X) + layout.Right.Average(l => l.X) - width) > 0.08 * width) return false;
            return Math.Abs(layout.Left.Average(l => l.Y) - layout.Right.Average(l => l.Y)) <= 0.12 * height;
        }

        /// <summary>The outermost vertical line of at least 2 LEDs, seeded from <paramref name="seeds"/> (outermost first), bottom first.</summary>
        private static List<PhotoLed> Column(IEnumerable<Spot> seeds, List<Spot> spots)
        {
            foreach (var seed in seeds)
            {
                var line = Line(spots, seed, s => s.X, s => s.Y, s => s.Width);
                if (line.Count >= 2 && Spread(line, s => s.Y) > 1.5 * line.Average(s => s.Size)) return line.OrderByDescending(s => s.Y).Select(s => s.ToLed()).ToList();
            }

            return new List<PhotoLed>();
        }

        /// <summary>
        /// The LEDs on a line through <paramref name="seed"/>, grown LED by LED in both directions: the next LED is the
        /// closest spot of a similar size ahead whose <paramref name="across"/> coordinate continues the line (gentle
        /// curves are fine). Gaps of a few LEDs are bridged only when the next LED sits exactly on the line (LED blocks
        /// in corners).
        /// </summary>
        private static List<Spot> Line(List<Spot> spots, Spot seed, Func<Spot, double> across, Func<Spot, double> along, Func<Spot, double> thickness)
        {
            var candidates = spots.Where(s => Math.Max(s.Area, seed.Area) <= 2.5 * Math.Min(s.Area, seed.Area)).ToList();
            var line = new List<Spot> { seed };
            foreach (var direction in new[] { 1, -1 })
            {
                var current = seed;
                var slope = 0.0;
                while (true)
                {
                    Spot next = null;
                    var nextDistance = double.MaxValue;
                    foreach (var s in candidates)
                    {
                        var distance = (along(s) - along(current)) * direction;
                        var size = Math.Max(s.Size, current.Size);
                        if (distance <= 0 || distance >= nextDistance || distance > 7 * size || line.Contains(s)) continue;
                        var offset = Math.Abs(across(s) - across(current) - slope * (along(s) - along(current)));
                        var tolerance = 0.6 * Math.Max(thickness(s), thickness(current)) + 1;
                        if (offset > (distance > 3.5 * size ? tolerance : tolerance + 0.15 * distance)) continue;
                        next = s;
                        nextDistance = distance;
                    }

                    if (next == null) break;
                    slope = (across(next) - across(current)) / (along(next) - along(current));
                    line.Add(next);
                    current = next;
                }
            }

            return line;
        }

        private static double Spread(List<Spot> spots, Func<Spot, double> along) => spots.Max(along) - spots.Min(along);

        /// <summary>Brightest color channel per pixel; transparent pixels are black.</summary>
        private static byte[] Brightness(int[] argb, int width, int height)
        {
            var value = new byte[width * height];
            for (var i = 0; i < value.Length; i++)
            {
                var c = argb[i];
                if (((c >> 24) & 0xFF) < 128) continue;
                value[i] = (byte)Math.Max((c >> 16) & 0xFF, Math.Max((c >> 8) & 0xFF, c & 0xFF));
            }

            return value;
        }

        /// <summary>Mean of each pixel's (2·<paramref name="radius"/>+1)² neighbourhood, from an integral image.</summary>
        private static byte[] LocalMean(byte[] value, int width, int height, int radius)
        {
            var sum = new long[(width + 1) * (height + 1)];
            for (var y = 0; y < height; y++)
            {
                long row = 0;
                for (var x = 0; x < width; x++)
                {
                    row += value[y * width + x];
                    sum[(y + 1) * (width + 1) + x + 1] = sum[y * (width + 1) + x + 1] + row;
                }
            }

            var mean = new byte[width * height];
            for (var y = 0; y < height; y++)
            {
                int y0 = Math.Max(0, y - radius), y1 = Math.Min(height, y + radius + 1);
                for (var x = 0; x < width; x++)
                {
                    int x0 = Math.Max(0, x - radius), x1 = Math.Min(width, x + radius + 1);
                    var total = sum[y1 * (width + 1) + x1] - sum[y0 * (width + 1) + x1] - sum[y1 * (width + 1) + x0] + sum[y0 * (width + 1) + x0];
                    mean[y * width + x] = (byte)(total / ((y1 - y0) * (x1 - x0)));
                }
            }

            return mean;
        }

        /// <summary>Bright, compact blobs of a plausible LED size.</summary>
        private static List<Spot> FindSpots(byte[] value, byte[] surroundings, int width, int height, int mode)
        {
            var mask = new bool[width * height];
            for (var i = 0; i < mask.Length; i++)
            {
                mask[i] = mode >= 0 ? value[i] >= mode : value[i] > 0 && value[i] - surroundings[i] >= -mode;
            }

            var maxSize = Math.Max(6, width * 0.06);
            var spots = new List<Spot>();
            var stack = new Stack<int>();
            for (var start = 0; start < mask.Length; start++)
            {
                if (!mask[start]) continue;
                mask[start] = false;
                stack.Push(start);
                int area = 0, minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
                double sumX = 0, sumY = 0;
                while (stack.Count > 0)
                {
                    var p = stack.Pop();
                    int x = p % width, y = p / width;
                    area++;
                    sumX += x;
                    sumY += y;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                    if (x > 0 && mask[p - 1]) { mask[p - 1] = false; stack.Push(p - 1); }
                    if (x < width - 1 && mask[p + 1]) { mask[p + 1] = false; stack.Push(p + 1); }
                    if (y > 0 && mask[p - width]) { mask[p - width] = false; stack.Push(p - width); }
                    if (y < height - 1 && mask[p + width]) { mask[p + width] = false; stack.Push(p + width); }
                }

                var w = maxX - minX + 1;
                var h = maxY - minY + 1;
                if (area < 4 || w > maxSize || h > maxSize) continue;
                if (Math.Max(w, h) > 5 * Math.Min(w, h)) continue;
                if (area < 0.45 * w * h) continue;
                spots.Add(new Spot { X = sumX / area, Y = sumY / area, Width = w, Height = h, Area = area });
            }

            return spots;
        }

        private class Spot
        {
            public double X;
            public double Y;
            public double Width;
            public double Height;
            public int Area;

            public double Size => Math.Max(Width, Height);

            public PhotoLed ToLed() => new PhotoLed(X + 0.5, Y + 0.5, Width, Height);
        }
    }
}
