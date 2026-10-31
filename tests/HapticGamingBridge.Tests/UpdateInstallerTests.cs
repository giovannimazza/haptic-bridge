using System.Diagnostics;
using HapticGamingBridge;
using Xunit;

namespace HapticGamingBridge.Tests
{
    public class UpdateInstallerTests
    {
        private const string Root = @"C:\Users\Someone\AppData\Local\Temp";
        private static string Good => Root + @"\HapticGamingBridge-update-0123abcd\HapticGamingBridge-Setup-1.2.0.exe";

        [Fact]
        public void An_installer_in_its_own_update_folder_is_accepted() => Assert.True(UpdateInstaller.IsSafeInstallerPath(Good, Root));

        [Theory]
        [InlineData(@"\HapticGamingBridge-update-0123\evil.exe")]                                   // wrong file name
        [InlineData(@"\HapticGamingBridge-update-0123\HapticGamingBridge-Setup-1.2.0.exe.exe")]
        [InlineData(@"\HapticGamingBridge-update-0123\HapticGamingBridge-Setup-1.2.exe")]
        [InlineData(@"\other-folder\HapticGamingBridge-Setup-1.2.0.exe")]                           // not an update folder
        [InlineData(@"\HapticGamingBridge-update-0123\sub\HapticGamingBridge-Setup-1.2.0.exe")]     // too deep
        [InlineData(@"\HapticGamingBridge-update-0123\..\HapticGamingBridge-Setup-1.2.0.exe")]      // path traversal
        [InlineData(@"\..\..\Windows\System32\HapticGamingBridge-update-0123\HapticGamingBridge-Setup-1.2.0.exe")]
        [InlineData(@"\HapticGamingBridge-Setup-1.2.0.exe")]                                        // straight in temp
        public void Any_other_path_is_refused(string relative) => Assert.False(UpdateInstaller.IsSafeInstallerPath(Root + relative, Root));

        [Theory]
        [InlineData(@"C:\Windows\System32\cmd.exe")]
        [InlineData(@"D:\Temp\HapticGamingBridge-update-0123\HapticGamingBridge-Setup-1.2.0.exe")]   // another drive's temp
        [InlineData(@"\\server\share\HapticGamingBridge-update-0123\HapticGamingBridge-Setup-1.2.0.exe")]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public void Paths_outside_the_temp_folder_are_refused(string path) => Assert.False(UpdateInstaller.IsSafeInstallerPath(path, Root));

        [Fact]
        public void The_installer_runs_silently_without_reinstalling_the_plugin_and_restarts_the_app()
        {
            Assert.Contains("/SILENT", UpdateInstaller.Arguments);
            Assert.DoesNotContain("/VERYSILENT", UpdateInstaller.Arguments);   // the user should see that something is happening
            Assert.Contains("/NORESTART", UpdateInstaller.Arguments);
            Assert.Contains("/TASKS=\"\"", UpdateInstaller.Arguments);          // no plugin installer window, no shortcut changes
            Assert.Contains("/relaunch=1", UpdateInstaller.Arguments);
        }

        [Fact]
        public void Only_a_downloaded_installer_is_ever_started()
        {
            ProcessStartInfo started = null;
            Assert.False(UpdateInstaller.Launch(@"C:\Windows\System32\cmd.exe", i => { started = i; return true; }));
            Assert.Null(started);

            var folder = Path.Combine(Path.GetTempPath(), UpdateInstaller.TempFolderPrefix + Guid.NewGuid().ToString("N"));
            var exe = Path.Combine(folder, "HapticGamingBridge-Setup-1.2.0.exe");
            Assert.True(UpdateInstaller.Launch(exe, i => { started = i; return true; }));
            Assert.Equal(exe, started.FileName);
            Assert.Equal(UpdateInstaller.Arguments, started.Arguments);
            Assert.False(started.UseShellExecute);
        }

        [Fact]
        public void A_launch_that_throws_is_reported_as_a_failure()
        {
            var exe = Path.Combine(Path.GetTempPath(), UpdateInstaller.TempFolderPrefix + "x", "HapticGamingBridge-Setup-1.2.0.exe");
            Assert.False(UpdateInstaller.Launch(exe, _ => throw new InvalidOperationException("blocked")));
        }

        [Theory]
        [InlineData(@"C:\Users\Me\AppData\Local\Programs\Haptic Gaming Bridge", @"C:\Users\Me\AppData\Local\Programs\Haptic Gaming Bridge\", true)]
        [InlineData(@"C:\Users\Me\AppData\Local\Programs\Haptic Gaming Bridge\", @"C:\Users\Me\AppData\Local\Programs\Haptic Gaming Bridge", true)]
        [InlineData(@"c:\users\me\appdata\local\programs\haptic gaming bridge", @"C:\Users\Me\AppData\Local\Programs\Haptic Gaming Bridge", true)]
        [InlineData(@"C:\dev\haptic-spike\app\bin\Debug\net10.0-windows", @"C:\Users\Me\AppData\Local\Programs\Haptic Gaming Bridge", false)]
        [InlineData(@"C:\Users\Me\Downloads\unzipped", @"C:\Users\Me\AppData\Local\Programs\Haptic Gaming Bridge", false)]
        public void Only_a_copy_the_installer_put_there_counts_as_installed(string running, string registered, bool expected)
            => Assert.Equal(expected, UpdateInstaller.IsInstalledCopy(running, () => registered));

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Without_an_install_record_it_is_not_an_installed_copy(string registered)
            => Assert.False(UpdateInstaller.IsInstalledCopy(@"C:\x", () => registered));

        [Fact]
        public void A_broken_install_record_is_not_an_installed_copy()
            => Assert.False(UpdateInstaller.IsInstalledCopy(@"C:\x", () => throw new InvalidOperationException()));
    }
}
