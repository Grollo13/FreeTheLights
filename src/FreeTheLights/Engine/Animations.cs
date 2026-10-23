using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using FreeTheLights.Core;
using FreeTheLights.Settings;

namespace FreeTheLights.Engine
{
    /// <summary>
    /// What the animations (idle, engine, pit limiter) share: every LED gets a position along the animation, 0 at its
    /// start and 1 at its end, so one animation serves every kind of group. Rows run from the left, columns from the
    /// bottom, matrices column by column from the left; the front plate elements of a part form one line from the
    /// left of the wheel to the right. Mirrored, the position runs from the center (0) to both ends (1).
    /// </summary>
    public static class Animations
    {
        /// <summary>How long the changing colors take to go once through the rainbow.</summary>
        public const int HueCycleMs = 10000;

        /// <summary>Colors an LED at a position along the animation. A struct, so painting doesn't allocate.</summary>
        public interface ISampler
        {
            /// <summary>The color at <paramref name="position"/> (0..1); <paramref name="step"/> is the distance between two LEDs.</summary>
            LedColor Sample(double position, double step);
        }

        /// <summary>Paints every LED of <paramref name="group"/> with <paramref name="sampler"/>.</summary>
        public static void Paint<T>(DevicePart part, LedGroup group, LedColor[] layer, bool mirror, ref T sampler) where T : struct, ISampler
        {
            switch (group.Kind)
            {
                case LedGroupKind.Element:
                {
                    var line = ElementLine.Of(part);
                    var position = Along(line.ColumnOf(group), line.Columns, mirror, out var step);
                    layer[group.PhysicalIndex(0)] = sampler.Sample(position, step);
                    break;
                }

                case LedGroupKind.Matrix:
                {
                    var w = group.Columns;
                    for (var x = 0; x < w; x++)
                    {
                        var position = Along(x, w, mirror, out var step);
                        var color = sampler.Sample(position, step);
                        for (var y = 0; y < group.Rows; y++) layer[group.PhysicalIndex(x, y)] = color;
                    }

                    break;
                }

                default:
                {
                    // Logical index: rows from the left, columns from the bottom.
                    var n = group.Count;
                    for (var i = 0; i < n; i++)
                    {
                        var position = Along(i, n, mirror, out var step);
                        layer[group.PhysicalIndex(i)] = sampler.Sample(position, step);
                    }

                    break;
                }
            }
        }

        /// <summary>
        /// The position of LED <paramref name="index"/> of <paramref name="count"/> along the animation, at the LED's
        /// center; <paramref name="step"/> is the distance between two LEDs. Mirrored, from the center outwards (LEDs are
        /// then twice as far apart); computed from whole numbers, so mirror images get exactly the same position.
        /// </summary>
        public static double Along(int index, int count, bool mirror, out double step)
        {
            count = Math.Max(1, count);
            step = mirror ? 2.0 / count : 1.0 / count;
            return mirror ? Math.Abs(2 * index + 1 - count) / (double)count : (index + 0.5) / count;
        }

        /// <summary>
        /// Brightness of an LED <paramref name="distance"/> behind a moving light with a tail of <paramref name="tail"/>:
        /// full at the light, fading to dark at the end of the tail; dark ahead of the light. The tail is at least one
        /// LED long, so the light never skips an LED.
        /// </summary>
        public static double Tail(double distance, double tail, double step)
        {
            if (distance < -step / 2) return 0;
            if (distance < 0) distance = 0;
            tail = Math.Max(tail, step);
            return distance >= tail ? 0 : 1 - distance / tail;
        }

        /// <summary>One breath for a phase of 0..1: dim at 0 and 1, full at 0.5.</summary>
        public static double Breath(double phase, double dim) => dim + (1 - dim) * (0.5 - 0.5 * Math.Cos(2 * Math.PI * phase));

        /// <summary>The fraction of <paramref name="periodMs"/> that has passed at <paramref name="nowMs"/> (0..1).</summary>
        public static double Phase(long nowMs, int periodMs) => periodMs <= 0 ? 0 : (double)(nowMs % periodMs) / periodMs;

        /// <summary>The color of an animation at a position and time.</summary>
        public static LedColor ColorAt(LedColor color, AnimationColorMode mode, double position, long nowMs)
        {
            switch (mode)
            {
                case AnimationColorMode.Cycle: return LedColor.FromHue(Phase(nowMs, HueCycleMs));
                case AnimationColorMode.Rainbow: return LedColor.FromHue(position + Phase(nowMs, HueCycleMs));
                default: return color;
            }
        }

        /// <summary><paramref name="color"/> at <paramref name="brightness"/>; dark LEDs are opaque black, so they stay owned.</summary>
        public static LedColor Shade(LedColor color, double brightness) =>
            brightness <= 0 || color.IsTransparent ? LedColor.Off : color.Scale(brightness);

        /// <summary>
        /// Where a part's front plate elements sit along the animation: from the leftmost to the rightmost element,
        /// elements above each other share a position. Built once per part: render copies are never modified.
        /// </summary>
        private sealed class ElementLine
        {
            private static readonly ConditionalWeakTable<DevicePart, ElementLine> Cache = new ConditionalWeakTable<DevicePart, ElementLine>();

            private readonly Dictionary<Guid, int> columnOf = new Dictionary<Guid, int>();
            private readonly int count;

            private ElementLine(DevicePart part)
            {
                var xs = new List<KeyValuePair<Guid, double>>();
                foreach (var element in part.Elements) xs.Add(new KeyValuePair<Guid, double>(element.Id, X(part, element)));
                count = xs.Count;

                // Elements above each other form a column; the columns are the LEDs of the line.
                var columns = new SortedSet<double>();
                foreach (var x in xs) columns.Add(Math.Round(x.Value, 3));
                var index = new Dictionary<double, int>();
                foreach (var x in columns) index[x] = index.Count;

                Columns = columns.Count;
                foreach (var x in xs) columnOf[x.Key] = index[Math.Round(x.Value, 3)];
            }

            /// <summary>Number of columns of elements.</summary>
            public int Columns { get; }

            public static ElementLine Of(DevicePart part)
            {
                var line = Cache.GetValue(part, p => new ElementLine(p));
                if (line.count == CountElements(part)) return line;

                // The part changed since (only happens outside the game loop, e.g. in tests): build it again.
                Cache.Remove(part);
                return Cache.GetValue(part, p => new ElementLine(p));
            }

            /// <summary>The column of an element, counted from the left.</summary>
            public int ColumnOf(LedGroup element) => columnOf.TryGetValue(element.Id, out var column) ? column : 0;

            private static int CountElements(DevicePart part)
            {
                var n = 0;
                foreach (var group in part.Groups)
                {
                    if (group != null && group.IsElement) n++;
                }

                return n;
            }

            /// <summary>Horizontal position of an element: on SimHub's photo, else on the front plate grid.</summary>
            private static double X(DevicePart part, LedGroup element)
            {
                if (element.HasPhotoPosition) return element.PhotoX;
                try
                {
                    return FrontPlatePresets.Position(part.FrontPlate, element.Side, element.ElementSlot).x;
                }
                catch (ArgumentOutOfRangeException)
                {
                    return element.Side == LedGroupSide.Left ? -1 : element.Side == LedGroupSide.Right ? 1 : 0;
                }
            }
        }
    }
}
