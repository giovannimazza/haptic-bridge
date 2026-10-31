using System.Windows;
using System.Windows.Media;

namespace HapticBridge
{
    // Dark replacement for MessageBox so prompts match the rest of the app.
    public partial class DialogWindow : Window
    {
        private DialogWindow(string title, string message) { InitializeComponent(); SourceInitialized += (_, _) => TitleBar.Dark(this); Title = title; TitleText.Text = title; MessageText.Text = message; }

        // Returns true when the user confirms. danger = red confirm button for destructive actions.
        public static bool Confirm(Window owner, string title, string message, string yes = "Yes", string no = "No", bool danger = false)
        {
            var d = new DialogWindow(title, message) { Owner = owner };
            d.YesBtn.Content = yes;
            d.NoBtn.Content = no;
            if (danger) d.YesBtn.Style = (Style)Application.Current.FindResource("BtnDanger");
            return d.ShowDialog() == true;
        }

        public static void Info(Window owner, string title, string message)
        {
            var d = new DialogWindow(title, message) { Owner = owner };
            d.NoBtn.Visibility = Visibility.Collapsed;
            d.YesBtn.Content = "OK";
            d.YesBtn.IsCancel = true;
            d.ShowDialog();
        }

        private void Yes_Click(object sender, RoutedEventArgs e) => DialogResult = true;

        private void No_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
