using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace HapticGamingBridge.Tests
{
    // These talk to the real plugin inside the Logi Plugin Service through its named pipe, so they need Options+ running
    // with the plugin loaded and the desktop app closed (the app holds a pipe connection). They skip themselves otherwise.
    // Each one sends a few short haptic events, so the mouse will buzz briefly.
    internal static class Live
    {
        public const string PipeName = "HapticGamingBridge";
        public static readonly string LogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Logi", "LogiPluginService", "Logs", "plugin_logs", "HapticGamingBridge.log");

        public static NamedPipeClientStream Connect(int timeoutMs = 3000)
        {
            var c = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            c.Connect(timeoutMs);
            return c;
        }

        public static bool Reachable()
        {
            try { using var c = Connect(1500); return File.Exists(LogPath); } catch { return false; }
        }

        public static long LogOffset() => File.Exists(LogPath) ? new FileInfo(LogPath).Length : 0;

        public static string LogSince(long offset)
        {
            using var fs = new FileStream(LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (offset > fs.Length) offset = 0;   // log was rotated
            fs.Seek(offset, SeekOrigin.Begin);
            return new StreamReader(fs).ReadToEnd();
        }

        public static async Task<string> WaitForLog(long offset, Func<string, bool> done, int timeoutMs = 4000)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            string log;
            do { await Task.Delay(100); log = LogSince(offset); } while (!done(log) && sw.ElapsedMilliseconds < timeoutMs);
            return log;
        }

        public static void Send(Stream s, string text)
        {
            var b = Encoding.UTF8.GetBytes(text);
            s.Write(b, 0, b.Length); s.Flush();
        }

        public static bool Raised(string log, string name) => Regex.IsMatch(log, @"Raising " + Regex.Escape(name) + @"\s*$", RegexOptions.Multiline);
    }

    [Trait("Category", "Live")]
    public class LivePluginTests
    {
        private static void Require() => Skip.IfNot(Live.Reachable(), "Plugin pipe not reachable (is Options+ running with the plugin loaded, and the app closed?)");

        [SkippableFact]
        public async Task Valid_event_is_raised()
        {
            Require();
            var off = Live.LogOffset();
            using (var c = Live.Connect()) Live.Send(c, "recoil_1\n");
            Assert.True(Live.Raised(await Live.WaitForLog(off, l => Live.Raised(l, "recoil_1")), "recoil_1"));
        }

        [SkippableFact]
        public async Task Malformed_event_names_are_rejected_and_plugin_keeps_serving()
        {
            Require();
            var bad = new[] { "../../x", "a b", "1abc", "x;y", "ev\u0001il", "<script>alert(1)</script>", new string('a', 100), "recoil_1 knock", "recoil_1\tknock" };
            var off = Live.LogOffset();
            using (var c = Live.Connect())
            {
                foreach (var b in bad) Live.Send(c, b + "\n");
                Live.Send(c, "done\n");
            }
            var log = await Live.WaitForLog(off, l => Live.Raised(l, "done"));
            Assert.True(Live.Raised(log, "done"), "plugin stopped serving after malformed names");
            foreach (var b in bad.Where(b => b.Length < 50)) Assert.DoesNotContain("Raising " + b, log);
            Assert.DoesNotContain("Raising " + new string('a', 100), log);
        }

        [SkippableFact]
        public async Task Wellformed_but_undeclared_name_does_not_kill_the_connection()
        {
            Require();
            var off = Live.LogOffset();
            using (var c = Live.Connect()) { Live.Send(c, "not_declared_anywhere\n"); Live.Send(c, "knock\n"); }
            var log = await Live.WaitForLog(off, l => Live.Raised(l, "knock"));
            Assert.True(Live.Raised(log, "knock"), "an undeclared event name made the plugin drop the rest of the connection");
        }

        [SkippableFact]
        public async Task Oversized_line_is_dropped_and_not_written_to_the_log()
        {
            Require();
            var huge = new string('a', 300_000);
            var off = Live.LogOffset();
            using (var c = Live.Connect()) { Live.Send(c, huge); Live.Send(c, "\n"); Live.Send(c, "alert\n"); }
            var log = await Live.WaitForLog(off, l => Live.Raised(l, "alert"));
            Assert.True(Live.Raised(log, "alert"), "plugin did not recover after an oversized line");
            Assert.True(log.Length < 20_000, $"an oversized line was echoed into the log ({log.Length} chars)");
        }

        [SkippableFact]
        public async Task Binary_garbage_does_not_break_the_plugin()
        {
            Require();
            var rnd = new Random(1234);
            var junk = new byte[4096]; rnd.NextBytes(junk);
            var off = Live.LogOffset();
            using (var c = Live.Connect())
            {
                c.Write(junk, 0, junk.Length);
                Live.Send(c, "\ninteract_2\n");
            }
            Assert.True(Live.Raised(await Live.WaitForLog(off, l => Live.Raised(l, "interact_2")), "interact_2"));
        }

        [SkippableFact]
        public async Task A_silent_connection_does_not_block_other_clients()
        {
            Require();
            using var hog = Live.Connect();   // connects and says nothing
            var off = Live.LogOffset();
            using (var other = Live.Connect(2000)) Live.Send(other, "interact_1\n");
            Assert.True(Live.Raised(await Live.WaitForLog(off, l => Live.Raised(l, "interact_1")), "interact_1"));
        }

        [SkippableFact]
        public async Task Burst_of_events_does_not_wedge_the_plugin()
        {
            Require();
            var off = Live.LogOffset();
            using (var c = Live.Connect())
            {
                var sb = new StringBuilder();
                for (var i = 0; i < 300; i++) sb.Append("move\n");
                Live.Send(c, sb.ToString());
                Live.Send(c, "done\n");
            }
            Assert.True(Live.Raised(await Live.WaitForLog(off, l => Live.Raised(l, "done"), 8000), "done"));
        }

        [SkippableFact]
        public void Pipe_is_not_open_to_other_accounts()
        {
            Require();
            using var c = new NamedPipeClientStream(".", Live.PipeName, PipeAccessRights.WriteData | PipeAccessRights.ReadPermissions,
                PipeOptions.None, TokenImpersonationLevel.Identification, HandleInheritability.None);
            c.Connect(3000);
            var broad = new[] { WellKnownSidType.WorldSid, WellKnownSidType.AuthenticatedUserSid, WellKnownSidType.BuiltinUsersSid, WellKnownSidType.AnonymousSid, WellKnownSidType.NetworkSid };
            var offenders = c.GetAccessControl().GetAccessRules(true, false, typeof(SecurityIdentifier)).Cast<PipeAccessRule>()
                .Where(r => r.AccessControlType == AccessControlType.Allow && broad.Any(w => r.IdentityReference.Equals(new SecurityIdentifier(w, null))))
                .Select(r => $"{r.IdentityReference} {r.PipeAccessRights}").ToList();
            Assert.True(offenders.Count == 0, "pipe DACL grants access to broad groups: " + string.Join("; ", offenders));
        }
    }
}
