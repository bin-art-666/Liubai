using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Windows.Graphics;

namespace Liubai;

internal sealed class AppBarReservation : IDisposable
{
    const uint New = 0, Remove = 1, QueryPosition = 2, SetPosition = 3, Activate = 6, WindowPositionChanged = 9;
    const uint PositionNotification = 1, FullscreenNotification = 2;
    [StructLayout(LayoutKind.Sequential)]
    struct Data
    {
        public uint Size;
        public IntPtr Window;
        public uint Callback, Edge;
        public Native.Rect Rectangle;
        public IntPtr Parameter;
    }
    delegate IntPtr WindowProcedure(IntPtr window, uint message, IntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr data);
    readonly IntPtr window;
    readonly DispatcherQueue dispatcher;
    readonly Action reposition;
    readonly Action<bool> fullscreen;
    readonly Action<uint, long>? environmentChanged;
    readonly WindowProcedure procedure;
    readonly uint callback = RegisterWindowMessage("Liubai.GlassBar.AppBar");
    readonly uint explorerCreated = RegisterWindowMessage("TaskbarCreated");
    readonly UIntPtr subclassId = new(0x4C47);
    bool registered, disposed, queued, notificationQueued;
    bool positionNotification, activationNotification;
    RectInt32? bounds;

    internal AppBarReservation(IntPtr window, DispatcherQueue dispatcher, Action reposition, Action<bool> fullscreen,
        Action<uint, long>? environmentChanged = null)
    {
        this.window = window; this.dispatcher = dispatcher; this.reposition = reposition; this.fullscreen = fullscreen;
        this.environmentChanged = environmentChanged;
        procedure = HandleMessage;
        if (!SetWindowSubclass(window, procedure, subclassId, UIntPtr.Zero))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        WTSRegisterSessionNotification(window, 0);
    }

    Data CreateData() => new() { Size = (uint)Marshal.SizeOf<Data>(), Window = window, Callback = callback, Edge = 1 };

    internal RectInt32 Reserve(RectInt32 requested)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!registered)
        {
            var registration = CreateData();
            if (SHAppBarMessage(New, ref registration) == UIntPtr.Zero)
                throw new InvalidOperationException("Windows 无法预留顶部空间。");
            registered = true;
        }
        var data = CreateData();
        data.Rectangle = new Native.Rect { Left = requested.X, Top = requested.Y, Right = requested.X + requested.Width, Bottom = requested.Y + requested.Height };
        SHAppBarMessage(QueryPosition, ref data);
        data.Rectangle.Bottom = data.Rectangle.Top + requested.Height;
        var approved = new RectInt32(data.Rectangle.Left, data.Rectangle.Top, data.Rectangle.Right - data.Rectangle.Left, requested.Height);
        if (bounds == null || !Native.Same(bounds.Value, approved))
        {
            SHAppBarMessage(SetPosition, ref data);
            bounds = new RectInt32(data.Rectangle.Left, data.Rectangle.Top, data.Rectangle.Right - data.Rectangle.Left, data.Rectangle.Bottom - data.Rectangle.Top);
        }
        return bounds.Value;
    }

    internal void Release()
    {
        if (registered) { var data = CreateData(); SHAppBarMessage(Remove, ref data); registered = false; }
        bounds = null;
    }

    void QueuePosition()
    {
        if (queued || disposed) return;
        queued = true;
        if (!dispatcher.TryEnqueue(() => { queued = false; if (!disposed) reposition(); })) queued = false;
    }

    void QueueNotification()
    {
        if (notificationQueued || disposed) return;
        notificationQueued = true;
        dispatcher.TryEnqueue(() =>
        {
            notificationQueued = false;
            bool activation = activationNotification, position = positionNotification;
            activationNotification = positionNotification = false;
            if (!registered || disposed) return;
            var data = CreateData();
            if (activation) SHAppBarMessage(Activate, ref data);
            if (position) SHAppBarMessage(WindowPositionChanged, ref data);
        });
    }

    IntPtr HandleMessage(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr data)
    {
        if (!disposed)
        {
            if (message is 0x007E or 0x02E0 or 0x001A or 0x0218 or 0x02B1 or 0x031A or 0x031E)
            {
                long parameter = wParam.ToInt64();
                dispatcher.TryEnqueue(() => { if (!disposed) environmentChanged?.Invoke(message, parameter); });
            }
            if (message == callback)
            {
                if ((uint)wParam.ToInt64() == PositionNotification) QueuePosition();
                else if ((uint)wParam.ToInt64() == FullscreenNotification)
                {
                    bool active = lParam != IntPtr.Zero;
                    dispatcher.TryEnqueue(() => { if (!disposed) fullscreen(active); });
                }
            }
            else if (message == explorerCreated) { registered = false; bounds = null; QueuePosition(); }
            else if (message is 0x007E or 0x02E0 or 0x001A) QueuePosition();
            else if (registered && message == 0x0006) { activationNotification = true; QueueNotification(); }
            else if (registered && message == 0x0047) { positionNotification = true; QueueNotification(); }
        }
        return DefSubclassProc(handle, message, wParam, lParam);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (registered) { var data = CreateData(); SHAppBarMessage(Remove, ref data); registered = false; }
        bounds = null;
        WTSUnRegisterSessionNotification(window);
        RemoveWindowSubclass(window, procedure, subclassId);
    }

    [DllImport("shell32.dll")] static extern UIntPtr SHAppBarMessage(uint message, ref Data data);
    [DllImport("wtsapi32.dll")] static extern bool WTSRegisterSessionNotification(IntPtr window, uint flags);
    [DllImport("wtsapi32.dll")] static extern bool WTSUnRegisterSessionNotification(IntPtr window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint RegisterWindowMessage(string name);
    [DllImport("comctl32.dll", SetLastError = true)] static extern bool SetWindowSubclass(IntPtr window, WindowProcedure procedure, UIntPtr id, UIntPtr data);
    [DllImport("comctl32.dll")] static extern bool RemoveWindowSubclass(IntPtr window, WindowProcedure procedure, UIntPtr id);
    [DllImport("comctl32.dll")] static extern IntPtr DefSubclassProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}
