using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;

namespace Liubai;

// Native events only wake detection. Color decisions remain on the sampler/tracker path.
internal sealed class ForegroundActivity : IDisposable
{
    readonly DispatcherQueue dispatcher;
    readonly Action wake;
    readonly WinEvent callback;
    IntPtr foregroundHook, minimizeHook, geometryHook;
    uint watchedPid;
    IntPtr watchedWindow;
    IntPtr geometryWindow;
    WindowGeometry? lastFrame;
    int queued;
    bool disposed;
    internal bool Available => foregroundHook != IntPtr.Zero && minimizeHook != IntPtr.Zero && geometryHook != IntPtr.Zero;

    internal ForegroundActivity(DispatcherQueue dispatcher, Action wake)
    {
        this.dispatcher = dispatcher; this.wake = wake; callback = Event;
        foregroundHook = SetWinEventHook(3, 3, IntPtr.Zero, callback, 0, 0, 2);
        minimizeHook = SetWinEventHook(0x16, 0x17, IntPtr.Zero, callback, 0, 0, 2);
        WatchGeometry();
    }
    void WatchGeometry()
    {
        watchedWindow = Native.GetForegroundWindow();
        GetWindowThreadProcessId(watchedWindow, out uint pid);
        if (pid == watchedPid && geometryHook != IntPtr.Zero) return;
        if (geometryHook != IntPtr.Zero) UnhookWinEvent(geometryHook);
        geometryHook = IntPtr.Zero; watchedPid = pid;
        if (pid != 0 && pid != Environment.ProcessId)
            geometryHook = SetWinEventHook(0x800B, 0x800B, IntPtr.Zero, callback, pid, 0, 2);
    }
    void Event(IntPtr hook, uint kind, IntPtr window, int objectId, int childId, uint thread, uint time)
    {
        if (disposed || window == IntPtr.Zero) return;
        if (kind is 0x16 or 0x17 && window != watchedWindow && window != Native.GetForegroundWindow()) return;
        if (kind == 0x800B && (objectId != 0 || childId != 0 || window != Native.GetForegroundWindow())) return;
        if (kind == 0x800B && GetWindowRect(window, out var rect))
        {
            var frame = new WindowGeometry(rect.Left, rect.Top, rect.Right, rect.Bottom);
            if (geometryWindow == window && lastFrame == frame) return;
            geometryWindow = window; lastFrame = frame;
        }
        else { geometryWindow = IntPtr.Zero; lastFrame = null; }
        if (Interlocked.Exchange(ref queued, 1) != 0) return;
        if (!dispatcher.TryEnqueue(() =>
        {
            Interlocked.Exchange(ref queued, 0);
            if (disposed) return;
            WatchGeometry(); wake();
        })) Interlocked.Exchange(ref queued, 0);
    }
    public void Dispose()
    {
        disposed = true;
        foreach (var hook in new[] { foregroundHook, minimizeHook, geometryHook })
            if (hook != IntPtr.Zero) UnhookWinEvent(hook);
        foregroundHook = minimizeHook = geometryHook = IntPtr.Zero;
    }
    delegate void WinEvent(IntPtr hook, uint kind, IntPtr window, int objectId, int childId, uint thread, uint time);
    [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint first, uint last, IntPtr module, WinEvent callback, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr window, out Native.Rect rect);
}
