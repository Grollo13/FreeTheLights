namespace FreeTheLights.Core
{
    /// <summary>
    /// Tells when the car's engine started or stopped, for the engine animations. Fed once per game loop with the
    /// engine state the game reports; the caller passes the time, so it can be tested without waiting. Not thread-safe:
    /// only the game loop uses it.
    /// </summary>
    public sealed class EngineStateTracker
    {
        /// <summary>No start or stop seen (since the game or the car changed).</summary>
        public const long Never = long.MinValue;

        /// <summary>How long a new engine state must hold before it counts, so a stalling engine doesn't flicker.</summary>
        public const int SettleMs = 250;

        private bool known;
        private bool running;
        private string carId;
        private bool changing;
        private long changingSinceMs;

        /// <summary>When the engine was last seen starting; <see cref="Never"/> = not in this game and car.</summary>
        public long StartedAtMs { get; private set; } = Never;

        /// <summary>When the engine was last seen stopping; <see cref="Never"/> = not in this game and car.</summary>
        public long StoppedAtMs { get; private set; } = Never;

        /// <summary>The engine runs (as far as known; false outside a game).</summary>
        public bool IsRunning => known && running;

        /// <summary>
        /// Records the engine state at <paramref name="nowMs"/>. The first state of a game or car only sets the
        /// starting point: joining a session with the engine already running is no start.
        /// </summary>
        public void Update(bool gameRunning, string car, bool engineRunning, long nowMs)
        {
            if (!gameRunning)
            {
                Reset();
                return;
            }

            if (!known || car != carId)
            {
                Reset();
                known = true;
                carId = car;
                running = engineRunning;
                return;
            }

            if (engineRunning == running)
            {
                changing = false;
                return;
            }

            if (!changing)
            {
                changing = true;
                changingSinceMs = nowMs;
            }

            if (nowMs - changingSinceMs < SettleMs) return;

            changing = false;
            running = engineRunning;
            if (running) StartedAtMs = nowMs;
            else StoppedAtMs = nowMs;
        }

        public void Reset()
        {
            known = false;
            running = false;
            carId = null;
            changing = false;
            StartedAtMs = Never;
            StoppedAtMs = Never;
        }
    }
}
