namespace Liubai;

public enum ExpandedMode { Automatic, Custom, FollowWindow }
public enum AppearanceSpeed { Responsive, Balanced, Calm }
public enum AppearanceAnimation { Standard, Soft, Off }

public sealed record ExpandedAppearanceOptions(bool Enabled = true, ExpandedMode Mode = ExpandedMode.Automatic,
    string ColorHex = "#202020", AppearanceSpeed Speed = AppearanceSpeed.Balanced,
    AppearanceAnimation Animation = AppearanceAnimation.Standard)
{
    internal bool IsValid => Enum.IsDefined(Mode) && Enum.IsDefined(Speed) && Enum.IsDefined(Animation) && MaterialOptions.IsColor(ColorHex);
}

internal readonly record struct AppearanceTiming(int Source, int Scene, int Quiet, int Content, int Poll)
{
    internal static AppearanceTiming For(AppearanceSpeed speed) => speed switch
    {
        AppearanceSpeed.Responsive => new(60, 180, 60, 180, 50),
        AppearanceSpeed.Calm => new(180, 280, 100, 450, 90),
        _ => new(120, 220, 80, 250, 75)
    };
}

internal readonly record struct ExpandedSnapshot(bool IsExpanded, long Window, uint Color, bool IsDark, bool HasColor = true)
{
    internal static ExpandedSnapshot Normal => new(false, 0, 0x202020, true, false);
    internal bool UsesSolid(ExpandedAppearanceOptions options) => options.Enabled && IsExpanded
        && (options.Mode == ExpandedMode.Custom || HasColor);
}

internal readonly record struct WindowGeometry(int Left, int Top, int Right, int Bottom);
internal readonly record struct WindowObservation(bool IsExpanded, long Window = 0, uint? Color = null, bool Hold = false,
    WindowGeometry Frame = default);

internal static class WindowColorAnalysis
{
    internal static double Brightness(uint rgb) => (.2126 * ((rgb >> 16) & 255)
        + .7152 * ((rgb >> 8) & 255) + .0722 * (rgb & 255)) / 255;

    internal static bool WhiteForeground(uint rgb)
    {
        static double Linear(uint c) { double v = c / 255d; return v <= .04045 ? v / 12.92 : Math.Pow((v + .055) / 1.055, 2.4); }
        double luminance = .2126 * Linear((rgb >> 16) & 255) + .7152 * Linear((rgb >> 8) & 255) + .0722 * Linear(rgb & 255);
        return (1.05 / (luminance + .05)) >= ((luminance + .05) / .05);
    }

    internal static uint? Representative(IEnumerable<uint> pixels)
    {
        var groups = new Dictionary<uint, List<uint>>();
        int total = 0;
        foreach (uint rgb in pixels)
        {
            uint key = rgb & 0xF0F0F0;
            if (!groups.TryGetValue(key, out var group)) groups[key] = group = new();
            group.Add(rgb); total++;
        }
        if (total < 16) return null;
        var dominant = groups.Values.MaxBy(g => g.Count)!;
        if (dominant.Count < total * .22) return null;
        uint r = (uint)dominant.Average(c => (c >> 16) & 255);
        uint g = (uint)dominant.Average(c => (c >> 8) & 255);
        uint b = (uint)dominant.Average(c => c & 255);
        return (r << 16) | (g << 8) | b;
    }

    internal static bool Similar(uint a, uint b, int tolerance = 12) =>
        Math.Abs((int)((a >> 16) & 255) - (int)((b >> 16) & 255)) <= tolerance
        && Math.Abs((int)((a >> 8) & 255) - (int)((b >> 8) & 255)) <= tolerance
        && Math.Abs((int)(a & 255) - (int)(b & 255)) <= tolerance;

    internal static uint Background(ExpandedAppearanceOptions options, ExpandedSnapshot snapshot) => options.Mode switch
    {
        ExpandedMode.Custom => Convert.ToUInt32(options.ColorHex[1..], 16),
        ExpandedMode.FollowWindow => snapshot.Color,
        _ => snapshot.IsDark ? 0x000000u : 0xFFFFFFu
    };
}

internal sealed class ExpandedAppearanceTracker
{
    internal ExpandedSnapshot Current { get; private set; } = ExpandedSnapshot.Normal;
    ExpandedSnapshot? candidate;
    long candidateSince;
    WindowGeometry frame;
    long frameWindow, lastMotion;
    internal AppearanceSpeed Speed { get; set; } = AppearanceSpeed.Balanced;
    internal bool Pending => candidate != null;
    internal void Invalidate() => candidate = null;

    internal ExpandedSnapshot? Observe(WindowObservation observation, long now)
    {
        if (observation.Hold) { candidate = null; return null; }
        if (frameWindow != observation.Window || frame != observation.Frame)
        {
            frameWindow = observation.Window; frame = observation.Frame; lastMotion = now;
        }
        uint color = observation.Color ?? Current.Color;
        bool hasColor = observation.Color.HasValue || observation.Window == Current.Window && Current.IsExpanded && Current.HasColor;
        double brightness = WindowColorAnalysis.Brightness(color);
        bool dark = observation.Window == Current.Window ? Current.IsDark : brightness < .5;
        if (brightness <= .44) dark = true;
        else if (brightness >= .60) dark = false;
        var next = observation.IsExpanded ? new ExpandedSnapshot(true, observation.Window, color, dark, hasColor)
            : Current with { IsExpanded = false, Window = 0, HasColor = false };
        static bool Same(ExpandedSnapshot a, ExpandedSnapshot b) => a.IsExpanded == b.IsExpanded
            && a.Window == b.Window && a.HasColor == b.HasColor && a.IsDark == b.IsDark && WindowColorAnalysis.Similar(a.Color, b.Color);
        if (Same(next, Current)) { candidate = null; return null; }
        if (candidate is { } previous && Same(previous, next))
        {
            var timing = AppearanceTiming.For(Speed);
            int wait = next.IsExpanded != Current.IsExpanded ? timing.Scene
                : next.Window != Current.Window ? timing.Source : timing.Content;
            // Geometry settling and color confirmation run concurrently, never serially.
            if (now - candidateSince < wait || now - lastMotion < timing.Quiet) return null;
            Current = next; candidate = null; return Current;
        }
        candidate = next; candidateSince = now;
        return null;
    }
}
