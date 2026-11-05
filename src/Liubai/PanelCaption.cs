using System.Runtime.InteropServices;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace Liubai;

// Register OS caption hit regions, rather than synthesizing mouse messages from XAML events.
internal sealed class PanelCaption : IDisposable
{
    readonly Window owner;
    readonly FrameworkElement header, controls;
    readonly Func<bool> enabled;
    InputNonClientPointerSource? source;
    bool disposed, queued;
    internal PanelCaption(Window owner, FrameworkElement header, FrameworkElement controls, Func<bool> enabled)
    {
        this.owner = owner; this.header = header; this.controls = controls; this.enabled = enabled;
        header.Loaded += Loaded; header.SizeChanged += SizeChanged; controls.SizeChanged += SizeChanged;
        owner.AppWindow.Changed += WindowChanged;
    }
    void Loaded(object sender, RoutedEventArgs args) => Queue();
    void SizeChanged(object sender, SizeChangedEventArgs args) => Queue();
    void WindowChanged(Microsoft.UI.Windowing.AppWindow sender, Microsoft.UI.Windowing.AppWindowChangedEventArgs args)
    { if (args.DidSizeChange) Queue(); }
    internal void Queue()
    {
        if (disposed || queued) return; queued = true;
        if (!owner.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => { queued = false; Refresh(); })) queued = false;
    }
    internal void Refresh()
    {
        if (disposed) return;
        source ??= InputNonClientPointerSource.GetForWindowId(owner.AppWindow.Id);
        if (!enabled() || owner.Content is not FrameworkElement root || header.XamlRoot == null || header.ActualHeight <= 0 || controls.ActualWidth <= 0)
        { source.ClearRegionRects(NonClientRegionKind.Caption); return; }
        double scale = header.XamlRoot.RasterizationScale;
        var origin = new Native.Point(); var handle = WinRT.Interop.WindowNative.GetWindowHandle(owner);
        ClientToScreen(handle, ref origin); GetWindowRect(handle, out var window);
        var position = header.TransformToVisual(root).TransformPoint(new());
        var buttons = controls.TransformToVisual(root).TransformPoint(new());
        int x = origin.X - window.Left + (int)Math.Round(4 * scale);
        int y = origin.Y - window.Top + (int)Math.Round(4 * scale);
        int right = origin.X - window.Left + (int)Math.Floor((buttons.X - 6) * scale);
        int bottom = origin.Y - window.Top + (int)Math.Ceiling((position.Y + header.ActualHeight + 4) * scale);
        source.SetRegionRects(NonClientRegionKind.Caption, right > x && bottom > y ? [new RectInt32(x, y, right - x, bottom - y)] : []);
    }
    internal async Task Verify(bool active)
    {
        if (owner.Content is FrameworkElement root) root.UpdateLayout();
        await Task.Delay(120); Refresh();
        var rectangles = source!.GetRegionRects(NonClientRegionKind.Caption) ?? [];
        if (!active) { if (rectangles.Length != 0) throw new Exception("Docked panel retained caption drag region"); return; }
        if (rectangles.Length == 0) throw new Exception("Detached window has no native caption region");
        var rectangle = rectangles[0]; var handle = WinRT.Interop.WindowNative.GetWindowHandle(owner);
        GetWindowRect(handle, out var window);
        if (HitTest(handle, window.Left + rectangle.X + rectangle.Width / 2, window.Top + rectangle.Y + rectangle.Height / 2) != 2)
            throw new Exception("Windows does not recognize native caption hit area");
        var point = controls.TransformToVisual((UIElement)owner.Content).TransformPoint(new(controls.ActualWidth / 2, controls.ActualHeight / 2));
        var client = new Native.Point { X = (int)Math.Round(point.X * header.XamlRoot.RasterizationScale), Y = (int)Math.Round(point.Y * header.XamlRoot.RasterizationScale) };
        ClientToScreen(handle, ref client);
        if (HitTest(handle, client.X, client.Y) == 2) throw new Exception("Caption region overlaps interactive controls");
    }
    static int HitTest(IntPtr window, int x, int y)
    {
        var coordinate = new IntPtr(unchecked((int)((uint)(ushort)x | ((uint)(ushort)y << 16))));
        if (SendMessageTimeout(window, 0x84, IntPtr.Zero, coordinate, 2, 1000, out var result) == IntPtr.Zero) throw new Exception("Caption hit test timed out");
        return result.ToInt32();
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true;
        header.Loaded -= Loaded; header.SizeChanged -= SizeChanged; controls.SizeChanged -= SizeChanged; owner.AppWindow.Changed -= WindowChanged;
        try { source?.ClearRegionRects(NonClientRegionKind.Caption); } catch { /* Window may already be destroyed. */ }
    }
    [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr window, ref Native.Point point);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr window, out Native.Rect rectangle);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);
}
