using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace HapticGamingBridge
{
    // Over-the-air updates from this project's GitHub releases. This file is the only place in the app that uses the
    // network, and the only one (besides Links.cs) that starts another program. Everything that comes from the network is
    // untrusted: it is parsed defensively, only allow-listed hosts are contacted, and an installer is never run unless its
    // SHA-256 matches the digest GitHub published for that release asset. See docs/updates.md.

    public record ReleaseAsset(string Name, string Url, long Size, string Sha256);

    public record ReleaseInfo(string Tag, Version Version, string Title, string Notes, string PageUrl, ReleaseAsset Installer)
    {
        // An installer with a published digest can be downloaded and verified; otherwise the user is sent to the release page.
        public bool CanInstall => Installer != null && Installer.Sha256 != null;
    }

    public enum UpdateOutcome { UpToDate, Available, Failed }

    public record UpdateCheck(UpdateOutcome Outcome, ReleaseInfo Release, string Message);

    public class UpdateException : Exception
    {
        public UpdateException(string message) : base(message) { }
    }

    public record UpdatePolicy(bool RequireSignature = false, string SignerContains = null)
    {
        // Releases are not code-signed yet, so an unsigned installer is accepted when its digest matches. Once releases are
        // signed (see docs/code-signing.md) set RequireSignature to true and SignerContains to the certificate's subject.
        public static readonly UpdatePolicy Default = new();
    }

    public sealed class UpdateService : IDisposable
    {
        public const string Repo = "giovannimazza/haptic-bridge";
        public static readonly Uri LatestReleaseApi = new($"https://api.github.com/repos/{Repo}/releases/latest");

        public const long MaxInstallerBytes = 300L * 1024 * 1024;
        public const long MinInstallerBytes = 1L * 1024 * 1024;
        public const int MaxApiBytes = 1_000_000;
        public const int MaxRedirects = 5;
        public const int MaxNotesLength = 4000;

        private static readonly string[] ApiHosts = { "api.github.com" };
        // github.com serves the first hop; GitHub's release storage serves the file.
        private static readonly string[] DownloadHosts =
            { "github.com", "objects.githubusercontent.com", "release-assets.githubusercontent.com", "github-releases.githubusercontent.com" };

        private static readonly Regex InstallerName = new(@"^HapticGamingBridge-Setup-\d{1,4}\.\d{1,4}\.\d{1,4}\.exe$", RegexOptions.CultureInvariant);
        private static readonly Regex Sha256Digest = new(@"^sha256:([0-9a-fA-F]{64})$", RegexOptions.CultureInvariant);

        private readonly HttpClient _http;
        private readonly Func<string, SignatureStatus> _verifySignature;
        private readonly UpdatePolicy _policy;

        public Version Current { get; }
        public TimeSpan ApiTimeout { get; set; } = TimeSpan.FromSeconds(20);

        public UpdateService(HttpMessageHandler handler = null, Version current = null, Func<string, SignatureStatus> verifySignature = null, UpdatePolicy policy = null)
        {
            Current = current ?? CurrentVersion();
            _verifySignature = verifySignature ?? (path => Authenticode.Verify(path));
            _policy = policy ?? UpdatePolicy.Default;
            // Redirects are followed by hand so that every hop can be checked against the allow-list.
            _http = new HttpClient(handler ?? new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false }) { Timeout = TimeSpan.FromMinutes(15) };
            _http.DefaultRequestHeaders.UserAgent.ParseAdd($"HapticGamingBridge/{Current} (+https://github.com/{Repo})");
            _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            _http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        }

        public void Dispose() => _http.Dispose();

        public static Version CurrentVersion()
        {
            var text = FileVersionInfo.GetVersionInfo(Environment.ProcessPath ?? "").ProductVersion;
            return ParseVersion(text) ?? new Version(0, 0, 0);
        }

        // "v1.2.3", "1.2", "1.2.3-beta.1" and "1.2.3+abc" all parse; anything else is null. Always three components.
        public static Version ParseVersion(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var s = text.Trim();
            if (s.Length > 0 && (s[0] == 'v' || s[0] == 'V')) s = s[1..];
            var cut = s.IndexOfAny(new[] { '-', '+' });
            if (cut >= 0) s = s[..cut];
            if (!Regex.IsMatch(s, @"^\d{1,4}(\.\d{1,4}){1,3}$")) return null;
            if (!Version.TryParse(s, out var v)) return null;
            return new Version(v.Major, v.Minor, Math.Max(v.Build, 0));
        }

        // ---------------------------------------------------------------------------------------- checking

        public async Task<UpdateCheck> CheckAsync(CancellationToken ct = default)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(ApiTimeout);
                var json = await GetTextAsync(LatestReleaseApi, timeout.Token);
                var release = ParseRelease(json);
                if (release == null) return new UpdateCheck(UpdateOutcome.UpToDate, null, "No release to compare with.");
                return release.Version > Current
                    ? new UpdateCheck(UpdateOutcome.Available, release, $"Version {release.Version} is available.")
                    : new UpdateCheck(UpdateOutcome.UpToDate, release, $"You have the latest version ({Current}).");
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return Failed("GitHub did not answer in time."); }
            catch (OperationCanceledException) { throw; }
            catch (HttpRequestException) { return Failed("Could not reach GitHub. Check your internet connection."); }
            catch (UpdateException ex) { return Failed(ex.Message); }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException) { return Failed("GitHub sent an answer the app could not read."); }
        }

        private static UpdateCheck Failed(string message) => new(UpdateOutcome.Failed, null, message);

        // Reads the JSON of "latest release". Returns null for drafts and prereleases. Everything is validated: the tag is a
        // version, the installer has the exact expected name and URL, a sane size and (optionally) a SHA-256 digest.
        public static ReleaseInfo ParseRelease(string json)
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new UpdateException("GitHub sent an answer the app could not read.");
            if (Flag(root, "draft") || Flag(root, "prerelease")) return null;

            var tag = Str(root, "tag_name");
            var version = ParseVersion(tag);
            if (version == null) throw new UpdateException("The latest release has a version number the app does not understand.");

            var page = Str(root, "html_url");
            if (page == null || !Links.IsAllowed(page)) page = Links.Releases;
            var notes = Str(root, "body") ?? "";
            if (notes.Length > MaxNotesLength) notes = notes[..MaxNotesLength] + "…";

            ReleaseAsset installer = null;
            if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                var wanted = $"HapticGamingBridge-Setup-{version}.exe";
                foreach (var a in assets.EnumerateArray())
                {
                    if (a.ValueKind != JsonValueKind.Object || Str(a, "name") != wanted || !InstallerName.IsMatch(wanted)) continue;
                    var url = Str(a, "browser_download_url");
                    var size = a.TryGetProperty("size", out var sz) && sz.ValueKind == JsonValueKind.Number && sz.TryGetInt64(out var n) ? n : -1;
                    if (url != url?.Trim() || !IsExpectedAssetUrl(url, tag, wanted) || size < MinInstallerBytes || size > MaxInstallerBytes) continue;
                    var m = Sha256Digest.Match(Str(a, "digest") ?? "");
                    installer = new ReleaseAsset(wanted, url, size, m.Success ? m.Groups[1].Value.ToLowerInvariant() : null);
                    break;
                }
            }
            return new ReleaseInfo(tag, version, Str(root, "name") ?? tag, notes, page, installer);
        }

        // The download must be exactly this project's release asset for this tag.
        public static bool IsExpectedAssetUrl(string url, string tag, string assetName)
            => url == $"https://github.com/{Repo}/releases/download/{tag}/{assetName}";

        private static bool Flag(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
        private static string Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        // ---------------------------------------------------------------------------------------- downloading

        // Downloads the installer into a fresh temp folder and returns its path, or throws UpdateException. The file is only
        // returned if it has the declared size, the published SHA-256 and an acceptable signature; otherwise it is deleted.
        public async Task<string> DownloadAsync(ReleaseInfo release, IProgress<double> progress = null, CancellationToken ct = default)
        {
            if (release?.Installer == null) throw new UpdateException("This release has no installer the app can use.");
            if (release.Installer.Sha256 == null) throw new UpdateException("This release has no published checksum, so the download cannot be verified.");
            var asset = release.Installer;
            if (!InstallerName.IsMatch(asset.Name) || !IsExpectedAssetUrl(asset.Url, release.Tag, asset.Name))
                throw new UpdateException("The installer address is not the one this project publishes.");

            var dir = Path.Combine(Path.GetTempPath(), UpdateInstaller.TempFolderPrefix + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, asset.Name);
            try
            {
                using var response = await SendFollowingAsync(new Uri(asset.Url), DownloadHosts, ct);
                if (response.Content.Headers.ContentLength is long declared && declared != asset.Size)
                    throw new UpdateException("The download is not the size GitHub lists for it.");

                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                long total = 0;
                await using (var source = await response.Content.ReadAsStreamAsync(ct))
                await using (var target = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    var buffer = new byte[81920];
                    int read;
                    while ((read = await source.ReadAsync(buffer, ct)) > 0)
                    {
                        total += read;
                        if (total > asset.Size) throw new UpdateException("The download is larger than GitHub lists for it.");
                        hash.AppendData(buffer, 0, read);
                        await target.WriteAsync(buffer.AsMemory(0, read), ct);
                        progress?.Report((double)total / asset.Size);
                    }
                }
                if (total != asset.Size) throw new UpdateException("The download ended early.");
                if (!string.Equals(Convert.ToHexString(hash.GetHashAndReset()), asset.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new UpdateException("The download does not match the checksum GitHub published, so it was discarded.");

                CheckSignature(path);
                return path;
            }
            catch
            {
                try { Directory.Delete(dir, recursive: true); } catch { }
                throw;
            }
        }

        private void CheckSignature(string path)
        {
            var status = _verifySignature(path);
            if (status == SignatureStatus.Invalid) throw new UpdateException("The installer has a signature that is not valid, so it was discarded.");
            if (status == SignatureStatus.NotSigned && _policy.RequireSignature) throw new UpdateException("The installer is not signed, so it was discarded.");
            if (status == SignatureStatus.Valid && _policy.SignerContains != null && !Authenticode.SignerSubject(path).Contains(_policy.SignerContains, StringComparison.OrdinalIgnoreCase))
                throw new UpdateException("The installer is signed by someone other than expected, so it was discarded.");
        }

        // ---------------------------------------------------------------------------------------- HTTP

        private async Task<string> GetTextAsync(Uri url, CancellationToken ct)
        {
            using var response = await SendFollowingAsync(url, ApiHosts, ct);
            if (response.Content.Headers.ContentLength > MaxApiBytes) throw new UpdateException("GitHub sent an unexpectedly large answer.");
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var buffer = new MemoryStream();
            var chunk = new byte[16384];
            int read;
            while ((read = await stream.ReadAsync(chunk, ct)) > 0)
            {
                if (buffer.Length + read > MaxApiBytes) throw new UpdateException("GitHub sent an unexpectedly large answer.");
                buffer.Write(chunk, 0, read);
            }
            return System.Text.Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
        }

        // GET that follows redirects by hand. Every address, including each redirect target, must be https on an allowed host.
        private async Task<HttpResponseMessage> SendFollowingAsync(Uri url, string[] allowedHosts, CancellationToken ct)
        {
            for (var hop = 0; hop <= MaxRedirects; hop++)
            {
                if (!IsAllowed(url, allowedHosts)) throw new UpdateException("The update tried to use an address that is not allowed.");
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                var code = (int)response.StatusCode;
                if (code is 301 or 302 or 303 or 307 or 308)
                {
                    var target = response.Headers.Location;
                    response.Dispose();
                    if (target == null) throw new UpdateException("The update server sent a redirect without a target.");
                    url = target.IsAbsoluteUri ? target : new Uri(url, target);
                    continue;
                }
                if (!response.IsSuccessStatusCode)
                {
                    response.Dispose();
                    throw new UpdateException(code == 403 || code == 429 ? "GitHub is limiting requests right now. Try again later." : $"GitHub answered with an error ({code}).");
                }
                return response;
            }
            throw new UpdateException("The update server redirected too many times.");
        }

        public static bool IsAllowed(Uri url, string[] allowedHosts)
            => url != null && url.IsAbsoluteUri && url.Scheme == Uri.UriSchemeHttps && url.IsDefaultPort && url.UserInfo.Length == 0
               && allowedHosts.Contains(url.Host, StringComparer.OrdinalIgnoreCase);

        public static bool IsAllowedDownloadHost(Uri url) => IsAllowed(url, DownloadHosts);
    }

    // Starting the downloaded installer, and recognising a copy of the app that was put there by that installer.
    public static class UpdateInstaller
    {
        public const string TempFolderPrefix = "HapticGamingBridge-update-";
        public const string InnoAppId = "{6E1266AA-120C-4A08-90C4-B432667A9CE4}";   // AppId in installer/HapticGamingBridge.iss

        // Silent with a progress window, no reboot, no plugin or shortcut tasks (the existing ones stay), and start the app
        // again when finished.
        public const string Arguments = "/SILENT /NORESTART /SUPPRESSMSGBOXES /TASKS=\"\" /relaunch=1";

        // Only an installer that DownloadAsync put in its own temp folder is ever started.
        public static bool IsSafeInstallerPath(string path, string tempRoot = null)
        {
            try
            {
                var full = Path.GetFullPath(path);
                var root = Path.GetFullPath(tempRoot ?? Path.GetTempPath()).TrimEnd('\\') + "\\";
                var folder = Path.GetFileName(Path.GetDirectoryName(full)) ?? "";
                return full.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                    && folder.StartsWith(TempFolderPrefix, StringComparison.Ordinal)
                    && Regex.IsMatch(Path.GetFileName(full), @"^HapticGamingBridge-Setup-\d{1,4}\.\d{1,4}\.\d{1,4}\.exe$")
                    && Path.GetDirectoryName(Path.GetDirectoryName(full)).TrimEnd('\\').Equals(root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        public static bool Launch(string installerPath, Func<ProcessStartInfo, bool> start = null)
        {
            if (!IsSafeInstallerPath(installerPath)) return false;
            var info = new ProcessStartInfo(installerPath, Arguments) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(installerPath) };
            try
            {
                if (start != null) return start(info);
                return Process.Start(info) != null;
            }
            catch { return false; }
        }

        // True when this exe sits in the folder the installer wrote to, so an update can replace it. A copy run from a build
        // folder or unzipped somewhere is not updated in place.
        public static bool IsInstalledCopy(string appFolder = null, Func<string> readInstallLocation = null)
        {
            try
            {
                var installed = (readInstallLocation ?? ReadInstallLocationFromRegistry)();
                if (string.IsNullOrWhiteSpace(installed)) return false;
                var here = Path.GetFullPath(appFolder ?? AppContext.BaseDirectory).TrimEnd('\\');
                return Path.GetFullPath(installed).TrimEnd('\\').Equals(here, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        private static string ReadInstallLocationFromRegistry()
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey($@"Software\Microsoft\Windows\CurrentVersion\Uninstall\{InnoAppId}_is1");
            return key?.GetValue("Inno Setup: App Path") as string ?? key?.GetValue("InstallLocation") as string;
        }

        // Removes update folders left behind by an earlier run (an update that was started, or one that failed).
        public static void CleanOldDownloads(TimeSpan olderThan)
        {
            try
            {
                foreach (var dir in Directory.EnumerateDirectories(Path.GetTempPath(), TempFolderPrefix + "*"))
                    if (DateTime.UtcNow - Directory.GetLastWriteTimeUtc(dir) > olderThan)
                        try { Directory.Delete(dir, recursive: true); } catch { }
            }
            catch { }
        }
    }
}
