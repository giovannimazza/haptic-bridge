using System;
using System.Globalization;

namespace HapticGamingBridge
{
    // Converts a weapon's rate of fire (rounds per minute) to the cooldown in milliseconds the engine uses, and back.
    public static class FireRate
    {
        public const int MinCooldownMs = 20;                        // 3000 rounds per minute; the repeat timer cannot be steadier than ~15 ms
        public const int MaxCooldownMs = ProfileStore.MaxCooldownMs;
        public const int ApproximateBelowMs = 50;                   // under this the timer's jitter is a large part of the interval

        // Milliseconds between two shots, or null when the input is not a usable positive number.
        public static int? ToCooldownMs(double roundsPerMinute)
        {
            if (double.IsNaN(roundsPerMinute) || double.IsInfinity(roundsPerMinute) || roundsPerMinute <= 0) return null;
            return (int)Math.Clamp(Math.Round(60_000.0 / roundsPerMinute), MinCooldownMs, MaxCooldownMs);
        }

        // Rounds per minute for a cooldown, or null for 0 ms (no delay: no rate).
        public static int? ToRoundsPerMinute(int cooldownMs)
            => cooldownMs > 0 ? (int)Math.Round(60_000.0 / cooldownMs) : null;

        // Accepts "600", "600.5" and "600,5", whatever the Windows language is.
        public static bool TryParse(string text, out double roundsPerMinute)
        {
            roundsPerMinute = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;
            return double.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out roundsPerMinute);
        }

        // Short text for the editor: what the cooldown means, or a warning when it is too fast to be exact.
        public static string Describe(int cooldownMs)
        {
            if (cooldownMs <= 0) return "No delay: every press and every timer tick fires.";
            if (cooldownMs < ApproximateBelowMs)
                return $"{cooldownMs} ms between shots. Very fast: the timer is only accurate to about 15 ms, so the real rate is approximate and the mouse may merge pulses.";
            return $"{cooldownMs} ms between shots.";
        }
    }
}
