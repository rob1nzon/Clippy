namespace Clippy.Core.Classes
{
    public static class MascotMotion
    {
        public const double IdleRotation = 1;
        public const double HoverRotation = 1.5;
        public const double HoverLift = 2;
        public const double HitPadding = 6;
        public const double CycleSeconds = 5;

        public static bool ShouldRun(bool enabled, bool systemAnimations, bool visible, bool loaded) =>
            enabled && systemAnimations && visible && loaded;
    }
}
