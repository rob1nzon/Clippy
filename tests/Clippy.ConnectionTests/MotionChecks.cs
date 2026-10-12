using Clippy.Core.Classes;

internal static class MotionChecks
{
    public static void Run(Action<bool, string> assert)
    {
        assert(MascotMotion.ShouldRun(true, true, true, true), "mascot motion enabled only for a visible loaded image");
        assert(!MascotMotion.ShouldRun(false, true, true, true) && !MascotMotion.ShouldRun(true, false, true, true), "mascot motion respects app toggle and Windows reduced-motion setting");
        assert(!MascotMotion.ShouldRun(true, true, false, true) && !MascotMotion.ShouldRun(true, true, true, false), "hidden and unloaded mascots do not animate");
        // Worst transformed corner at 200% size, including the hover lift.
        var radians = MascotMotion.HoverRotation * Math.PI / 180;
        var protrusion = 100 * (Math.Sin(radians) + Math.Cos(radians) - 1) + MascotMotion.HoverLift;
        assert(protrusion < MascotMotion.HitPadding && MascotMotion.HitPadding <= 8, "reserved region padding covers motion at maximum mascot size without enlarging HWND");
        Console.WriteLine("All mascot motion checks passed.");
    }
}
