using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace HapticBridge
{
    public partial class PickerWindow : Window
    {
        private readonly List<RunningApp> _all = RunningApp.Load();

        public string ResultPath { get; private set; }
        public string ResultName { get; private set; }

        public PickerWindow()
        {
            InitializeComponent();
            SourceInitialized += (_, _) => TitleBar.Dark(this);
            AppList.ItemsSource = _all;
        }

        private void Search_TextChanged(object sender, TextChangedEventArgs e)
        {
            var q = Search.Text.Trim();
            AppList.ItemsSource = q == "" ? _all
                : _all.Where(a => a.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || a.Path.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        private void AppList_SelectionChanged(object sender, SelectionChangedEventArgs e) => SelectBtn.IsEnabled = AppList.SelectedItem != null;

        private void AppList_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (AppList.SelectedItem != null) Select_Click(sender, e);
        }

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Executables (*.exe)|*.exe" };
            if (dlg.ShowDialog(this) != true) return;
            Finish(dlg.FileName, Path.GetFileNameWithoutExtension(dlg.FileName));
        }

        private void Select_Click(object sender, RoutedEventArgs e)
        {
            if (AppList.SelectedItem is RunningApp a) Finish(a.Path, a.Name);
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

        private void Finish(string path, string name)
        {
            ResultPath = path; ResultName = name;
            DialogResult = true;
        }
    }
}
