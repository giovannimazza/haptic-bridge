using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace HapticBridge
{
    public partial class MainWindow : Window
    {
        private static readonly string ProfilesPath =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HapticBridge", "profiles.json");

        private readonly ObservableCollection<Profile> _profiles = new();
        private readonly PipeSender _sender = new();
        private readonly Engine _engine;
        private readonly InputHook _hook;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly System.Windows.Forms.NotifyIcon _tray = new();
        private readonly DispatcherTimer _testTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };
        private bool _capturing, _exiting, _syncing;
        private int _runningTicks;
        private Rule _editing;          // rule being edited, null when adding
        private string _editInput;      // captured trigger for the editor

        private Profile Selected => ProfileList.SelectedItem as Profile;

        public MainWindow()
        {
            InitializeComponent();
            SourceInitialized += (_, _) => TitleBar.Dark(this);
            TypeBox.ItemsSource = new[] { "Keyboard Key", "Mouse Button" };
            PatternBox.ItemsSource = Events.Patterns;
            ModBox.ItemsSource = Events.Modifiers;

            Load();
            ProfileList.ItemsSource = _profiles;

            _engine = new Engine(() => _clock.ElapsedMilliseconds, _sender.Send, InputHook.ModifierHeld);
            _hook = new InputHook();
            _hook.Input += OnInput;

            // The hook only raises events; repeat and profile switching run on timers.
            var tick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            tick.Tick += (_, _) => _engine.Tick();
            tick.Start();
            var fg = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            fg.Tick += (_, _) => RefreshActive();
            fg.Start();
            _testTimer.Tick += (_, _) => FireTest();

            _tray.Icon = new System.Drawing.Icon(Application.GetResourceStream(new Uri("pack://application:,,,/icon.ico")).Stream);
            _tray.Text = "Haptic Bridge";
            _tray.Visible = true;
            _tray.DoubleClick += (_, _) => { Show(); WindowState = WindowState.Normal; Activate(); };
            var menu = new System.Windows.Forms.ContextMenuStrip();
            menu.Items.Add("Open", null, (_, _) => { Show(); Activate(); });
            menu.Items.Add("Exit", null, (_, _) => { _exiting = true; Close(); });
            _tray.ContextMenuStrip = menu;

            Deactivated += (_, _) => Save();
        }

        // ---------------------------------------------------------------- runtime

        private void OnInput(string input, bool down)
        {
            if (_capturing && down)
            {
                // Ignore inputs of the other type so a stray click doesn't end a keyboard capture.
                if (Events.IsMouse(input) != (TypeBox.SelectedIndex == 1)) return;
                _capturing = false;
                _editInput = input;
                CaptureBtn.Content = Events.TriggerLabel(input);
                return;
            }
            _engine.OnInput(input, down);
        }

        private void RefreshActive()
        {
            var fgPath = InputHook.ForegroundExe();
            var fg = Path.GetFileName(fgPath);
            // Match by file name so versioned install folders (app-1.0.9266) keep working.
            var active = fg == "" ? null : _profiles.FirstOrDefault(p => string.Equals(Path.GetFileName(p.Exe), fg, StringComparison.OrdinalIgnoreCase));
            // Profiles saved with only a file name, or whose folder changed, pick up the real path (and icon) here.
            if (active != null && !File.Exists(active.Exe)) { active.Exe = fgPath; Save(); }
            _engine.Active = active;
            foreach (var p in _profiles) p.IsActive = p == active;

            // Process list is comparatively costly, so refresh it every 2 s instead of every tick.
            if (_runningTicks++ % 4 == 0)
            {
                var running = Process.GetProcesses().Select(pr => { var n = pr.ProcessName; pr.Dispose(); return n; })
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var p in _profiles) p.IsRunning = running.Contains(Path.GetFileNameWithoutExtension(p.Exe));
            }

            StatusText.Text = _sender.Connected ? "Plugin connected" : "Plugin: waiting";
            StatusPill.Background = _sender.Connected ? (Brush)FindResource("Green") : (Brush)FindResource("Hover");
        }

        private void EnabledSwitch_Click(object sender, RoutedEventArgs e) => _engine.Enabled = EnabledSwitch.IsChecked == true;

        // ---------------------------------------------------------------- storage

        private void Load()
        {
            try
            {
                if (File.Exists(ProfilesPath))
                    foreach (var p in JsonSerializer.Deserialize<Profile[]>(File.ReadAllText(ProfilesPath)) ?? Array.Empty<Profile>())
                        _profiles.Add(p);
            }
            catch (Exception ex) { DialogWindow.Info(null, "Profiles not readable", "Profiles could not be read, starting empty: " + ex.Message); }
        }

        private void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ProfilesPath));
            File.WriteAllText(ProfilesPath, JsonSerializer.Serialize(_profiles, new JsonSerializerOptions { WriteIndented = true }));
        }

        // ---------------------------------------------------------------- profiles

        private void ProfileList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var p = Selected;
            CloseEditor();   // the side editor belongs to the previous profile
            DetailView.DataContext = p;
            DetailView.Visibility = p == null ? Visibility.Collapsed : Visibility.Visible;
            EmptyState.Visibility = p == null ? Visibility.Visible : Visibility.Collapsed;
        }

        private PickerWindow Pick()
        {
            var dlg = new PickerWindow { Owner = this };
            return dlg.ShowDialog() == true ? dlg : null;
        }

        private void AddProfile_Click(object sender, RoutedEventArgs e)
        {
            if (Pick() is not { } dlg) return;
            var p = new Profile { Name = dlg.ResultName, Exe = dlg.ResultPath };
            _profiles.Add(p);
            ProfileList.SelectedItem = p;
            Save();
        }

        private void ChangeApp_Click(object sender, RoutedEventArgs e)
        {
            if (Selected is not { } p || Pick() is not { } dlg) return;
            p.Name = dlg.ResultName;
            p.Exe = dlg.ResultPath;
            Save();
        }

        private void Duplicate_Click(object sender, RoutedEventArgs e)
        {
            if (Selected is not { } src || Pick() is not { } dlg) return;
            var copy = new Profile { Name = dlg.ResultName, Exe = dlg.ResultPath };
            foreach (var r in src.Rules)
                copy.Rules.Add(new Rule { Input = r.Input, Event = r.Event, CooldownMs = r.CooldownMs, Repeat = r.Repeat, Modifier = r.Modifier });
            _profiles.Add(copy);
            ProfileList.SelectedItem = copy;
            Save();
        }

        private void DeleteProfileRow_Click(object sender, RoutedEventArgs e) => DeleteProfile((Profile)((FrameworkElement)sender).DataContext);

        private void DeleteProfile(Profile p)
        {
            if (p == null) return;
            if (!DialogWindow.Confirm(this, "Delete Profile", $"Are you sure you want to delete the profile '{p.Name}' and all its mappings?", yes: "Delete", no: "Cancel", danger: true)) return;
            _profiles.Remove(p);
            Save();
        }

        // ---------------------------------------------------------------- action rows

        private static Rule RuleOf(object sender) => (Rule)((FrameworkElement)sender).DataContext;

        private void EditAction_Click(object sender, RoutedEventArgs e) => OpenEditor(RuleOf(sender));

        private void RemoveAction_Click(object sender, RoutedEventArgs e)
        {
            Selected?.Rules.Remove(RuleOf(sender));
            Save();
        }

        private void AddAction_Click(object sender, RoutedEventArgs e) => OpenEditor(null);

        // ---------------------------------------------------------------- editor page

        private void OpenEditor(Rule rule)
        {
            _editing = rule;
            _editInput = rule?.Input;
            var src = rule ?? new Rule();
            EditorTitle.Text = rule == null ? "Add New Action" : "Edit Action";
            TypeBox.SelectedIndex = rule != null && Events.IsMouse(rule.Input) ? 1 : 0;
            CaptureBtn.Content = rule == null ? "Click to capture input" : rule.TriggerLabel;
            PatternBox.SelectedValue = src.Event;
            ModBox.SelectedValue = src.Modifier ?? "";
            CooldownBox.Text = src.CooldownMs.ToString();
            HoldBox.IsChecked = src.Repeat;
            EditorView.Visibility = Visibility.Visible;
        }

        private void CloseEditor()
        {
            _capturing = false;
            _testTimer.Stop();
            EditorView.Visibility = Visibility.Collapsed;
        }

        private void EditorBack_Click(object sender, RoutedEventArgs e) => CloseEditor();

        private void TypeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // A captured trigger of the other type is no longer valid.
            if (_editInput != null && Events.IsMouse(_editInput) != (TypeBox.SelectedIndex == 1))
            {
                _editInput = null;
                CaptureBtn.Content = "Click to capture input";
            }
        }

        private void Capture_Click(object sender, RoutedEventArgs e)
        {
            _capturing = true;
            CaptureBtn.Content = TypeBox.SelectedIndex == 1 ? "Press a mouse button..." : "Press a key...";
        }

        private void CooldownSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_syncing) return;
            _syncing = true;
            CooldownBox.Text = ((int)e.NewValue).ToString();
            _syncing = false;
        }

        private void CooldownBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_syncing || !int.TryParse(CooldownBox.Text, out var v)) return;
            _syncing = true;
            CooldownSlider.Value = Math.Clamp(v, 0, 1000);
            _syncing = false;
        }

        private void SaveAction_Click(object sender, RoutedEventArgs e)
        {
            if (Selected is not { } p) return;
            if (_editInput == null) { DialogWindow.Info(this, "Input missing", "Capture an input first."); return; }
            if (!int.TryParse(CooldownBox.Text, out var cd) || cd < 0) cd = 0;

            var rule = new Rule
            {
                Input = _editInput,
                Event = (string)PatternBox.SelectedValue ?? Events.Patterns[0].Id,
                Modifier = (string)ModBox.SelectedValue ?? "",
                CooldownMs = cd,
                Repeat = HoldBox.IsChecked == true
            };
            // Replacing the item (not mutating it) refreshes the row.
            if (_editing != null) p.Rules[p.Rules.IndexOf(_editing)] = rule; else p.Rules.Add(rule);
            Save();
            CloseEditor();
        }

        // Test button: plays the selected pattern, and repeats while the mouse button stays down.
        private void FireTest() { if (PatternBox.SelectedValue is string id) _sender.Send(id); }

        private void Test_Down(object sender, MouseButtonEventArgs e)
        {
            TestBtn.CaptureMouse();
            FireTest();
            _testTimer.Start();
        }

        private void Test_Up(object sender, MouseButtonEventArgs e)
        {
            _testTimer.Stop();
            TestBtn.ReleaseMouseCapture();
        }

        private void Test_Lost(object sender, MouseEventArgs e) => _testTimer.Stop();

        // Closing the window hides it to the tray; "Exit" in the tray menu really quits.
        protected override void OnClosing(CancelEventArgs e)
        {
            Save();
            if (!_exiting) { e.Cancel = true; Hide(); return; }
            _tray.Visible = false;
            _tray.Dispose();
            _hook.Dispose();
            _sender.Dispose();
            base.OnClosing(e);
        }
    }
}
