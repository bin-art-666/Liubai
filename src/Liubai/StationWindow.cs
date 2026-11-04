using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.System;

namespace Liubai;

internal sealed class StationWindow : PanelWindow
{
    readonly StationStore store;
    readonly StackPanel cards = new() { Spacing = 9 };
    readonly TextBlock count = Hint("");
    readonly TextBlock status = Hint("");
    readonly ToggleButton pin = new();
    readonly Button expand = IconButton("\uE740", "展开为独立窗口");
    readonly Button clear = new() { Content = "清空" };
    readonly Grid root = new() { Padding = new Thickness(20, 16, 20, 16), RowSpacing = 12, Background = new SolidColorBrush(Colors.Transparent), AllowDrop = true };
    readonly Microsoft.UI.Dispatching.DispatcherQueueTimer leaveTimer;
    DesktopMonitor display;
    bool busy, destroyed, openedForDrop;
    int dockTop;
    RectInt32? floatingBounds;
    int renderVersion;
    protected override bool DismissOnOutside => !Detached && !Pinned;
    protected override bool Frameless => !Detached;
    internal bool Detached { get; private set; }
    internal string DisplayDevice => display.Device;
    internal bool Pinned => pin.IsChecked == true;

    internal StationWindow(StationStore store, DesktopMonitor display)
    {
        this.store = store; this.display = display; Title = "留白 · 中转站";
        var presenter = (OverlappedPresenter)AppWindow.Presenter;
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsResizable = presenter.IsMaximizable = presenter.IsMinimizable = false; presenter.IsAlwaysOnTop = true;
        AppWindow.IsShownInSwitchers = false;
        int corner = 2, border = unchecked((int)0xFFFFFFFE);
        var handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        Native.DwmSetWindowAttribute(handle, 33, ref corner, sizeof(int)); Native.DwmSetWindowAttribute(handle, 34, ref border, sizeof(int));
        SystemBackdrop = new GlassBackdrop(GlassStyle.Frosted);
        leaveTimer = DispatcherQueue.CreateTimer(); leaveTimer.IsRepeating = false; leaveTimer.Interval = TimeSpan.FromMilliseconds(550);
        leaveTimer.Tick += (_, _) => { if (openedForDrop && !Pinned && !busy) Hide(); };
        root.RowDefinitions.Add(new() { Height = GridLength.Auto }); root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) }); root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var header = new Grid { ColumnSpacing = 10 };
        header.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var titleGrip = new Border { Background = new SolidColorBrush(Colors.Transparent), Child = new TextBlock { Text = "中转站", FontSize = 23, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold } };
        titleGrip.PointerPressed += (_, e) => { if (Detached && e.GetCurrentPoint(titleGrip).Properties.IsLeftButtonPressed) { e.Handled = true; Native.DragWindow(handle); } };
        header.Children.Add(titleGrip);
        var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        StyleIconButton(pin, "固定中转站"); pin.Content = Glyph("\uE718");
        var close = IconButton("\uE8BB", "收起中转站");
        expand.Click += (_, _) => ToggleDetached(); close.Click += (_, _) => Hide();
        controls.Children.Add(expand); controls.Children.Add(pin); controls.Children.Add(close);
        Grid.SetColumn(controls, 1); header.Children.Add(controls); root.Children.Add(header);
        var intro = new StackPanel { Spacing = 6 }; intro.Children.Add(Hint("拖入文件、文字或链接，切换窗口后再取走。")); intro.Children.Add(count);
        Grid.SetRow(intro, 1); root.Children.Add(intro);
        var scroll = new ScrollViewer { Content = cards, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(scroll, 2); root.Children.Add(scroll);
        var footer = new StackPanel { Spacing = 9 }; footer.Children.Add(status);
        footer.Children.Add(clear);
        footer.Children.Add(Hint("虚拟附件自动保存到本机。移除记录不会删除文件。")); Grid.SetRow(footer, 3); root.Children.Add(footer);
        clear.Click += async (_, _) => await Clear();
        pin.Checked += (_, _) => { openedForDrop = false; leaveTimer.Stop(); UpdatePin(); };
        pin.Unchecked += (_, _) => { UpdatePin(); if (IsOpen) status.Text = Detached ? "独立窗口保持打开" : "已恢复点击外部自动收起"; };
        root.DragEnter += Over; root.DragOver += Over;
        root.DragLeave += (_, _) => ScheduleDragDismiss();
        root.Drop += async (_, e) =>
        {
            e.Handled = true; var deferral = e.GetDeferral(); bool saved = false;
            try { saved = await Receive(e.DataView); e.AcceptedOperation = saved ? DataPackageOperation.Copy : DataPackageOperation.None; }
            finally { deferral.Complete(); }
            if (saved) ConfirmDrop();
        };
        store.Changed += StoreChanged;
        Closed += (_, _) => { destroyed = true; renderVersion++; leaveTimer.Stop(); store.Changed -= StoreChanged; };
        InitializePanel(root);
    }
    static TextBlock Hint(string text) => new() { Text = text, FontSize = 12, Opacity = .7, TextWrapping = TextWrapping.Wrap };
    static FontIcon Glyph(string glyph) => new() { Glyph = glyph, FontSize = 15 };
    static void StyleIconButton(ButtonBase button, string name)
    {
        button.Width = button.Height = 32; button.MinWidth = button.MinHeight = 0;
        button.Padding = new Thickness(0); button.BorderThickness = new Thickness(0);
        button.Background = new SolidColorBrush(Colors.Transparent);
        AutomationProperties.SetName(button, name); ToolTipService.SetToolTip(button, name);
    }
    static Button IconButton(string glyph, string name)
    { var button = new Button { Content = Glyph(glyph) }; StyleIconButton(button, name); return button; }
    static UIElement CopyIcon()
    {
        var canvas = new Canvas { Width = 20, Height = 20 };
        var back = new Microsoft.UI.Xaml.Shapes.Rectangle { Width = 11, Height = 13, RadiusX = 3, RadiusY = 3, StrokeThickness = 1.4,
            Stroke = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 110, 110, 110)) };
        Canvas.SetLeft(back, 2); Canvas.SetTop(back, 5); canvas.Children.Add(back);
        var front = new Microsoft.UI.Xaml.Shapes.Rectangle { Width = 11, Height = 13, RadiusX = 3, RadiusY = 3, StrokeThickness = 1.4,
            Fill = new SolidColorBrush(Colors.Transparent), Stroke = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 120, 212)) };
        Canvas.SetLeft(front, 7); Canvas.SetTop(front, 1); canvas.Children.Add(front); return canvas;
    }
    void UpdatePin()
    {
        pin.Content = Glyph(Pinned ? "\uE77A" : "\uE718");
        ToolTipService.SetToolTip(pin, Pinned ? "取消固定" : "固定中转站");
        ((OverlappedPresenter)AppWindow.Presenter).IsAlwaysOnTop = !Detached || Pinned;
    }
    internal void HideDocked() { if (!Detached) Hide(); }
    internal void ConfirmDrop()
    {
        openedForDrop = false; leaveTimer.Stop();
        // The drag source can receive one final activation event after completing
        // its drop. Let that event settle before resuming outside dismissal.
        var lease = KeepOpen();
        if (!DispatcherQueue.TryEnqueue(async () =>
        {
            try { if (!destroyed && IsOpen) { ShowPanel(); await Task.Delay(350); } }
            finally { lease.Dispose(); }
        })) lease.Dispose();
    }
    internal void ToggleDetached()
    {
        if (busy || ModalActive) return;
        using var lease = KeepOpen();
        if (Detached) floatingBounds = new RectInt32(AppWindow.Position.X, AppWindow.Position.Y, AppWindow.Size.Width, AppWindow.Size.Height);
        Detached = !Detached; openedForDrop = false; leaveTimer.Stop();
        var presenter = (OverlappedPresenter)AppWindow.Presenter;
        presenter.SetBorderAndTitleBar(Detached, false); presenter.IsResizable = Detached;
        AppWindow.IsShownInSwitchers = Detached;
        if (Detached)
        {
            Native.RestoreResizeFrame(WinRT.Interop.WindowNative.GetWindowHandle(this));
            var work = display.Info.Work; double scale = display.Dpi / 96d;
            var bounds = floatingBounds ?? new RectInt32(work.Left + (work.Right - work.Left) / 5, work.Top + (work.Bottom - work.Top) / 8,
                (int)(650 * scale), (int)(680 * scale));
            AppWindow.MoveAndResize(Clamp(bounds));
        }
        else Position(dockTop);
        expand.Content = Glyph(Detached ? "\uE73F" : "\uE740");
        string name = Detached ? "停靠回顶栏" : "展开为独立窗口";
        AutomationProperties.SetName(expand, name); ToolTipService.SetToolTip(expand, name);
        UpdatePin(); ShowPanel();
    }
    RectInt32 Clamp(RectInt32 bounds)
    {
        var work = display.Info.Work;
        int width = Math.Min(bounds.Width, work.Right - work.Left), height = Math.Min(bounds.Height, work.Bottom - work.Top);
        return new RectInt32(Math.Clamp(bounds.X, work.Left, work.Right - width), Math.Clamp(bounds.Y, work.Top, work.Bottom - height), width, height);
    }
    internal void Open(DesktopMonitor monitor, int top, bool forDrop = false)
    {
        display = monitor; dockTop = top; leaveTimer.Stop(); openedForDrop = forDrop;
        if (!Detached) Position(top);
        Refresh(); status.Text = store.LoadWarning ?? (store.Items.Count == 0 ? "把内容拖到这里，或直接拖到顶栏收纳盒。" : "拖动卡片内容即可取出；也可点击复制。");
        ShowPanel(!forDrop);
    }
    internal void UpdateDisplay(DesktopMonitor monitor)
    {
        display = monitor;
        if (IsOpen) { if (!Detached) Position(monitor.Info.Work.Top); else AppWindow.MoveAndResize(Clamp(new RectInt32(AppWindow.Position.X, AppWindow.Position.Y, AppWindow.Size.Width, AppWindow.Size.Height))); }
    }
    void Position(int top)
    {
        var work = display.Info.Work; double scale = display.Dpi / 96d;
        int width = Math.Min((int)(430 * scale), work.Right - work.Left), height = Math.Min((int)(660 * scale), work.Bottom - work.Top);
        AppWindow.MoveAndResize(new RectInt32(work.Right - width, Math.Clamp(top, work.Top, work.Bottom - height), width, height));
    }
    void StoreChanged() { if (!destroyed && IsOpen) Refresh(); }
    void Over(object sender, DragEventArgs e)
    {
        leaveTimer.Stop(); e.Handled = true;
        e.AcceptedOperation = !busy && !store.ReadOnly && StationTransfer.Supported(e.DataView) ? DataPackageOperation.Copy : DataPackageOperation.None;
        if (e.AcceptedOperation == DataPackageOperation.Copy) { e.DragUIOverride.Caption = "存入中转站"; e.DragUIOverride.IsCaptionVisible = true; }
    }
    internal void ScheduleDragDismiss() { if (openedForDrop && !Pinned) { leaveTimer.Stop(); leaveTimer.Start(); } }
    internal async Task<bool> Receive(DataPackageView view)
    {
        if (busy || destroyed || store.ReadOnly) return false;
        leaveTimer.Stop(); busy = true;
        using var lease = KeepOpen();
        try
        {
            status.Text = "正在存入…";
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            using var import = await StationTransfer.Read(view, store.AttachmentDirectory, cancellation.Token);
            var inputs = import.Inputs;
            if (inputs.Length == 0) throw new InvalidDataException("没有可存入的内容。");
            var result = await store.Add(inputs);
            import.Commit();
            status.Text = result.Added == 0 ? "内容已在中转站，无需重复存入。" : $"已存入 {result.Added} 项" + (result.Duplicates > 0 ? $"，忽略 {result.Duplicates} 项重复内容" : "");
            openedForDrop = false; return true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        { status.Text = ex is InvalidDataException ? ex.Message : "存入失败，本批内容未保存，请重试。"; openedForDrop = false; return false; }
        finally { busy = false; }
    }
    void Refresh()
    {
        int version = ++renderVersion; cards.Children.Clear(); count.Text = $"{store.Items.Count} / {StationStore.Capacity} 项 · 自动保存";
        clear.IsEnabled = !store.ReadOnly && store.Items.Count > 0;
        if (store.Items.Count == 0)
        {
            cards.Children.Add(new Border { Padding = new Thickness(22, 36, 22, 36), CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(35, 128, 128, 128)), Child = new TextBlock { Text = "先放一下\n稍后接着用", FontSize = 20, Opacity = .6, TextAlignment = TextAlignment.Center } });
            return;
        }
        foreach (var item in store.Items.Reverse()) AddCard(item, version);
    }
    void AddCard(StationItem item, int version)
    {
        var body = new Grid { ColumnSpacing = 8 };
        body.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); body.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        string label = item.Kind switch { StationKind.File => "文件", StationKind.Folder => "文件夹", StationKind.Link => "链接", _ => "文字" };
        var meta = Hint(label + " · 拖动取出");
        var text = new TextBlock { Text = DisplayTitle(item), TextWrapping = TextWrapping.Wrap, MaxLines = 3, TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 14 };
        var gripContent = new StackPanel { Spacing = 5 }; gripContent.Children.Add(meta); gripContent.Children.Add(text);
        if (item.Kind == StationKind.Link) gripContent.Children.Add(Hint(new Uri(item.Value).IdnHost));
        var grip = new Border { Child = gripContent, Background = new SolidColorBrush(Colors.Transparent), CanDrag = true, Padding = new Thickness(2, 4, 2, 4) };
        AutomationProperties.SetName(grip, "拖出" + label + "：" + DisplayTitle(item)); ToolTipService.SetToolTip(grip, item.Value.Length > 800 ? item.Value[..800] + "…" : item.Value);
        IDisposable? dragLease = null;
        grip.DragStarting += async (_, e) =>
        {
            if (busy) { e.Cancel = true; return; }
            dragLease = KeepOpen(); var deferral = e.GetDeferral();
            try { e.AllowedOperations = DataPackageOperation.Copy; await StationTransfer.Fill(e.Data, item); }
            catch { e.Cancel = true; dragLease.Dispose(); dragLease = null; status.Text = "该文件暂不可用、已移动或已删除，请检查原位置。"; }
            finally { deferral.Complete(); }
        };
        grip.DropCompleted += (_, e) =>
        {
            dragLease?.Dispose(); dragLease = null;
            status.Text = e.DropResult == DataPackageOperation.None ? "已取消拖出，内容仍在中转站。" : "已交给目标窗口，内容仍保留在中转站。";
            if (e.DropResult != DataPackageOperation.None && !Pinned && !Detached) Hide();
        };
        body.Children.Add(grip);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        var copy = IconButton("", "复制内容"); copy.Content = CopyIcon();
        copy.Click += async (_, _) => await Run(async () => { var package = new DataPackage(); await StationTransfer.Fill(package, item); Clipboard.SetContent(package); Clipboard.Flush(); status.Text = "已复制，可到目标窗口粘贴。"; });
        buttons.Children.Add(copy);
        if (item.Kind == StationKind.Link)
        {
            var open = IconButton("\uE8A7", "打开链接");
            open.Click += async (_, _) => await Run(async () => { status.Text = await Launcher.LaunchUriAsync(new Uri(item.Value)) ? "已交给浏览器打开。" : "未能打开链接，可以复制后重试。"; }); buttons.Children.Add(open);
        }
        var remove = IconButton("\uE74D", "移除记录"); remove.IsEnabled = !store.ReadOnly;
        remove.Click += async (_, _) => await Run(async () => { await store.Remove(item.Id); status.Text = "已移除记录，原文件未改动。"; }); buttons.Children.Add(remove);
        Grid.SetColumn(buttons, 1); body.Children.Add(buttons);
        var card = new Border { Child = body, Padding = new Thickness(12), CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(25, 128, 128, 128)), BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(28, 128, 128, 128)) };
        cards.Children.Add(card);
        if (item.Kind is StationKind.File or StationKind.Folder) _ = CheckFile(item, meta, grip, copy, version, label);
    }
    static string DisplayTitle(StationItem item) => item.Kind is StationKind.File or StationKind.Folder
        ? Path.GetFileName(item.Value) is { Length: > 0 } name ? name : item.Value
        : item.Kind == StationKind.Link && item.Title.Length > 0 ? item.Title : item.Value;
    async Task CheckFile(StationItem item, TextBlock meta, Border grip, Button copy, int version, string label)
    {
        bool available;
        try { await StationTransfer.Storage(item).WaitAsync(TimeSpan.FromSeconds(3)); available = true; }
        catch { available = false; }
        if (destroyed || version != renderVersion) return;
        meta.Text = available ? label + " · 拖动取出" : label + " · 原位置暂不可用或已失效";
        grip.CanDrag = available; copy.IsEnabled = available;
    }
    async Task Run(Func<Task> action)
    {
        if (busy || destroyed || ModalActive) return;
        busy = true; using var lease = KeepOpen();
        try { await action(); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { status.Text = "操作未完成，请重试。文件记录不会移动或删除原文件。"; }
        finally { busy = false; }
    }
    async Task Clear() => await Run(async () =>
    {
        if (store.ReadOnly || store.Items.Count == 0) return;
        var dialog = new ContentDialog { XamlRoot = root.XamlRoot, Title = "清空中转站？", Content = "仅移除中转站里的记录，不会删除原文件。", PrimaryButtonText = "清空记录", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary) { await store.Clear(); status.Text = "中转站已清空，原文件未改动。"; }
    });
    protected override void OnHiding()
    {
        if (Detached) floatingBounds = new RectInt32(AppWindow.Position.X, AppWindow.Position.Y, AppWindow.Size.Width, AppWindow.Size.Height);
        leaveTimer.Stop(); openedForDrop = false; pin.IsChecked = false; renderVersion++;
    }
}
