using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace FreeTheLights.Core
{
    /// <summary>
    /// Which inputs (wheel buttons, encoder steps, keys: SimHub's input names such as "JoystickPlugin.Wheel_B05") are
    /// held, and when each was last pressed. SimHub reports presses and releases on background threads while the game
    /// loop reads the state, so every member is thread-safe.
    /// </summary>
    public sealed class InputState
    {
        private readonly object sync = new object();
        private readonly Func<long> clock;
        private readonly Dictionary<string, Entry> inputs = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Press times from an own clock (for when they are never compared with other times).</summary>
        public InputState()
            : this(Stopwatch.StartNew())
        {
        }

        /// <summary>Press times from <paramref name="clock"/>, in ms: the game loop's clock, so they compare with its nowMs.</summary>
        public InputState(Func<long> clock)
        {
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        private InputState(Stopwatch watch)
            : this(() => watch.ElapsedMilliseconds)
        {
        }

        /// <summary>Raised after an input was pressed, with its name (on the thread that reported it).</summary>
        public event Action<string> Pressed;

        /// <summary>An input's name as shown to the user, without the reading plugin's prefix ("JoystickPlugin.Wheel_B05" becomes "Wheel_B05").</summary>
        public static string DisplayName(string input)
        {
            if (string.IsNullOrEmpty(input)) return input ?? string.Empty;
            var dot = input.IndexOf('.');
            return dot > 0 && dot < input.Length - 1 ? input.Substring(dot + 1) : input;
        }

        public void Press(string input)
        {
            if (string.IsNullOrEmpty(input)) return;
            var now = clock();
            lock (sync)
            {
                inputs[input] = new Entry { Held = true, LastPressMs = now };
            }

            Pressed?.Invoke(input);
        }

        public void Release(string input)
        {
            if (string.IsNullOrEmpty(input)) return;
            lock (sync)
            {
                if (inputs.TryGetValue(input, out var entry))
                {
                    entry.Held = false;
                    inputs[input] = entry;
                }
            }
        }

        public bool IsHeld(string input)
        {
            if (string.IsNullOrEmpty(input)) return false;
            lock (sync)
            {
                return inputs.TryGetValue(input, out var entry) && entry.Held;
            }
        }

        /// <summary>
        /// The input is held, or was pressed at most <paramref name="minimumOnMs"/> before <paramref name="nowMs"/>: a
        /// press and an immediate release (an encoder step) still counts for that long.
        /// </summary>
        public bool IsHeldOrPressedWithin(string input, long nowMs, int minimumOnMs)
        {
            if (string.IsNullOrEmpty(input)) return false;
            lock (sync)
            {
                return inputs.TryGetValue(input, out var entry) && (entry.Held || nowMs - entry.LastPressMs <= minimumOnMs);
            }
        }

        /// <summary>
        /// Forgets every input. SimHub never reports a release that got lost (a wheel unplugged or reset while a button
        /// was held), so the plugin starts afresh when a game starts or stops.
        /// </summary>
        public void Clear()
        {
            lock (sync)
            {
                inputs.Clear();
            }
        }

        private struct Entry
        {
            public bool Held;
            public long LastPressMs;
        }
    }
}
