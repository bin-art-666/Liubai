using System.Runtime.InteropServices;

namespace Liubai;

// Enable per-pixel desktop composition without an acrylic effect or blur region.
// Paint the redirected Win32 background black so its alpha does not become white.
internal sealed class TransparentWindowFrame : IDisposable
{
    readonly IntPtr window;
    readonly WindowProcedure procedure;
    readonly UIntPtr subclassId = new(0x4C4E);
    bool disposed;
    [StructLayout(LayoutKind.Sequential)] struct Margins { public int Left, Right, Top, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct BlurBehind
    {
        public uint Flags;
        [MarshalAs(UnmanagedType.Bool)] public bool Enable;
        public IntPtr Region;
        [MarshalAs(UnmanagedType.Bool)] public bool Transition;
    }
    delegate IntPtr WindowProcedure(IntPtr window, uint message, IntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr data);
    internal TransparentWindowFrame(IntPtr window)
    {
        this.window = window; procedure = HandleMessage;
        if (!SetWindowSubclass(window, procedure, subclassId, UIntPtr.Zero)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        Configure();
        IntPtr dc = GetDC(window);
        try { if (dc != IntPtr.Zero) Clear(dc); }
        finally { if (dc != IntPtr.Zero) ReleaseDC(window, dc); }
    }
    void Configure()
    {
        var margins = new Margins(); DwmExtendFrameIntoClientArea(window, ref margins);
        IntPtr region = CreateRectRgn(-2, -2, -1, -1);
        try
        {
            var blur = new BlurBehind { Flags = 3, Enable = true, Region = region };
            DwmEnableBlurBehindWindow(window, ref blur);
        }
        finally { if (region != IntPtr.Zero) DeleteObject(region); }
    }
    bool Clear(IntPtr dc) => GetClientRect(window, out var rect) && FillRect(dc, ref rect, GetStockObject(4)) != 0;
    IntPtr HandleMessage(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr data)
    {
        if (!disposed && message == 0x0014 && Clear(wParam)) return new IntPtr(1);
        if (!disposed && message == 0x031E) Configure();
        return DefSubclassProc(handle, message, wParam, lParam);
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; RemoveWindowSubclass(window, procedure, subclassId);
        var blur = new BlurBehind { Flags = 1, Enable = false }; DwmEnableBlurBehindWindow(window, ref blur);
    }
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr window, out Native.Rect rect);
    [DllImport("user32.dll")] static extern int FillRect(IntPtr dc, ref Native.Rect rect, IntPtr brush);
    [DllImport("gdi32.dll")] static extern IntPtr GetStockObject(int item);
    [DllImport("gdi32.dll")] static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr item);
    [DllImport("dwmapi.dll")] static extern int DwmEnableBlurBehindWindow(IntPtr window, ref BlurBehind blur);
    [DllImport("dwmapi.dll")] static extern int DwmExtendFrameIntoClientArea(IntPtr window, ref Margins margins);
    [DllImport("comctl32.dll", SetLastError = true)] static extern bool SetWindowSubclass(IntPtr window, WindowProcedure callback, UIntPtr id, UIntPtr data);
    [DllImport("comctl32.dll")] static extern bool RemoveWindowSubclass(IntPtr window, WindowProcedure callback, UIntPtr id);
    [DllImport("comctl32.dll")] static extern IntPtr DefSubclassProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}
