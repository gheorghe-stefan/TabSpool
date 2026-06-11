using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace TabSpool;

internal static class Program
{
    // Win32 Constants
    private const int WH_MOUSE_LL = 14;
    private const int WM_MOUSEWHEEL = 0x020A;
    private const uint GA_ROOT = 2;

    private const byte VK_CONTROL = 0x11;
    private const byte VK_SHIFT = 0x10;
    private const byte VK_TAB = 0x09;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    // Win32 Structures
    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT Pt;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public UIntPtr DwExtraInfo;
    }

    // Win32 APIs
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hWnd, uint gaFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(long point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    // Global settings and state
    private static int _tabBarHeight = 64;
    private static int _excludeRightMargin = 140;
    private static long _scrollCooldownMs = 75;
    private static bool _reverseDirection = false;
    private static long _lastScrollTime = 0;

    private static LowLevelMouseProc? _hookProc;
    private static IntPtr _hookId = IntPtr.Zero;

    private static readonly string ConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.txt");

    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new TabSpoolContext());
    }

    private static void LoadConfig()
    {
        try
        {
            if (!File.Exists(ConfigPath))
            {
                const string defaultContent = """
                    # TabSpool Configuration
                    # You can modify these settings and select 'Reload Config' from the system tray menu.

                    # Height of the tab bar hot zone from the top of the window (in pixels)
                    TabBarHeight=64

                    # Exclude window controls area (minimize/maximize/close) from the right edge (in pixels)
                    ExcludeRightMargin=140

                    # Cooldown between tab switches (in milliseconds)
                    ScrollCooldownMs=75

                    # Reverse the scroll direction (true/false)
                    ReverseDirection=false
                    """;
                File.WriteAllText(ConfigPath, defaultContent, Encoding.UTF8);
            }

            foreach (var line in File.ReadLines(ConfigPath))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("#") || !trimmed.Contains('='))
                    continue;

                var parts = trimmed.Split('=', 2);
                if (parts.Length != 2)
                    continue;

                var key = parts[0].Trim();
                var val = parts[1].Trim();

                switch (key)
                {
                    case "TabBarHeight":
                        if (int.TryParse(val, out var tbh)) _tabBarHeight = tbh;
                        break;
                    case "ExcludeRightMargin":
                        if (int.TryParse(val, out var erm)) _excludeRightMargin = erm;
                        break;
                    case "ScrollCooldownMs":
                        if (long.TryParse(val, out var sc)) _scrollCooldownMs = sc;
                        break;
                    case "ReverseDirection":
                        if (bool.TryParse(val, out var rd)) _reverseDirection = rd;
                        break;
                }
            }
        }
        catch
        {
            // Fallback to defaults
        }
    }

    private static IntPtr SetHook(LowLevelMouseProc proc)
    {
        return SetWindowsHookEx(WH_MOUSE_LL, proc, GetModuleHandle(null), 0);
    }

    private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam == (IntPtr)WM_MOUSEWHEEL)
        {
            var hs = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            short delta = (short)((hs.MouseData >> 16) & 0xFFFF);

            long val = ((long)hs.Pt.Y << 32) | ((long)hs.Pt.X & 0xFFFFFFFFL);
            IntPtr hwnd = WindowFromPoint(val);

            if (hwnd != IntPtr.Zero)
            {
                IntPtr rootHwnd = GetAncestor(hwnd, GA_ROOT);
                var classNameBuf = new StringBuilder(256);
                GetClassName(rootHwnd, classNameBuf, classNameBuf.Capacity);
                string className = classNameBuf.ToString();

                if (className == "Chrome_WidgetWin_1")
                {
                    if (GetWindowRect(rootHwnd, out var rect))
                    {
                        if (hs.Pt.Y >= rect.Top && hs.Pt.Y <= rect.Top + _tabBarHeight &&
                            hs.Pt.X >= rect.Left && hs.Pt.X <= rect.Right - _excludeRightMargin)
                        {
                            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                            if (now - _lastScrollTime >= _scrollCooldownMs)
                            {
                                _lastScrollTime = now;

                                bool isScrollUp = delta > 0;
                                if (_reverseDirection)
                                {
                                    isScrollUp = !isScrollUp;
                                }

                                IntPtr fgWnd = GetForegroundWindow();
                                if (fgWnd != rootHwnd)
                                {
                                    SetForegroundWindow(rootHwnd);
                                }

                                SendTabSwitch(isScrollUp);
                            }

                            return (IntPtr)1; // Consume event
                        }
                    }
                }
            }
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private static void SendTabSwitch(bool prev)
    {
        SendKey(VK_CONTROL, 0);
        if (prev)
        {
            SendKey(VK_SHIFT, 0);
        }
        SendKey(VK_TAB, 0);
        SendKey(VK_TAB, KEYEVENTF_KEYUP);
        if (prev)
        {
            SendKey(VK_SHIFT, KEYEVENTF_KEYUP);
        }
        SendKey(VK_CONTROL, KEYEVENTF_KEYUP);
    }

    private static void SendKey(byte vk, uint flags)
    {
        keybd_event(vk, 0, flags, UIntPtr.Zero);
    }

    private static Icon CreateCustomIcon()
    {
        using var bitmap = new Bitmap(32, 32);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        // Background circle (dark grey/blue)
        using var bgBrush = new SolidBrush(Color.FromArgb(40, 44, 52));
        g.FillEllipse(bgBrush, 2, 2, 28, 28);

        // Border (cyan/blue)
        using var borderPen = new Pen(Color.FromArgb(97, 175, 239), 2);
        g.DrawEllipse(borderPen, 2, 2, 28, 28);

        // Inner wheel (coral)
        using var wheelBrush = new SolidBrush(Color.FromArgb(224, 108, 117));
        g.FillRectangle(wheelBrush, 13, 8, 6, 16);

        // Inner wheel notch
        using var notchBrush = new SolidBrush(Color.White);
        g.FillRectangle(notchBrush, 15, 10, 2, 4);

        // Arrow markers (green)
        using var arrowBrush = new SolidBrush(Color.FromArgb(152, 195, 121));
        Point[] leftArrow = {
            new Point(9, 16),
            new Point(5, 13),
            new Point(5, 19)
        };
        g.FillPolygon(arrowBrush, leftArrow);

        Point[] rightArrow = {
            new Point(23, 16),
            new Point(27, 13),
            new Point(27, 19)
        };
        g.FillPolygon(arrowBrush, rightArrow);

        return Icon.FromHandle(bitmap.GetHicon());
    }

    private class TabSpoolContext : ApplicationContext
    {
        private readonly NotifyIcon _trayIcon;
        private readonly Icon _customIcon;

        public TabSpoolContext()
        {
            LoadConfig();

            var contextMenu = new ContextMenuStrip();
            contextMenu.Items.Add("Open Config", null, OpenConfig_Click);
            contextMenu.Items.Add("Reload Config", null, ReloadConfig_Click);
            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add("Exit", null, Exit_Click);

            _customIcon = CreateCustomIcon();

            _trayIcon = new NotifyIcon
            {
                Icon = _customIcon,
                ContextMenuStrip = contextMenu,
                Text = "TabSpool - Browser Tab Wheel Switcher",
                Visible = true
            };

            _hookProc = HookCallback;
            _hookId = SetHook(_hookProc);

            if (_hookId == IntPtr.Zero)
            {
                MessageBox.Show("Failed to install low-level mouse hook.", "TabSpool Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                ExitThread();
            }
        }

        private void OpenConfig_Click(object? sender, EventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "notepad.exe",
                    Arguments = $"\"{ConfigPath}\"",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not open config file: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ReloadConfig_Click(object? sender, EventArgs e)
        {
            LoadConfig();
            _trayIcon.ShowBalloonTip(3000, "TabSpool", "Configuration reloaded successfully!", ToolTipIcon.Info);
        }

        private void Exit_Click(object? sender, EventArgs e)
        {
            CleanUp();
            Application.Exit();
        }

        private void CleanUp()
        {
            if (_hookId != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookId);
                _hookId = IntPtr.Zero;
            }

            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _customIcon.Dispose();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                CleanUp();
            }
            base.Dispose(disposing);
        }
    }
}
