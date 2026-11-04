using System.Runtime.InteropServices;
using Windows.Graphics;

namespace Liubai;

internal sealed record DesktopMonitor(IntPtr Handle, string Device, bool Primary, Native.Rect Bounds, uint Dpi)
{
    internal Native.MonitorInfo Info => Native.MonitorInformation(Handle);
    internal RectInt32 BarBounds(int height) => new(Bounds.Left, Bounds.Top, Bounds.Right - Bounds.Left,
        (int)Math.Round(height * Dpi / 96d));
}

internal static class Native
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct MonitorInfo { public int Size; public Rect Monitor, Work; public int Flags; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct MonitorInfoEx
    {
        public int Size; public Rect Monitor, Work; public int Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device;
    }
    delegate bool MonitorCallback(IntPtr monitor, IntPtr dc, ref Rect rectangle, IntPtr data);

    internal static IReadOnlyList<DesktopMonitor> Monitors()
    {
        var result = new List<DesktopMonitor>();
        MonitorCallback callback = (IntPtr monitor, IntPtr dc, ref Rect rectangle, IntPtr data) =>
        {
            var info = new MonitorInfoEx { Size = Marshal.SizeOf<MonitorInfoEx>(), Device = "" };
            if (GetMonitorInfoEx(monitor, ref info) && info.Monitor.Right > info.Monitor.Left && info.Monitor.Bottom > info.Monitor.Top)
            {
                if (GetDpiForMonitor(monitor, 0, out uint dpi, out _) != 0) dpi = 96;
                result.Add(new(monitor, info.Device, (info.Flags & 1) != 0, info.Monitor, Math.Max(96, dpi)));
            }
            return true;
        };
        if (!EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        return result.OrderByDescending(m => m.Primary).ThenBy(m => m.Device).ToArray();
    }

    internal static MonitorInfo MonitorInformation(IntPtr monitor)
    {
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        return info;
    }
    internal static bool MonitorExists(IntPtr monitor)
    {
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        return GetMonitorInfo(monitor, ref info);
    }
    internal static IntPtr MonitorForWindow(IntPtr window) => MonitorFromWindow(window, 2);

    internal static MonitorInfo PrimaryMonitor()
    {
        return Monitors().First(m => m.Primary).Info;
    }

    internal static RectInt32 BarBounds(IntPtr window, int height)
    {
        var monitor = PrimaryMonitor().Monitor;
        return new RectInt32(monitor.Left, monitor.Top, monitor.Right - monitor.Left,
            (int)Math.Round(height * GetDpiForWindow(window) / 96d));
    }

    internal static bool Same(RectInt32 a, RectInt32 b) => a.X == b.X && a.Y == b.Y && a.Width == b.Width && a.Height == b.Height;

    internal static void KeepOnTop(IntPtr window) => SetWindowPos(window, new IntPtr(-1), 0, 0, 0, 0, 0x0013);

    internal static bool PanelHasFocus(IntPtr panel)
    {
        var foreground = GetForegroundWindow();
        return IsPanelWindow(panel, foreground);
    }

    internal static bool IsPanelWindow(IntPtr panel, IntPtr candidate) => candidate == panel
        || (candidate != IntPtr.Zero && (IsChild(panel, candidate) || GetAncestor(candidate, 3) == panel)); // GA_ROOTOWNER

    internal static void DragWindow(IntPtr window)
    {
        ReleaseCapture();
        SendMessage(window, 0x00A1, new IntPtr(2), IntPtr.Zero); // WM_NCLBUTTONDOWN, HTCAPTION
    }

    internal static void RestoreResizeFrame(IntPtr window)
    {
        uint style = unchecked((uint)GetWindowLongPtr(window, -16).ToInt64());
        SetWindowLongPtr(window, -16, new IntPtr(unchecked((int)((style & ~0x80000000u) | 0x00040000u))));
        int policy = 2;
        DwmSetWindowAttribute(window, 2, ref policy, sizeof(int));
        SetWindowPos(window, IntPtr.Zero, 0, 0, 0, 0, 0x0037);
    }

    [DllImport("user32.dll")] static extern bool ReleaseCapture();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    internal static void RemoveWindowFrame(IntPtr window)
    {
        // Hiding a WinUI caption alone can leave the legacy non-client edge visible.
        const uint frameStyles = 0x00C00000 | 0x00040000;
        uint style = unchecked((uint)GetWindowLongPtr(window, -16).ToInt64());
        uint borderless = (style & ~frameStyles) | 0x80000000; // WS_POPUP
        if (style != borderless)
        {
            SetWindowLongPtr(window, -16, new IntPtr(unchecked((int)borderless)));
            SetWindowPos(window, IntPtr.Zero, 0, 0, 0, 0, 0x0037); // FRAMECHANGED, no move/size/activate/z-order
        }
        int policy = 1; // DWMNCRP_DISABLED
        DwmSetWindowAttribute(window, 2, ref policy, sizeof(int));
        int noBorder = unchecked((int)0xFFFFFFFE);
        DwmSetWindowAttribute(window, 34, ref noBorder, sizeof(int));
    }

    [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(Point point, uint flags);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll")] static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorCallback callback, IntPtr data);
    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = CharSet.Unicode)] static extern bool GetMonitorInfoEx(IntPtr monitor, ref MonitorInfoEx info);
    [DllImport("shcore.dll")] static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint x, out uint y);
    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern IntPtr GetAncestor(IntPtr window, uint flags);
    [DllImport("user32.dll")] static extern bool IsChild(IntPtr parent, IntPtr child);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("dwmapi.dll")] internal static extern int DwmSetWindowAttribute(IntPtr window, uint attribute, ref int value, uint size);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
}
