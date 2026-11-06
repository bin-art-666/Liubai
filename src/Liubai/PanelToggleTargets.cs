using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Liubai;

// Toolbar toggle clicks belong to the panel interaction, not outside dismissal.
// Resolve current geometry at click time (DPI, monitor, height and order can change).
internal static class PanelToggleTargets
{
    static readonly Dictionary<IntPtr, Func<Native.Point, bool>> targets = new();
    internal static void Register(Window owner, params Button[] buttons)
        => RegisterDynamic(owner, () => buttons);
    internal static void RegisterDynamic(Window owner, Func<IEnumerable<Button>> buttons)
    {
        var handle = WinRT.Interop.WindowNative.GetWindowHandle(owner);
        targets[handle] = point =>
        {
            if (owner.Content is not FrameworkElement root || root.XamlRoot == null) return false;
            var origin = new Native.Point(); if (!ClientToScreen(handle, ref origin)) return false;
            double scale = root.XamlRoot.RasterizationScale;
            foreach (var button in buttons())
            {
                if (!button.IsLoaded || !button.IsEnabled || button.Visibility != Visibility.Visible || button.ActualWidth <= 0 || !ReferenceEquals(button.XamlRoot, root.XamlRoot)) continue;
                var bounds = button.TransformToVisual(root).TransformBounds(new(0, 0, button.ActualWidth, button.ActualHeight));
                double x = (point.X - origin.X) / scale, y = (point.Y - origin.Y) / scale;
                if (x >= bounds.Left && x < bounds.Right && y >= bounds.Top && y < bounds.Bottom) return true;
            }
            return false;
        };
        owner.Closed += (_, _) => targets.Remove(handle);
    }
    internal static bool Contains(Native.Point point)
    {
        var underPointer = WindowFromPoint(point);
        return targets.Any(pair => Native.IsPanelWindow(pair.Key, underPointer) && pair.Value(point));
    }
    internal static bool OwnsForegroundPointer()
    {
        if (!GetCursorPos(out var point) || !targets.ContainsKey(Native.GetForegroundWindow())) return false;
        return Contains(point);
    }
    [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr window, ref Native.Point point);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out Native.Point point);
    [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(Native.Point point);
}
