using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

namespace Clippy.Services
{
    // Native tray APIs work with WinUI's existing window/message loop.
    internal sealed class TrayService : IDisposable
    {
        public const uint CallbackMessage = 0x8001;
        private readonly IntPtr window;
        private readonly IntPtr icon;
        private readonly Action show, hide, settings, exit;
        private readonly uint taskbarCreated = RegisterWindowMessage("TaskbarCreated");
        private bool visible, disposed;
        private NotifyIconData data;

        public TrayService(IntPtr window, Action show, Action hide, Action settings, Action exit)
        {
            this.window = window;
            this.show = show;
            this.hide = hide;
            this.settings = settings;
            this.exit = exit;
            icon = LoadImage(IntPtr.Zero, Path.Combine(AppContext.BaseDirectory, "Assets", "Clippy", "Clippy.ico"), 1, 0, 0, 0x10 | 0x40);
            if (icon == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not load Clippy tray icon.");
            data = new NotifyIconData
            {
                Size = (uint)Marshal.SizeOf<NotifyIconData>(), Window = window, Id = 1,
                Flags = 1 | 2 | 4 | 0x80, Callback = CallbackMessage, Icon = icon,
                Tip = "Clippy — click to show", Info = "", InfoTitle = "", Version = 4
            };
        }

        public void SetVisible(bool value)
        {
            if (disposed || visible == value) return;
            if (value)
            {
                if (!ShellNotifyIcon(0, ref data))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not add Clippy to the tray.");
                ShellNotifyIcon(4, ref data); // NOTIFYICON_VERSION_4
            }
            else ShellNotifyIcon(2, ref data);
            visible = value;
        }

        public bool HandleMessage(uint message, nint lParam)
        {
            if (message == taskbarCreated && visible)
            {
                visible = false;
                SetVisible(true);
                return false;
            }
            if (message != CallbackMessage || !visible) return false;
            var notification = (int)(lParam.ToInt64() & 0xffff);
            // NIN_SELECT, NIN_KEYSELECT: mouse click or keyboard activation.
            if (notification == 0x400 || notification == 0x401) show();
            else if (notification == 0x7b) ShowMenu(); // WM_CONTEXTMENU
            return true;
        }

        private void ShowMenu()
        {
            var menu = CreatePopupMenu();
            if (menu == IntPtr.Zero) return;
            uint command;
            try
            {
                AppendMenu(menu, 0, 1, "Show Clippy");
                AppendMenu(menu, 0, 2, "Hide Clippy");
                AppendMenu(menu, 0, 3, "Settings");
                AppendMenu(menu, 0x800, 0, null);
                AppendMenu(menu, 0, 4, "Exit");
                GetCursorPos(out var point);
                SetForegroundWindow(window);
                command = TrackPopupMenuEx(menu, 0x100 | 0x2, point.X, point.Y, window, IntPtr.Zero);
                PostMessage(window, 0, IntPtr.Zero, IntPtr.Zero);
                ShellNotifyIcon(3, ref data); // Return focus to the notification area.
            }
            finally { DestroyMenu(menu); }
            switch (command)
            {
                case 1: show(); break;
                case 2: hide(); break;
                case 3: settings(); break;
                case 4: exit(); break;
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            SetVisible(false);
            DestroyIcon(icon);
            disposed = true;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NotifyIconData
        {
            public uint Size;
            public IntPtr Window;
            public uint Id, Flags, Callback;
            public IntPtr Icon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
            public uint State, StateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
            public uint Version;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
            public uint InfoFlags;
            public Guid Guid;
            public IntPtr BalloonIcon;
        }

        [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
        [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ShellNotifyIcon(uint message, ref NotifyIconData data);
        [DllImport("user32.dll", EntryPoint = "LoadImageW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadImage(IntPtr instance, string path, uint type, int width, int height, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string message);
        [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
        [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool AppendMenu(IntPtr menu, uint flags, nuint id, string text);
        [DllImport("user32.dll")] private static extern uint TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr window, IntPtr parameters);
        [DllImport("user32.dll")] private static extern bool DestroyMenu(IntPtr menu);
        [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    }
}
