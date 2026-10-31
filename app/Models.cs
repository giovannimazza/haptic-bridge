using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
        private bool _active, _running;

        public string Name { get => _name; set { _name = value; Raise(); } }

        // Full path of the game executable. Matching uses the file name only, so versioned install folders keep working.
        public string Exe { get => _exe; set { _exe = value; Raise(); Raise(nameof(Icon)); } }

        public ObservableCollection<Rule> Rules { get; set; } = new();

        [JsonIgnore] public bool IsActive { get => _active; set { if (_active != value) { _active = value; Raise(); } } }
        [JsonIgnore] public bool IsRunning { get => _running; set { if (_running != value) { _running = value; Raise(); } } }
        [JsonIgnore] public ImageSource Icon => IconCache.Get(Exe);
        public override string ToString() => Name;
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
