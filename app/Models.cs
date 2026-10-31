using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Windows.Media;

namespace HapticGamingBridge
{
    public abstract class Notify : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        protected void Raise([CallerMemberName] string name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class Rule
    {
        public string Input { get; set; } = "LMB";          // LMB, RMB, MMB, X1, X2 or a WPF Key name
        public string Event { get; set; } = "recoil_1";     // must exist in the plugin's DefaultEventSource.yaml
        public int CooldownMs { get; set; } = 75;           // felt natural by ear with Repeat; the mouse merges/drops fast events
        public bool Repeat { get; set; }                    // keep firing while the input is held
        public string Modifier { get; set; } = "";          // "", Ctrl, Shift, Alt, LMB, RMB

        [JsonIgnore] public string TriggerLabel => Events.TriggerLabel(Input);
        [JsonIgnore] public string ModifierLabel => Events.ModifierLabel(Modifier);
    }

    public class Profile : Notify
    {
        private string _name = "New profile", _exe = "";
        private bool _active, _running, _enabled = true;

        public string Name { get => _name; set { _name = value; Raise(); } }

        // Full path of the game executable. Matching uses the file name only, so versioned install folders keep working.
        public string Exe { get => _exe; set { _exe = value; Raise(); Raise(nameof(Icon)); } }

        public ObservableCollection<Rule> Rules { get; set; } = new();

        // A disabled profile is kept but never triggers anything. Files from older versions have no value: they stay enabled.
        public bool Enabled { get => _enabled; set { if (_enabled != value) { _enabled = value; Raise(); } } }

        [JsonIgnore] public bool IsActive { get => _active; set { if (_active != value) { _active = value; Raise(); } } }
        [JsonIgnore] public bool IsRunning { get => _running; set { if (_running != value) { _running = value; Raise(); } } }
        [JsonIgnore] public ImageSource Icon => IconCache.Get(Exe);
        public override string ToString() => Name;
    }

    public static class ProfileNames
    {
        public const int MaxLength = 60;

        // A tidy display name, or null when nothing usable is left: control characters and runs of whitespace become one
        // space, the ends are trimmed and the length is capped.
        public static string Clean(string name)
        {
            if (name == null) return null;
            var s = Regex.Replace(name, @"[\p{Cc}\s]+", " ").Trim();
            if (s.Length > MaxLength) s = s[..MaxLength].TrimEnd();
            return s.Length == 0 ? null : s;
        }

        // "Change app" renames the profile after the new program only when its name was just the old program's name
        // (or the placeholder); a name the user chose is kept.
        public static string AfterChangingApp(string currentName, string oldExe, string newDefaultName)
        {
            var oldDefault = Path.GetFileNameWithoutExtension(oldExe ?? "");
            var current = (currentName ?? "").Trim();
            var wasDefault = current == "" || current == "New profile" || string.Equals(current, oldDefault, StringComparison.OrdinalIgnoreCase);
            return wasDefault ? newDefaultName : currentName;
        }
    }

    public static class ProfileMatcher
    {
        // The profile that should react to the program in the foreground, or null. Matching is by file name so versioned
        // install folders keep working, and disabled profiles are skipped.
        public static Profile Find(IEnumerable<Profile> profiles, string foregroundPath)
        {
            var name = Path.GetFileName(foregroundPath ?? "");
            if (name == "") return null;
            return profiles.FirstOrDefault(p => p.Enabled && string.Equals(Path.GetFileName(p.Exe ?? ""), name, StringComparison.OrdinalIgnoreCase));
        }
    }

    public record PatternInfo(string Id, string Label);
    public record ModifierInfo(string Id, string Label);

    // Keep Patterns in sync with HapticGamingBridgePlugin/src/package/events/DefaultEventSource.yaml.
    public static class Events
    {
        public static readonly PatternInfo[] Patterns =
        {
            new("recoil_1", "Kick - Sharp hit"),
            new("recoil_2", "Thump - Heavy hit"),
            new("interact_1", "Chime - Bright tick"),
            new("interact_2", "Click - Quick confirm"),
            new("move", "Step - Light tap"),
            new("alert", "Buzz - Warning pulse"),
            new("done", "Success - Finished"),
            new("knock", "Knock - Double rap"),
        };

        public static readonly ModifierInfo[] Modifiers =
        {
            new("", "None"), new("Shift", "Shift"), new("Ctrl", "Control"), new("Alt", "Alt"),
            new("LMB", "Left Mouse"), new("RMB", "Right Mouse"),
        };

        private static readonly HashSet<string> MouseInputs = new() { "LMB", "RMB", "MMB", "X1", "X2" };
        public static bool IsMouse(string input) => input != null && MouseInputs.Contains(input);

        public static string TriggerLabel(string input) => input switch
        {
            "LMB" => "Left Click", "RMB" => "Right Click", "MMB" => "Middle Click",
            "X1" => "Mouse Back", "X2" => "Mouse Forward", _ => input
        };

        public static string ModifierLabel(string id) => Array.Find(Modifiers, m => m.Id == (id ?? ""))?.Label ?? id;
    }
}
