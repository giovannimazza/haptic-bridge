using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace HapticGamingBridge
{
    public static class IconCache
    {
        private static readonly Dictionary<string, ImageSource> Cache = new(StringComparer.OrdinalIgnoreCase);

        // Null when the file is missing or has no icon; the UI shows a placeholder tile instead.
        public static ImageSource Get(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            if (Cache.TryGetValue(path, out var cached)) return cached;
            ImageSource src = null;
            try
            {
                using var icon = Icon.ExtractAssociatedIcon(path);
                if (icon != null)
                {
                    src = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    src.Freeze();
                }
            }
            catch { }
            return Cache[path] = src;
        }
    }

    public class RunningApp
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public ImageSource Icon => IconCache.Get(Path);

        // Only apps with a visible window; elevated/protected processes refuse MainModule and are skipped.
        public static List<RunningApp> Load()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var list = new List<RunningApp>();
            foreach (var p in Process.GetProcesses())
            {
                if (p.Id == Environment.ProcessId || p.MainWindowHandle == IntPtr.Zero || p.MainWindowTitle == "") continue;
                string path;
                try { path = p.MainModule.FileName; } catch { continue; }
                if (seen.Add(path)) list.Add(new RunningApp { Name = p.ProcessName, Path = path });
            }
            return list.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }
}
