using System;
using System.Collections.Generic;

namespace HapticGamingBridge
{
    // Pure input -> haptic logic; clock, sender and modifier lookup are injected so it can be tested.
    public class Engine
    {
        private readonly Func<long> _nowMs;
        private readonly Action<string> _send;
        private readonly Func<string, bool> _modifierHeld;
        private readonly Dictionary<Rule, long> _last = new();
        private readonly Dictionary<string, Rule> _held = new();

        public Profile Active { get; set; }
        public bool Enabled { get; set; } = true;               // global master switch

        public Engine(Func<long> nowMs, Action<string> send, Func<string, bool> modifierHeld)
        {
            _nowMs = nowMs; _send = send; _modifierHeld = modifierHeld;
        }

        public void OnInput(string input, bool down)
        {
            if (!down) { _held.Remove(input); return; }
            if (!Enabled || _held.ContainsKey(input)) return;   // key auto-repeat: Repeat rules use Tick instead

            var rule = Find(input);
            if (rule == null) return;
            _held[input] = rule;
            TryFire(rule);
        }

        public void Tick()
        {
            if (!Enabled) return;
            foreach (var rule in new List<Rule>(_held.Values))
                if (rule.Repeat) TryFire(rule);
        }

        private Rule Find(string input)
        {
            if (Active == null) return null;
            Rule fallback = null;
            foreach (var r in Active.Rules)
            {
                if (r.Input != input) continue;
                if (r.Modifier == "") fallback ??= r;
                else if (_modifierHeld(r.Modifier)) return r;   // modifier rule wins over the plain one
            }
            return fallback;
        }

        private void TryFire(Rule rule)
        {
            var now = _nowMs();
            if (_last.TryGetValue(rule, out var t) && now - t < rule.CooldownMs) return;
            _last[rule] = now;
            _send(rule.Event);
        }

        public static void SelfTest()
        {
            long clock = 0; var sent = new List<string>(); var mods = new HashSet<string>();
            var p = new Profile();
            p.Rules.Add(new Rule { Input = "LMB", Event = "recoil_1", CooldownMs = 600, Repeat = true });
            p.Rules.Add(new Rule { Input = "LMB", Event = "recoil_2", Modifier = "Ctrl" });
            p.Rules.Add(new Rule { Input = "F", Event = "interact_1", CooldownMs = 100 });
            var e = new Engine(() => clock, sent.Add, mods.Contains) { Active = p };

            e.OnInput("LMB", true); Assert(sent.Count == 1, "first press fires");
            clock = 300; e.Tick(); Assert(sent.Count == 1, "cooldown blocks repeat");
            clock = 650; e.Tick(); Assert(sent.Count == 2, "repeat fires after cooldown");
            e.OnInput("LMB", false); clock = 2000; e.Tick(); Assert(sent.Count == 2, "no repeat after release");
            e.OnInput("F", true); e.OnInput("F", true); Assert(sent.Count == 3, "key auto-repeat ignored");
            e.OnInput("F", false); mods.Add("Ctrl"); clock = 3000;
            e.OnInput("LMB", true); Assert(sent[^1] == "recoil_2", "modifier rule wins");
            e.OnInput("LMB", false); e.Active = null; e.OnInput("F", true); Assert(sent.Count == 4, "no active profile, no fire");
            e.Active = p; e.OnInput("F", false); e.Enabled = false; clock = 9000; e.OnInput("F", true); Assert(sent.Count == 4, "disabled, no fire");
        }

        private static void Assert(bool ok, string what) { if (!ok) throw new Exception("SelfTest failed: " + what); }
    }
}
