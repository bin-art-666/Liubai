using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.System;

namespace Liubai;

internal sealed class GuardAlertPresenter
{
    readonly List<GuardAlertWindow> windows = [];
    GuardOptions? current;
    Action? dismiss;
    IntPtr previousForeground;
    internal bool Visible => windows.Count > 0;
    internal int Count => windows.Count;
    internal bool Blackout => Visible && current?.Mode == GuardAlertMode.Blackout;
    internal void Show(GuardOptions options, Action dismissed)
    {
        if (Visible) return;
        current = options; dismiss = dismissed; previousForeground = Native.GetForegroundWindow();
        try { CreateWindows(); }
        catch { Hide(); throw; }
    }
    void CreateWindows(bool listen = true)
    {
        var monitors = Native.Monitors();
        var active = Native.MonitorForWindow(previousForeground);
        var targets = current!.Mode == GuardAlertMode.Blackout ? monitors : monitors.Where(m => m.Handle == active).DefaultIfEmpty(monitors.First(m => m.Primary)).ToArray();
        foreach (var monitor in targets)
        {
            var window = new GuardAlertWindow(monitor, current, () => dismiss?.Invoke()); windows.Add(window); window.ShowAlert();
        }
        windows.FirstOrDefault(w => w.Monitor == active)?.Activate();
        if (listen) windows.FirstOrDefault()?.ListenForEnter();
    }
    internal void Refresh()
    {
        if (!Visible) return;
        var old = windows.ToArray(); windows.Clear();
        try { CreateWindows(false); }
        catch
        {
            foreach (var window in windows.ToArray()) window.Destroy();
            windows.Clear(); windows.AddRange(old); return;
        }
        foreach (var window in old) window.Destroy();
        windows.FirstOrDefault()?.ListenForEnter();
    }
    internal void Hide()
    {
        bool wasVisible = Visible;
        foreach (var window in windows.ToArray()) window.Destroy();
        windows.Clear(); current = null; dismiss = null;
        if (wasVisible && previousForeground != IntPtr.Zero) SetForegroundWindow(previousForeground);
        previousForeground = IntPtr.Zero;
    }
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);
}

internal sealed class GuardAlertWindow : Window
{
    const int Hotkey = 0x4C42;
    readonly IntPtr handle;
    readonly Action dismiss;
    readonly Procedure procedure;
    bool closing, registered, subclassed;
    internal IntPtr Monitor { get; }
    internal GuardAlertWindow(DesktopMonitor monitor, GuardOptions options, Action dismiss)
    {
        Monitor = monitor.Handle; this.dismiss = dismiss; Title = "留白 · 屏幕守护提醒";
        handle = WinRT.Interop.WindowNative.GetWindowHandle(this); procedure = Handle;
        var presenter = (OverlappedPresenter)AppWindow.Presenter;
        presenter.SetBorderAndTitleBar(false, false); presenter.IsAlwaysOnTop = true;
        presenter.IsResizable = presenter.IsMinimizable = presenter.IsMaximizable = false;
        AppWindow.IsShownInSwitchers = false;
        bool black = options.Mode == GuardAlertMode.Blackout;
        var panel = new StackPanel { Spacing = 18, MaxWidth = 600, Padding = new Thickness(28), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var message = new TextBlock { Text = options.Message, FontSize = black ? 28 : 22, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap };
        if (black) message.Foreground = new SolidColorBrush(Colors.White);
        double logicalHeight = (monitor.Bounds.Bottom - monitor.Bounds.Top) / (monitor.Dpi / 96d);
        panel.Children.Add(new ScrollViewer { Content = message, MaxHeight = Math.Max(40, Math.Min(black ? 400 : 125, logicalHeight - 190)), HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        panel.Children.Add(new TextBlock { Text = "按 Enter 解除 · 解除后冷却 30 秒", FontSize = 12, TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center, Opacity = .7 });
        if (black) foreach (var text in panel.Children.OfType<TextBlock>()) text.Foreground = new SolidColorBrush(Colors.White);
        var close = new Button { Content = "解除（Enter）", HorizontalAlignment = HorizontalAlignment.Center };
        close.Click += (_, _) => dismiss(); panel.Children.Add(close);
        var root = new Grid { Background = new SolidColorBrush(black ? Colors.Black : Colors.Transparent) }; root.Children.Add(panel); Content = root;
        root.KeyDown += (_, e) => { if (e.Key is VirtualKey.Enter or VirtualKey.Escape) { e.Handled = true; dismiss(); } };
        if (!black) SystemBackdrop = new GlassBackdrop(GlassStyle.Frosted);
        double scale = monitor.Dpi / 96d;
        var bounds = monitor.Bounds;
        int width = black ? bounds.Right - bounds.Left : Math.Min((int)(500 * scale), bounds.Right - bounds.Left);
        int height = black ? bounds.Bottom - bounds.Top : Math.Min((int)(300 * scale), bounds.Bottom - bounds.Top);
        AppWindow.MoveAndResize(new RectInt32(bounds.Left + (bounds.Right - bounds.Left - width) / 2, bounds.Top + (bounds.Bottom - bounds.Top - height) / 2, width, height));
        int corner = black ? 1 : 2, border = unchecked((int)0xFFFFFFFE);
        Native.DwmSetWindowAttribute(handle, 33, ref corner, sizeof(int)); Native.DwmSetWindowAttribute(handle, 34, ref border, sizeof(int));
        AppWindow.Closing += (_, e) => { if (!closing) { e.Cancel = true; dismiss(); } };
        Closed += (_, _) => Release();
    }
    internal void ShowAlert() { AppWindow.Show(false); Activate(); Native.RemoveWindowFrame(handle); }
    internal void ListenForEnter()
    {
        subclassed = SetWindowSubclass(handle, procedure, new UIntPtr(0x4C42), UIntPtr.Zero);
        if (subclassed) registered = RegisterHotKey(handle, Hotkey, 0x4000, 0x0D);
    }
    IntPtr Handle(IntPtr window, uint message, IntPtr w, IntPtr l, UIntPtr id, UIntPtr data)
    {
        if (!closing && message == 0x0312 && w.ToInt32() == Hotkey) { DispatcherQueue.TryEnqueue(() => dismiss()); return IntPtr.Zero; }
        return DefSubclassProc(window, message, w, l);
    }
    void Release()
    {
        if (registered) { UnregisterHotKey(handle, Hotkey); registered = false; }
        if (subclassed) { RemoveWindowSubclass(handle, procedure, new UIntPtr(0x4C42)); subclassed = false; }
    }
    internal void Destroy() { if (closing) return; closing = true; Release(); Close(); }
    delegate IntPtr Procedure(IntPtr h, uint message, IntPtr w, IntPtr l, UIntPtr id, UIntPtr data);
    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr h, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr h, int id);
    [DllImport("comctl32.dll")] static extern bool SetWindowSubclass(IntPtr h, Procedure p, UIntPtr id, UIntPtr data);
    [DllImport("comctl32.dll")] static extern bool RemoveWindowSubclass(IntPtr h, Procedure p, UIntPtr id);
    [DllImport("comctl32.dll")] static extern IntPtr DefSubclassProc(IntPtr h, uint message, IntPtr w, IntPtr l);
}
