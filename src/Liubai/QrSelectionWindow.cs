using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;
using Windows.Graphics;
using Windows.System;

namespace Liubai;

internal sealed record QrSelection(byte[] Pixels, int Width, int Height);

// One frozen virtual-desktop surface. Mapping through its actual logical size keeps
// selections aligned with physical pixels, including negative origins and mixed DPI.
internal sealed class QrSelectionWindow : Window
{
    static QrSelectionWindow? current;
    static int generation;
    internal static bool Active => current != null;
    internal static void CancelActive() { generation++; current?.Finish(null); }
    readonly TaskCompletionSource<QrSelection?> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    sealed class CrosshairGrid : Grid { internal CrosshairGrid() { ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Cross); } }
    readonly Grid root = new CrosshairGrid();
    readonly Canvas adorners = new() { IsHitTestVisible = false };
    readonly Border outline = new() { BorderBrush = new SolidColorBrush(Colors.White), BorderThickness = new Thickness(1) };
    readonly Border[] shades = Enumerable.Range(0, 4).Select(_ => new Border { Background = new SolidColorBrush(Windows.UI.Color.FromArgb(105, 0, 0, 0)) }).ToArray();
    readonly Image image = new() { Stretch = Stretch.Fill };
    readonly Button cancel = new() { Content = "取消 · Esc", HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(16) };
    readonly RectInt32 bounds;
    byte[]? pixels;
    Point start;
    bool dragging, finished;

    QrSelectionWindow(RectInt32 bounds, byte[] pixels)
    {
        this.bounds = bounds; this.pixels = pixels;
        Title = "留白 · 框选二维码";
        var presenter = (OverlappedPresenter)AppWindow.Presenter;
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsResizable = presenter.IsMaximizable = presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = true; AppWindow.IsShownInSwitchers = false;
        int corner = 1;
        Native.DwmSetWindowAttribute(WinRT.Interop.WindowNative.GetWindowHandle(this), 33, ref corner, sizeof(int));
        var bitmap = new WriteableBitmap(bounds.Width, bounds.Height);
        using (var stream = bitmap.PixelBuffer.AsStream()) stream.Write(pixels);
        bitmap.Invalidate(); image.Source = bitmap;
        root.Children.Add(image);
        foreach (var shade in shades) adorners.Children.Add(shade);
        adorners.Children.Add(outline); root.Children.Add(adorners);
        var hint = new TextBlock { Text = "拖动框选二维码，松开识别 · 右键或 Esc 取消", Foreground = new SolidColorBrush(Colors.White) };
        root.Children.Add(new Border { Child = hint, Background = new SolidColorBrush(Windows.UI.Color.FromArgb(220, 30, 30, 30)),
            Padding = new Thickness(16, 10, 16, 10), CornerRadius = new CornerRadius(8), IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 16, 0, 0) });
        root.Children.Add(cancel); Content = root;
        cancel.Click += (_, _) => Finish(null);
        root.Loaded += (_, _) => cancel.Focus(FocusState.Programmatic);
        root.SizeChanged += (_, _) => { if (dragging) Finish(null); else Draw(new Rect()); };
        root.PointerPressed += Pressed; root.PointerMoved += Moved; root.PointerReleased += Released;
        root.PointerCanceled += (_, _) => Finish(null);
        root.PointerCaptureLost += (_, _) => { if (dragging) Finish(null); };
        root.KeyDown += (_, e) => { if (e.Key == VirtualKey.Escape) { e.Handled = true; Finish(null); } };
        Activated += (_, e) => { if (e.WindowActivationState == WindowActivationState.Deactivated) Finish(null); };
        Closed += (_, _) => { finished = true; this.pixels = null; image.Source = null; if (current == this) current = null; completion.TrySetResult(null); };
        AppWindow.MoveAndResize(bounds);
    }

    internal static async Task<QrSelection?> Select()
    {
        if (Active) return null;
        int epoch = generation;
        var monitors = Native.Monitors();
        int left = monitors.Min(m => m.Bounds.Left), top = monitors.Min(m => m.Bounds.Top);
        int right = monitors.Max(m => m.Bounds.Right), bottom = monitors.Max(m => m.Bounds.Bottom);
        var bounds = new RectInt32(left, top, checked(right - left), checked(bottom - top));
        if ((long)bounds.Width * bounds.Height > QrDecoder.MaximumPixels) throw new InvalidOperationException("屏幕总分辨率过大，暂时无法框选");
        byte[] pixels = await Task.Run(() => BarAppearanceTransition.Capture(bounds)) ?? throw new InvalidOperationException("未能截取屏幕，请重试");
        if (Active || epoch != generation) return null;
        var window = new QrSelectionWindow(bounds, pixels); current = window;
        try
        {
            window.Activate();
            Native.RemoveWindowFrame(WinRT.Interop.WindowNative.GetWindowHandle(window));
            window.AppWindow.MoveAndResize(bounds);
            Native.KeepOnTop(WinRT.Interop.WindowNative.GetWindowHandle(window));
            return await window.completion.Task;
        }
        finally { window.Finish(null); }
    }

    Point Clamp(Point p) => new(Math.Clamp(p.X, 0, root.ActualWidth), Math.Clamp(p.Y, 0, root.ActualHeight));
    Rect Selection(Point p) => new(Math.Min(start.X, p.X), Math.Min(start.Y, p.Y), Math.Abs(start.X - p.X), Math.Abs(start.Y - p.Y));
    void Pressed(object sender, PointerRoutedEventArgs e)
    {
        if (e.GetCurrentPoint(root).Properties.IsRightButtonPressed) { Finish(null); e.Handled = true; return; }
        if (!e.GetCurrentPoint(root).Properties.IsLeftButtonPressed || finished || e.OriginalSource is Button) return;
        start = Clamp(e.GetCurrentPoint(root).Position); dragging = root.CapturePointer(e.Pointer); e.Handled = true;
        Draw(Selection(start));
    }
    void Moved(object sender, PointerRoutedEventArgs e) { if (dragging) Draw(Selection(Clamp(e.GetCurrentPoint(root).Position))); }
    void Released(object sender, PointerRoutedEventArgs e)
    {
        if (!dragging || finished || pixels == null) return;
        var rect = Selection(Clamp(e.GetCurrentPoint(root).Position)); dragging = false; root.ReleasePointerCaptures(); e.Handled = true;
        if (rect.Width < 4 || rect.Height < 4) { Draw(new Rect()); return; }
        int x = Math.Clamp((int)Math.Floor(rect.X * bounds.Width / root.ActualWidth), 0, bounds.Width - 1);
        int y = Math.Clamp((int)Math.Floor(rect.Y * bounds.Height / root.ActualHeight), 0, bounds.Height - 1);
        int right = Math.Clamp((int)Math.Ceiling(rect.Right * bounds.Width / root.ActualWidth), x + 1, bounds.Width);
        int bottom = Math.Clamp((int)Math.Ceiling(rect.Bottom * bounds.Height / root.ActualHeight), y + 1, bounds.Height);
        Finish(new(QrDecoder.Crop(pixels, bounds.Width, bounds.Height, x, y, right - x, bottom - y), right - x, bottom - y));
    }
    void Draw(Rect r)
    {
        Set(shades[0], 0, 0, root.ActualWidth, r.Top);
        Set(shades[1], 0, r.Top, r.Left, r.Height);
        Set(shades[2], r.Right, r.Top, root.ActualWidth - r.Right, r.Height);
        Set(shades[3], 0, r.Bottom, root.ActualWidth, root.ActualHeight - r.Bottom);
        Set(outline, r.X, r.Y, r.Width, r.Height);
    }
    static void Set(FrameworkElement element, double x, double y, double w, double h)
    { Canvas.SetLeft(element, x); Canvas.SetTop(element, y); element.Width = Math.Max(0, w); element.Height = Math.Max(0, h); }
    void Finish(QrSelection? selection)
    {
        if (finished) return;
        finished = true; dragging = false; root.ReleasePointerCaptures(); pixels = null; image.Source = null;
        if (current == this) current = null;
        completion.TrySetResult(selection); Close();
    }
}
