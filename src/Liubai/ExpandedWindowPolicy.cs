namespace Liubai;

// Scene eligibility is geometry-only. Neither sampled colors nor partial visibility
// can promote a normal/snapped window into the maximized appearance.
internal static class ExpandedWindowPolicy
{
    internal static bool IsExpanded(WindowGeometry frame, WindowGeometry screen, WindowGeometry work,
        uint dpi, bool maximized)
    {
        if (frame.Right <= frame.Left || frame.Bottom <= frame.Top) return false;
        int tolerance = (int)Math.Ceiling(2 * Math.Max(96, dpi) / 96d);
        static bool Covers(WindowGeometry a, WindowGeometry b, int tolerance) =>
            a.Left <= b.Left + tolerance && a.Right >= b.Right - tolerance
            && a.Top <= b.Top + tolerance && a.Bottom >= b.Bottom - tolerance;
        return Covers(frame, screen, tolerance) || maximized && Covers(frame, work, tolerance);
    }
}
