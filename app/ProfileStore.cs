using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace HapticGamingBridge
{
    // Reads and writes profiles.json. The file is user-editable, so everything read from it is treated as untrusted.
    public static class ProfileStore
    {
        public const long MaxFileBytes = 5 * 1024 * 1024;
        public const int MaxCooldownMs = 60_000;
        public const int MaxProfiles = 200, MaxRulesPerProfile = 500, MaxTextLength = 200;

        private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

        // Earlier versions kept profiles in %APPDATA%\HapticBridge; copy them over once. The old file is left in place.
        public static bool Migrate(string oldPath, string newPath)
        {
            if (!File.Exists(oldPath) || File.Exists(newPath)) return false;
            Directory.CreateDirectory(Path.GetDirectoryName(newPath));
            File.Copy(oldPath, newPath);
            return true;
        }

        // Throws on unreadable or malformed files; the caller decides what to tell the user.
        public static List<Profile> Load(string path)
        {
            if (!File.Exists(path)) return new List<Profile>();
            if (new FileInfo(path).Length > MaxFileBytes) throw new InvalidDataException("profiles.json is too large");
            var raw = JsonSerializer.Deserialize<Profile[]>(File.ReadAllText(path)) ?? Array.Empty<Profile>();
            return Sanitize(raw);
        }

        // Writes to a temp file and swaps it in, so a crash mid-write cannot leave a half-written profiles.json.
        public static void Save(string path, IEnumerable<Profile> profiles)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(profiles, Options));
            File.Move(tmp, path, overwrite: true);
        }

        public static List<Profile> Sanitize(IEnumerable<Profile> profiles)
        {
            var knownEvents = Events.Patterns.Select(p => p.Id).ToHashSet();
            var knownModifiers = Events.Modifiers.Select(m => m.Id).ToHashSet();
            var result = new List<Profile>();

            foreach (var p in profiles.Where(p => p != null).Take(MaxProfiles))
            {
                var clean = new Profile { Name = Trim(p.Name, "Profile"), Exe = SafeExe(p.Exe), Enabled = p.Enabled };
                foreach (var r in (p.Rules ?? new()).Where(r => r != null).Take(MaxRulesPerProfile))
                {
                    // Event names go to the plugin over the pipe, so only known ones survive.
                    if (!knownEvents.Contains(r.Event ?? "") || string.IsNullOrWhiteSpace(r.Input) || r.Input.Length > 32) continue;
                    clean.Rules.Add(new Rule
                    {
                        Input = r.Input,
                        Event = r.Event,
                        Modifier = knownModifiers.Contains(r.Modifier ?? "") ? r.Modifier ?? "" : "",
                        CooldownMs = Math.Clamp(r.CooldownMs, 0, MaxCooldownMs),
                        Repeat = r.Repeat
                    });
                }
                result.Add(clean);
            }
            return result;
        }

        // Only a bare file name or a local drive path is kept. A UNC path would make Windows contact that server
        // when the icon is read, which leaks the user's network credentials to it.
        public static string SafeExe(string exe)
        {
            exe = Trim(exe, "");
            if (exe == "") return "";
            var hasSeparator = exe.IndexOfAny(new[] { '\\', '/' }) >= 0;
            if (!hasSeparator) return exe.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ? "" : exe;
            var localDrive = exe.Length > 2 && char.IsAsciiLetter(exe[0]) && exe[1] == ':' && exe[2] is '\\' or '/';
            return localDrive && !exe.StartsWith(@"\\") && exe.IndexOfAny(Path.GetInvalidPathChars()) < 0 ? exe : "";
        }

        private static string Trim(string s, string fallback)
        {
            s = (s ?? "").Trim();
            if (s == "") return fallback;
            return s.Length > MaxTextLength ? s[..MaxTextLength] : s;
        }
    }
}
