using System.Runtime.InteropServices;

namespace Liubai;

internal readonly record struct WindowScene(bool IsExpanded, WindowGeometry Frame, Native.MonitorInfo Display);

internal static class WindowSceneDetector
{
    internal static WindowScene Read(IntPtr target, IntPtr expectedMonitor = default)
    {
        if (!IsWindowVisible(target) || IsIconic(target)) return default;
        if (DwmGetWindowAttribute(target, 14, out int cloaked, sizeof(int)) == 0 && cloaked != 0) return default;
        if (DwmGetFrameBounds(target, 9, out var rect, Marshal.SizeOf<Native.Rect>()) != 0 && !GetWindowRect(target, out rect)) return default;
        var monitorHandle = Native.MonitorForWindow(target);
        if (expectedMonitor != IntPtr.Zero && expectedMonitor != monitorHandle) return default;
        var monitor = Native.MonitorInformation(monitorHandle);
        static WindowGeometry Geometry(Native.Rect r) => new(r.Left, r.Top, r.Right, r.Bottom);
        var frame = Geometry(rect);
        bool expanded = ExpandedWindowPolicy.IsExpanded(frame, Geometry(monitor.Monitor), Geometry(monitor.Work),
            Native.GetDpiForWindow(target), IsZoomed(target));
        return new(expanded, frame, monitor);
    }
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] static extern bool IsZoomed(IntPtr window);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr window, out Native.Rect rect);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr window, uint attribute, out int value, int size);
    [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")] static extern int DwmGetFrameBounds(IntPtr window, uint attribute, out Native.Rect rect, int size);
}
