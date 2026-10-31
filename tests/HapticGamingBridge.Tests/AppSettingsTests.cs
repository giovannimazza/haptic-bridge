using HapticGamingBridge;
using Xunit;

namespace HapticGamingBridge.Tests
{
    public class AppSettingsTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "hb-settings-" + Guid.NewGuid().ToString("N"));
        private string SettingsFile => Path.Combine(_dir, "settings.json");
        public AppSettingsTests() => Directory.CreateDirectory(_dir);
        public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

        [Fact]
        public void Defaults_check_for_updates_but_do_not_install_by_themselves()
        {
            var s = new AppSettings();
            Assert.True(s.CheckForUpdates);
            Assert.False(s.InstallUpdatesAutomatically);
            Assert.Equal("", s.SkippedVersion);
            Assert.Null(s.LastCheckUtc);
        }

        [Fact]
        public void A_missing_file_gives_the_defaults() => Assert.True(SettingsStore.Load(SettingsFile).CheckForUpdates);

        [Fact]
        public void Settings_survive_a_save_and_load_and_leave_no_temp_file()
        {
            var when = new DateTime(2026, 11, 1, 10, 30, 0, DateTimeKind.Utc);
            SettingsStore.Save(SettingsFile, new AppSettings { CheckForUpdates = false, InstallUpdatesAutomatically = true, SkippedVersion = "1.2.0", LastCheckUtc = when });
            var back = SettingsStore.Load(SettingsFile);
            Assert.False(back.CheckForUpdates);
            Assert.True(back.InstallUpdatesAutomatically);
            Assert.Equal("1.2.0", back.SkippedVersion);
            Assert.Equal(when, back.LastCheckUtc);
            Assert.False(File.Exists(SettingsFile + ".tmp"));
        }

        [Theory]
        [InlineData("{ not json")]
        [InlineData("")]
        [InlineData("null")]
        [InlineData("[1,2,3]")]
        [InlineData("{\"CheckForUpdates\": \"maybe\"}")]
        public void A_damaged_file_gives_the_defaults_and_never_throws(string content)
        {
            File.WriteAllText(SettingsFile, content);
            var s = SettingsStore.Load(SettingsFile);
            Assert.True(s.CheckForUpdates);
            Assert.False(s.InstallUpdatesAutomatically);
        }

        [Fact]
        public void An_oversized_file_is_ignored()
        {
            File.WriteAllText(SettingsFile, "{\"CheckForUpdates\": false}" + new string(' ', 200_000));
            Assert.True(SettingsStore.Load(SettingsFile).CheckForUpdates);
        }

        [Theory]
        [InlineData("not a version")]
        [InlineData("../../evil")]
        [InlineData("1.2.3.4.5.6")]
        public void A_skipped_version_that_is_not_a_version_is_cleared(string skipped)
        {
            File.WriteAllText(SettingsFile, "{\"SkippedVersion\": \"" + skipped + "\"}");
            Assert.Equal("", SettingsStore.Load(SettingsFile).SkippedVersion);
        }

        [Fact]
        public void A_last_check_in_the_far_future_is_cleared_so_checks_are_not_blocked_forever()
        {
            File.WriteAllText(SettingsFile, "{\"LastCheckUtc\": \"2099-01-01T00:00:00Z\"}");
            Assert.Null(SettingsStore.Load(SettingsFile).LastCheckUtc);
        }
    }
}
