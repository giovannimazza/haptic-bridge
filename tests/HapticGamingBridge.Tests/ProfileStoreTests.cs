using System.Text.Json;
using HapticGamingBridge;
using Xunit;

namespace HapticGamingBridge.Tests
{
    // profiles.json is editable by the user (or by anything running as them), so loading it is a trust boundary.
    public class ProfileStoreTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "hb-tests-" + Guid.NewGuid().ToString("N"));
        private string PathOf(string name = "profiles.json") => Path.Combine(_dir, name);

        public ProfileStoreTests() => Directory.CreateDirectory(_dir);
        public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

        private static Profile P(string exe, params Rule[] rules)
        {
            var p = new Profile { Name = "g", Exe = exe };
            foreach (var r in rules) p.Rules.Add(r);
            return p;
        }

        [Fact]
        public void Missing_file_loads_empty()
            => Assert.Empty(ProfileStore.Load(PathOf()));

        [Fact]
        public void Roundtrip_keeps_rules_and_leaves_no_temp_file()
        {
            ProfileStore.Save(PathOf(), new[] { P(@"C:\Games\a.exe", new Rule { Input = "LMB", Event = "recoil_1", CooldownMs = 75, Repeat = true, Modifier = "Ctrl" }) });
            var back = ProfileStore.Load(PathOf());
            var r = Assert.Single(Assert.Single(back).Rules);
            Assert.Equal(("LMB", "recoil_1", 75, true, "Ctrl"), (r.Input, r.Event, r.CooldownMs, r.Repeat, r.Modifier));
            Assert.False(File.Exists(PathOf() + ".tmp"));
        }

        [Theory]
        [InlineData("{ not json")]
        [InlineData("")]
        [InlineData("[{\"Name\": 5}]")]
        [InlineData("\u0000\u0001\u0002")]
        public void Malformed_file_throws_and_is_not_overwritten(string content)
        {
            File.WriteAllText(PathOf(), content);
            Assert.ThrowsAny<Exception>(() => ProfileStore.Load(PathOf()));
            Assert.Equal(content, File.ReadAllText(PathOf()));
        }

        [Fact]
        public void Oversized_file_is_rejected()
        {
            File.WriteAllText(PathOf(), new string(' ', (int)ProfileStore.MaxFileBytes + 1));
            Assert.Throws<InvalidDataException>(() => ProfileStore.Load(PathOf()));
        }

        [Fact]
        public void Null_entries_and_null_fields_are_repaired()
        {
            File.WriteAllText(PathOf(), "[null, {\"Name\":null,\"Exe\":null,\"Rules\":null}, {\"Rules\":[null]}]");
            var loaded = ProfileStore.Load(PathOf());
            Assert.Equal(2, loaded.Count);
            Assert.All(loaded, p => { Assert.False(string.IsNullOrEmpty(p.Name)); Assert.Equal("", p.Exe); Assert.Empty(p.Rules); });
        }

        [Fact]
        public void Unknown_events_never_reach_the_pipe()
        {
            var clean = ProfileStore.Sanitize(new[]
            {
                P("a.exe",
                    new Rule { Input = "LMB", Event = "recoil_1" },
                    new Rule { Input = "LMB", Event = "../../etc/passwd" },
                    new Rule { Input = "LMB", Event = "recoil_1\nknock" },
                    new Rule { Input = "LMB", Event = null },
                    new Rule { Input = "LMB", Event = "not_declared" })
            });
            Assert.Equal("recoil_1", Assert.Single(Assert.Single(clean).Rules).Event);
        }

        [Fact]
        public void Cooldown_is_clamped_and_bad_modifier_reset()
        {
            var r = Assert.Single(Assert.Single(ProfileStore.Sanitize(new[]
            {
                P("a.exe", new Rule { Input = "F", Event = "move", CooldownMs = int.MaxValue, Modifier = "Win+Del" })
            })).Rules);
            Assert.Equal(ProfileStore.MaxCooldownMs, r.CooldownMs);
            Assert.Equal("", r.Modifier);
            Assert.Equal(0, Assert.Single(Assert.Single(ProfileStore.Sanitize(new[] { P("a.exe", new Rule { Input = "F", Event = "move", CooldownMs = -50 }) })).Rules).CooldownMs);
        }

        [Fact]
        public void Huge_text_and_rule_counts_are_capped()
        {
            var many = Enumerable.Range(0, ProfileStore.MaxRulesPerProfile + 50).Select(_ => new Rule { Input = "F", Event = "move" }).ToArray();
            var p = P("a.exe", many); p.Name = new string('x', 10_000);
            var clean = Assert.Single(ProfileStore.Sanitize(new[] { p }));
            Assert.Equal(ProfileStore.MaxRulesPerProfile, clean.Rules.Count);
            Assert.Equal(ProfileStore.MaxTextLength, clean.Name.Length);
        }

        // Reading an icon from \\server\share makes Windows open an SMB connection and send the user's NTLM hash.
        [Theory]
        [InlineData(@"\\attacker\share\game.exe")]
        [InlineData(@"//attacker/share/game.exe")]
        [InlineData(@"\\?\UNC\attacker\share\game.exe")]
        [InlineData(@"\\.\pipe\something")]
        [InlineData(@"http://evil.example/x.exe")]
        [InlineData(@"..\..\Windows\System32\cmd.exe")]
        [InlineData("a<b>.exe")]
        public void Non_local_or_odd_exe_paths_are_dropped(string exe)
            => Assert.Equal("", ProfileStore.SafeExe(exe));

        [Theory]
        [InlineData("game.exe")]
        [InlineData(@"C:\Games\Cool Game\game.exe")]
        [InlineData("D:/Games/game.exe")]
        public void Local_paths_and_bare_names_are_kept(string exe)
            => Assert.Equal(exe, ProfileStore.SafeExe(exe));

        [Fact]
        public void Icon_lookup_with_hostile_paths_does_not_throw_or_hang()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            foreach (var p in new[] { null, "", " ", "..\\..\\x.exe", "C:\\nonexistent\\x.exe", new string('a', 5000), "con", "NUL" })
                Assert.Null(IconCache.Get(p));
            Assert.True(sw.ElapsedMilliseconds < 3000);
        }

        [Fact]
        public void Migrate_copies_old_profiles_once_and_keeps_the_original()
        {
            var oldP = PathOf("old.json"); var newP = Path.Combine(_dir, "new", "profiles.json");
            File.WriteAllText(oldP, "[]");
            Assert.True(ProfileStore.Migrate(oldP, newP));
            Assert.True(File.Exists(oldP));
            File.WriteAllText(newP, "[{\"Name\":\"mine\"}]");
            Assert.False(ProfileStore.Migrate(oldP, newP));                 // never overwrites
            Assert.Contains("mine", File.ReadAllText(newP));
            Assert.False(ProfileStore.Migrate(PathOf("missing.json"), PathOf("x.json")));
        }

        [Fact]
        public void Failed_save_does_not_destroy_the_existing_file()
        {
            ProfileStore.Save(PathOf(), new[] { P("a.exe") });
            var before = File.ReadAllText(PathOf());
            // A directory where the temp file should go makes the write fail before the swap.
            Directory.CreateDirectory(PathOf() + ".tmp");
            Assert.ThrowsAny<Exception>(() => ProfileStore.Save(PathOf(), new[] { P("b.exe") }));
            Assert.Equal(before, File.ReadAllText(PathOf()));
        }
    }
}
