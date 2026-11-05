using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.Web.WebView2.Core;

namespace Liubai;

internal sealed partial class AiWindow : PanelWindow
{
    readonly AiOptionsStore options;
    readonly string directory;
    readonly Action changed;
    readonly bool verification;
    readonly Grid root = new() { Padding = new Thickness(20, 16, 20, 16), RowSpacing = 12, Background = new SolidColorBrush(Colors.Transparent) };
    readonly Grid browsers = new();
    readonly TextBlock status = new() { FontSize = 12, Opacity = .7, TextWrapping = TextWrapping.Wrap };
    readonly ComboBox provider = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly Button expand = Icon("\uE740", "展开为独立窗口");
    readonly ToggleButton pin = new();
    readonly Button back = Icon("\uE72B", "返回 AI 对话");
    readonly Button retry = new() { Content = "重新加载", Visibility = Visibility.Collapsed };
    readonly Dictionary<string, Page> pages = new();
    readonly Microsoft.UI.Dispatching.DispatcherQueueTimer saveTimer;
    DesktopMonitor display;
    int dockTop;
    bool destroyed, syncing;
    string fixtureNavigation = "";
    Page? current;
    Popup? popup;
    AiBounds? floating;
    internal bool Detached { get; private set; }
    internal bool Initializing => current?.Ready is { IsCompleted: false };
    internal string Status => status.Text;
    internal string DisplayDevice => display.Device;
    protected override bool DismissOnOutside => !Detached && pin.IsChecked != true;
    protected override bool Frameless => !Detached;
    sealed class Page(AiEndpoint endpoint)
    {
        internal AiEndpoint Endpoint = endpoint;
        internal WebView2 View = new() { DefaultBackgroundColor = Colors.Transparent };
        internal CoreWebView2Environment? Environment;
        internal Task? Ready;
        internal bool Failed;
        internal bool Retry;
        internal string Status = endpoint.Name + " · 正在载入网页…";
    }
    sealed record Choice(string Id, string Name) { public override string ToString() => Name; }
    sealed record Popup(Page Owner, WebView2 View);
    internal AiWindow(AiOptionsStore options, string directory, DesktopMonitor display, Action changed, bool verification = false)
    {
        this.options = options; this.directory = directory; this.display = display; this.changed = changed;
        this.verification = verification;
        Title = "留白 · AI 助手";
        var presenter = (OverlappedPresenter)AppWindow.Presenter;
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsResizable = presenter.IsMaximizable = presenter.IsMinimizable = false; presenter.IsAlwaysOnTop = true;
        AppWindow.IsShownInSwitchers = false;
        var handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        int corner = 2, border = unchecked((int)0xFFFFFFFE);
        Native.DwmSetWindowAttribute(handle, 33, ref corner, sizeof(int)); Native.DwmSetWindowAttribute(handle, 34, ref border, sizeof(int));
        SystemBackdrop = new GlassBackdrop(GlassStyle.Frosted);
        root.RowDefinitions.Add(new() { Height = GridLength.Auto }); root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) }); root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var header = new Grid { ColumnSpacing = 10 };
        header.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var grip = new Border { Background = new SolidColorBrush(Colors.Transparent), Child = new TextBlock { Text = "AI 助手", FontSize = 23, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold } };
        header.Children.Add(grip);
        var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        Style(pin, "固定 AI 面板"); pin.Content = Glyph("\uE718");
        var close = Icon("\uE8BB", "收起 AI 面板");
        expand.Click += (_, _) => ToggleDetached(); close.Click += (_, _) => Hide();
        pin.Click += (_, _) => { try { options.Save(options.Data with { Pinned = pin.IsChecked == true }); } catch { ApplyPin(); SetStatus("固定状态保存失败，请重试。"); } };
        controls.Children.Add(expand); controls.Children.Add(pin); controls.Children.Add(close);
        Grid.SetColumn(controls, 1); header.Children.Add(controls); root.Children.Add(header);
        InitializeFloatingCaption(header, controls, () => Detached);
        var navigation = new Grid { ColumnSpacing = 8 };
        navigation.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); navigation.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        back.Visibility = Visibility.Collapsed; back.Click += (_, _) => ClosePopup(); navigation.Children.Add(back);
        AutomationProperties.SetName(provider, "AI 面板服务"); Grid.SetColumn(provider, 1); navigation.Children.Add(provider);
        provider.SelectionChanged += (_, _) => { if (!syncing && provider.SelectedItem is Choice choice) try { options.Save(options.Data with { Provider = choice.Id }); } catch { SetStatus("AI 服务保存失败，请重试。"); } };
        Grid.SetRow(navigation, 1); root.Children.Add(navigation);
        var content = new Border { CornerRadius = new CornerRadius(12), Child = browsers, Background = new SolidColorBrush(Colors.Transparent) };
        Grid.SetRow(content, 2); root.Children.Add(content);
        var footer = new StackPanel { Spacing = 6 }; footer.Children.Add(status); footer.Children.Add(retry);
        retry.Click += async (_, _) => { if (current is null) return; if (current.View.CoreWebView2 != null && !current.Failed) current.View.Reload(); else { Remove(current); current = null; await ShowSelected(); } };
        Grid.SetRow(footer, 3); root.Children.Add(footer);
        saveTimer = DispatcherQueue.CreateTimer(); saveTimer.Interval = TimeSpan.FromMilliseconds(400); saveTimer.IsRepeating = false;
        saveTimer.Tick += (_, _) => SaveFloating();
        AppWindow.Changed += (_, e) => { if (Detached && !destroyed && (e.DidPositionChange || e.DidSizeChange)) { saveTimer.Stop(); saveTimer.Start(); } };
        Closed += (_, _) => { destroyed = true; EndDockResize(true); saveTimer.Stop(); ClosePopup(); foreach (var page in pages.Values) page.View.Close(); pages.Clear(); };
        var shell = new Grid(); shell.Children.Add(root); InitializeDockResize(shell);
        InitializePanel(shell); ApplyOptions();
    }
    static FontIcon Glyph(string value) => new() { Glyph = value, FontSize = 15 };
    static void Style(ButtonBase button, string name)
    {
        button.Width = button.Height = 32; button.MinWidth = button.MinHeight = 0; button.Padding = new Thickness(0);
        button.BorderThickness = new Thickness(0); button.Background = new SolidColorBrush(Colors.Transparent);
        AutomationProperties.SetName(button, name); ToolTipService.SetToolTip(button, name);
    }
    static Button Icon(string value, string name) { var button = new Button { Content = Glyph(value) }; Style(button, name); return button; }
    void SetStatus(string text) { if (!destroyed) { status.Text = text; changed(); } }
    void PageStatus(Page page, string text, bool retry = false)
    {
        page.Status = text; page.Retry = retry;
        if (!destroyed && current == page) { this.retry.Visibility = retry ? Visibility.Visible : Visibility.Collapsed; SetStatus(text); }
    }
    static void MemoryTarget(WebView2 view, bool low)
    {
        try { if (view.CoreWebView2 is { } core) core.MemoryUsageTargetLevel = low ? CoreWebView2MemoryUsageTargetLevel.Low : CoreWebView2MemoryUsageTargetLevel.Normal; }
        catch { /* A failed browser is recovered through the explicit reload action. */ }
    }
    internal void ApplyOptions()
    {
        if (destroyed) return;
        if (!dockResizing) { dockWidth = options.Data.DockWidth; dockHeight = options.Data.DockHeight; dockOffset = options.Data.DockTopOffset; }
        foreach (var grip in new[] { dockGrip, dockTopGrip, dockBottomGrip }) grip.IsEnabled = !options.ReadOnly;
        if (IsOpen && !Detached && !dockResizing) Position();
        syncing = true;
        try
        {
            provider.Items.Clear();
            foreach (string id in AiPolicy.Providers)
            {
                if (id == "custom" && options.Data.CustomUrl.Length == 0) continue;
                try { provider.Items.Add(new Choice(id, AiPolicy.Resolve(options.Data with { Provider = id }).Name)); } catch (InvalidDataException) { }
            }
            provider.SelectedItem = provider.Items.OfType<Choice>().FirstOrDefault(c => c.Id == options.Data.Provider);
            provider.IsEnabled = !options.ReadOnly && popup == null; pin.IsEnabled = !options.ReadOnly; ApplyPin();
        }
        finally { syncing = false; }
        if (IsOpen) _ = ShowSelected();
    }
    void ApplyPin()
    {
        pin.IsChecked = options.Data.Pinned; pin.Content = Glyph(options.Data.Pinned ? "\uE77A" : "\uE718");
        ToolTipService.SetToolTip(pin, options.Data.Pinned ? "取消固定" : "固定 AI 面板");
        ((OverlappedPresenter)AppWindow.Presenter).IsAlwaysOnTop = !Detached || options.Data.Pinned;
    }
    internal async Task Open(DesktopMonitor monitor, int top)
    {
        display = monitor; dockTop = top;
        if (!Detached) Position(); else ClampFloating();
        ShowPanel(); await ShowSelected();
    }
    async Task ShowSelected()
    {
        if (destroyed) return;
        var endpoint = AiPolicy.Resolve(options.Data);
        if (current?.Endpoint.Key == endpoint.Key && current.Ready != null)
        { MemoryTarget(current.View, false); return; }
        ClosePopup();
        if (current != null) { current.View.Visibility = Visibility.Collapsed; MemoryTarget(current.View, true); }
        if (!pages.TryGetValue(endpoint.Key, out var page))
        {
            if (options.Data.Provider == "custom") foreach (var old in pages.Values.Where(p => !AiPolicy.BuiltInProviders.Select(id => AiPolicy.Resolve(new(Provider: id)).Key).Contains(p.Endpoint.Key)).ToArray()) Remove(old);
            page = new(endpoint); pages.Add(endpoint.Key, page); browsers.Children.Add(page.View);
        }
        current = page; page.View.Visibility = Visibility.Visible;
        MemoryTarget(page.View, false);
        page.Ready ??= Initialize(page);
        if (!page.Failed) PageStatus(page, page.Status, page.Retry);
        changed(); await page.Ready;
    }
    async Task Initialize(Page page)
    {
        try
        {
            PageStatus(page, "正在载入 " + page.Endpoint.Name + "…");
            string profile = Path.Combine(directory, "AiWebProfiles", page.Endpoint.Key);
            page.Environment = await CoreWebView2Environment.CreateWithOptionsAsync(null, profile, null);
            if (destroyed) return;
            await page.View.EnsureCoreWebView2Async(page.Environment);
            if (destroyed) return;
            Configure(page.View, page);
            if (verification) page.View.NavigateToString("<!doctype html><html><meta charset='utf-8'><style>body{font:16px system-ui;padding:20px;background:#eef5fa}textarea{width:90%;height:180px;padding:10px;font:16px system-ui}</style><h2>AI 面板测试页</h2><p>仅本机测试，不连接 AI 网站。</p><textarea id='draft'>保留我的草稿</textarea></html>");
            else page.View.CoreWebView2.Navigate(page.Endpoint.Url);
            if (!IsOpen) MemoryTarget(page.View, true);
        }
        catch { page.Failed = true; PageStatus(page, "网页初始化失败，请检查 Edge WebView2 运行时或点击重新加载。", true); }
        finally { if (!destroyed) changed(); }
    }
    void Configure(WebView2 view, Page page)
    {
        var core = view.CoreWebView2; core.Settings.IsStatusBarEnabled = false;
        core.NewWindowRequested += async (_, args) =>
        {
            args.Handled = true;
            if (destroyed || popup != null) { SetStatus("请先返回 AI 对话，再打开其他页面。"); return; }
            using var lease = KeepOpen(); var deferral = args.GetDeferral(); WebView2? target = null;
            try
            {
                target = new WebView2 { DefaultBackgroundColor = Colors.Transparent }; browsers.Children.Add(target);
                popup = new(page, target); back.Visibility = Visibility.Visible; provider.IsEnabled = false; page.View.Visibility = Visibility.Collapsed;
                await target.EnsureCoreWebView2Async(page.Environment);
                if (destroyed || popup?.View != target) return;
                Configure(target, page); args.NewWindow = target.CoreWebView2;
            }
            catch { if (popup?.View == target) ClosePopup(); SetStatus("附加页面无法打开，请返回后重试。"); }
            finally { deferral.Complete(); }
        };
        core.WindowCloseRequested += (_, _) => { if (popup?.View == view) ClosePopup(); else Hide(); };
        core.NavigationStarting += (_, args) =>
        {
            if (verification) fixtureNavigation = args.Uri;
            // NavigateToString has a synthetic navigation URI. Only the local fixture uses it;
            // all real website navigation is checked by the production policy.
            if (!verification && !AiPolicy.AllowedNavigation(args.Uri)) { args.Cancel = true; SetStatus("该链接无法在 AI 面板内打开，仅支持安全网页。"); return; }
            PageStatus(page, page.Endpoint.Name + " · 正在载入网页…");
        };
        core.NavigationCompleted += (_, args) =>
        {
            if (destroyed) return;
            string host = Uri.TryCreate(core.Source, UriKind.Absolute, out var uri) ? uri.Host : "";
            PageStatus(page, args.IsSuccess ? page.Endpoint.Name + (host.Length > 0 ? " · " + host : "") : "网页加载失败（" + args.WebErrorStatus + "），可点击重新加载。", !args.IsSuccess);
        };
        core.ProcessFailed += (_, _) => { if (!destroyed) { page.Failed = true; PageStatus(page, "网页进程已停止，点击重新加载可恢复页面。", true); } };
    }
    void ClosePopup()
    {
        if (popup is not { } value) return;
        popup = null; browsers.Children.Remove(value.View); value.View.Close(); back.Visibility = Visibility.Collapsed; provider.IsEnabled = !options.ReadOnly;
        if (current == value.Owner) current.View.Visibility = Visibility.Visible;
    }
    void Remove(Page page) { pages.Remove(page.Endpoint.Key); browsers.Children.Remove(page.View); page.View.Close(); }
    internal void HideDocked() { if (!Detached) Hide(); }
    protected override void OnHiding()
    {
        SaveFloating();
        foreach (var page in pages.Values) MemoryTarget(page.View, true);
        changed();
    }
    internal void ToggleDetached()
    {
        if (ModalActive) return;
        using var lease = KeepOpen(); SaveFloating(); Detached = !Detached;
        foreach (var host in new[] { dockGripHost, dockTopGripHost, dockBottomGripHost }) host.Visibility = Detached ? Visibility.Collapsed : Visibility.Visible;
        dockGrip.Visibility = Detached ? Visibility.Collapsed : Visibility.Visible;
        var presenter = (OverlappedPresenter)AppWindow.Presenter;
        presenter.SetBorderAndTitleBar(Detached, false); presenter.IsResizable = Detached; AppWindow.IsShownInSwitchers = Detached;
        if (Detached) { Native.RestoreResizeFrame(WinRT.Interop.WindowNative.GetWindowHandle(this)); ClampFloating(); } else Position();
        expand.Content = Glyph(Detached ? "\uE73F" : "\uE740");
        string name = Detached ? "停靠回顶栏" : "展开为独立窗口"; AutomationProperties.SetName(expand, name); ToolTipService.SetToolTip(expand, name);
        ApplyPin(); ShowPanel(); UpdateFloatingCaption();
    }
    void SaveFloating()
    {
        saveTimer.Stop(); if (!Detached || current == null || destroyed) return;
        floating = new(AppWindow.Position.X, AppWindow.Position.Y, AppWindow.Size.Width, AppWindow.Size.Height);
        try { options.SaveBounds(current.Endpoint.Key, floating); } catch { SetStatus("窗口位置保存失败，请检查设置目录权限。"); }
    }
    void ClampFloating()
    {
        var work = display.Info.Work; var endpoint = AiPolicy.Resolve(options.Data);
        var value = AiPolicy.Fit(floating ?? options.Data.Layouts?.GetValueOrDefault(endpoint.Key), new(work.Left, work.Top, work.Right - work.Left, work.Bottom - work.Top), display.Dpi);
        AppWindow.MoveAndResize(new(value.X, value.Y, value.Width, value.Height));
    }
    void Position()
    {
        var work = display.Info.Work;
        var bounds = AiPolicy.DockBounds(dockWidth, dockHeight, dockOffset, new(work.Left, work.Top, work.Right - work.Left, work.Bottom - work.Top), dockTop, display.Dpi);
        AppWindow.MoveAndResize(new(bounds.X, bounds.Y, bounds.Width, bounds.Height));
    }
    internal void UpdateDisplay(DesktopMonitor monitor) { display = monitor; if (IsOpen) { if (Detached) ClampFloating(); else Position(); } }
}
