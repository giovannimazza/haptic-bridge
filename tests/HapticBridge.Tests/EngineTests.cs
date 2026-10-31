using HapticBridge;
using Xunit;

namespace HapticBridge.Tests
{
    public class EngineTests
    {
        private long _clock;
        private readonly List<string> _sent = new();
        private readonly HashSet<string> _mods = new();

        private Engine Make(params Rule[] rules)
        {
            var p = new Profile();
            foreach (var r in rules) p.Rules.Add(r);
            return new Engine(() => _clock, _sent.Add, _mods.Contains) { Active = p };
        }

        [Fact]
        public void Press_fires_once_and_cooldown_blocks_repeat()
        {
            var e = Make(new Rule { Input = "LMB", Event = "recoil_1", CooldownMs = 600, Repeat = true });
            e.OnInput("LMB", true);
            _clock = 300; e.Tick();
            Assert.Single(_sent);
            _clock = 650; e.Tick();
            Assert.Equal(2, _sent.Count);
        }

        [Fact]
        public void Release_stops_repeat()
        {
            var e = Make(new Rule { Input = "LMB", Event = "recoil_1", CooldownMs = 10, Repeat = true });
            e.OnInput("LMB", true);
            e.OnInput("LMB", false);
            _clock = 5000; e.Tick();
            Assert.Single(_sent);
        }

        [Fact]
        public void Os_key_autorepeat_is_ignored()
        {
            var e = Make(new Rule { Input = "F", Event = "interact_1", CooldownMs = 0 });
            for (var i = 0; i < 20; i++) e.OnInput("F", true);
            Assert.Single(_sent);
        }

        [Fact]
        public void Modifier_rule_wins_over_plain_rule_only_while_held()
        {
            var e = Make(new Rule { Input = "LMB", Event = "recoil_1" },
                         new Rule { Input = "LMB", Event = "recoil_2", Modifier = "Ctrl" });
            e.OnInput("LMB", true); e.OnInput("LMB", false);
            _mods.Add("Ctrl"); _clock = 5000;
            e.OnInput("LMB", true);
            Assert.Equal(new[] { "recoil_1", "recoil_2" }, _sent);
        }

        [Fact]
        public void Modifier_only_rule_does_not_fire_without_modifier()
        {
            var e = Make(new Rule { Input = "LMB", Event = "recoil_2", Modifier = "Shift" });
            e.OnInput("LMB", true);
            Assert.Empty(_sent);
        }

        [Fact]
        public void Disabled_engine_never_fires()
        {
            var e = Make(new Rule { Input = "LMB", Event = "recoil_1", Repeat = true, CooldownMs = 0 });
            e.Enabled = false;
            e.OnInput("LMB", true); _clock = 9000; e.Tick();
            Assert.Empty(_sent);
        }

        [Fact]
        public void No_active_profile_never_fires()
        {
            var e = Make(new Rule { Input = "LMB", Event = "recoil_1" });
            e.Active = null;
            e.OnInput("LMB", true);
            Assert.Empty(_sent);
        }

        // Rules come from a user-editable file; extreme values must not crash or overflow the cooldown check.
        [Theory]
        [InlineData(int.MinValue)]
        [InlineData(-1)]
        [InlineData(int.MaxValue)]
        public void Extreme_cooldowns_do_not_throw(int cooldown)
        {
            var e = Make(new Rule { Input = "LMB", Event = "recoil_1", CooldownMs = cooldown, Repeat = true });
            e.OnInput("LMB", true);
            _clock = long.MaxValue / 2; e.Tick();
            Assert.NotEmpty(_sent);
        }

        [Fact]
        public void Burst_of_inputs_is_throttled_by_cooldown()
        {
            var e = Make(new Rule { Input = "RMB", Event = "knock", CooldownMs = 100 });
            for (var i = 0; i < 1000; i++) { _clock = i; e.OnInput("RMB", true); e.OnInput("RMB", false); }
            Assert.InRange(_sent.Count, 1, 11);   // 1000 ms of input at a 100 ms cooldown
        }
    }
}
