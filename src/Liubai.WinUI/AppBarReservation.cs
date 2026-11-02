using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Windows.Graphics;

namespace Liubai.Modern;

/// <summary>Reserves shell workspace only while the top bar is resident.</summary>
internal sealed class AppBarReservation : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    struct AppBarData
    {
        public uint Size;
        public IntPtr Window;
        public uint Callback, Edge;
        public Native.Rect Rect;
        public IntPtr Parameter;
    }
    delegate IntPtr SubclassProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr data);
    readonly IntPtr window;
    readonly DispatcherQueue dispatcher;
    readonly Action reposition;
    readonly SubclassProc procedure;
    readonly uint callbackMessage, taskbarCreated;
    readonly UIntPtr subclassId = new(0x4C42);
    bool registered, disposed, queued, notifying, notifyPosition, notifyActivation, hasBounds;
    public RectInt32 Bounds { get; private set; }
    public bool IsReserved => registered && hasBounds;

    public AppBarReservation(IntPtr window, DispatcherQueue dispatcher, Action reposition)
    {
        this.window = window; this.dispatcher = dispatcher; this.reposition = reposition;
        callbackMessage = RegisterWindowMessage("Liubai.AppBar.Position");
        taskbarCreated = RegisterWindowMessage("TaskbarCreated");
        procedure = HandleMessage;
        if (!SetWindowSubclass(window, procedure, subclassId, UIntPtr.Zero))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    }
    AppBarData Data() => new() { Size = (uint)Marshal.SizeOf<AppBarData>(), Window = window, Callback = callbackMessage, Edge = 1 };

    public RectInt32 Update(bool resident, RectInt32 requested)
    {
        if (disposed) return requested;
        if (!resident) { Release(); return requested; }
        if (!registered)
        {
            var registration = Data();
            if (SHAppBarMessage(0, ref registration) == UIntPtr.Zero)
                throw new InvalidOperationException("无法向 Windows 注册顶部栏预留空间。");
            registered = true; hasBounds = false;
        }
        var data = Data();
        data.Rect = new Native.Rect { Left = requested.X, Top = requested.Y, Right = requested.X + requested.Width, Bottom = requested.Y + requested.Height };
        SHAppBarMessage(2, ref data);
        data.Rect.Bottom = data.Rect.Top + requested.Height;
        var approved = new RectInt32(data.Rect.Left, data.Rect.Top, data.Rect.Right - data.Rect.Left, requested.Height);
        // ABM_SETPOS broadcasts changes. Avoid broadcasting the same rectangle again.
        if (!hasBounds || !Same(Bounds, approved))
        {
            SHAppBarMessage(3, ref data);
            Bounds = new RectInt32(data.Rect.Left, data.Rect.Top, data.Rect.Right - data.Rect.Left, data.Rect.Bottom - data.Rect.Top);
            hasBounds = true;
        }
        return Bounds;
    }
    public static bool Same(RectInt32 left, RectInt32 right) => left.X == right.X && left.Y == right.Y && left.Width == right.Width && left.Height == right.Height;
    void Release()
    {
        if (!registered) return;
        registered = false; hasBounds = false;
        var data = Data(); SHAppBarMessage(1, ref data);
    }
    void QueuePosition()
    {
        if (disposed || queued) return;
        queued = true;
        dispatcher.TryEnqueue(() => { queued = false; if (!disposed && !App.IsExiting) reposition(); });
    }
    void QueueNotifications()
    {
        if (disposed || notifying) return;
        notifying = true;
        dispatcher.TryEnqueue(() =>
        {
            notifying = false;
            bool activation = notifyActivation, position = notifyPosition;
            notifyActivation = notifyPosition = false;
            if (!registered || disposed) return;
            var data = Data();
            if (activation) SHAppBarMessage(6, ref data);
            if (position) SHAppBarMessage(9, ref data);
        });
    }
    IntPtr HandleMessage(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr data)
    {
        if (!disposed)
        {
            if (message == callbackMessage && wParam.ToInt64() == 1) QueuePosition();
            else if (message == taskbarCreated) { registered = false; hasBounds = false; QueuePosition(); }
            else if (message is 0x007E or 0x02E0 or 0x001A) QueuePosition();
            else if (registered && message == 0x0006) { notifyActivation = true; QueueNotifications(); }
            else if (registered && message == 0x0047) { notifyPosition = true; QueueNotifications(); }
        }
        return DefSubclassProc(handle, message, wParam, lParam);
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; Release(); RemoveWindowSubclass(window, procedure, subclassId);
    }
    [DllImport("shell32.dll")] static extern UIntPtr SHAppBarMessage(uint message, ref AppBarData data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint RegisterWindowMessage(string name);
    [DllImport("comctl32.dll", SetLastError = true)] static extern bool SetWindowSubclass(IntPtr window, SubclassProc procedure, UIntPtr id, UIntPtr data);
    [DllImport("comctl32.dll")] static extern bool RemoveWindowSubclass(IntPtr window, SubclassProc procedure, UIntPtr id);
    [DllImport("comctl32.dll")] static extern IntPtr DefSubclassProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}
