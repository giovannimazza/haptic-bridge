using HapticGamingBridge;
using Xunit;

namespace HapticGamingBridge.Tests
{
    public class ProfileMatcherTests
    {
        private static Profile P(string exe, bool enabled = true) => new Profile { Name = exe, Exe = exe, Enabled = enabled };

        [Fact]
        public void Matches_by_file_name_ignoring_case_and_folder()
        {
            var game = P(@"C:\Games\old-folder\Game.exe");
            Assert.Same(game, ProfileMatcher.Find(new[] { P("other.exe"), game }, @"D:\new\place\GAME.EXE"));
        }

        [Fact]
        public void Disabled_profile_is_never_selected()
        {
            Assert.Null(ProfileMatcher.Find(new[] { P("game.exe", enabled: false) }, @"C:\x\game.exe"));
        }

        [Fact]
        public void Enabled_profile_wins_over_a_disabled_one_for_the_same_program()
        {
            var on = P("game.exe");
            Assert.Same(on, ProfileMatcher.Find(new[] { P("game.exe", enabled: false), on }, @"C:\x\game.exe"));
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("unknown.exe")]
        public void Nothing_in_the_foreground_or_no_profile_means_no_match(string fg)
            => Assert.Null(ProfileMatcher.Find(new[] { P("game.exe") }, fg));

        [Fact]
        public void Profile_with_empty_or_null_exe_never_matches()
        {
            Assert.Null(ProfileMatcher.Find(new[] { P(""), new Profile { Exe = null } }, @"C:\x\game.exe"));
        }
    }
}
