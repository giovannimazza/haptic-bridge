using System;
using System.Diagnostics;

namespace HapticGamingBridge
{
    // The only place that starts another program: it opens this project's own GitHub pages, and the maintainer's PayPal and Ko-fi
    // donation pages, in the default browser. Anything else is refused, so the Info tab cannot be turned into a launcher
    // for arbitrary URLs or files.
    public static class Links
    {
        public const string Repo = "https://github.com/giovannimazza/haptic-bridge";
        public const string Issues = Repo + "/issues";
        public const string Releases = Repo + "/releases";
        public const string Coffee = "https://paypal.me/giovanbattistamazza";
        public const string Kofi = "https://ko-fi.com/giovanbattistamazza";

        public static bool IsAllowed(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return false;
            if (u.Scheme != Uri.UriSchemeHttps || !u.IsDefaultPort || u.UserInfo.Length != 0) return false;

            if (u.Host == "github.com")
                return u.AbsolutePath == "/giovannimazza/haptic-bridge" || u.AbsolutePath.StartsWith("/giovannimazza/haptic-bridge/", StringComparison.Ordinal);

            if (u.Host == "paypal.me")
                return u.AbsolutePath == "/giovanbattistamazza" || u.AbsolutePath == "/giovanbattistamazza/";

            if (u.Host == "ko-fi.com")
                return u.AbsolutePath == "/giovanbattistamazza" || u.AbsolutePath == "/giovanbattistamazza/";

            return false;
        }

        // Returns false when the link was refused or the browser could not be started.
        public static bool Open(string url)
        {
            if (!IsAllowed(url)) return false;
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); return true; }
            catch { return false; }
        }
    }
}
