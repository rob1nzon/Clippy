using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Clippy.Services
{
    // One monitor, bounded resolution, memory only. No temp files or image logging.
    internal static class ScreenCaptureService
    {
        public static async Task<byte[]> CaptureJpegAsync(IntPtr clippyWindow, CancellationToken token)
        {
            var pixels = await Task.Run(() => CapturePixels(clippyWindow, token), token);
            if (pixels == null) return null;
            using var stream = new InMemoryRandomAccessStream();
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, stream).AsTask(token);
            encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore,
                (uint)pixels.Width, (uint)pixels.Height, 96, 96, pixels.Bytes);
            await encoder.FlushAsync().AsTask(token);
            token.ThrowIfCancellationRequested();
            stream.Seek(0);
            using var reader = new DataReader(stream);
            await reader.LoadAsync((uint)stream.Size).AsTask(token);
            var jpeg = new byte[(int)stream.Size];
            reader.ReadBytes(jpeg);
            return jpeg;
        }

        private sealed record Pixels(int Width, int Height, byte[] Bytes);

        private static Pixels CapturePixels(IntPtr window, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            // Never capture the secure/lock desktop, even for a manual request.
            if (!IsDesktopAvailable()) return null;
            return CaptureMonitor(window, token);
        }

        private static bool IsDesktopAvailable()
        {
            var desktop = OpenInputDesktop(0, false, 1);
            if (desktop == IntPtr.Zero) return false;
            try
            {
                var name = new StringBuilder(256);
                return GetUserObjectInformation(desktop, 2, name, name.Capacity * 2, out _) &&
                    name.ToString().Equals("Default", StringComparison.OrdinalIgnoreCase);
            }
            finally { CloseDesktop(desktop); }
        }

        private static Pixels CaptureMonitor(IntPtr window, CancellationToken token)
        {
            var monitor = MonitorFromWindow(window, 2);
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (!GetMonitorInfo(monitor, ref info)) throw new Win32Exception();
            var rect = info.Monitor;
            var foreground = GetForegroundWindow();
            GetWindowThreadProcessId(foreground, out var process);
            if (process != (uint)Environment.ProcessId && GetWindowRect(foreground, out var front) &&
                front.Left <= rect.Left && front.Top <= rect.Top && front.Right >= rect.Right && front.Bottom >= rect.Bottom)
                return null;
            var sourceWidth = rect.Right - rect.Left;
            var sourceHeight = rect.Bottom - rect.Top;
            if (sourceWidth <= 0 || sourceHeight <= 0) return null;
            var scale = Math.Min(1d, 1280d / Math.Max(sourceWidth, sourceHeight));
            var width = Math.Max(1, (int)(sourceWidth * scale));
            var height = Math.Max(1, (int)(sourceHeight * scale));
            var screen = GetDC(IntPtr.Zero);
            if (screen == IntPtr.Zero) throw new Win32Exception();
            IntPtr memory = IntPtr.Zero, bitmap = IntPtr.Zero, previous = IntPtr.Zero;
            try
            {
                memory = CreateCompatibleDC(screen);
                bitmap = CreateCompatibleBitmap(screen, width, height);
                if (memory == IntPtr.Zero || bitmap == IntPtr.Zero) throw new Win32Exception();
                previous = SelectObject(memory, bitmap);
                if (previous == IntPtr.Zero || previous == new IntPtr(-1)) throw new Win32Exception();
                SetStretchBltMode(memory, 4); // HALFTONE downsampling.
                // SRCCOPY, without CAPTUREBLT: do not explicitly include layered overlays.
                if (!StretchBlt(memory, 0, 0, width, height, screen, rect.Left, rect.Top, sourceWidth, sourceHeight, 0x00CC0020))
                    throw new Win32Exception();
                SelectObject(memory, previous);
                previous = IntPtr.Zero;
                var header = new BitmapInfo
                {
                    Size = 40, Width = width, Height = -height, Planes = 1, BitCount = 32,
                    SizeImage = (uint)(width * height * 4)
                };
                var bytes = new byte[width * height * 4];
                if (GetDIBits(memory, bitmap, 0, (uint)height, bytes, ref header, 0) != height) throw new Win32Exception();
                token.ThrowIfCancellationRequested();
                if (!IsDesktopAvailable()) return null;
                return new Pixels(width, height, bytes);
            }
            finally
            {
                if (previous != IntPtr.Zero && previous != new IntPtr(-1)) SelectObject(memory, previous);
                if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
                if (memory != IntPtr.Zero) DeleteDC(memory);
                ReleaseDC(IntPtr.Zero, screen);
            }
        }

        public static bool IsVisible(IntPtr window) => IsWindowVisible(window);
        public static bool IsOurForegroundWindow()
        {
            GetWindowThreadProcessId(GetForegroundWindow(), out var process);
            return process == (uint)Environment.ProcessId;
        }

        [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public Rect Monitor, Work; public uint Flags; }
        [StructLayout(LayoutKind.Sequential)] private struct BitmapInfo
        {
            public uint Size; public int Width, Height; public ushort Planes, BitCount;
            public uint Compression, SizeImage; public int XPelsPerMeter, YPelsPerMeter; public uint ClrUsed, ClrImportant;
            public uint Color;
        }
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Rect rect);
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
        [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
        [DllImport("user32.dll")] private static extern IntPtr OpenInputDesktop(uint flags, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint access);
        [DllImport("user32.dll")] private static extern bool CloseDesktop(IntPtr desktop);
        [DllImport("user32.dll", EntryPoint = "GetUserObjectInformationW", CharSet = CharSet.Unicode)]
        private static extern bool GetUserObjectInformation(IntPtr handle, int index, StringBuilder information, int length, out int needed);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int width, int height);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] private static extern int SetStretchBltMode(IntPtr dc, int mode);
        [DllImport("gdi32.dll")] private static extern bool StretchBlt(IntPtr dest, int x, int y, int width, int height,
            IntPtr source, int sourceX, int sourceY, int sourceWidth, int sourceHeight, uint operation);
        [DllImport("gdi32.dll")] private static extern int GetDIBits(IntPtr dc, IntPtr bitmap, uint start, uint lines,
            [Out] byte[] bytes, ref BitmapInfo info, uint usage);
    }
}
