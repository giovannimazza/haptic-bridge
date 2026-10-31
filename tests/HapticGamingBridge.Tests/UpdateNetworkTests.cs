using System.Security.Cryptography;
using HapticGamingBridge;
using Xunit;

namespace HapticGamingBridge.Tests
{
    // These talk to the real GitHub, read only, to prove the parsing and the allow-lists work against what GitHub really
    // sends. They skip themselves when offline or rate limited. Run them with tests\run-testroom.ps1, or:
    //   dotnet test --filter Category=Network
    // The full 50 MB download is only done when HB_NETWORK_DOWNLOAD=1, the first 1% is always exercised.
    [Trait("Category", "Network")]
    [Collection("network")]
    public class UpdateNetworkTests
    {
        private static async Task<(UpdateService Service, UpdateCheck Check)> CheckReal()
        {
            var service = new UpdateService(current: new Version(1, 0, 0));
            var check = await service.CheckAsync();
            Skip.If(check.Outcome == UpdateOutcome.Failed, "GitHub could not be reached or is limiting requests: " + check.Message);
            return (service, check);
        }

        [SkippableFact]
        public async Task The_real_latest_release_is_understood()
        {
            var (service, check) = await CheckReal();
            using var _ = service;
            Assert.Equal(UpdateOutcome.Available, check.Outcome);                       // 1.0.0 is older than anything published
            var release = check.Release;
            Assert.True(release.Version >= new Version(1, 1, 0));
            Assert.True(release.CanInstall, "the real release has an installer with a published digest");
            Assert.Matches("^[0-9a-f]{64}$", release.Installer.Sha256);
            Assert.True(UpdateService.IsExpectedAssetUrl(release.Installer.Url, release.Tag, release.Installer.Name));
            Assert.True(release.Installer.Size is > UpdateService.MinInstallerBytes and < UpdateService.MaxInstallerBytes);
            Assert.True(Links.IsAllowed(release.PageUrl));
        }

        [SkippableFact]
        public async Task The_real_release_is_not_an_update_for_a_build_that_is_newer()
        {
            var service = new UpdateService(current: new Version(99, 0, 0));
            using var _ = service;
            var check = await service.CheckAsync();
            Skip.If(check.Outcome == UpdateOutcome.Failed, check.Message);
            Assert.Equal(UpdateOutcome.UpToDate, check.Outcome);
        }

        [SkippableFact]
        public async Task A_real_download_passes_the_redirect_checks_and_can_be_cancelled_early()
        {
            var (service, check) = await CheckReal();
            using var _ = service;
            using var cts = new CancellationTokenSource();
            var before = Directory.GetDirectories(Path.GetTempPath(), UpdateInstaller.TempFolderPrefix + "*");
            var started = false;
            // Cancel after the first percent: by then the first hop and every redirect have been accepted by the allow-list.
            var progress = new Progress<double>(p => { started = true; if (p >= 0.01) cts.Cancel(); });

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.DownloadAsync(check.Release, progress, cts.Token));
            Assert.True(started, "no data arrived from the real download");
            Assert.Empty(Directory.GetDirectories(Path.GetTempPath(), UpdateInstaller.TempFolderPrefix + "*").Except(before));
        }

        [SkippableFact]
        public async Task The_full_real_download_matches_the_checksum_github_published()
        {
            Skip.IfNot(Environment.GetEnvironmentVariable("HB_NETWORK_DOWNLOAD") == "1", "set HB_NETWORK_DOWNLOAD=1 to download the whole installer");
            var (service, check) = await CheckReal();
            using var _ = service;
            var path = await service.DownloadAsync(check.Release);
            try
            {
                Assert.Equal(check.Release.Installer.Size, new FileInfo(path).Length);
                using var f = File.OpenRead(path);
                Assert.Equal(check.Release.Installer.Sha256, Convert.ToHexString(SHA256.HashData(f)).ToLowerInvariant());
                Assert.True(UpdateInstaller.IsSafeInstallerPath(path));
            }
            finally { try { Directory.Delete(Path.GetDirectoryName(path), true); } catch { } }
        }
    }
}
