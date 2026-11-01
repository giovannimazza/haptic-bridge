using HapticGamingBridge;
using Xunit;

namespace HapticGamingBridge.Tests
{
    // The Info tab starts the default browser, so what it may open is restricted to the project's GitHub pages
    // and the maintainer's Ko-fi donation page.
    public class LinksTests
    {
        [Theory]
        [InlineData("https://github.com/giovannimazza/haptic-bridge")]
        [InlineData("https://github.com/giovannimazza/haptic-bridge/issues")]
        [InlineData("https://github.com/giovannimazza/haptic-bridge/releases")]
        [InlineData("https://github.com/giovannimazza/haptic-bridge#readme")]
        [InlineData("https://ko-fi.com/giovanbattistamazza")]
        public void Project_and_donation_pages_are_allowed(string url) => Assert.True(Links.IsAllowed(url));

        [Theory]
        [InlineData("http://github.com/giovannimazza/haptic-bridge")]                      // not https
        [InlineData("https://github.com/giovannimazza/haptic-bridge-evil")]                // prefix trick
        [InlineData("https://github.com/someone-else/haptic-bridge")]
        [InlineData("https://github.com.evil.example/giovannimazza/haptic-bridge")]
        [InlineData("https://evil.example/https://github.com/giovannimazza/haptic-bridge")]
        [InlineData("https://user:pw@github.com/giovannimazza/haptic-bridge")]
        [InlineData("https://github.com:8443/giovannimazza/haptic-bridge")]
        [InlineData("https://paypal.me/giovanbattistamazza")]            // PayPal is no longer linked
        [InlineData("https://ko-fi.com/someone-else")]
        [InlineData("https://ko-fi.com/giovanbattistamazza-evil")]
        [InlineData("https://ko-fi.com.evil.example/giovanbattistamazza")]
        [InlineData("https://www.ko-fi.com/giovanbattistamazza")]
        [InlineData("http://ko-fi.com/giovanbattistamazza")]
        [InlineData("https://user@ko-fi.com/giovanbattistamazza")]
        [InlineData("file:///C:/Windows/System32/cmd.exe")]
        [InlineData("C:\\Windows\\System32\\cmd.exe")]
        [InlineData("calc.exe")]
        [InlineData("javascript:alert(1)")]
        [InlineData("")]
        [InlineData(null)]
        public void Everything_else_is_refused(string url) => Assert.False(Links.IsAllowed(url));

        [Fact]
        public void Open_refuses_without_starting_anything()
            => Assert.False(Links.Open("file:///C:/Windows/System32/cmd.exe"));
    }
}
