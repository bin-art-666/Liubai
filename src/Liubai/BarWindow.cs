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
    readonly Button settings, shelf, todo, quickNote, screenshot, qrScan;
    readonly ToolTip screenshotTip;
    readonly Microsoft.UI.Dispatching.DispatcherQueueTimer screenshotTipTimer;
    readonly StackPanel actions;
    readonly BarAppearanceTransition transition;
    string? renderedAppearance;
    SettingsWindow? settingsWindow;
    PlannerWindow? plannerWindow;
    bool openingPlanner;
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

        screenshot = CreateToolbarButton(null, "截图");
        screenshot.Content = CreateVectorIcon(screenshot,
            "M 4 7 L 7 7 L 9 4 L 15 4 L 17 7 L 20 7 C 21.1 7 22 7.9 22 9 L 22 18 C 22 19.1 21.1 20 20 20 L 4 20 C 2.9 20 2 19.1 2 18 L 2 9 C 2 7.9 2.9 7 4 7 Z "
            + "M 16 13 C 16 15.2 14.2 17 12 17 C 9.8 17 8 15.2 8 13 C 8 10.8 9.8 9 12 9 C 14.2 9 16 10.8 16 13 Z", "1.4");
        screenshotTip = new ToolTip { Content = "一键截取整个屏幕", MaxWidth = 340, PlacementTarget = screenshot,
            Placement = Microsoft.UI.Xaml.Controls.Primitives.PlacementMode.Bottom };
        ToolTipService.SetToolTip(screenshot, screenshotTip);
        screenshotTipTimer = DispatcherQueue.CreateTimer(); screenshotTipTimer.IsRepeating = false;
        screenshotTipTimer.Interval = TimeSpan.FromSeconds(3);
        screenshotTipTimer.Tick += (_, _) => { screenshotTip.IsOpen = false; screenshotTip.Content = "一键截取整个屏幕"; };
        qrScan = CreateToolbarButton(null, "扫码");
        qrScan.Content = CreateVectorIcon(qrScan,
            "M 8 3 L 3 3 L 3 8 M 16 3 L 21 3 L 21 8 M 3 16 L 3 21 L 8 21 M 21 16 L 21 21 L 16 21 "
            + "M 7 7 L 10 7 L 10 10 L 7 10 Z M 14 7 L 17 7 L 17 10 L 14 10 Z M 7 14 L 10 14 L 10 17 L 7 17 Z M 14 14 L 17 14 L 17 17 M 14 17 L 14 19 M 19 12 L 19 14", "1.4");
        todo = CreateToolbarButton(null, "待办");
        todo.Content = CreateVectorIcon(todo,
            "M 8 5 L 6 5 C 4.9 5 4 5.9 4 7 L 4 19 C 4 20.1 4.9 21 6 21 L 18 21 C 19.1 21 20 20.1 20 19 L 20 7 C 20 5.9 19.1 5 18 5 L 16 5 "
            + "M 9 3 L 15 3 C 15.6 3 16 3.4 16 4 L 16 7 L 8 7 L 8 4 C 8 3.4 8.4 3 9 3 Z M 8 14 L 11 17 L 16 12", "1.4");
        quickNote = CreateToolbarButton(null, "随手记");
        quickNote.Content = CreateVectorIcon(quickNote,
            "M 11 4 L 6 4 C 4.9 4 4 4.9 4 6 L 4 18 C 4 19.1 4.9 20 6 20 L 18 20 C 19.1 20 20 19.1 20 18 L 20 13 "
            + "M 10 11 L 17 4 C 17.8 3.2 18.8 3.2 19.6 4 L 20 4.4 C 20.8 5.2 20.8 6.2 20 7 L 13 14 L 9 15 Z M 16 5 L 19 8");
        shelf = CreateToolbarButton(null, "收纳盒");
        shelf.Content = CreateShelfIcon(shelf);
        InitializeStationToolbar();
        settings = CreateToolbarButton("\uE713", "设置");
        actions = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 2, Margin = new Thickness(0, 0, 8, 0),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
        };
        actions.Children.Add(screenshot);
        actions.Children.Add(qrScan);
        actions.Children.Add(todo);
        actions.Children.Add(quickNote);
        actions.Children.Add(shelf);
        actions.Children.Add(settings);
        surface = new Border { Child = actions, BorderThickness = new Thickness(0) };
        var cover = new Image { IsHitTestVisible = false, Stretch = Stretch.Fill, Visibility = Visibility.Collapsed };
        transition = new BarAppearanceTransition(cover);
        var layers = new Grid(); layers.Children.Add(surface); layers.Children.Add(cover);
        Content = layers;
        settings.Click += (_, _) => ToggleSettings();
        todo.Click += (_, _) => OpenPlanner(PlannerPage.Tasks);
        quickNote.Click += (_, _) => OpenPlanner(PlannerPage.Notes);
        screenshot.Click += async (_, _) => await TakeScreenshot();
        qrScan.Click += async (_, _) => await ScanQr();
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

    static Button CreateToolbarButton(string? glyph, string name)
    {
        var button = new Button
        {
            Content = glyph == null ? null : new FontIcon { Glyph = glyph, FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = 15 },
            Width = 34, Height = 28, MinWidth = 0, MinHeight = 0, Padding = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Colors.Transparent), BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(7)
        };
        AutomationProperties.SetName(button, name);
        ToolTipService.SetToolTip(button, name);
        return button;
    }

    static Viewbox CreateShelfIcon(Button owner)
    {
        // The selected stacked-card design uses a 24-unit canvas. At 20 px its
        // visible outline matches the settings glyph's approximately 15 px size.
        const string data = "M 7 3 L 18 3 C 19.1 3 20 3.9 20 5 L 20 16 "
            + "M 5 7 L 15 7 C 16.1 7 17 7.9 17 9 L 17 19 C 17 20.1 16.1 21 15 21 L 5 21 C 3.9 21 3 20.1 3 19 L 3 9 C 3 7.9 3.9 7 5 7 Z "
            + "M 7 12 L 13 12 M 7 16 L 11 16";
        return CreateVectorIcon(owner, data);
    }

    static Viewbox CreateVectorIcon(Button owner, string data, string thickness = "1.5")
    {
        var outline = (Microsoft.UI.Xaml.Shapes.Path)Microsoft.UI.Xaml.Markup.XamlReader.Load(
            "<Path xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" Data=\"" + data
            + "\" StrokeThickness=\"" + thickness + "\" StrokeStartLineCap=\"Round\" StrokeEndLineCap=\"Round\" StrokeLineJoin=\"Round\" />");
        outline.SetBinding(Microsoft.UI.Xaml.Shapes.Shape.StrokeProperty,
            new Microsoft.UI.Xaml.Data.Binding { Source = owner, Path = new PropertyPath("Foreground") });
        var canvas = new Canvas { Width = 24, Height = 24, IsHitTestVisible = false };
        canvas.Children.Add(outline);
        return new Viewbox { Width = 20, Height = 20, Child = canvas, IsHitTestVisible = false };
    }

    void SetToolbarColors(ElementTheme theme, SolidColorBrush? ink)
    {
        foreach (var button in actions.Children.OfType<Button>())
        {
            button.RequestedTheme = theme;
            if (ink != null)
            {
                button.Foreground = ink;
                if (button.Content is IconElement icon) icon.Foreground = ink;
            }
            else
            {
                button.ClearValue(Control.ForegroundProperty);
                if (button.Content is IconElement icon) icon.ClearValue(IconElement.ForegroundProperty);
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
        else if (Environment.GetCommandLineArgs().Contains("--verify-planner")) _ = VerifyPlanner();
        else if (Environment.GetCommandLineArgs().Contains("--verify-reminders")) _ = VerifyReminders();
        else if (Environment.GetCommandLineArgs().Contains("--verify-screenshot")) _ = VerifyScreenshot();
        else if (Environment.GetCommandLineArgs().Contains("--verify-qr")) _ = VerifyQr();
        else if (Environment.GetCommandLineArgs().Contains("--verify-station")) _ = VerifyStation();
    }

    async void Quit()
    {
        if (manager != null) manager.Quit();
        else if (await FlushPlanner())
        {
            if (App.Reminders != null) await App.Reminders.Stop();
            await ScreenshotService.Flush();
            await App.FlushStation();
            Shutdown(); WindowsCompositionQueue.ExitApplication();
        }
    }
    internal void Shutdown()
    {
        if (closing) return;
        closing = true; QrSelectionWindow.CancelActive(); qrResults?.Destroy(); if (qrTip != null) qrTip.IsOpen = false;
        stationHoverTimer?.Stop(); stationWindow?.Destroy();
        screenshotTipTimer.Stop(); screenshotTip.IsOpen = false;
        transition.Cancel(); appearanceMonitor?.Dispose(); tray?.Dispose(); tray = null;
        settingsWindow?.Destroy(); if (manager == null) plannerWindow?.Destroy();
        reservation.Dispose(); SystemBackdrop = null; Close();
    }
    void UpdateTray()
    {
        if (!Display.Primary) { tray?.Dispose(); tray = null; return; }
        tray ??= new TrayIcon(handle, DispatcherQueue, OpenSettings, ToggleBar, () => barVisible,
            () => preferences.Style, SetStyle, Quit, () => preferences.FollowPaused,
            () => { if (manager != null) manager.TogglePause(); else { preferences.SetFollowPaused(!preferences.FollowPaused); preferences.SaveCurrent(); ApplyExpanded(); } },
            () => OpenPlanner(PlannerPage.Tasks), () => OpenPlanner(PlannerPage.Notes), () => OpenStation());
    }
    internal void UpdateDisplay(DesktopMonitor display)
    {
        bool changed = Display != display;
        Display = display; UpdateTray();
        if (changed) { transition.Cancel(); Position(); appearanceMonitor?.Refresh(); settingsWindow?.UpdateDisplay(display); stationWindow?.UpdateDisplay(display); }
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
    internal void HidePanels(bool includeDetached = true) { settingsWindow?.Hide(); plannerWindow?.Hide(); qrResults?.Hide(); if (includeDetached) stationWindow?.Hide(); else stationWindow?.HideDocked(); stationHoverTimer?.Stop(); }
    internal Task<bool> FlushPlanner() => plannerWindow?.FlushAsync() ?? Task.FromResult(true);
    internal bool PanelModal => QrSelectionWindow.Active || stationWindow?.ModalActive == true || qrResults?.ModalActive == true || settingsWindow?.ModalActive == true || plannerWindow?.ModalActive == true;
    internal void RequestSettings()
    {
        if (surface.IsLoaded) OpenSettings(); else surface.Loaded += ShowSettingsOnLoad;
    }
    internal void Recover(bool suspended)
    {
        if (closing) return;
        QrSelectionWindow.CancelActive();
        transition.Cancel(); appearanceMonitor?.SetEnabled(false);
        if (suspended) { HidePanels(); return; }
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
        foreach (var button in actions.Children.OfType<Button>()) button.Height = Math.Min(28, preferences.BarHeight);
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
        if (PanelModal || manager?.HasModal == true) return;
        stationWindow?.HideDocked(); stationHoverTimer?.Stop();
        qrResults?.Hide();
        plannerWindow?.Hide();
        manager?.HideOtherPanels(this);
        settingsWindow ??= new SettingsWindow(preferences,
            () => { if (manager != null) manager.ApplyMaterial(); else ApplyGlass(); },
            () => { if (manager != null) manager.ApplyHeight(); else ApplyHeight(); },
            () => { if (manager != null) manager.ApplySettings(); else ApplyExpanded(); }, Quit, Display);
        settingsWindow.UpdateDisplay(Display);
        settingsWindow.RefreshStyle(preferences.Style);
        settingsWindow.Open(windowShown ? AppWindow.Position.Y + AppWindow.Size.Height : Display.Info.Work.Top);
    }
    async void OpenPlanner(PlannerPage page)
    {
        if (openingPlanner || closing || PanelModal || manager?.HasModal == true) return;
        stationWindow?.HideDocked(); stationHoverTimer?.Stop();
        qrResults?.Hide();
        if (plannerWindow?.IsOpen == true && plannerWindow.Page == page && plannerWindow.DisplayDevice == Display.Device)
        { plannerWindow.Hide(); return; }
        openingPlanner = true;
        try
        {
            settingsWindow?.Hide(); manager?.HideOtherPanels(this);
            plannerWindow ??= manager?.GetPlanner(Display) ?? new PlannerWindow(App.Planner, Display);
            await plannerWindow.Open(page, Display, windowShown ? AppWindow.Position.Y + AppWindow.Size.Height : Display.Info.Work.Top);
        }
        finally { openingPlanner = false; }
    }
    internal bool RequestReminder(Guid id)
    {
        if (openingPlanner || closing || PanelModal) return false;
        OpenReminder(id); return true;
    }
    async void OpenReminder(Guid id)
    {
        openingPlanner = true;
        try
        {
            qrResults?.Hide();
            stationWindow?.HideDocked(); stationHoverTimer?.Stop();
            settingsWindow?.Hide(); manager?.HideOtherPanels(this);
            plannerWindow ??= manager?.GetPlanner(Display) ?? new PlannerWindow(App.Planner, Display);
            await plannerWindow.OpenReminder(id, Display, windowShown ? AppWindow.Position.Y + AppWindow.Size.Height : Display.Info.Work.Top);
        }
        finally { openingPlanner = false; }
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
        else { HidePanels(); reservation.Release(); }
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
