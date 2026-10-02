using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;

namespace Clippy.Helpers
{
    // A real HWND region excludes empty space from both drawing and input,
    // including mouse input destined for other processes.
    internal sealed class WindowInputRegion
    {
        public readonly record struct PixelArea(int X, int Y, int Width, int Height);
        private readonly IntPtr window;
        private string lastShape = "";

        public WindowInputRegion(IntPtr window) => this.window = window;

        public void RestoreFullWindow()
        {
            if (lastShape == "full") return;
            if (SetWindowRgn(window, IntPtr.Zero, true) == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            lastShape = "full";
        }

        public void SetAreas(IEnumerable<PixelArea> clientAreas)
        {
            var origin = new Point();
            ClientToScreen(window, ref origin);
            GetWindowRect(window, out var bounds);
            var areas = clientAreas.Where(r => r.Width > 0 && r.Height > 0).ToArray();
            if (areas.Length == 0) return; // Wait for the first layout; don't clip unmeasured controls.
            var offsetX = origin.X - bounds.Left;
            var offsetY = origin.Y - bounds.Top;
            var shape = offsetX + "," + offsetY + ":" + string.Join(";", areas.Select(r => $"{r.X},{r.Y},{r.Width},{r.Height}"));
            if (shape == lastShape) return;
            var combined = CreateRectRgn(0, 0, 0, 0);
            if (combined == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                foreach (var area in areas)
                {
                    var part = CreateRectRgn(area.X + offsetX, area.Y + offsetY,
                        area.X + offsetX + area.Width, area.Y + offsetY + area.Height);
                    if (part == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
                    try
                    {
                        if (CombineRgn(combined, combined, part, 2) == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
                    }
                    finally { DeleteObject(part); }
                }
                if (SetWindowRgn(window, combined, true) == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
                // Windows owns the region only after a successful SetWindowRgn.
                combined = IntPtr.Zero;
                lastShape = shape;
            }
            finally { if (combined != IntPtr.Zero) DeleteObject(combined); }
        }

        [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
        [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);
        [DllImport("gdi32.dll", SetLastError = true)] private static extern int CombineRgn(IntPtr destination, IntPtr first, IntPtr second, int mode);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr region);
        [DllImport("user32.dll", SetLastError = true)] private static extern int SetWindowRgn(IntPtr window, IntPtr region, [MarshalAs(UnmanagedType.Bool)] bool redraw);
        [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr window, ref Point point);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Rect rectangle);
    }
}
