using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Liubai;

public sealed partial class BarWindow : Window
{
    readonly AppearanceStore preferences;
    readonly BarManager? manager;
    internal DesktopMonitor Display { get; private set; }
    readonly Border surface;
    readonly Button settings, shelf;
    readonly StackPanel actions;
    readonly BarAppearanceTransition transition;
    string? renderedAppearance;
    SettingsWindow? settingsWindow;
    readonly AppBarReservation reservation;
    readonly IntPtr handle;
    readonly int originalWorkTop;
    TrayIcon? tray;
    ExpandedWindowMonitor? appearanceMonitor;
    ExpandedSnapshot expandedSnapshot = ExpandedSnapshot.Normal;
    bool rendering, windowShown;
    bool barVisible = true, fullscreen;
    bool closing;

    public BarWindow(AppearanceStore preferences) : this(preferences, Native.Monitors().First(m => m.Primary), null) { }

    internal BarWindow(AppearanceStore preferences, DesktopMonitor display, BarManager? manager)
    {
        this.preferences = preferences;
        this.manager = manager; Display = display;
        Title = "留白 · 玻璃顶栏";
        handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        originalWorkTop = Native.PrimaryMonitor().Work.Top;
        var presenter = (OverlappedPresenter)AppWindow.Presenter;
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsResizable = presenter.IsMaximizable = presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = true;
        AppWindow.IsShownInSwitchers = false;
        // A full-width screen edge has square corners and no floating-window border.
        int cornerPreference = 1;
        Native.DwmSetWindowAttribute(handle, 33, ref cornerPreference, sizeof(int));
        int borderColor = unchecked((int)0xFFFFFFFE);
        Native.DwmSetWindowAttribute(handle, 34, ref borderColor, sizeof(int));

        shelf = CreateToolbarButton("\uE7B8", "收纳盒");
        settings = CreateToolbarButton("\uE713", "设置");
        actions = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 2, Margin = new Thickness(0, 0, 8, 0),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
        };
        actions.Children.Add(shelf);
        actions.Children.Add(settings);
        surface = new Border { Child = actions, BorderThickness = new Thickness(0) };
        var cover = new Image { IsHitTestVisible = false, Stretch = Stretch.Fill, Visibility = Visibility.Collapsed };
        transition = new BarAppearanceTransition(cover);
        var layers = new Grid(); layers.Children.Add(surface); layers.Children.Add(cover);
        Content = layers;
        settings.Click += (_, _) => ToggleSettings();
        surface.ActualThemeChanged += (_, _) =>
        {
            if (!transition.Active && !expandedSnapshot.UsesSolid(preferences.Expanded)) UpdateSurface();
        };
        UpdateSurface();
        ApplyGlass();
        reservation = new AppBarReservation(handle, DispatcherQueue, Position, active =>
        {
            if (closing) return;
            fullscreen = active && Native.MonitorForWindow(Native.GetForegroundWindow()) == Display.Handle;
            UpdateVisibility();
        }, (message, parameter) => manager?.EnvironmentChanged(message, parameter));
        Closed += (_, _) => { if (!closing) Quit(); };
    }

    static Button CreateToolbarButton(string glyph, string name)
    {
        var button = new Button
        {
            Content = new FontIcon { Glyph = glyph, FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = 15 },
            Width = 34, Height = 28, MinWidth = 0, MinHeight = 0, Padding = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Colors.Transparent), BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(7)
        };
        AutomationProperties.SetName(button, name);
        ToolTipService.SetToolTip(button, name);
        return button;
    }

    void SetToolbarColors(ElementTheme theme, SolidColorBrush? ink)
    {
        foreach (var button in new[] { shelf, settings })
        {
            button.RequestedTheme = theme;
            if (ink != null)
            {
                button.Foreground = ink;
                ((FontIcon)button.Content).Foreground = ink;
            }
            else
            {
                button.ClearValue(Control.ForegroundProperty);
                ((FontIcon)button.Content).ClearValue(IconElement.ForegroundProperty);
            }
        }
    }

    public void Start(bool showSettings)
    {
        Position();
        AppWindow.Show(false);
        windowShown = true;
        Native.RemoveWindowFrame(handle);
        UpdateTray();
        appearanceMonitor = new ExpandedWindowMonitor(DispatcherQueue, () => windowShown ? AppWindow.Size.Height : 0,
            snapshot => { expandedSnapshot = snapshot; RenderAppearance(true); UpdateVisibility(); },
            () => Display, () => preferences.Expanded.Mode != ExpandedMode.Custom);
        appearanceMonitor.SetSpeed(preferences.Expanded.Speed);
        appearanceMonitor.SetEnabled(preferences.Expanded.Enabled && !preferences.FollowPaused && manager?.Suspended != true);
        if (showSettings)
        {
            if (surface.IsLoaded) OpenSettings();
            else surface.Loaded += ShowSettingsOnLoad;
        }
        if (Environment.GetCommandLineArgs().Contains("--verify-expanded")) _ = VerifyExpanded();
        else if (Environment.GetCommandLineArgs().Contains("--verify-shell")) _ = VerifyShell();
    }

    void Quit() { if (manager != null) manager.Quit(); else { Shutdown(); WindowsCompositionQueue.ExitApplication(); } }
    internal void Shutdown()
    {
        if (closing) return;
        closing = true; transition.Cancel(); appearanceMonitor?.Dispose(); tray?.Dispose(); tray = null;
        settingsWindow?.Destroy(); reservation.Dispose(); SystemBackdrop = null; Close();
    }
    void UpdateTray()
    {
        if (!Display.Primary) { tray?.Dispose(); tray = null; return; }
        tray ??= new TrayIcon(handle, DispatcherQueue, OpenSettings, ToggleBar, () => barVisible,
            () => preferences.Style, SetStyle, Quit, () => preferences.FollowPaused,
            () => { if (manager != null) manager.TogglePause(); else { preferences.SetFollowPaused(!preferences.FollowPaused); preferences.SaveCurrent(); ApplyExpanded(); } });
    }
    internal void UpdateDisplay(DesktopMonitor display)
    {
        bool changed = Display != display;
        Display = display; UpdateTray();
        if (changed) { transition.Cancel(); Position(); appearanceMonitor?.Refresh(); settingsWindow?.UpdateDisplay(display); }
    }
    internal void RefreshMaterial() => ApplyGlass();
    internal void RefreshEnvironment()
    {
        if (closing) return;
        if (SystemBackdrop is GlassBackdrop glass) glass.Update(preferences.Material);
        if (!transition.Active) RenderAppearance();
        appearanceMonitor?.Refresh();
    }
    internal void RefreshHeight() => Position();
    internal void RefreshSettings() { ApplyExpanded(); ApplyGlass(); Position(); }
    internal void RefreshPanel() => settingsWindow?.RefreshStyle(preferences.Style);
    internal bool Monitoring => appearanceMonitor?.Enabled == true;
    internal int SampleCount => appearanceMonitor?.SampleCount ?? 0;
    internal bool TrayRegistered => tray?.IsRegistered == true;
    internal Windows.Graphics.RectInt32 BarRectangle => new(AppWindow.Position.X, AppWindow.Position.Y, AppWindow.Size.Width, AppWindow.Size.Height);
    internal void HideSettings() => settingsWindow?.Hide();
    internal void RequestSettings()
    {
        if (surface.IsLoaded) OpenSettings(); else surface.Loaded += ShowSettingsOnLoad;
    }
    internal void Recover(bool suspended)
    {
        if (closing) return;
        transition.Cancel(); appearanceMonitor?.SetEnabled(false);
        if (suspended) { settingsWindow?.Hide(); return; }
        // A new controller reconnects to DWM after resume/theme/composition changes.
        SystemBackdrop = null; renderedAppearance = null;
        Position(); ApplyExpanded(); appearanceMonitor?.Refresh();
    }

    void ShowSettingsOnLoad(object sender, RoutedEventArgs args)
    {
        surface.Loaded -= ShowSettingsOnLoad;
        // Complex controls must be created after the bar's Loaded/layout pass completes.
        DispatcherQueue.TryEnqueue(OpenSettings);
    }

    void Position()
    {
        if (closing || !barVisible || !Native.MonitorExists(Display.Handle)) return;
        actions.Visibility = preferences.BarHeight >= 24 ? Visibility.Visible : Visibility.Collapsed;
        shelf.Height = settings.Height = Math.Min(28, preferences.BarHeight);
        var approved = reservation.Reserve(Display.BarBounds(preferences.BarHeight));
        var actual = new Windows.Graphics.RectInt32(AppWindow.Position.X, AppWindow.Position.Y, AppWindow.Size.Width, AppWindow.Size.Height);
        if (!Native.Same(approved, actual)) { transition.Cancel(); AppWindow.MoveAndResize(approved); }
    }

    void UpdateSurface() => RenderAppearance();

    void RenderAppearance(bool animate = false)
    {
        if (rendering || closing) return;
        string key = expandedSnapshot.UsesSolid(preferences.Expanded)
            ? "solid:" + WindowColorAnalysis.Background(preferences.Expanded, expandedSnapshot)
            : "glass:" + preferences.Material + ":" + surface.ActualTheme;
        if (animate && key == renderedAppearance) return;
        bool fading = animate && windowShown && surface.IsLoaded && transition.Begin(
            new(AppWindow.Position.X, AppWindow.Position.Y, AppWindow.Size.Width, AppWindow.Size.Height), preferences.Expanded.Animation);
        if (!fading) transition.Cancel();
        rendering = true;
        try
        {
        // Keep native acrylic warm behind the opaque maximized surface. Returning
        // to glass then reveals an existing material instead of reconnecting DWM.
        if (SystemBackdrop is GlassBackdrop warmGlass) warmGlass.Update(preferences.Material);
        else SystemBackdrop = new GlassBackdrop(preferences.Material);
        if (expandedSnapshot.UsesSolid(preferences.Expanded))
        {
            uint rgb = WindowColorAnalysis.Background(preferences.Expanded, expandedSnapshot);
            bool white = WindowColorAnalysis.WhiteForeground(rgb);
            // Only the button theme changes; the acrylic/root theme stays stable.
            surface.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
            surface.BorderThickness = new Thickness(0);
            var ink = new SolidColorBrush(white ? Colors.White : Colors.Black);
            SetToolbarColors(white ? ElementTheme.Dark : ElementTheme.Light, ink);
            return;
        }
        surface.RequestedTheme = ElementTheme.Default;
        SetToolbarColors(ElementTheme.Default, null);
        bool dark = surface.ActualTheme == ElementTheme.Dark;
        surface.BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(dark ? (byte)32 : (byte)24, dark ? (byte)255 : (byte)34, dark ? (byte)255 : (byte)66, dark ? (byte)255 : (byte)56));
        surface.Background = preferences.Material.NoBase || DesktopAcrylicController.IsSupported() ? new SolidColorBrush(Colors.Transparent)
            : new SolidColorBrush(dark ? Windows.UI.Color.FromArgb(255, 31, 39, 38) : Windows.UI.Color.FromArgb(255, 239, 245, 242));
        if (SystemBackdrop is GlassBackdrop glass) glass.Update(preferences.Material);
        else SystemBackdrop = new GlassBackdrop(preferences.Material);
        surface.BorderThickness = new Thickness(0, 0, 0, preferences.Material.ShowLine ? 1 : 0);
        }
        finally
        {
            rendering = false; renderedAppearance = key;
            if (fading) transition.Complete(preferences.Expanded.Animation,
                () => SystemBackdrop is not GlassBackdrop backdrop || backdrop.IsReady);
        }
    }

    void ApplyGlass() => RenderAppearance();

    void ApplyExpanded()
    {
        appearanceMonitor?.SetEnabled(preferences.Expanded.Enabled && !preferences.FollowPaused && manager?.Suspended != true);
        appearanceMonitor?.SetSpeed(preferences.Expanded.Speed);
        if (appearanceMonitor != null) expandedSnapshot = appearanceMonitor.Current;
        RenderAppearance();
        UpdateVisibility();
    }

    void UpdateVisibility()
    {
        if (closing) return;
        bool show = barVisible && (!fullscreen || preferences.Expanded.Enabled);
        if (show != windowShown)
        {
            transition.Cancel();
            if (show) { Position(); AppWindow.Show(false); Native.RemoveWindowFrame(handle); }
            else AppWindow.Hide();
            windowShown = show;
        }
        if (show && fullscreen) Native.KeepOnTop(handle);
    }

    void ToggleSettings()
    {
        if (settingsWindow?.IsOpen == true) settingsWindow.Hide();
        else OpenSettings();
    }

    void OpenSettings()
    {
        manager?.HideOtherPanels(this);
        settingsWindow ??= new SettingsWindow(preferences,
            () => { if (manager != null) manager.ApplyMaterial(); else ApplyGlass(); },
            () => { if (manager != null) manager.ApplyHeight(); else ApplyHeight(); },
            () => { if (manager != null) manager.ApplySettings(); else ApplyExpanded(); }, Quit, Display);
        settingsWindow.UpdateDisplay(Display);
        settingsWindow.RefreshStyle(preferences.Style);
        settingsWindow.Open(windowShown ? AppWindow.Position.Y + AppWindow.Size.Height : Display.Info.Work.Top);
    }
    void ToggleBar()
    {
        if (manager != null) { manager.ToggleVisibility(); return; }
        SetVisible(!barVisible);
    }
    internal void SetVisible(bool visible)
    {
        barVisible = visible;
        if (barVisible) Position();
        else { settingsWindow?.Hide(); reservation.Release(); }
        UpdateVisibility();
    }

    void ApplyHeight()
    {
        Position();
    }

    void SetStyle(GlassStyle style)
    {
        try { preferences.Save(style); if (manager != null) manager.ApplyMaterial(); else ApplyGlass(); settingsWindow?.RefreshStyle(style); }
        catch { OpenSettings(); settingsWindow?.ShowSaveError(); }
    }

    async Task VerifyShell()
    {
        string result = "PASS", error = "";
        int residentTop = 0, hiddenTop = 0, restoredTop = 0;
        bool trayRegistered = false;
        int originalHeight = preferences.BarHeight;
        var heightChecks = new List<object>();
        try
        {
            await Task.Delay(900);
            residentTop = Native.PrimaryMonitor().Work.Top;
            if (residentTop < AppWindow.Position.Y + AppWindow.Size.Height)
                throw new InvalidOperationException("Top workspace was not reserved.");
            trayRegistered = tray?.IsRegistered == true;
            if (!trayRegistered) throw new InvalidOperationException("Tray icon was not registered.");
            OpenSettings();
            await Task.Delay(750);
            Activate();
            await Task.Delay(250);
            if (settingsWindow?.IsOpen == true)
                throw new InvalidOperationException("Panel did not dismiss when focus moved outside.");
            OpenSettings();
            await Task.Delay(250);
            settingsWindow?.Hide();
            foreach (int height in new[] { AppearanceStore.MinimumHeight, 12, 24, 28, 72, AppearanceStore.MaximumHeight, originalHeight })
            {
                preferences.SetHeight(height);
                ApplyHeight();
                await Task.Delay(250);
                int expected = Native.BarBounds(handle, height).Height;
                int workTop = Native.PrimaryMonitor().Work.Top;
                if (AppWindow.Size.Height != expected || workTop != AppWindow.Position.Y + expected)
                    throw new InvalidOperationException($"Bar height/workspace did not update for {height}.");
                heightChecks.Add(new { height, physicalHeight = expected, workTop });
            }
            ToggleBar();
            await Task.Delay(250);
            hiddenTop = Native.PrimaryMonitor().Work.Top;
            if (hiddenTop != originalWorkTop) throw new InvalidOperationException("Hiding the bar did not release workspace.");
            ToggleBar();
            await Task.Delay(250);
            if (Native.PrimaryMonitor().Work.Top != residentTop) throw new InvalidOperationException("Showing the bar did not restore workspace.");
            foreach (GlassStyle style in Enum.GetValues<GlassStyle>())
            {
                SystemBackdrop = new GlassBackdrop(style == GlassStyle.Custom ? preferences.Custom : MaterialOptions.ForPreset(style));
                await Task.Delay(200);
            }
            if (SystemBackdrop is GlassBackdrop customGlass)
            {
                customGlass.Update(new MaterialOptions(true, 0, 0, false, "#7CAEDD", false));
                await Task.Delay(200);
                customGlass.Update(new MaterialOptions(false, 1, 1, false, "#231A38"));
                await Task.Delay(200);
                customGlass.Update(preferences.Material);
            }
            reservation.Dispose();
            await Task.Delay(200);
            restoredTop = Native.PrimaryMonitor().Work.Top;
            if (restoredTop != originalWorkTop) throw new InvalidOperationException("Workspace was not restored.");
        }
        catch (Exception ex) { result = "FAIL"; error = ex.ToString(); }
        finally
        {
            Directory.CreateDirectory(App.DataDirectory);
            preferences.SetHeight(originalHeight);
            File.WriteAllText(Path.Combine(App.DataDirectory, "shell-check.json"), System.Text.Json.JsonSerializer.Serialize(new { result, error, acrylicSupported = DesktopAcrylicController.IsSupported(), trayRegistered, heightChecks, originalWorkTop, residentTop, hiddenTop, restoredTop }));
            Close();
        }
    }
}
