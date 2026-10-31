using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace HapticGamingBridge
{
    // Dark title bar matching the app header; Windows 11 only for the custom colors, older builds just get the dark mode.
    public static class TitleBar
    {
        [DllImport("dwmapi")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        public static void Dark(System.Windows.Window w)
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(w).Handle;
            int on = 1, caption = 0x1A1310, text = 0xF5EDEA, border = 0x3A2A24;   // COLORREF is 0x00BBGGRR
            DwmSetWindowAttribute(hwnd, 20, ref on, 4);        // DWMWA_USE_IMMERSIVE_DARK_MODE
            DwmSetWindowAttribute(hwnd, 35, ref caption, 4);   // DWMWA_CAPTION_COLOR (#10131A)
            DwmSetWindowAttribute(hwnd, 36, ref text, 4);      // DWMWA_TEXT_COLOR
            DwmSetWindowAttribute(hwnd, 34, ref border, 4);    // DWMWA_BORDER_COLOR
        }
    }

    // Global low-level keyboard/mouse hooks plus foreground-process lookup.
    public class InputHook : IDisposable
    {
        private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);
        [DllImport("user32")] private static extern IntPtr SetWindowsHookEx(int id, HookProc fn, IntPtr mod, uint thread);
        [DllImport("user32")] private static extern bool UnhookWindowsHookEx(IntPtr h);
        [DllImport("user32")] private static extern IntPtr CallNextHookEx(IntPtr h, int code, IntPtr w, IntPtr l);
        [DllImport("user32")] private static extern short GetAsyncKeyState(int vk);
        [DllImport("user32")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("kernel32")] private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("kernel32")] private static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32")] private static extern bool QueryFullProcessImageName(IntPtr h, uint flags, StringBuilder sb, ref int size);

        private readonly HookProc _kb, _ms;   // fields keep the delegates alive
        private readonly IntPtr _kbHook, _msHook;

        public event Action<string, bool> Input;

        public InputHook()
        {
            _kb = KeyboardProc; _ms = MouseProc;
            var mod = Process.GetCurrentProcess().MainModule.BaseAddress;
            _kbHook = SetWindowsHookEx(13, _kb, mod, 0);
            _msHook = SetWindowsHookEx(14, _ms, mod, 0);
            if (_kbHook == IntPtr.Zero || _msHook == IntPtr.Zero) throw new InvalidOperationException("Cannot install input hooks");
        }

        private IntPtr KeyboardProc(int code, IntPtr w, IntPtr l)
        {
            if (code >= 0)
            {
                int msg = (int)w, vk = Marshal.ReadInt32(l);
                bool down = msg == 0x100 || msg == 0x104, up = msg == 0x101 || msg == 0x105;
                if (down || up) Input?.Invoke(System.Windows.Input.KeyInterop.KeyFromVirtualKey(vk).ToString(), down);
            }
            return CallNextHookEx(_kbHook, code, w, l);
        }

        private IntPtr MouseProc(int code, IntPtr w, IntPtr l)
        {
            if (code >= 0)
            {
                int msg = (int)w;
                string name = msg switch
                {
                    0x201 or 0x202 => "LMB", 0x204 or 0x205 => "RMB", 0x207 or 0x208 => "MMB",
                    0x20B or 0x20C => (Marshal.ReadInt32(l, 8) >> 16) == 1 ? "X1" : "X2",   // MSLLHOOKSTRUCT.mouseData high word
                    _ => null
                };
                if (name != null) Input?.Invoke(name, msg is 0x201 or 0x204 or 0x207 or 0x20B);
            }
            return CallNextHookEx(_msHook, code, w, l);
        }

        public static bool ModifierHeld(string m) => m switch
        {
            "Ctrl" => (GetAsyncKeyState(0x11) & 0x8000) != 0,
            "Shift" => (GetAsyncKeyState(0x10) & 0x8000) != 0,
            "Alt" => (GetAsyncKeyState(0x12) & 0x8000) != 0,
            "LMB" => (GetAsyncKeyState(0x01) & 0x8000) != 0,
            "RMB" => (GetAsyncKeyState(0x02) & 0x8000) != 0,
            _ => false
        };

        // Full path of the foreground process, or "" when it cannot be read (e.g. an elevated game).
        public static string ForegroundExe()
        {
            GetWindowThreadProcessId(GetForegroundWindow(), out var pid);
            var h = OpenProcess(0x1000, false, pid);   // PROCESS_QUERY_LIMITED_INFORMATION
            if (h == IntPtr.Zero) return "";
            try
            {
                var sb = new StringBuilder(1024); int n = sb.Capacity;
                return QueryFullProcessImageName(h, 0, sb, ref n) ? sb.ToString() : "";
            }
            finally { CloseHandle(h); }
        }

        public void Dispose()
        {
            UnhookWindowsHookEx(_kbHook); UnhookWindowsHookEx(_msHook);
        }
    }
}
