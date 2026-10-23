using FreeTheLights.Core;
using Xunit;

namespace FreeTheLights.Tests
{
    public class EngineStateTests
    {
        private const string Car = "porsche_911_gt3_r";

        [Fact]
        public void The_first_state_of_a_game_is_no_start()
        {
            var engine = new EngineStateTracker();
            engine.Update(true, Car, true, 1000);
            engine.Update(true, Car, true, 2000);

            Assert.True(engine.IsRunning);
            Assert.Equal(EngineStateTracker.Never, engine.StartedAtMs);
            Assert.Equal(EngineStateTracker.Never, engine.StoppedAtMs);
        }

        [Fact]
        public void A_start_counts_once_it_held_long_enough()
        {
            var engine = new EngineStateTracker();
            engine.Update(true, Car, false, 1000);
            engine.Update(true, Car, true, 2000);
            engine.Update(true, Car, true, 2000 + EngineStateTracker.SettleMs - 1);
            Assert.False(engine.IsRunning);
            Assert.Equal(EngineStateTracker.Never, engine.StartedAtMs);

            engine.Update(true, Car, true, 2000 + EngineStateTracker.SettleMs);
            Assert.True(engine.IsRunning);
            Assert.Equal(2000 + EngineStateTracker.SettleMs, engine.StartedAtMs);

            engine.Update(true, Car, false, 5000);
            engine.Update(true, Car, false, 5000 + EngineStateTracker.SettleMs);
            Assert.False(engine.IsRunning);
            Assert.Equal(5000 + EngineStateTracker.SettleMs, engine.StoppedAtMs);
            Assert.Equal(2000 + EngineStateTracker.SettleMs, engine.StartedAtMs);
        }

        [Fact]
        public void A_short_flicker_is_ignored()
        {
            var engine = new EngineStateTracker();
            engine.Update(true, Car, true, 1000);
            engine.Update(true, Car, false, 2000);
            engine.Update(true, Car, true, 2100);
            engine.Update(true, Car, false, 2200);
            engine.Update(true, Car, true, 2300 + EngineStateTracker.SettleMs);

            Assert.True(engine.IsRunning);
            Assert.Equal(EngineStateTracker.Never, engine.StoppedAtMs);
        }

        [Fact]
        public void Leaving_the_game_or_changing_the_car_starts_afresh()
        {
            var engine = new EngineStateTracker();
            engine.Update(true, Car, false, 1000);
            engine.Update(true, Car, true, 2000);
            engine.Update(true, Car, true, 3000);
            Assert.NotEqual(EngineStateTracker.Never, engine.StartedAtMs);

            engine.Update(false, null, false, 4000);
            Assert.False(engine.IsRunning);
            Assert.Equal(EngineStateTracker.Never, engine.StartedAtMs);

            engine.Update(true, Car, false, 5000);
            engine.Update(true, "bmw_m4_gt3", true, 6000);
            engine.Update(true, "bmw_m4_gt3", true, 7000);
            Assert.True(engine.IsRunning);
            Assert.Equal(EngineStateTracker.Never, engine.StartedAtMs);
        }
    }
}
