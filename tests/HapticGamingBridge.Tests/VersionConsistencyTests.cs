using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using HapticGamingBridge;
using Xunit;

namespace HapticGamingBridge.Tests
{
    // The version shown in the app, set on the installer and described in the changelog must be the same number,
    // otherwise the Info tab shows a stale version.
    public class VersionConsistencyTests
    {
        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "app", "app.csproj"))) dir = dir.Parent;
            return dir?.FullName ?? throw new InvalidOperationException("repository root not found");
        }

        private static string ProjectVersion() =>
            XDocument.Load(Path.Combine(RepoRoot(), "app", "app.csproj")).Descendants("Version").First().Value;

        [Fact]
        public void The_installer_uses_the_same_version_as_the_app_project()
        {
            var iss = File.ReadAllText(Path.Combine(RepoRoot(), "installer", "HapticGamingBridge.iss"));
            var match = Regex.Match(iss, @"#define AppVersion ""([^""]+)""");
            Assert.True(match.Success, "no default AppVersion in the installer script");
            Assert.Equal(ProjectVersion(), match.Groups[1].Value);
        }

        [Fact]
        public void The_built_app_reports_the_project_version()
        {
            var shown = FileVersionInfo.GetVersionInfo(typeof(Engine).Assembly.Location).ProductVersion;
            Assert.Equal(ProjectVersion(), shown);
        }

        [Fact]
        public void The_changelog_has_a_section_for_the_current_version()
        {
            var changelog = File.ReadAllText(Path.Combine(RepoRoot(), "CHANGELOG.md"));
            Assert.Matches(@"(?m)^## " + Regex.Escape(ProjectVersion()) + @"\b", changelog);
        }

        [Fact]
        public void The_version_looks_like_major_minor_patch()
            => Assert.Matches(@"^\d+\.\d+\.\d+$", ProjectVersion());
    }
}
