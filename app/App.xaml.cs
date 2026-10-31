using System;
using System.IO;
using System.Windows;

namespace HapticBridge
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // WinExe has no console, so the self-check reports through the exit code and a temp file.
            if (Array.IndexOf(e.Args, "--selftest") >= 0)
            {
                try { Engine.SelfTest(); File.WriteAllText(Path.Combine(Path.GetTempPath(), "haptic-bridge-selftest.txt"), "ok"); Shutdown(0); }
                catch (Exception ex) { File.WriteAllText(Path.Combine(Path.GetTempPath(), "haptic-bridge-selftest.txt"), ex.Message); Shutdown(1); }
                return;
            }

            new MainWindow().Show();
        }
    }
}
