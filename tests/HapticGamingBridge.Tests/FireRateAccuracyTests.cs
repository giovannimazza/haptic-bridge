using HapticGamingBridge;
using Xunit;

namespace HapticGamingBridge.Tests
{
    // A fire rate is only worth converting if the engine really delivers it. These simulate a button held down for ten
    // seconds while the repeat timer ticks the way a Windows timer does, and count the shots.
    public class FireRateAccuracyTests
    {
        private static int Shots(int cooldownMs, int tickMs, int heldMs = 10_000)
        {
            long clock = 0; var sent = 0;
            var p = new Profile();
            p.Rules.Add(new Rule { Input = "LMB", Event = "recoil_1", CooldownMs = cooldownMs, Repeat = true });
            var e = new Engine(() => clock, _ => sent++, _ => false) { Active = p };
            e.OnInput("LMB", true);
            for (clock = tickMs; clock <= heldMs; clock += tickMs) e.Tick();
            return sent;
        }

        [Theory]
        [InlineData(300)]
        [InlineData(600)]
        [InlineData(750)]
        [InlineData(900)]
        [InlineData(1200)]
        public void Held_fire_matches_the_requested_rate_with_a_16ms_timer(int rpm)
        {
            var cooldown = FireRate.ToCooldownMs(rpm).Value;
            var expected = 10_000.0 / cooldown + 1;                    // the press itself plus one shot per cooldown
            var shots = Shots(cooldown, tickMs: 16);
            Assert.InRange(shots, expected * 0.98, expected * 1.02);
        }

        [Fact]
        public void A_coarse_timer_does_not_make_shots_drift_later_and_later()
        {
            // 80 ms cooldown on a 50 ms timer used to fire every 100 ms (25% slow).
            var shots = Shots(80, tickMs: 50);
            Assert.InRange(shots, 10_000 / 80 * 0.95, 10_000 / 80 + 2);
        }
    }
}
