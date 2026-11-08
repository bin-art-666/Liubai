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

internal sealed partial class StationWindow : PanelWindow
{
    readonly StationStore store;
    readonly StackPanel cards = PanelTheme.Track(new StackPanel { Margin = new Thickness(0, 0, 14, 0) }, static panel => panel.Spacing = Math.Round(PanelTheme.Value<Thickness>("CardPadding").Top / 2));
    readonly TextBlock stationTitle = PanelTheme.Title("中转站");
    readonly TextBlock status = Hint("");
    readonly ToggleButton pin = new();
    readonly Button expand = IconButton("\uE740", "展开为独立窗口");
    readonly Button clear = ItemAction("清空中转站", SweepPath, header: true);
    readonly Grid root = PanelTheme.Layout();
    readonly Microsoft.UI.Dispatching.DispatcherQueueTimer leaveTimer;
    readonly StationPanelLayout panelLayout;
    readonly Microsoft.UI.Dispatching.DispatcherQueueTimer layoutTimer;
    DesktopMonitor display;
    bool busy, destroyed, openedForDrop;
    int dockTop;
    RectInt32? floatingBounds;
    int renderVersion;
    int visibleItems = 50;
    protected override bool DismissOnOutside => !Detached && !Pinned;
    protected override bool Frameless => !Detached;
    internal bool Detached { get; private set; }
    internal string DisplayDevice => display.Device;
    internal bool Pinned => pin.IsChecked == true;

    internal StationWindow(StationStore store, DesktopMonitor display)
    {
        this.store = store; this.display = display; Title = "留白 · 中转站";
        panelLayout = new(store.WindowLayoutPath); dockLayout = panelLayout.Data;
        if (panelLayout.Data.Floating is { } saved) floatingBounds = new(saved.X, saved.Y, saved.Width, saved.Height);
        root.AllowDrop = true;
        var presenter = (OverlappedPresenter)AppWindow.Presenter;
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsResizable = presenter.IsMaximizable = presenter.IsMinimizable = false; presenter.IsAlwaysOnTop = true;
        AppWindow.IsShownInSwitchers = false;
        int corner = 2, border = unchecked((int)0xFFFFFFFE);
        var handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        Native.DwmSetWindowAttribute(handle, 33, ref corner, sizeof(int)); Native.DwmSetWindowAttribute(handle, 34, ref border, sizeof(int));
        SystemBackdrop = PanelTheme.Backdrop();
        leaveTimer = DispatcherQueue.CreateTimer(); leaveTimer.IsRepeating = false; leaveTimer.Interval = TimeSpan.FromMilliseconds(550);
        leaveTimer.Tick += (_, _) => { if (openedForDrop && !Pinned && !busy) Hide(); };
        layoutTimer = DispatcherQueue.CreateTimer(); layoutTimer.IsRepeating = false; layoutTimer.Interval = TimeSpan.FromMilliseconds(400);
        layoutTimer.Tick += (_, _) => SaveFloatingLayout();
        AppWindow.Changed += (_, e) => { if (Detached && !destroyed && (e.DidPositionChange || e.DidSizeChange)) { layoutTimer.Stop(); layoutTimer.Start(); } };
        root.RowDefinitions.Add(new() { Height = GridLength.Auto }); root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) }); root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var titleGrip = new Border { Background = new SolidColorBrush(Colors.Transparent), Child = stationTitle };
        var controls = PanelTheme.Actions();
        StyleIconButton(pin, "固定中转站"); SetStationIcon(pin, "\uE718");
        var close = IconButton("\uE8BB", "收起中转站");
        expand.Click += (_, _) => ToggleDetached(); close.Click += (_, _) => Hide();
        var unifiedSearch=IconButton("\uE721","统一搜索资料、分组、待办和笔记");unifiedSearch.Click+=(_,_)=>SearchRequested?.Invoke();stationCommands.Children.Add(unifiedSearch);
        stationCommands.Children.Add(clear); stationCommands.Children.Add(archive);
        controls.Children.Add(expand); controls.Children.Add(pin); controls.Children.Add(close);
        var header = InitializeStationHeader(titleGrip, controls); root.Children.Add(header);
        var intro = new StackPanel { Spacing = 6 };

        Grid.SetRow(intro, 1); root.Children.Add(intro);
        var scroll = new ScrollViewer { Content = cards, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        InitializeBrowser(scroll);
        var footer = new StackPanel { Spacing = 9 }; InitializeWorkspace(intro, footer); InitializeSelection(footer); InitializeUndo(footer); footer.Children.Add(status); footer.Children.Add(viewAllAfterDrop);
        footer.Children.Add(cancelArchive); footer.Children.Add(archiveProgress);
        InitializeStationFeedback(); Grid.SetRow(footer, 3); root.Children.Add(footer);
        clear.Click += async (_, _) => await Clear();
        archive.Click += async (_, _) => await Archive();
        cancelArchive.Click += (_, _) => archiveCancellation?.Cancel();
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
        Closed += (_, _) => { destroyed = true; EndStationResize(true); layoutTimer.Stop(); archiveCancellation?.Cancel(); renderVersion++; CancelPreviews(); previewCache.Clear(); leaveTimer.Stop(); store.Changed -= StoreChanged; feedbackTimer.Stop(); embeddedSearch?.Stop(); DisposeWorkspace(); };
        var shell = new Grid(); shell.Children.Add(root); InitializeGroupWheel(shell); InitializeStationResize(shell); InitializeQuickLook(shell); InitializePanel(shell);
    }
    static TextBlock Hint(string text) => PanelTheme.Hint(text);
    static FontIcon Glyph(string glyph) => PanelTheme.Glyph(glyph);
    static void StyleIconButton(ButtonBase button, string name) => PanelTheme.FramedIconStyle(button, name);
    internal static Button IconButton(string glyph, string name)
    { var button = new Button(); StyleIconButton(button, name); SetStationIcon(button,glyph); return button; }
    void UpdatePin()
    {
        SetStationIcon(pin, Pinned ? "\uE77A" : "\uE718");
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
        if (Detached) SaveQuery();
        if (Detached) SaveFloatingLayout();
        Detached = !Detached; openedForDrop = false; leaveTimer.Stop();
        foreach (var host in stationResizeHosts) host.Visibility = Detached ? Visibility.Collapsed : Visibility.Visible;
        var presenter = (OverlappedPresenter)AppWindow.Presenter;
        presenter.SetBorderAndTitleBar(Detached, false); presenter.IsResizable = Detached;
        AppWindow.IsShownInSwitchers = Detached;
        if (Detached)
        {
            Native.RestoreResizeFrame(WinRT.Interop.WindowNative.GetWindowHandle(this));
            var work = display.Info.Work; double scale = display.Dpi / 96d;
            var bounds = floatingBounds ?? new RectInt32(work.Left + (work.Right - work.Left) / 5, work.Top + (work.Bottom - work.Top) / 8,
                (int)(850 * scale), (int)(680 * scale));
            AppWindow.MoveAndResize(Clamp(bounds));
        }
        else Position(dockTop);
        SetStationIcon(expand, Detached ? "\uE73F" : "\uE740");
        string name = Detached ? "停靠回顶栏" : "展开为独立窗口";
        AutomationProperties.SetName(expand, name); ToolTipService.SetToolTip(expand, name);
        UpdatePin(); UpdateBrowserMode(); visibleItems = 50; Refresh(); ShowPanel(); UpdateFloatingCaption();
    }
    RectInt32 Clamp(RectInt32 bounds)
    {
        var work = display.Info.Work;
        int width = Math.Min(bounds.Width, work.Right - work.Left), height = Math.Min(bounds.Height, work.Bottom - work.Top);
        return new RectInt32(Math.Clamp(bounds.X, work.Left, work.Right - width), Math.Clamp(bounds.Y, work.Top, work.Bottom - height), width, height);
    }
    internal void Open(DesktopMonitor monitor, int top, bool forDrop = false)
    {
        if(SearchActive)CloseSearch(false);
        display = monitor; dockTop = top; leaveTimer.Stop(); openedForDrop = forDrop;
        if (!Detached) Position(top);
        Refresh(); status.Text = store.LoadWarning ?? panelLayout.Warning ?? "";
        ShowPanel(!forDrop); dayTimer.Start();
    }
    internal void UpdateDisplay(DesktopMonitor monitor)
    {
        display = monitor;
        if (IsOpen) { if (!Detached) Position(monitor.Info.Work.Top); else AppWindow.MoveAndResize(Clamp(new RectInt32(AppWindow.Position.X, AppWindow.Position.Y, AppWindow.Size.Width, AppWindow.Size.Height))); }
    }
    void Position(int top)
    {
        var work = display.Info.Work;
        var bounds = StationPanelPolicy.Bounds(dockLayout, new(work.Left, work.Top, work.Right - work.Left, work.Bottom - work.Top), top, display.Dpi);
        AppWindow.MoveAndResize(new(bounds.X, bounds.Y, bounds.Width, bounds.Height));
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
            if(await StationTransfer.InternalItems(view,store) is {} existing)
            { if(workspaceId is {} group) await App.Work.Store.Link(group,existing.Select(i=>new WorkRef(WorkKind.Station,i.Id))); status.Text="项目已在中转站，分组关联已更新"; openedForDrop=false; return true; }
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            using var import = await StationTransfer.Read(view, store.AttachmentDirectory, cancellation.Token);
            var inputs = import.Inputs;
            if (inputs.Length == 0) throw new InvalidDataException("没有可存入的内容。");
            var before = store.Items.Select(i => i.Id).ToHashSet();
            using var collection = App.Work.SuspendCollection();
            var result = await store.Add(inputs);
            import.Commit();
            if(workspaceId is {} currentGroup) await App.Work.Store.Link(currentGroup,inputs.Select(input=>store.Items.First(i=>i.Kind==input.Kind&&i.Value.Equals(input.Value,input.Kind is StationKind.File or StationKind.Folder ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))).Select(i=>new WorkRef(WorkKind.Station,i.Id)));
            status.Text = result.Added == 0 ? "内容已在中转站，无需重复存入。" : $"已存入 {result.Added} 项" + (result.Duplicates > 0 ? $"，忽略 {result.Duplicates} 项重复内容" : "");
            if (ActiveQuery.Filtered && (result.Added == 0 || store.Items.Where(i => !before.Contains(i.Id)).Any(i => !ActiveQuery.Matches(i, DateTimeOffset.Now, TimeZoneInfo.Local))))
            { status.Text += "；部分内容不在当前筛选中，可查看全部。"; viewAllAfterDrop.Visibility = Visibility.Visible; }
            openedForDrop = false; return true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        { status.Text = ex is InvalidDataException ? ex.Message : "存入失败，本批内容未保存，请重试。"; openedForDrop = false; return false; }
        finally { busy = false; RefreshActionState(); }
    }
    void RefreshActionState()
    {
        UpdateExpandedActions();
        int total = CurrentItems().Length;
        string clearLabel = ActiveQuery.Filtered ? $"清空当前结果（{total} 项）" : "清空中转站";
        string archiveLabel = ActiveQuery.Filtered ? $"归档当前结果（{total} 项）" : "归档到文件夹";
        if(workspaceId!=null) clearLabel="移出当前分组中的资料";
        if(Scene()!=null&&selection.Count==0) archiveLabel="归档整个工作分组";
        AutomationProperties.SetName(clear, clearLabel); ToolTipService.SetToolTip(clear, clearLabel);
        AutomationProperties.SetName(archive, archiveLabel); ToolTipService.SetToolTip(archive, archiveLabel);
        clear.IsEnabled = !busy && !store.ReadOnly && total > 0 && workspacePage == 0;
        archive.IsEnabled = !busy && (total > 0 || Scene() != null);
        AutomationProperties.SetHelpText(clear, $"仅移除当前范围中的 {total} 项记录，不删除原文件");
        AutomationProperties.SetHelpText(archive, $"归档当前范围中的 {total} 项");
    }
    void Refresh()
    {
        RefreshQuickLook();
        CancelPreviews(); previewCancellation = new CancellationTokenSource();
        int version = ++renderVersion; cards.Children.Clear(); selectionCards.Clear(); selectionGrips.Clear();
        var now = DateTimeOffset.Now; renderedDay = now.LocalDateTime.Date;
        RefreshWorkspaces();
        var matches = CurrentItems();
        stationTitle.Text = $"中转站 · {store.Items.Count}"; UpdateStationHeader();
        RefreshCategories(now); UpdateBrowserMode(); RefreshActionState();
        if (!Detached) viewAllAfterDrop.Visibility = Visibility.Collapsed;
        PruneSelection(matches);
        if (RenderWorkspacePage()) return;
        if (matches.Length == 0)
        {
            if(RenderMissingWorkspaceItems()) return;
            var empty = PanelTheme.Card(new TextBlock {
                Text = store.Items.Count == 0 ? "暂无资料" : "没有匹配内容", FontSize = 18, Opacity = .6, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center });
            empty.Padding = new Thickness(22, 36, 22, 36); cards.Children.Add(empty);
            return;
        }
        string? group = null;
        foreach (var item in matches.Take(visibleItems))
        {
            string next = StationQuery.Group(item.Created, now, TimeZoneInfo.Local);
            if (Detached && group != next) { cards.Children.Add(new TextBlock { Text = next, FontSize = 12, Opacity = .7, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(2, 8, 0, 2) }); group = next; }
            AddCard(item, version);
        }
        RenderMissingWorkspaceItems();
        if (matches.Length > visibleItems)
        {
            var more = new Button { Content = $"继续显示（还有 {matches.Length - visibleItems} 项）", HorizontalAlignment = HorizontalAlignment.Stretch };
            more.Click += (_, _) => { visibleItems += 50; Refresh(); }; cards.Children.Add(more);
        }
    }
    void AddCard(StationItem item, int version)
    {
        var body = new Grid { ColumnSpacing = 10 };
        body.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); body.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        string label = item.Kind switch { StationKind.File => "文件", StationKind.Folder => "文件夹", StationKind.Link => "链接", _ => "文字" };
        var text = new TextBlock { Text = DisplayTitle(item), TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
        ToolTipService.SetToolTip(text, DisplayTitle(item));
        string detail = (item.Kind == StationKind.Link ? new Uri(item.Value).IdnHost + " · " : "") + StationQuery.TimeLabel(item.Created, DateTimeOffset.Now, TimeZoneInfo.Local);
        var meta = Hint(detail); meta.MaxLines = 1; meta.TextWrapping = TextWrapping.NoWrap; meta.TextTrimming = TextTrimming.CharacterEllipsis;
        ToolTipService.SetToolTip(meta, "存入时间：" + item.Created.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz"));
        var gripContent = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center }; gripContent.Children.Add(text); gripContent.Children.Add(meta);
        var preview = CreatePreview(item);
        var content = new Grid { ColumnSpacing = 10, MinHeight = 48 };
        content.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); content.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        content.Children.Add(preview); Grid.SetColumn(gripContent, 1); content.Children.Add(gripContent);
        var grip = new ContentControl { Content = content, IsTabStop=true, HorizontalContentAlignment=HorizontalAlignment.Stretch, Background = new SolidColorBrush(Colors.Transparent), CanDrag = true, Padding = new Thickness(0) };
        AutomationProperties.SetName(grip, "拖出" + label + "：" + DisplayTitle(item)); ToolTipService.SetToolTip(grip, item.Value.Length > 800 ? item.Value[..800] + "…" : item.Value);
        IDisposable? dragLease = null;
        grip.DragStarting += async (_, e) =>
        {
            if (busy || ModalActive) { e.Cancel = true; return; }
            dragLease = KeepOpenForDrag(); var deferral = e.GetDeferral();
            try { e.AllowedOperations = DataPackageOperation.Copy; await FillSelection(e.Data, item); }
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
        var buttons = BuildItemActions(item, out var copy, out var open);
        buttons.Visibility = Detached ? Visibility.Collapsed : Visibility.Visible;
        Grid.SetColumn(buttons, 1); body.Children.Add(buttons);
        var card = PanelTheme.Card(body);
        PanelTheme.Track(card, static surface => surface.Padding = PanelTheme.Value<Thickness>("StationCardPadding"));
        RegisterSelection(item, card, grip);
        grip.DoubleTapped += async (_, e) => { if (!Detached || busy || ModalActive) return; e.Handled = true; await Run(() => ItemCommand(item, StationAction.Open)); };
        cards.Children.Add(card);
        if (item.Kind is StationKind.File or StationKind.Folder) _ = LoadPreview(item, preview, card, meta, grip, copy, open, version, detail, previewCancellation!.Token);
    }
    static string DisplayTitle(StationItem item) => item.Title.Length > 0 ? item.Title : item.Kind is StationKind.File or StationKind.Folder
        ? Path.GetFileName(item.Value) is { Length: > 0 } name ? name : item.Value
        : item.Kind == StationKind.Link && item.Title.Length > 0 ? item.Title : item.Value;
    async Task Run(Func<Task> action)
    {
        if (busy || destroyed || ModalActive) return;
        busy = true; using var lease = KeepOpen();
        try { await action(); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { status.Text = "操作未完成，请重试。文件记录不会移动或删除原文件。"; }
        finally { busy = false; RefreshActionState(); }
    }
    async Task Clear() => await Run(async () =>
    {
        var snapshot = CurrentItems();
        if (store.ReadOnly || snapshot.Length == 0) return;
        var dialog = new ContentDialog { XamlRoot = root.XamlRoot, Title = workspaceId!=null ? "移出当前分组？" : ActiveQuery.Filtered ? "清空当前筛选结果？" : "清空中转站？",
            Content = $"将移除 {snapshot.Length} 项记录。范围：{ActiveQuery.Description}。\n不会删除原文件，其他记录保留。", PrimaryButtonText = "清空记录", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary) { if (workspaceId is {} id) { await Remember("已移出分组",()=>App.Work.Store.UnlinkMany(id,snapshot.Select(i=>new WorkRef(WorkKind.Station,i.Id)))); status.Text="已移出当前分组，原记录保留"; } else { int removed=0;await Remember("已清空当前记录",async ()=>removed=await store.RemoveArchived(snapshot)); status.Text = $"已移除 {removed} 项记录，原文件未改动。"; } }
    });
    protected override void OnHiding()
    {
        groupMenuGeneration++;activeGroupMenu?.Close();
        CloseQuickLook();
        CloseSearch(false);
        workspaceHoverVersion++;
        undoToastTimer.Stop();undoToast.Visibility=Visibility.Collapsed;feedbackTimer.Stop();status.Text="";
        SaveFloatingLayout();
        if (Detached) SaveQuery(); dayTimer.Stop();
        leaveTimer.Stop(); openedForDrop = false; pin.IsChecked = false; renderVersion++; CancelPreviews();
        cards.Children.Clear(); previewCache.Clear(); ClearSelection();
    }
}
