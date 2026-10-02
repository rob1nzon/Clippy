using System.Runtime.InteropServices;
using Clippy.Helpers;

internal static class WindowRegionChecks
{
    public static void Run(Action<bool, string> assert)
    {
        var window = CreateWindowEx(0, "STATIC", "Clippy region test", 0x80000000,
            0, 0, 380, 1000, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (window == IntPtr.Zero) throw new Exception("Could not create native test HWND.");
        var query = CreateRectRgn(0, 0, 0, 0);
        try
        {
            var region = new WindowInputRegion(window);
            region.SetAreas(new[]
            {
                new WindowInputRegion.PixelArea(12, 700, 356, 100),
                new WindowInputRegion.PixelArea(268, 808, 100, 100),
                new WindowInputRegion.PixelArea(12, 916, 356, 76)
            });
            GetWindowRgn(window, query);
            assert(!PtInRegion(query, 100, 200) && !PtInRegion(query, 100, 850), "native window region excludes empty viewport and gap beside mascot");
            assert(PtInRegion(query, 100, 750) && PtInRegion(query, 300, 850) && PtInRegion(query, 100, 950), "native window region keeps messages, mascot and input interactive");
            region.SetAreas(new[] { new WindowInputRegion.PixelArea(12, 8, 100, 100) });
            GetWindowRgn(window, query);
            assert(PtInRegion(query, 50, 50) && !PtInRegion(query, 100, 750), "collapsed native window removes old chat hit area");
            region.RestoreFullWindow();
            assert(GetWindowRgn(window, query) == 0, "full window restored for MCP dialog and popup menus");
        }
        finally { DeleteObject(query); DestroyWindow(window); }
    }

    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(uint extended, string className, string name, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern int GetWindowRgn(IntPtr window, IntPtr region);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr region);
    [DllImport("gdi32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool PtInRegion(IntPtr region, int x, int y);
}
