using System.Text.RegularExpressions;
using Xunit;

namespace HapticGamingBridge.Tests
{
    // The site in /site is static pages served with a strict Content-Security-Policy. These tests keep the pages inside it
    // (a page that breaks the policy still looks fine in a text editor and only fails in the browser), keep the English and
    // Italian pages in step, and keep every outgoing link on a short list of hosts.
    public class SiteTests
    {
        private static string Root()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "site"))) dir = dir.Parent;
            return Path.Combine(dir?.FullName ?? throw new InvalidOperationException("repository root not found"), "site");
        }

        private static readonly string[] Pages = { "index.html", Path.Combine("it", "index.html") };
        private static string Html(string page) => File.ReadAllText(Path.Combine(Root(), page));
        public static IEnumerable<object[]> PageData() => Pages.Select(p => new object[] { p });

        [Theory, MemberData(nameof(PageData))]
        public void Pages_have_no_inline_styles_scripts_or_event_handlers(string page)
        {
            var html = Html(page);
            Assert.DoesNotMatch(@"\sstyle\s*=", html);                                   // blocked by style-src 'self'
            Assert.DoesNotMatch(@"<style[\s>]", html);
            Assert.DoesNotMatch(@"\son[a-z]+\s*=", html);                                  // onclick, onload ...
            Assert.DoesNotMatch(@"javascript:", html);
            foreach (Match m in Regex.Matches(html, @"<script\b[^>]*>", RegexOptions.IgnoreCase))
                Assert.Matches(@"\bsrc\s*=\s*""[^""]+""", m.Value);                      // only external files from this site
        }

        [Theory, MemberData(nameof(PageData))]
        public void Pages_load_their_files_only_from_the_site_itself(string page)
        {
            // Tags that make the browser fetch something by themselves. Links the visitor clicks (<a>) are checked below.
            var tags = Regex.Matches(Html(page), @"<(?:link|script|img|source|iframe|embed|object|video|audio|form)\b[^>]*>", RegexOptions.IgnoreCase);
            Assert.True(tags.Count >= 5, "the check found almost no tags to look at, so it would pass without checking anything");
            foreach (Match tag in tags)
                foreach (Match attr in Regex.Matches(tag.Value, @"\b(?:src|href|action|data)\s*=\s*""([^""]*)"""))
                {
                    var url = attr.Groups[1].Value;
                    Assert.False(url.StartsWith("http", StringComparison.OrdinalIgnoreCase) || url.StartsWith("//"), $"{tag.Value} would load from another site");
                }
        }

        [Theory, MemberData(nameof(PageData))]
        public void Outgoing_links_go_only_to_the_projects_github_and_the_donation_page(string page)
        {
            foreach (Match m in Regex.Matches(Html(page), @"href\s*=\s*""(https?://[^""]+)"""))
            {
                var uri = new Uri(m.Groups[1].Value);
                Assert.Equal(Uri.UriSchemeHttps, uri.Scheme);
                Assert.True(
                    (uri.Host == "github.com" && uri.AbsolutePath.StartsWith("/giovannimazza/haptic-bridge")) ||
                    (uri.Host == "ko-fi.com" && uri.AbsolutePath == "/giovanbattistamazza"),
                    $"unexpected link {uri}");
            }
        }

        [Theory, MemberData(nameof(PageData))]
        public void Pages_are_accessible_in_the_basics(string page)
        {
            var html = Html(page);
            Assert.Matches(@"<html lang=""(en|it)""", html);
            Assert.Contains("name=\"viewport\"", html);
            foreach (Match img in Regex.Matches(html, @"<img\b[^>]*>"))
            {
                Assert.Matches(@"\balt\s*=", img.Value);
                Assert.Matches(@"\bwidth\s*=", img.Value);                               // reserved space: no layout jump
                Assert.Matches(@"\bheight\s*=", img.Value);
            }
            Assert.Single(Regex.Matches(html, @"<h1[\s>]"));
            Assert.Contains("class=\"skip\"", html);
            Assert.Contains("id=\"main\"", html);
        }

        [Fact]
        public void The_policy_in_the_pages_matches_the_one_the_server_sends()
        {
            var conf = File.ReadAllText(Path.Combine(Root(), "nginx.conf"));
            var header = Regex.Match(conf, @"add_header Content-Security-Policy ""([^""]+)""").Groups[1].Value;
            Assert.False(string.IsNullOrEmpty(header));
            Assert.DoesNotContain("unsafe-inline", header);
            Assert.DoesNotContain("unsafe-eval", header);
            var withoutAncestors = Regex.Replace(header, @";\s*frame-ancestors[^;]*", "");     // a <meta> policy cannot carry frame-ancestors
            foreach (var page in Pages)
            {
                var meta = Regex.Match(Html(page), @"Content-Security-Policy""\s+content=""([^""]+)""").Groups[1].Value;
                Assert.Equal(withoutAncestors, meta);
            }
        }

        [Fact]
        public void The_english_and_italian_pages_have_the_same_structure()
        {
            string Skeleton(string page)
            {
                var html = Html(page);
                var ids = Regex.Matches(html, @"\bid=""([^""]+)""").Select(m => m.Groups[1].Value).OrderBy(x => x);
                var hooks = Regex.Matches(html, @"\b(data-[a-z-]+)").Select(m => m.Groups[1].Value).OrderBy(x => x);
                var externals = Regex.Matches(html, @"href=""(https?://[^""]+)""").Select(m => m.Groups[1].Value).Distinct().OrderBy(x => x);
                var sections = Regex.Matches(html, @"<section\b").Count;
                var details = Regex.Matches(html, @"<details\b").Count;
                var images = Regex.Matches(html, @"<img\b").Count;
                return string.Join("|", ids) + "#" + string.Join("|", hooks) + "#" + string.Join("|", externals) + $"#{sections}-{details}-{images}";
            }
            Assert.Equal(Skeleton(Pages[0]), Skeleton(Pages[1]));
        }

        [Fact]
        public void Every_file_the_pages_reference_exists()
        {
            foreach (var page in Pages)
            {
                var baseDir = Path.GetDirectoryName(Path.Combine(Root(), page));
                foreach (Match m in Regex.Matches(Html(page), @"\b(?:src|href)\s*=\s*""((?:\.\./)?assets/[^""#?]+)"""))
                    Assert.True(File.Exists(Path.GetFullPath(Path.Combine(baseDir, m.Groups[1].Value))), $"{page} references missing {m.Groups[1].Value}");
            }
        }

        [Fact]
        public void The_script_only_contacts_github_and_never_writes_html_from_what_it_receives()
        {
            var js = File.ReadAllText(Path.Combine(Root(), "assets", "app.js"));
            Assert.Contains("const REPO = 'giovannimazza/haptic-bridge'", js);
            Assert.Contains("https://api.github.com/repos/${REPO}/releases/latest", js);
            Assert.DoesNotMatch(@"innerHTML|outerHTML|insertAdjacentHTML|document\.write|eval\(|new Function", js);
            var hosts = Regex.Matches(js, @"https?://([a-z0-9.\-]+)").Select(m => m.Groups[1].Value).Distinct().ToArray();
            Assert.All(hosts, h => Assert.Contains(h, new[] { "api.github.com", "github.com" }));
        }

        [Fact]
        public void The_server_configuration_serves_only_get_and_head_and_hides_its_version()
        {
            var conf = File.ReadAllText(Path.Combine(Root(), "nginx.conf"));
            Assert.Contains("server_tokens off", conf);
            Assert.Contains("access_log off", conf);
            Assert.Contains("GET|HEAD", conf);
            foreach (var header in new[] { "X-Content-Type-Options", "X-Frame-Options", "Referrer-Policy", "Permissions-Policy" })
                Assert.Contains(header, conf);
        }
    }
}
