using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.UI.Dispatching;

namespace Liubai;

internal sealed class ExpandedWindowMonitor : IDisposable
{
    readonly DispatcherQueueTimer timer;
    readonly WindowColorSampler sampler = new();
    readonly ExpandedAppearanceTracker tracker = new();
    readonly Func<int> barHeight;
    readonly Action<ExpandedSnapshot> changed;
    readonly DispatcherQueue dispatcher;
    readonly Action interrupt;
    readonly Func<bool> animating;
    readonly Func<DesktopMonitor> display;
    ForegroundActivity? activity;
    long fastUntil, stableSince;
    WindowObservation previous;
    int generation;
    bool wakePending;
    bool enabled, busy, disposed;
    internal ExpandedSnapshot Current => tracker.Current;
    internal bool Enabled => enabled;
    internal int SampleCount { get; private set; }
    internal ExpandedWindowMonitor(DispatcherQueue dispatcher, Func<int> barHeight, Action<ExpandedSnapshot> changed,
        Action interrupt, Func<bool> animating, Func<DesktopMonitor>? display = null)
    {
        this.dispatcher = dispatcher; this.barHeight = barHeight; this.changed = changed;
        this.interrupt = interrupt; this.animating = animating;
        this.display = display ?? (() => Native.Monitors().First(m => m.Primary));
        timer = dispatcher.CreateTimer(); timer.Interval = TimeSpan.FromMilliseconds(250);
        timer.Tick += (_, _) => _ = Tick();
    }
    internal void SetEnabled(bool value)
    {
        if (disposed || enabled == value) return;
        enabled = value;
        generation++;
        tracker.Invalidate();
        if (enabled) { activity = new ForegroundActivity(dispatcher, Wake); timer.Start(); Wake(); }
        else { timer.Stop(); activity?.Dispose(); activity = null; }
    }
    internal void SetSpeed(AppearanceSpeed speed) { tracker.Speed = speed; if (enabled) Wake(); }
    internal void Refresh() { if (enabled) Wake(); }
    void Wake()
    {
        if (!enabled || disposed) return;
        generation++; tracker.Invalidate(); interrupt();
        fastUntil = Environment.TickCount64 + 1000; stableSince = Environment.TickCount64;
        timer.Interval = TimeSpan.FromMilliseconds(AppearanceTiming.For(tracker.Speed).Poll);
        if (busy) wakePending = true; else _ = Tick();
    }
    async Task Tick()
    {
        if (busy || disposed || !enabled || animating()) return;
        busy = true;
        int version = generation;
        try
        {
            int height = barHeight();
            var monitor = display();
            var observation = await Task.Run(() => sampler.Read(height, monitor));
            SampleCount++;
            if (disposed || !enabled || version != generation) return;
            long now = Environment.TickCount64;
            if (observation.IsExpanded != previous.IsExpanded || observation.Window != previous.Window
                || observation.Frame != previous.Frame || observation.Hold != previous.Hold
                || (observation.Color is { } a && previous.Color is { } b && !WindowColorAnalysis.Similar(a, b)))
            { stableSince = now; fastUntil = now + 1000; }
            previous = observation;
            if (tracker.Observe(observation, now) is { } snapshot) changed(snapshot);
            int interval = tracker.Pending || now < fastUntil ? AppearanceTiming.For(tracker.Speed).Poll
                : activity?.Available == true && now - stableSince >= 3000 ? (observation.IsExpanded ? 600 : 1000) : 250;
            timer.Interval = TimeSpan.FromMilliseconds(interval);
        }
        catch { /* Keep the last stable appearance when capture is unavailable. */ }
        finally
        {
            busy = false;
            if (wakePending) { wakePending = false; if (enabled && !disposed) _ = Tick(); }
        }
    }
    public void Dispose() { disposed = true; enabled = false; generation++; timer.Stop(); activity?.Dispose(); }
}

internal sealed class WindowColorSampler
{
    IntPtr lastTarget;
    uint lastPid;
    bool ignoredProcess;
    static readonly HashSet<string> transientClasses = ["Shell_TrayWnd", "Shell_SecondaryTrayWnd", "#32768", "MultitaskingViewFrame", "TaskSwitcherWnd"];
    static readonly HashSet<string> transientProcesses = new(StringComparer.OrdinalIgnoreCase)
        { "StartMenuExperienceHost", "ShellExperienceHost", "ShellHost", "SearchHost", "SearchApp", "LockApp", "LogonUI" };
    internal WindowObservation Read(int barHeight, DesktopMonitor? display = null)
    {
        display ??= Native.Monitors().First(m => m.Primary);
        var foreground = Native.GetForegroundWindow();
        if (foreground == IntPtr.Zero) return new(false, Hold: true);
        var target = Native.GetAncestor(foreground, 3);
        if (target == IntPtr.Zero) target = foreground;
        GetWindowThreadProcessId(target, out uint pid);
        string className = ClassName(target);
        bool desktop = className is "Progman" or "WorkerW";
        if (pid != lastPid)
        {
            lastPid = pid;
            try { using var process = Process.GetProcessById((int)pid); ignoredProcess = transientProcesses.Contains(process.ProcessName); }
            catch { ignoredProcess = true; }
        }
        if (!desktop && (pid == Environment.ProcessId || transientClasses.Contains(className) || ignoredProcess))
            return lastTarget != IntPtr.Zero && IsExpanded(lastTarget, display.Handle) ? new(true, Hold: true) : new(false);
        if (desktop || Native.MonitorForWindow(target) != display.Handle)
        {
            IntPtr local = IntPtr.Zero;
            EnumWindows((window, _) =>
            {
                if (!Eligible(window, display.Handle)) return true;
                local = window; return false;
            }, IntPtr.Zero);
            if (local == IntPtr.Zero) return new(false);
            target = local;
        }
        lastTarget = target;
        var observation = ReadTarget(target, barHeight, display);
        if (Native.GetForegroundWindow() != foreground) return new(false, Hold: true);
        return observation;
    }
    static bool Eligible(IntPtr window, IntPtr monitor)
    {
        if (!IsWindowVisible(window) || IsIconic(window) || Native.MonitorForWindow(window) != monitor) return false;
        if (!GetWindowRect(window, out var rect) || rect.Right <= rect.Left || rect.Bottom <= rect.Top) return false;
        var bounds = Native.MonitorInformation(monitor).Monitor;
        if (rect.Right <= bounds.Left || rect.Left >= bounds.Right || rect.Bottom <= bounds.Top || rect.Top >= bounds.Bottom) return false;
        if (Native.GetAncestor(window, 3) != window || (GetWindowLongPtr(window, -20).ToInt64() & 0x80) != 0) return false;
        GetWindowThreadProcessId(window, out uint pid);
        if (pid == Environment.ProcessId || transientClasses.Contains(ClassName(window)) || ClassName(window) is "Progman" or "WorkerW") return false;
        if (DwmGetWindowAttribute(window, 14, out int cloaked, sizeof(int)) == 0 && cloaked != 0) return false;
        try { using var process = Process.GetProcessById((int)pid); return !transientProcesses.Contains(process.ProcessName); }
        catch { return false; }
    }
    internal static bool IsExpanded(IntPtr target, IntPtr monitorHandle = default)
    {
        if (!IsWindowVisible(target) || IsIconic(target)) return false;
        if (DwmGetWindowAttribute(target, 14, out int cloaked, sizeof(int)) == 0 && cloaked != 0) return false;
        if (DwmGetFrameBounds(target, 9, out var rect, Marshal.SizeOf<Native.Rect>()) != 0 && !GetWindowRect(target, out rect)) return false;
        var targetMonitor = Native.MonitorForWindow(target);
        if (monitorHandle != IntPtr.Zero && targetMonitor != monitorHandle) return false;
        var monitor = Native.MonitorInformation(targetMonitor);
        int centerX = rect.Left + (rect.Right - rect.Left) / 2, centerY = rect.Top + (rect.Bottom - rect.Top) / 2;
        if (centerX < monitor.Monitor.Left || centerX >= monitor.Monitor.Right || centerY < monitor.Monitor.Top || centerY >= monitor.Monitor.Bottom) return false;
        if (IsZoomed(target)) return true;
        int tolerance = (int)Math.Ceiling(8 * Math.Max(96, Native.GetDpiForWindow(target)) / 96d);
        return rect.Left <= monitor.Work.Left + tolerance && rect.Right >= monitor.Work.Right - tolerance
            && rect.Top <= monitor.Work.Top + tolerance && rect.Bottom >= monitor.Work.Bottom - tolerance;
    }
    internal WindowObservation ReadTarget(IntPtr target, int barHeight, DesktopMonitor? display = null)
    {
        GetWindowRect(target, out var rect);
        bool expanded = IsExpanded(target, display?.Handle ?? IntPtr.Zero);
        return new(expanded, target.ToInt64(), expanded ? CaptureTopColor(target, barHeight, display?.Info ?? Native.MonitorInformation(Native.MonitorForWindow(target))) : null,
            Frame: new(rect.Left, rect.Top, rect.Right, rect.Bottom));
    }

    static uint? CaptureTopColor(IntPtr target, int barHeight, Native.MonitorInfo displayInfo)
    {
        if (!GetClientRect(target, out var client)) return null;
        Native.Point origin = default;
        if (!ClientToScreen(target, ref origin)) return null;
        var monitor = displayInfo.Monitor;
        double scale = Math.Max(96, Native.GetDpiForWindow(target)) / 96d;
        int inset = (int)Math.Round(8 * scale);
        int left = Math.Max(origin.X + inset, monitor.Left), right = Math.Min(origin.X + client.Right - inset, monitor.Right);
        int top = Math.Max(origin.Y + (int)(2 * scale), monitor.Top + barHeight + (int)(2 * scale));
        int bottom = Math.Min(Math.Min(origin.Y + client.Bottom, monitor.Bottom), top + (int)(48 * scale));
        if (right - left < 100 || bottom - top < 8) return null;
        const int width = 96, height = 16;
        IntPtr screen = GetDC(IntPtr.Zero), memory = IntPtr.Zero, bitmap = IntPtr.Zero, previous = IntPtr.Zero;
        if (screen == IntPtr.Zero) return null;
        try
        {
            memory = CreateCompatibleDC(screen);
            var info = new BitmapInfo { Size = 40, Width = width, Height = -height, Planes = 1, Bits = 32 };
            bitmap = CreateDIBSection(screen, ref info, 0, out var bits, IntPtr.Zero, 0);
            if (memory == IntPtr.Zero || bitmap == IntPtr.Zero || bits == IntPtr.Zero) return null;
            previous = SelectObject(memory, bitmap);
            SetStretchBltMode(memory, 3);
            if (!StretchBlt(memory, 0, 0, width, height, screen, left, top, right - left, bottom - top, 0x40CC0020)) return null;
            GdiFlush();
            byte[] data = new byte[width * height * 4]; Marshal.Copy(bits, data, 0, data.Length);
            var pixels = new List<uint>();
            for (int y = 1; y < height; y += 3)
                for (int x = 3; x < width; x += 6)
                {
                    var point = new Native.Point { X = left + x * (right - left) / width, Y = top + y * (bottom - top) / height };
                    if (Native.GetAncestor(WindowFromPoint(point), 2) != target) continue;
                    int i = (y * width + x) * 4;
                    pixels.Add(((uint)data[i + 2] << 16) | ((uint)data[i + 1] << 8) | data[i]);
                }
            return WindowColorAnalysis.Representative(pixels);
        }
        finally
        {
            if (previous != IntPtr.Zero && previous != new IntPtr(-1)) SelectObject(memory, previous);
            if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
            if (memory != IntPtr.Zero) DeleteDC(memory);
            ReleaseDC(IntPtr.Zero, screen);
        }
    }
    static string ClassName(IntPtr window) { var text = new StringBuilder(256); GetClassName(window, text, text.Capacity); return text.ToString(); }
    [StructLayout(LayoutKind.Sequential)]
    struct BitmapInfo
    {
        public uint Size; public int Width, Height; public ushort Planes, Bits;
        public uint Compression, ImageSize; public int XPels, YPels; public uint Used, Important, Colors;
    }
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
    delegate bool EnumWindow(IntPtr window, IntPtr data);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindow callback, IntPtr data);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr window, StringBuilder text, int length);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] static extern bool IsZoomed(IntPtr window);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr window, out Native.Rect rect);
    [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr window, out Native.Rect rect);
    [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr window, ref Native.Point point);
    [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(Native.Point point);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr window, uint attribute, out int value, int size);
    [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")] static extern int DwmGetFrameBounds(IntPtr window, uint attribute, out Native.Rect rect, int size);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfo info, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr item);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr item);
    [DllImport("gdi32.dll")] static extern int SetStretchBltMode(IntPtr dc, int mode);
    [DllImport("gdi32.dll")] static extern bool GdiFlush();
    [DllImport("gdi32.dll")] static extern bool StretchBlt(IntPtr dest, int x, int y, int width, int height, IntPtr source, int sx, int sy, int sw, int sh, uint operation);
}
