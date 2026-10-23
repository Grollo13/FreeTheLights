using System;
using System.Collections.Concurrent;
using FreeTheLights.Core;

namespace FreeTheLights.Engine
{
    /// <summary>
    /// The latest frame of every part, with the time the game loop rendered it. Written by the game loop, read by the LED
    /// effects and the preview; thread-safe.
    /// <para>
    /// A frame older than <see cref="MaxAgeMs"/> reads as none, so the LEDs are released (transparent, the effects below
    /// FreeTheLights show) instead of freezing on the last frame, for example a flag, when the game loop stops calling
    /// DataUpdate, fails before rendering, or one part keeps failing to render.
    /// </para>
    /// </summary>
    public sealed class FrameStore
    {
        /// <summary>How old a frame may be. The game loop renders many times per second, so this only passes when it stopped.</summary>
        public const int MaxAgeMs = 1000;

        private readonly ConcurrentDictionary<Guid, Frame> frames = new ConcurrentDictionary<Guid, Frame>();

        /// <summary>
        /// Stores the frame of a part (physical LED order of its channel), rendered at <paramref name="nowMs"/>. A frame
        /// with the same colors as the part's previous one keeps the previous array (with the new time). Call it from one
        /// thread (the game loop).
        /// </summary>
        public void Put(Guid partId, LedColor[] leds, long nowMs)
        {
            if (frames.TryGetValue(partId, out var previous) && SameColors(previous.Leds, leds)) leds = previous.Leds;
            frames[partId] = new Frame(leds, nowMs);
        }

        /// <summary>
        /// The part's latest frame, or null when there is none or it is older than <see cref="MaxAgeMs"/> at
        /// <paramref name="nowMs"/>. Frames with the same colors are the same array, so a reader comparing arrays by
        /// reference (the preview) only repaints when the colors changed.
        /// </summary>
        public LedColor[] Get(Guid partId, long nowMs)
        {
            return frames.TryGetValue(partId, out var frame) && nowMs - frame.RenderedMs <= MaxAgeMs ? frame.Leds : null;
        }

        private static bool SameColors(LedColor[] a, LedColor[] b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null || a.Length != b.Length) return false;
            for (var i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i]) return false;
            }

            return true;
        }

        private sealed class Frame
        {
            public Frame(LedColor[] leds, long renderedMs)
            {
                Leds = leds;
                RenderedMs = renderedMs;
            }

            public LedColor[] Leds { get; }

            public long RenderedMs { get; }
        }
    }
}
