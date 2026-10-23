using System;

namespace FreeTheLights.Core
{
    /// <summary>
    /// Tells when a burst of changes has been quiet for <see cref="DelayMs"/>, for example to save the settings once the
    /// user stops dragging a slider. The caller passes the time, so it can be tested without waiting.
    /// </summary>
    public sealed class Debouncer
    {
        private long lastChangeMs;

        public Debouncer(int delayMs)
        {
            DelayMs = Math.Max(0, delayMs);
        }

        public int DelayMs { get; }

        /// <summary>A change happened that hasn't been handled yet.</summary>
        public bool IsPending { get; private set; }

        /// <summary>Records a change at <paramref name="nowMs"/>; the quiet period starts again.</summary>
        public void Changed(long nowMs)
        {
            IsPending = true;
            lastChangeMs = nowMs;
        }

        /// <summary>True once per burst of changes, as soon as it has been quiet long enough; the burst then counts as handled.</summary>
        public bool TakeDue(long nowMs)
        {
            if (!IsPending || nowMs - lastChangeMs < DelayMs) return false;
            IsPending = false;
            return true;
        }

        /// <summary>The pending changes were handled some other way (for example saved when the page closed).</summary>
        public void Reset() => IsPending = false;
    }
}
