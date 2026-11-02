using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Win32;
using Windows.Graphics;

namespace Liubai.Modern;

public sealed class DesktopBar : Window
{
    readonly AutoHideState hiding = new();
    readonly DispatcherQueueTimer timer;
    readonly TextBlock count;
    readonly Button todoButton;
    readonly PanelWindow panel;
    readonly BarSettings barSettings;
    readonly AppBarReservation reservation;
    readonly int initialWorkingTop;
    readonly bool inspect;

    public DesktopBar(TodoStore store, bool inspect)
    {
        this.inspect = inspect;
        initialWorkingTop = Native.PrimaryMonitor().Work.Top;
        barSettings = new BarSettings(Path.Combine(Path.GetDirectoryName(store.FilePath)!, "display.json"));
        Title = "留白 · 桌面栏";
        Shell.Configure(this, inspect);
        reservation = new AppBarReservation(WinRT.Interop.WindowNative.GetWindowHandle(this), DispatcherQueue, Position);
        var root = new Grid { Padding = new Thickness(16, 0, 8, 0) };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var brand = UI.Text("留白", 12, .75); brand.CharacterSpacing = 160; brand.VerticalAlignment = VerticalAlignment.Center;
        root.Children.Add(brand);
        var todoContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, VerticalAlignment = VerticalAlignment.Center };
        todoContent.Children.Add(Icons.SquareCheck(17));
        count = UI.Text("", 12); count.VerticalAlignment = VerticalAlignment.Center;
        todoContent.Children.Add(count);
        todoButton = new Button { Content = todoContent, Background = new SolidColorBrush(Colors.Transparent), BorderThickness = new Thickness(0), Padding = new Thickness(10, 4, 10, 4), MinHeight = 28 };
        ToolTipService.SetToolTip(todoButton, "待办");
        Grid.SetColumn(todoButton, 2); root.Children.Add(todoButton);
        var settings = new Button { Content = Icons.Symbol("\uE713", 16), Background = new SolidColorBrush(Colors.Transparent), BorderThickness = new Thickness(0), Padding = new Thickness(10, 4, 10, 4), MinHeight = 28 };
        AutomationProperties.SetName(settings, "设置"); ToolTipService.SetToolTip(settings, "设置");
        Grid.SetColumn(settings, 3); root.Children.Add(settings);
        var surface = new Border { Child = root, BorderThickness = new Thickness(0, 0, 0, 1) };
        UI.BindBrush(surface, Border.BorderBrushProperty, "LiubaiBorderBrush"); Content = surface;
        panel = new PanelWindow(store, UpdateCount, Quit, () => barSettings.AutoHide, SetAutoHide, inspect);
        todoButton.Click += (_, _) => OpenPanel(false);
        settings.Click += (_, _) => OpenPanel(true);
        Shell.Backdrop(this);
        UpdateCount(); Position();
        timer = DispatcherQueue.CreateTimer(); timer.Interval = TimeSpan.FromMilliseconds(50);
        DispatcherQueue.ShutdownStarting += (_, _) => App.Trace("Dispatcher shutdown starting");
        timer.Tick += (_, _) =>
        {
            if (App.IsExiting) return;
            if (reservation.IsReserved && !AppBarReservation.Same(reservation.Bounds, new RectInt32(AppWindow.Position.X, AppWindow.Position.Y, AppWindow.Size.Width, AppWindow.Size.Height))) Position();
            var bounds = Native.PrimaryMonitor().Monitor;
            if (!Native.GetCursorPos(out var pointer)) return;
            bool edge = pointer.Y == bounds.Top && pointer.X >= bounds.Left && pointer.X < bounds.Right;
            bool onBar = AppWindow.IsVisible && ContainsPointer(AppWindow, pointer);
            // Include the narrow gap below the bar so the pointer can reach the panel.
            bool onPanel = panel.IsOpen && ContainsPointer(panel.AppWindow, pointer, AppWindow.Position.Y + AppWindow.Size.Height);
            bool visible = hiding.Update(edge, onBar || onPanel, panel.IsDialogOpen || (panel.IsOpen && panel.IsSettings) || inspect, barSettings.AutoHide);
            if (visible && !AppWindow.IsVisible) { Position(); AppWindow.Show(false); }
            else if (!visible)
            {
                panel.Hide();
                if (AppWindow.IsVisible) AppWindow.Hide();
            }
        };
        timer.Start();
        if (inspect)
        {
            hiding.Reveal(); AppWindow.Show(false);
            OpenPanel(Environment.GetCommandLineArgs().Contains("--show-settings"));
        }
        else if (!barSettings.AutoHide) { hiding.Reveal(); AppWindow.Show(false); }
        else AppWindow.Hide();
        Closed += (_, _) => { App.Trace("Bar closed"); timer.Stop(); if (!App.IsExiting) Quit(); };
        if (inspect && Environment.GetCommandLineArgs().Contains("--verify-reservation") && Environment.GetCommandLineArgs().Contains("--data-dir"))
            _ = VerifyReservation();
    }
    async Task VerifyReservation()
    {
        string output = Path.Combine(Path.GetDirectoryName(panel.Store.FilePath)!, "appbar-check.json");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            if (!SetAutoHide(false)) throw new Exception("Could not enable resident mode.");
            await Task.Delay(600);
            int residentTop = Native.PrimaryMonitor().Work.Top;
            if (!reservation.IsReserved || residentTop < reservation.Bounds.Y + reservation.Bounds.Height)
                throw new Exception("Shell did not reserve the top work area.");
            var actual = new RectInt32(AppWindow.Position.X, AppWindow.Position.Y, AppWindow.Size.Width, AppWindow.Size.Height);
            if (!AppBarReservation.Same(actual, reservation.Bounds)) throw new Exception("Bar moved outside its reserved rectangle.");
            if (!SetAutoHide(true)) throw new Exception("Could not enable auto-hide mode.");
            await Task.Delay(400);
            int autoHideTop = Native.PrimaryMonitor().Work.Top;
            if (reservation.IsReserved || autoHideTop != initialWorkingTop) throw new Exception("Auto-hide did not restore the work area.");
            if (!SetAutoHide(false)) throw new Exception("Could not re-enable resident mode.");
            await Task.Delay(400);
            reservation.Dispose();
            int disposedTop = Native.PrimaryMonitor().Work.Top;
            if (disposedTop != initialWorkingTop) throw new Exception("Disposal did not restore the work area.");
            File.WriteAllText(output, System.Text.Json.JsonSerializer.Serialize(new { result = "PASS", initialWorkingTop, residentTop, autoHideTop, disposedTop }));
        }
        catch (Exception error) { Directory.CreateDirectory(Path.GetDirectoryName(output)!); File.WriteAllText(output, System.Text.Json.JsonSerializer.Serialize(new { result = "FAIL", error = error.Message })); }
        finally { Quit(); }
    }
    bool SetAutoHide(bool enabled)
    {
        bool previous = barSettings.AutoHide;
        try
        {
            barSettings.Save(enabled);
            Position();
            hiding.Reveal();
            if (!AppWindow.IsVisible) { Position(); AppWindow.Show(false); }
            return true;
        }
        catch
        {
            try { if (barSettings.AutoHide != previous) { barSettings.Save(previous); Position(); } } catch { }
            return false;
        }
    }
    void Quit()
    {
        if (App.IsExiting) return;
        App.IsExiting = true; timer.Stop(); reservation.Dispose(); App.Trace("Exit requested"); Application.Current.Exit();
    }
    static bool ContainsPointer(AppWindow window, Native.Point pointer, int? extendTop = null)
    {
        var position = window.Position; var size = window.Size;
        return pointer.X >= position.X && pointer.X < position.X + size.Width
            && pointer.Y >= (extendTop ?? position.Y) && pointer.Y < position.Y + size.Height;
    }
    void UpdateCount()
    {
        int pending = panel.Store.Data.Items.Count(x => !x.Done);
        count.Text = pending.ToString(); count.Visibility = pending > 0 ? Visibility.Visible : Visibility.Collapsed;
        AutomationProperties.SetName(todoButton, "待办，" + pending + " 项未完成");
    }
    void Position()
    {
        var monitor = Native.PrimaryMonitor().Monitor;
        float scale = Native.GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96F;
        var requested = new RectInt32(monitor.Left, monitor.Top, monitor.Right - monitor.Left, (int)(36 * scale));
        var bounds = reservation.Update(!barSettings.AutoHide, requested);
        var current = new RectInt32(AppWindow.Position.X, AppWindow.Position.Y, AppWindow.Size.Width, AppWindow.Size.Height);
        if (!AppBarReservation.Same(current, bounds)) AppWindow.MoveAndResize(bounds);
    }
    void OpenPanel(bool settings)
    {
        if (App.IsExiting) return;
        hiding.Reveal();
        Position(); AppWindow.Show(false);
        if (panel.IsOpen && panel.IsSettings == settings) { panel.Hide(); return; }
        panel.Open(settings, AppWindow.Position.Y + AppWindow.Size.Height);
    }
}

internal static class Icons
{
    public static FontIcon Symbol(string glyph, double size = 16) => new() { Glyph = glyph, FontSize = size, FontFamily = new FontFamily("Segoe Fluent Icons") };
    public static Viewbox SquareCheck(double size)
    {
        var grid = new Grid { Width = 24, Height = 24 };
        var box = new Border { Margin = new Thickness(3), BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(2) };
        UI.BindBrush(box, Border.BorderBrushProperty, "LiubaiTextBrush"); grid.Children.Add(box);
        var check = new Microsoft.UI.Xaml.Shapes.Path
        {
            Data = new PathGeometry { Figures = { new PathFigure { StartPoint = new Windows.Foundation.Point(9, 12), Segments = { new LineSegment { Point = new Windows.Foundation.Point(11, 14) }, new LineSegment { Point = new Windows.Foundation.Point(15, 10) } } } } },
            StrokeThickness = 2, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round
        };
        UI.BindBrush(check, Microsoft.UI.Xaml.Shapes.Path.StrokeProperty, "LiubaiTextBrush"); grid.Children.Add(check);
        return new Viewbox { Width = size, Height = size, Child = grid };
    }
}

internal static class UI
{
    sealed class Bindings { public Dictionary<DependencyProperty, string?> Values = new(); public bool Updating; }
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<FrameworkElement, Bindings> bindings = new();
    public static void BindBrush(FrameworkElement element, DependencyProperty property, string? key)
    {
        if (!bindings.TryGetValue(element, out var values))
        {
            values = new Bindings(); bindings.Add(element, values);
            element.Loaded += (_, _) => Refresh(element);
            element.ActualThemeChanged += (_, _) => Refresh(element);
        }
        values.Values[property] = key; Refresh(element);
    }
    static void Refresh(FrameworkElement element)
    {
        if (!bindings.TryGetValue(element, out var values) || values.Updating) return;
        values.Updating = true;
        try
        {
            foreach (var entry in values.Values)
            {
                var brush = entry.Value == null ? new SolidColorBrush(Colors.Transparent) : ThemeBrush(entry.Value, element.ActualTheme);
                if (element is Panel panel && entry.Key == Panel.BackgroundProperty) panel.Background = brush;
                else if (element is Border border && entry.Key == Border.BackgroundProperty) border.Background = brush;
                else if (element is Border edge && entry.Key == Border.BorderBrushProperty) edge.BorderBrush = brush;
                else if (element is TextBlock text && entry.Key == TextBlock.ForegroundProperty) text.Foreground = brush;
                else if (element is FontIcon icon && entry.Key == FontIcon.ForegroundProperty) icon.Foreground = brush;
                else if (element is Control control && entry.Key == Control.BackgroundProperty) control.Background = brush;
                else if (element is Control foreground && entry.Key == Control.ForegroundProperty) foreground.Foreground = brush;
                else if (element is Microsoft.UI.Xaml.Shapes.Shape shape && entry.Key == Microsoft.UI.Xaml.Shapes.Shape.StrokeProperty) shape.Stroke = brush;
                else element.SetValue(entry.Key, brush);
            }
        }
        finally { values.Updating = false; }
    }
    static Brush ThemeBrush(string key, ElementTheme theme)
    {
        string name = theme == ElementTheme.Dark ? "Dark" : "Light";
        foreach (var dictionary in Application.Current.Resources.MergedDictionaries.Reverse())
            if (dictionary.ThemeDictionaries.TryGetValue(name, out var themed) && themed is ResourceDictionary resources && resources.TryGetValue(key, out var value) && value is Brush brush) return brush;
        return Brush(key, 255, 100, 116, 111);
    }
    public static void ApplyTheme(FrameworkElement root, string theme) => root.RequestedTheme = theme == "dark" ? ElementTheme.Dark : theme == "light" ? ElementTheme.Light : ElementTheme.Default;
    public static Border Card(UIElement content)
    {
        var card = new Border { Child = content, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1) };
        BindBrush(card, Border.BackgroundProperty, "LiubaiSurfaceBrush"); BindBrush(card, Border.BorderBrushProperty, "LiubaiBorderBrush"); return card;
    }
    public static Brush Brush(string key, byte a, byte r, byte g, byte b)
    {
        try { if (Application.Current.Resources[key] is Brush brush) return brush; } catch { }
        return new SolidColorBrush(Windows.UI.Color.FromArgb(a, r, g, b));
    }
    public static TextBlock Text(string text, double size = 14, double opacity = 1)
    {
        var block = new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Microsoft YaHei UI") };
        BindBrush(block, TextBlock.ForegroundProperty, opacity < 1 ? "LiubaiMutedBrush" : "LiubaiTextBrush"); return block;
    }
}

internal static class Shell
{
    public static void Configure(Window window, bool inspect)
    {
        var presenter = (OverlappedPresenter)window.AppWindow.Presenter;
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsResizable = false; presenter.IsMinimizable = false; presenter.IsMaximizable = false;
        presenter.IsAlwaysOnTop = true;
        window.AppWindow.IsShownInSwitchers = inspect;
        int preference = 2;
        Native.DwmSetWindowAttribute(WinRT.Interop.WindowNative.GetWindowHandle(window), 33, ref preference, sizeof(int));
    }
    public static void Backdrop(Window window)
    {
        bool solid = !Microsoft.UI.Composition.SystemBackdrops.DesktopAcrylicController.IsSupported();
        if (!solid && window.SystemBackdrop is not DesktopAcrylicBackdrop) window.SystemBackdrop = new DesktopAcrylicBackdrop();
        if (window.Content is Border border) UI.BindBrush(border, Border.BackgroundProperty, solid ? "LiubaiCanvasBrush" : null);
    }
    public static bool StartupEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            return key?.GetValue("Liubai") != null;
        }
        catch { return false; }
    }
    public static void SetStartup(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if (enabled) key.SetValue("Liubai", "\"" + Environment.ProcessPath + "\" --startup");
        else key.DeleteValue("Liubai", false);
    }
}

internal static class Native
{
    [StructLayout(LayoutKind.Sequential)] public struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct MonitorInfo { public int Size; public Rect Monitor, Work; public int Flags; }
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(Point point, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
    public static MonitorInfo PrimaryMonitor()
    {
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        GetMonitorInfo(MonitorFromPoint(default, 1), ref info); return info;
    }
}
