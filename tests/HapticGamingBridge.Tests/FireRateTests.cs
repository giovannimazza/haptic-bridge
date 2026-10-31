using HapticGamingBridge;
using Xunit;

namespace HapticGamingBridge.Tests
{
    public class FireRateTests
    {
        [Theory]
        [InlineData(60, 1000)]
        [InlineData(300, 200)]
        [InlineData(600, 100)]
        [InlineData(750, 80)]
        [InlineData(900, 67)]       // 66.67 rounds to 67
        [InlineData(1200, 50)]
        [InlineData(3000, 20)]
        public void Rounds_per_minute_become_milliseconds_between_shots(double rpm, int expectedMs)
            => Assert.Equal(expectedMs, FireRate.ToCooldownMs(rpm));

        [Theory]
        [InlineData(100000, FireRate.MinCooldownMs)]     // faster than the timer can follow: clamped
        [InlineData(0.5, FireRate.MaxCooldownMs)]
        [InlineData(1, FireRate.MaxCooldownMs)]
        public void Extreme_rates_are_clamped_to_what_the_engine_accepts(double rpm, int expectedMs)
            => Assert.Equal(expectedMs, FireRate.ToCooldownMs(rpm));

        [Theory]
        [InlineData(0)]
        [InlineData(-600)]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        [InlineData(double.NegativeInfinity)]
        public void Unusable_rates_give_no_cooldown(double rpm)
            => Assert.Null(FireRate.ToCooldownMs(rpm));

        [Theory]
        [InlineData(100, 600)]
        [InlineData(80, 750)]
        [InlineData(50, 1200)]
        [InlineData(1000, 60)]
        public void Cooldown_converts_back_to_rounds_per_minute(int ms, int expectedRpm)
            => Assert.Equal(expectedRpm, FireRate.ToRoundsPerMinute(ms));

        [Theory]
        [InlineData(0)]
        [InlineData(-5)]
        public void No_delay_has_no_rate(int ms) => Assert.Null(FireRate.ToRoundsPerMinute(ms));

        [Fact]
        public void Round_trip_stays_within_the_rounding_error_of_whole_milliseconds()
        {
            for (var rpm = 60; rpm <= 3000; rpm += 7)
            {
                var back = FireRate.ToRoundsPerMinute(FireRate.ToCooldownMs(rpm).Value).Value;
                Assert.InRange(back, rpm * 0.97, rpm * 1.03);
            }
        }

        [Theory]
        [InlineData("600", 600)]
        [InlineData("  600  ", 600)]
        [InlineData("600.5", 600.5)]
        [InlineData("600,5", 600.5)]         // decimal comma, as typed on an Italian keyboard
        public void Typed_rates_are_parsed_whatever_the_decimal_separator(string text, double expected)
        {
            Assert.True(FireRate.TryParse(text, out var rpm));
            Assert.Equal(expected, rpm);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        [InlineData("abc")]
        [InlineData("6 0 0")]
        [InlineData("--5")]
        public void Text_that_is_not_a_number_is_rejected(string text) => Assert.False(FireRate.TryParse(text, out _));

        [Fact]
        public void The_editor_hint_explains_the_cooldown_and_warns_when_it_is_too_fast_to_be_exact()
        {
            Assert.Equal("100 ms between shots.", FireRate.Describe(100));
            Assert.Contains("No delay", FireRate.Describe(0));
            Assert.Contains("approximate", FireRate.Describe(FireRate.ApproximateBelowMs - 1));
            Assert.DoesNotContain("approximate", FireRate.Describe(FireRate.ApproximateBelowMs));
        }
    }
}
