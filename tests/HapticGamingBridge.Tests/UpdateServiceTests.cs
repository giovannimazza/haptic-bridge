using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HapticGamingBridge;
using Xunit;

namespace HapticGamingBridge.Tests
{
    // The update service talks to GitHub, so these tests give it a fake GitHub and check what it accepts and refuses.
    // Nothing here touches the network.
    public class UpdateServiceTests
    {
        private readonly string[] _foldersBefore = Directory.GetDirectories(Path.GetTempPath(), UpdateInstaller.TempFolderPrefix + "*");
        private const string Repo = "giovannimazza/haptic-bridge";

        private sealed class FakeGitHub : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, HttpResponseMessage> _reply;
            public readonly List<Uri> Requests = new();
            public FakeGitHub(Func<HttpRequestMessage, HttpResponseMessage> reply) => _reply = reply;
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                Requests.Add(request.RequestUri);
                return Task.FromResult(_reply(request));
            }
        }

        private sealed class Hang : HttpMessageHandler
        {
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                await Task.Delay(Timeout.Infinite, ct);
                return null;
            }
        }

        // Serves a byte array through a non-seekable stream and counts how much of it was actually read.
        private sealed class CountingStream : Stream
        {
            private readonly MemoryStream _inner;
            public long Served;
            public CountingStream(byte[] data) => _inner = new MemoryStream(data);
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override int Read(byte[] buffer, int offset, int count) { var n = _inner.Read(buffer, offset, count); Served += n; return n; }
            public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) { var n = _inner.Read(buffer.Span); Served += n; return new ValueTask<int>(n); }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }

        private static readonly byte[] InstallerBytes = MakeBytes(1_200_000);
        private static byte[] MakeBytes(int n) { var b = new byte[n]; new Random(42).NextBytes(b); return b; }
        private static string Sha(byte[] b) => Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();

        private static string ReleaseJson(string tag = "v1.2.0", string digest = "auto", string assetName = null, string url = null, long? size = null,
            bool draft = false, bool prerelease = false, string body = "Fixes and features.")
        {
            var version = tag.TrimStart('v');
            assetName ??= $"HapticGamingBridge-Setup-{version}.exe";
            url ??= $"https://github.com/{Repo}/releases/download/{tag}/{assetName}";
            var asset = new Dictionary<string, object>
            {
                ["name"] = assetName, ["browser_download_url"] = url, ["size"] = size ?? InstallerBytes.Length,
            };
            if (digest == "auto") asset["digest"] = "sha256:" + Sha(InstallerBytes);
            else if (digest != null) asset["digest"] = digest;
            return JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["tag_name"] = tag, ["name"] = "Release " + tag, ["body"] = body, ["draft"] = draft, ["prerelease"] = prerelease,
                ["html_url"] = $"https://github.com/{Repo}/releases/tag/{tag}",
                ["assets"] = new[] { asset },
            });
        }

        private static HttpResponseMessage Json(string json, HttpStatusCode code = HttpStatusCode.OK)
            => new(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

        private static UpdateService Service(HttpMessageHandler h, string current = "1.1.0", Func<string, SignatureStatus> sig = null, UpdatePolicy policy = null)
            => new(h, new Version(current), sig ?? (_ => SignatureStatus.NotSigned), policy);

        // ------------------------------------------------------------------ version numbers

        [Theory]
        [InlineData("1.2.3", "1.2.3")]
        [InlineData("v1.2.3", "1.2.3")]
        [InlineData("V1.2", "1.2.0")]
        [InlineData("1.2.3-beta.1", "1.2.3")]
        [InlineData("1.2.3+abc", "1.2.3")]
        [InlineData("1.10.0", "1.10.0")]
        public void Version_numbers_are_parsed(string text, string expected) => Assert.Equal(new Version(expected), UpdateService.ParseVersion(text));

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("latest")]
        [InlineData("1")]
        [InlineData("1.2.3.4.5")]
        [InlineData("1.2.x")]
        [InlineData("99999.1.1")]
        [InlineData("-1.2.3")]
        public void Anything_else_is_not_a_version(string text) => Assert.Null(UpdateService.ParseVersion(text));

        [Fact]
        public void Versions_compare_as_numbers_not_as_text() => Assert.True(UpdateService.ParseVersion("1.10.0") > UpdateService.ParseVersion("1.9.0"));

        // ------------------------------------------------------------------ checking

        [Fact]
        public async Task A_newer_release_is_offered_with_a_verifiable_installer()
        {
            using var s = Service(new FakeGitHub(_ => Json(ReleaseJson("v1.2.0"))));
            var check = await s.CheckAsync();
            Assert.Equal(UpdateOutcome.Available, check.Outcome);
            Assert.Equal(new Version(1, 2, 0), check.Release.Version);
            Assert.True(check.Release.CanInstall);
            Assert.Equal(Sha(InstallerBytes), check.Release.Installer.Sha256);
            Assert.Equal($"https://github.com/{Repo}/releases/tag/v1.2.0", check.Release.PageUrl);
        }

        [Theory]
        [InlineData("1.2.0")]     // same
        [InlineData("1.3.0")]     // running a newer build than the release
        public async Task The_same_or_an_older_release_is_not_an_update(string current)
        {
            using var s = Service(new FakeGitHub(_ => Json(ReleaseJson("v1.2.0"))), current);
            Assert.Equal(UpdateOutcome.UpToDate, (await s.CheckAsync()).Outcome);
        }

        [Fact]
        public async Task The_check_only_asks_the_github_api_without_credentials()
        {
            var fake = new FakeGitHub(r => { Assert.Null(r.Headers.Authorization); return Json(ReleaseJson()); });
            using var s = Service(fake);
            await s.CheckAsync();
            Assert.Equal(new[] { new Uri($"https://api.github.com/repos/{Repo}/releases/latest") }, fake.Requests);
        }

        [Theory]
        [InlineData(true, false)]
        [InlineData(false, true)]
        public async Task Drafts_and_prereleases_are_ignored(bool draft, bool prerelease)
        {
            using var s = Service(new FakeGitHub(_ => Json(ReleaseJson("v9.0.0", draft: draft, prerelease: prerelease))));
            var check = await s.CheckAsync();
            Assert.NotEqual(UpdateOutcome.Available, check.Outcome);
        }

        [Fact]
        public async Task A_release_without_a_published_checksum_is_offered_but_cannot_be_installed()
        {
            using var s = Service(new FakeGitHub(_ => Json(ReleaseJson(digest: null))));
            var check = await s.CheckAsync();
            Assert.Equal(UpdateOutcome.Available, check.Outcome);
            Assert.False(check.Release.CanInstall);
            await Assert.ThrowsAsync<UpdateException>(() => s.DownloadAsync(check.Release));
        }

        [Theory]
        [InlineData("sha256:zz")]
        [InlineData("md5:d41d8cd98f00b204e9800998ecf8427e")]
        [InlineData("sha256:abc")]
        public async Task A_malformed_digest_counts_as_no_digest(string digest)
        {
            using var s = Service(new FakeGitHub(_ => Json(ReleaseJson(digest: digest))));
            Assert.False((await s.CheckAsync()).Release.CanInstall);
        }

        public static IEnumerable<object[]> BadAssets() => new[]
        {
            new object[] { "name that is not ours", "HapticGamingBridge-Setup-1.2.0.exe.evil" },
            new object[] { "path in the name", @"..\..\evil-1.2.0.exe" },
            new object[] { "wrong version in the name", "HapticGamingBridge-Setup-1.1.0.exe" },
        };

        [Theory]
        [MemberData(nameof(BadAssets))]
        public async Task An_asset_with_an_unexpected_name_is_not_an_installer(string why, string name)
        {
            using var s = Service(new FakeGitHub(_ => Json(ReleaseJson(assetName: name))));
            Assert.Null((await s.CheckAsync()).Release?.Installer);
            Assert.True(why.Length > 0);
        }

        [Theory]
        [InlineData("http://github.com/giovannimazza/haptic-bridge/releases/download/v1.2.0/HapticGamingBridge-Setup-1.2.0.exe")]
        [InlineData("https://evil.example/giovannimazza/haptic-bridge/releases/download/v1.2.0/HapticGamingBridge-Setup-1.2.0.exe")]
        [InlineData("https://github.com/someone-else/haptic-bridge/releases/download/v1.2.0/HapticGamingBridge-Setup-1.2.0.exe")]
        [InlineData("https://github.com/giovannimazza/haptic-bridge/releases/download/v1.1.0/HapticGamingBridge-Setup-1.2.0.exe")]
        [InlineData("https://github.com.evil.example/giovannimazza/haptic-bridge/releases/download/v1.2.0/HapticGamingBridge-Setup-1.2.0.exe")]
        [InlineData("https://user@github.com/giovannimazza/haptic-bridge/releases/download/v1.2.0/HapticGamingBridge-Setup-1.2.0.exe")]
        [InlineData(" https://github.com/giovannimazza/haptic-bridge/releases/download/v1.2.0/HapticGamingBridge-Setup-1.2.0.exe")]
        public async Task A_download_address_that_is_not_this_projects_release_asset_is_dropped(string url)
        {
            using var s = Service(new FakeGitHub(_ => Json(ReleaseJson(url: url))));
            Assert.Null((await s.CheckAsync()).Release?.Installer);
        }

        [Theory]
        [InlineData(0L)]
        [InlineData(1000L)]
        [InlineData(-5L)]
        [InlineData(500L * 1024 * 1024)]
        public async Task An_implausible_size_is_refused(long size)
        {
            using var s = Service(new FakeGitHub(_ => Json(ReleaseJson(size: size))));
            Assert.Null((await s.CheckAsync()).Release?.Installer);
        }

        [Fact]
        public async Task Release_notes_are_shortened_and_a_foreign_page_link_is_replaced()
        {
            var json = JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["tag_name"] = "v1.2.0", ["body"] = new string('x', 50_000), ["html_url"] = "https://evil.example/phish", ["assets"] = Array.Empty<object>(),
            });
            using var s = Service(new FakeGitHub(_ => Json(json)));
            var release = (await s.CheckAsync()).Release;
            Assert.True(release.Notes.Length <= UpdateService.MaxNotesLength + 1);
            Assert.Equal(Links.Releases, release.PageUrl);
        }

        [Theory]
        [InlineData("not json")]
        [InlineData("[]")]
        [InlineData("{}")]
        [InlineData("{\"tag_name\": 5}")]
        [InlineData("{\"tag_name\": \"nightly\"}")]
        public async Task Unreadable_answers_give_a_failure_not_an_exception(string body)
        {
            using var s = Service(new FakeGitHub(_ => Json(body)));
            var check = await s.CheckAsync();
            Assert.Equal(UpdateOutcome.Failed, check.Outcome);
            Assert.False(string.IsNullOrWhiteSpace(check.Message));
        }

        [Theory]
        [InlineData(HttpStatusCode.InternalServerError)]
        [InlineData(HttpStatusCode.NotFound)]
        [InlineData(HttpStatusCode.Forbidden)]            // rate limit
        [InlineData(HttpStatusCode.TooManyRequests)]
        public async Task Server_errors_give_a_failure_with_a_readable_message(HttpStatusCode code)
        {
            using var s = Service(new FakeGitHub(_ => Json("{}", code)));
            var check = await s.CheckAsync();
            Assert.Equal(UpdateOutcome.Failed, check.Outcome);
            Assert.DoesNotContain("Exception", check.Message);
        }

        [Fact]
        public async Task Being_offline_gives_a_failure_not_an_exception()
        {
            using var s = Service(new FakeGitHub(_ => throw new HttpRequestException("no network")));
            Assert.Equal(UpdateOutcome.Failed, (await s.CheckAsync()).Outcome);
        }

        [Fact]
        public async Task A_server_that_never_answers_times_out()
        {
            using var s = Service(new Hang());
            s.ApiTimeout = TimeSpan.FromMilliseconds(200);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var check = await s.CheckAsync();
            Assert.Equal(UpdateOutcome.Failed, check.Outcome);
            Assert.True(sw.ElapsedMilliseconds < 5000);
        }

        [Fact]
        public async Task Cancelling_the_check_cancels_it()
        {
            using var s = Service(new Hang());
            using var cts = new CancellationTokenSource(100);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => s.CheckAsync(cts.Token));
        }

        [Fact]
        public async Task An_oversized_api_answer_is_refused()
        {
            using var s = Service(new FakeGitHub(_ => Json("{\"tag_name\":\"v1.2.0\",\"body\":\"" + new string('a', 2_000_000) + "\"}")));
            Assert.Equal(UpdateOutcome.Failed, (await s.CheckAsync()).Outcome);
        }

        // ------------------------------------------------------------------ downloading

        private static ReleaseInfo Release(string tag = "v1.2.0", string sha = null, long? size = null)
        {
            var name = $"HapticGamingBridge-Setup-{tag.TrimStart('v')}.exe";
            return new ReleaseInfo(tag, new Version(tag.TrimStart('v')), tag, "", Links.Releases,
                new ReleaseAsset(name, $"https://github.com/{Repo}/releases/download/{tag}/{name}", size ?? InstallerBytes.Length, sha ?? Sha(InstallerBytes)));
        }

        private static HttpResponseMessage File200(byte[] bytes, bool length = true)
        {
            var r = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
            if (!length) r.Content.Headers.ContentLength = null;
            return r;
        }

        private static HttpResponseMessage Redirect(string to, HttpStatusCode code = HttpStatusCode.Found)
        {
            var r = new HttpResponseMessage(code);
            r.Headers.Location = new Uri(to, UriKind.RelativeOrAbsolute);
            return r;
        }

        private static void Cleanup(string path) { try { Directory.Delete(Path.GetDirectoryName(path), true); } catch { } }

        [Fact]
        public async Task A_matching_download_is_returned_and_progress_reaches_one()
        {
            var fake = new FakeGitHub(r => r.RequestUri.Host == "github.com" ? Redirect("https://release-assets.githubusercontent.com/some/path") : File200(InstallerBytes));
            using var s = Service(fake);
            var progress = new List<double>();
            var path = await s.DownloadAsync(Release(), new Progress<double>(progress.Add));
            try
            {
                Assert.Equal(InstallerBytes, await File.ReadAllBytesAsync(path));
                Assert.True(UpdateInstaller.IsSafeInstallerPath(path));
                await Task.Delay(100);   // Progress<T> reports on a thread-pool callback
                Assert.Equal(1.0, progress.Max(), 3);
                Assert.Equal(new[] { "github.com", "release-assets.githubusercontent.com" }, fake.Requests.Select(u => u.Host));
            }
            finally { Cleanup(path); }
        }

        [Fact]
        public async Task A_download_that_does_not_match_the_published_checksum_is_discarded()
        {
            var tampered = (byte[])InstallerBytes.Clone(); tampered[500_000] ^= 0xFF;
            using var s = Service(new FakeGitHub(_ => File200(tampered)));
            var ex = await Assert.ThrowsAsync<UpdateException>(() => s.DownloadAsync(Release()));
            Assert.Contains("checksum", ex.Message);
            Assert.Empty(LeftoverFolders());
        }

        [Theory]
        [InlineData(1)]      // one byte more than listed
        [InlineData(-1)]     // one byte less
        public async Task A_download_of_the_wrong_size_is_discarded(int delta)
        {
            var bytes = delta > 0 ? InstallerBytes.Concat(new byte[] { 0 }).ToArray() : InstallerBytes[..^1];
            using var s = Service(new FakeGitHub(_ => File200(bytes, length: false)));   // no Content-Length: the stream length decides
            await Assert.ThrowsAsync<UpdateException>(() => s.DownloadAsync(Release()));
            Assert.Empty(LeftoverFolders());
        }

        [Fact]
        public async Task A_download_far_larger_than_listed_is_cut_off_instead_of_filling_the_disk()
        {
            var source = new CountingStream(new byte[InstallerBytes.Length + 20_000_000]);
            using var s = Service(new FakeGitHub(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(source) }));
            await Assert.ThrowsAsync<UpdateException>(() => s.DownloadAsync(Release()));
            Assert.True(source.Served <= InstallerBytes.Length + 200_000, $"{source.Served} bytes were read although {InstallerBytes.Length} were listed");
            Assert.Empty(LeftoverFolders());
        }

        [Fact]
        public async Task A_declared_length_that_disagrees_with_the_release_is_refused_before_reading()
        {
            using var s = Service(new FakeGitHub(_ => File200(InstallerBytes[..^10])));
            await Assert.ThrowsAsync<UpdateException>(() => s.DownloadAsync(Release()));
        }

        [Theory]
        [InlineData("https://evil.example/installer.exe")]
        [InlineData("http://release-assets.githubusercontent.com/x")]                // not https
        [InlineData("https://githubusercontent.com.evil.example/x")]
        [InlineData("https://user@release-assets.githubusercontent.com/x")]
        [InlineData("https://release-assets.githubusercontent.com:8443/x")]
        [InlineData("ftp://github.com/x")]
        public async Task A_redirect_to_a_host_that_is_not_allowed_is_never_followed(string target)
        {
            var fake = new FakeGitHub(_ => Redirect(target));
            using var s = Service(fake);
            await Assert.ThrowsAsync<UpdateException>(() => s.DownloadAsync(Release()));
            Assert.Single(fake.Requests);                    // only the first hop was requested
            Assert.Empty(LeftoverFolders());
        }

        [Fact]
        public async Task A_relative_redirect_stays_on_the_same_allowed_host()
        {
            var calls = 0;
            using var s = Service(new FakeGitHub(r => calls++ == 0 ? Redirect("/giovannimazza/haptic-bridge/elsewhere") : File200(InstallerBytes)));
            var path = await s.DownloadAsync(Release());
            Cleanup(path);
        }

        [Fact]
        public async Task A_redirect_loop_is_stopped()
        {
            var fake = new FakeGitHub(_ => Redirect("https://github.com/loop"));
            using var s = Service(fake);
            await Assert.ThrowsAsync<UpdateException>(() => s.DownloadAsync(Release()));
            Assert.True(fake.Requests.Count <= UpdateService.MaxRedirects + 1);
        }

        [Theory]
        [InlineData(HttpStatusCode.NotFound)]
        [InlineData(HttpStatusCode.InternalServerError)]
        public async Task An_http_error_while_downloading_is_a_clean_failure(HttpStatusCode code)
        {
            using var s = Service(new FakeGitHub(_ => new HttpResponseMessage(code)));
            await Assert.ThrowsAsync<UpdateException>(() => s.DownloadAsync(Release()));
            Assert.Empty(LeftoverFolders());
        }

        [Fact]
        public async Task A_release_object_pointing_somewhere_else_is_refused_even_if_built_by_hand()
        {
            var evil = Release() with { Installer = Release().Installer with { Url = "https://evil.example/HapticGamingBridge-Setup-1.2.0.exe" } };
            using var s = Service(new FakeGitHub(_ => File200(InstallerBytes)));
            await Assert.ThrowsAsync<UpdateException>(() => s.DownloadAsync(evil));
        }

        // ------------------------------------------------------------------ signatures

        [Fact]
        public async Task A_bad_signature_is_refused_even_when_the_checksum_matches()
        {
            using var s = Service(new FakeGitHub(_ => File200(InstallerBytes)), sig: _ => SignatureStatus.Invalid);
            await Assert.ThrowsAsync<UpdateException>(() => s.DownloadAsync(Release()));
            Assert.Empty(LeftoverFolders());
        }

        [Fact]
        public async Task An_unsigned_installer_is_accepted_by_default_and_refused_when_signatures_are_required()
        {
            using (var lax = Service(new FakeGitHub(_ => File200(InstallerBytes)), sig: _ => SignatureStatus.NotSigned))
                Cleanup(await lax.DownloadAsync(Release()));
            using var strict = Service(new FakeGitHub(_ => File200(InstallerBytes)), sig: _ => SignatureStatus.NotSigned, policy: new UpdatePolicy(RequireSignature: true));
            await Assert.ThrowsAsync<UpdateException>(() => strict.DownloadAsync(Release()));
        }

        [Fact]
        public async Task A_valid_signature_is_accepted()
        {
            using var s = Service(new FakeGitHub(_ => File200(InstallerBytes)), sig: _ => SignatureStatus.Valid, policy: new UpdatePolicy(RequireSignature: true));
            Cleanup(await s.DownloadAsync(Release()));
        }

        // Update folders this test created and did not clean up.
        private string[] LeftoverFolders()
            => Directory.GetDirectories(Path.GetTempPath(), UpdateInstaller.TempFolderPrefix + "*").Except(_foldersBefore).ToArray();
    }
}
