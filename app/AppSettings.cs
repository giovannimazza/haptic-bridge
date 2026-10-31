using System;
using System.IO;
using System.Text.Json;

namespace HapticGamingBridge
{
    // Preferences that are not part of any profile. Stored next to profiles.json.
    public class AppSettings
    {
        // Ask GitHub for a newer release when the app starts and every few hours. Turning this off stops all network use.
        public bool CheckForUpdates { get; set; } = true;

        // Download and install a newer release by itself when the app starts. Off by default: the app only offers the update.
        public bool InstallUpdatesAutomatically { get; set; }

        // A version the user chose to ignore ("Skip this version").
        public string SkippedVersion { get; set; } = "";

        public DateTime? LastCheckUtc { get; set; }
    }

    public static class SettingsStore
    {
        private const long MaxBytes = 64 * 1024;

        // Never throws: a missing, unreadable or hostile file gives the defaults.
        public static AppSettings Load(string path)
        {
            try
            {
                if (!File.Exists(path) || new FileInfo(path).Length > MaxBytes) return new AppSettings();
                return Sanitize(JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)));
            }
            catch { return new AppSettings(); }
        }

        public static AppSettings Sanitize(AppSettings s)
        {
            s ??= new AppSettings();
            if (UpdateService.ParseVersion(s.SkippedVersion) == null) s.SkippedVersion = "";
            if (s.LastCheckUtc is DateTime t && t > DateTime.UtcNow.AddDays(1)) s.LastCheckUtc = null;
            return s;
        }

        // Written to a temp file and swapped in, like profiles.json.
        public static void Save(string path, AppSettings settings)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tmp, path, overwrite: true);
        }
    }
}
