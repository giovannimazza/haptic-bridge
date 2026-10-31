using HapticGamingBridge;
using Xunit;

namespace HapticGamingBridge.Tests
{
    public class ProfileNamesTests
    {
        [Theory]
        [InlineData("Counter Strike", "Counter Strike")]
        [InlineData("  Valorant  ", "Valorant")]
        [InlineData("My   game\t\n night", "My game night")]
        [InlineData("a\u0000b\u0007c", "a b c")]            // control characters never reach the UI or the file
        [InlineData("Jeu d'été ★", "Jeu d'été ★")]            // other languages and symbols are fine
        public void Names_are_tidied(string typed, string expected) => Assert.Equal(expected, ProfileNames.Clean(typed));

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("\t\n")]
        [InlineData(null)]
        public void Nothing_usable_means_no_new_name(string typed) => Assert.Null(ProfileNames.Clean(typed));

        [Fact]
        public void Long_names_are_cut_without_leaving_a_trailing_space()
        {
            var name = ProfileNames.Clean(new string('x', 59) + " " + new string('y', 50));
            Assert.True(name.Length <= ProfileNames.MaxLength);
            Assert.False(name.EndsWith(" "));
        }

        [Theory]
        [InlineData("Discord", @"C:\Apps\Discord.exe", "Game", "Game")]                 // still the program's own name: follows the new program
        [InlineData("discord", @"C:\Apps\Discord.exe", "Game", "Game")]                 // case does not matter
        [InlineData("New profile", @"Restory.exe", "Game", "Game")]                      // old placeholder
        [InlineData("", @"C:\Apps\Discord.exe", "Game", "Game")]
        [InlineData("My shooter", @"C:\Apps\Discord.exe", "Game", "My shooter")]       // a name the user chose is kept
        public void Changing_the_app_keeps_a_custom_name(string current, string oldExe, string newDefault, string expected)
            => Assert.Equal(expected, ProfileNames.AfterChangingApp(current, oldExe, newDefault));
    }
}
