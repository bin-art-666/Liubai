using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace Liubai;

internal sealed class ClockWindow : PanelWindow
{
    readonly ClockStore store;
    readonly Grid root = PanelTheme.Layout();
    readonly ToggleButton alarmTab = new() { Content = "闹钟" }, timerTab = new() { Content = "计时" };
    readonly StackPanel alarmPage = new() { Spacing = 12 }, timerPage = new() { Spacing = 12 }, alarmList = new() { Spacing = 8 };
    readonly TextBlock status = Hint("添加闹钟或开始计时后自动保存"), warning = Hint(""), alarmPreview = Hint("");
    readonly TextBlock remaining = new() { Text = "25:00", FontSize = 46, HorizontalAlignment = HorizontalAlignment.Center, FontFamily = new FontFamily("Segoe UI Variable Display") };
    readonly TextBlock timerDescription = Hint("");
    readonly CheckBox showRemaining = new() { Content = "在顶栏显示剩余时间" };
    readonly TimePicker alarmTime = new() { Header = "提醒时间", ClockIdentifier = "24HourClock", HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly TextBox alarmTitle = new() { PlaceholderText = "备注（可选），例如取快递", MaxLength = 120 };
    readonly TextBox timerTitle = new() { PlaceholderText = "用途（可选），例如煮面", MaxLength = 120 };
    readonly NumberBox hours = Number("时", 99, 0), minutes = Number("分", 59, 25), seconds = Number("秒", 59, 0);
    readonly Button addAlarm = new() { Content = "添加闹钟", HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly Button start = new() { Content = "开始计时" }, pause = new() { Content = "暂停" }, cancel = new() { Content = "取消计时" }, repeat = new() { Content = "再计一次" };
    readonly Microsoft.UI.Dispatching.DispatcherQueueTimer timer;
    readonly Dictionary<Guid, TextBlock> alarmLabels = new();
    DesktopMonitor display;
    bool busy, destroyed, timerSelected, syncingDuration, syncingDisplay;
    Guid? loadedCountdown;
    internal bool TimerSelected => timerSelected;
    internal ClockWindow(ClockStore store, DesktopMonitor display)
    {
        this.store = store; this.display = display; Title = "留白 · 时钟";
        var presenter = (OverlappedPresenter)AppWindow.Presenter;
        presenter.SetBorderAndTitleBar(false, false); presenter.IsAlwaysOnTop = true;
        presenter.IsResizable = presenter.IsMaximizable = presenter.IsMinimizable = false; AppWindow.IsShownInSwitchers = false;
        int corner = 2, border = unchecked((int)0xFFFFFFFE); var handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        Native.DwmSetWindowAttribute(handle, 33, ref corner, sizeof(int)); Native.DwmSetWindowAttribute(handle, 34, ref border, sizeof(int));
        SystemBackdrop = PanelTheme.Backdrop();
        root.RowDefinitions.Add(new() { Height = GridLength.Auto }); root.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) }); root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        tabs.Children.Add(alarmTab); tabs.Children.Add(timerTab);
        var close = Icon("\uE8BB", "收起时钟"); close.HorizontalAlignment = HorizontalAlignment.Right; close.Click += (_, _) => Hide(); root.Children.Add(PanelTheme.Header(tabs, close));
        // WinUI's overlay scrollbar otherwise covers the final input/spin button.
        var pages = new Grid { Margin = new Thickness(0, 0, 16, 0) }; pages.Children.Add(alarmPage); pages.Children.Add(timerPage);
        var scroll = new ScrollViewer { Content = pages, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }; Grid.SetRow(scroll, 1); root.Children.Add(scroll);
        var footer = new StackPanel { Spacing = 6 }; footer.Children.Add(showRemaining); footer.Children.Add(status); footer.Children.Add(warning);
        Name(showRemaining, "在顶栏显示剩余时间");
        ToolTipService.SetToolTip(showRemaining, "运行时显示剩余时间，暂停时显示 Ⅱ；结束后恢复图标。");
        showRemaining.Click += async (_, _) =>
        {
            if (syncingDisplay || busy) { UpdateTime(); return; }
            await Run(() => store.SetShowRemainingInBar(showRemaining.IsChecked == true));
        };
        footer.Children.Add(Hint("提醒需要留白保持运行。关机时不会响铃；睡眠或锁屏期间到期，恢复后补提醒。")); Grid.SetRow(footer, 2); root.Children.Add(footer);
        alarmTime.SelectedTime = new TimeSpan(DateTime.Now.AddMinutes(5).Hour, DateTime.Now.AddMinutes(5).Minute, 0);
        Name(alarmTime, "闹钟时间"); Name(alarmTitle, "闹钟备注"); Name(timerTitle, "计时用途"); Name(start, "开始计时"); Name(pause, "暂停或继续计时"); Name(cancel, "取消计时");
        alarmPage.Children.Add(new TextBlock { Text = "在指定时间提醒你", FontSize = 22 }); alarmPage.Children.Add(alarmTime); alarmPage.Children.Add(alarmPreview);
        alarmPage.Children.Add(alarmTitle); alarmPage.Children.Add(addAlarm); alarmPage.Children.Add(alarmList);
        timerPage.Children.Add(new TextBlock { Text = "留一点时间，专心做事", FontSize = 22 }); timerPage.Children.Add(remaining); timerPage.Children.Add(timerDescription);
        var presets = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        foreach (int value in new[] { 5, 10, 25, 60 })
        {
            var button = new Button { Content = value + " 分钟", Tag = "clock-preset" };
            button.Click += (_, _) => { if (!ActiveCountdown) { hours.Value = value / 60; minutes.Value = value % 60; seconds.Value = 0; UpdateTime(); } }; presets.Children.Add(button);
        }
        timerPage.Children.Add(presets);
        var duration = new Grid { ColumnSpacing = 8 };
        foreach (var box in new[] { hours, minutes, seconds })
        { duration.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); Grid.SetColumn(box, duration.Children.Count); duration.Children.Add(box); box.ValueChanged += (_, _) => { if (!syncingDuration) UpdateTime(); }; }
        timerPage.Children.Add(duration); timerPage.Children.Add(timerTitle);
        var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 }; controls.Children.Add(start); controls.Children.Add(pause); controls.Children.Add(repeat); controls.Children.Add(cancel); timerPage.Children.Add(controls);
        addAlarm.Click += async (_, _) => await Run(async () => { await store.AddAlarm(SelectedAlarm(), alarmTitle.Text, DateTimeOffset.Now); alarmTitle.Text = ""; status.Text = "闹钟已保存"; });
        start.Click += async (_, _) => await Run(async () => { await store.StartCountdown(Duration(), timerTitle.Text, DateTimeOffset.Now); status.Text = "计时已开始，收起面板后继续运行"; });
        pause.Click += async (_, _) => await Run(async () => { if (store.Data.Countdown?.State == ClockState.Paused) await store.ResumeCountdown(DateTimeOffset.Now); else await store.PauseCountdown(DateTimeOffset.Now); });
        cancel.Click += async (_, _) => await Run(async () => { await store.CancelCountdown(); status.Text = "计时已取消"; });
        repeat.Click += async (_, _) => await Run(async () => { if (store.Data.Countdown is { } item) await store.ApplyAction(new(item.Id, item.Token, ClockActionKind.Repeat), DateTimeOffset.Now); });
        alarmTab.Click += (_, _) => SelectPage(false); timerTab.Click += (_, _) => SelectPage(true); alarmTime.SelectedTimeChanged += (_, _) => UpdateTime();
        timer = DispatcherQueue.CreateTimer(); timer.Interval = TimeSpan.FromSeconds(1); timer.Tick += (_, _) => UpdateTime();
        store.Changed += Changed;
        Closed += (_, _) => { destroyed = true; timer.Stop(); store.Changed -= Changed; };
        InitializePanel(root); SelectPage(false);
    }
    static void Name(DependencyObject element, string name) => AutomationProperties.SetName(element, name);
    static TextBlock Hint(string text) => PanelTheme.Hint(text);
    static NumberBox Number(string header, int maximum, int initial)
    {
        var box = new NumberBox { Header = header, Minimum = 0, Maximum = maximum, Value = initial, SmallChange = 1,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact, HorizontalAlignment = HorizontalAlignment.Stretch, MinWidth = 0 };
        Name(box, "计时" + header); return box;
    }
    static Button Icon(string glyph, string name) => PanelTheme.Icon(glyph, name);
    bool ActiveCountdown => store.Data.Countdown?.State is ClockState.Running or ClockState.Paused;
    TimeOnly SelectedAlarm() => alarmTime.SelectedTime is { } time ? new(time.Hours, time.Minutes) : throw new InvalidDataException("请先选择提醒时间。");
    int Duration()
    {
        var values = new[] { hours.Value, minutes.Value, seconds.Value };
        if (values.Any(v => !double.IsFinite(v) || v != Math.Truncate(v))) throw new InvalidDataException("时、分、秒请填写整数。");
        if (hours.Value is < 0 or > 99 || minutes.Value is < 0 or > 59 || seconds.Value is < 0 or > 59) throw new InvalidDataException("小时范围为 0–99，分、秒范围为 0–59。");
        int result = (int)hours.Value * 3600 + (int)minutes.Value * 60 + (int)seconds.Value;
        if (result is < 1 or > ClockPolicy.MaximumSeconds) throw new InvalidDataException("请设置至少 1 秒的计时。");
        return result;
    }
    internal void SelectPage(bool countdown)
    {
        timerSelected = countdown; alarmTab.IsChecked = !countdown; timerTab.IsChecked = countdown;
        alarmPage.Visibility = countdown ? Visibility.Collapsed : Visibility.Visible; timerPage.Visibility = countdown ? Visibility.Visible : Visibility.Collapsed; UpdateTime();
    }
    internal void Open(DesktopMonitor monitor, int top, Guid? id = null)
    {
        display = monitor; Position(top); RefreshAlarms();
        SelectPage(ClockPolicy.PreferCountdown(store.Data, id));
        status.Text = store.LoadWarning ?? "添加闹钟或开始计时后自动保存";
        UpdateTime(); ShowPanel(); timer.Start();
    }
    internal void UpdateDisplay(DesktopMonitor monitor) { display = monitor; if (IsOpen) Position(monitor.Info.Work.Top); }
    void Position(int top)
    {
        var work = display.Info.Work; double scale = display.Dpi / 96d;
        int width = Math.Min((int)(440 * scale), work.Right - work.Left), height = Math.Min((int)(640 * scale), work.Bottom - work.Top);
        AppWindow.MoveAndResize(new RectInt32(work.Right - width, Math.Clamp(top, work.Top, work.Bottom - height), width, height));
    }
    void Changed() { if (!destroyed && IsOpen) { RefreshAlarms(); UpdateTime(); } }
    void RefreshAlarms()
    {
        alarmList.Children.Clear(); alarmLabels.Clear();
        if (store.Data.Alarms.Length == 0) alarmList.Children.Add(Hint("还没有闹钟。默认只提醒一次，开启后会显示具体日期。"));
        foreach (var item in store.Data.Alarms.OrderBy(e => ClockPolicy.Due(e, TimeZoneInfo.Local)))
        {
            var card = new Grid { ColumnSpacing = 8 };
            card.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); card.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            var content = new StackPanel { Spacing = 3 }; content.Children.Add(new TextBlock { Text = item.LocalAlarm!.Value.ToString("HH:mm"), FontSize = 27 });
            if (item.Title.Length > 0) content.Children.Add(new TextBlock { Text = item.Title, FontSize = 14, TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis });
            var label = Hint(""); content.Children.Add(label); alarmLabels[item.Id] = label; card.Children.Add(content);
            var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
            var toggle = new ToggleSwitch { IsOn = item.State == ClockState.Running, OnContent = "", OffContent = "", IsEnabled = !store.ReadOnly, MinWidth = 0 };
            Name(toggle, "启用闹钟 " + item.LocalAlarm.Value.ToString("HH:mm"));
            toggle.Toggled += async (_, _) =>
            {
                if (busy) { toggle.IsOn = item.State == ClockState.Running; return; }
                await Run(async () => await store.SetAlarmEnabled(item.Id, toggle.IsOn, DateTimeOffset.Now));
            };
            var edit = Icon("\uE70F", "编辑闹钟"); edit.IsEnabled = !store.ReadOnly; edit.Click += async (_, _) => await Edit(item);
            var remove = Icon("\uE74D", "删除闹钟"); remove.IsEnabled = !store.ReadOnly; remove.Click += async (_, _) => await Run(async () => await store.RemoveAlarm(item.Id));
            controls.Children.Add(toggle); controls.Children.Add(edit); controls.Children.Add(remove); Grid.SetColumn(controls, 1); card.Children.Add(controls);
            alarmList.Children.Add(PanelTheme.Card(card));
        }
    }
    void UpdateTime()
    {
        var now = DateTimeOffset.Now; var zone = TimeZoneInfo.Local;
        syncingDisplay = true;
        try { showRemaining.IsChecked = store.Data.ShowRemainingInBar; showRemaining.IsEnabled = !busy && !store.ReadOnly; }
        finally { syncingDisplay = false; }
        warning.Text = store.LoadWarning ?? App.Clocks?.Warning ?? ""; warning.Visibility = warning.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (alarmTime.SelectedTime != null) alarmPreview.Text = ClockPolicy.When(ClockPolicy.NextAlarm(SelectedAlarm(), now, zone), now, zone) + " · 只提醒一次";
        else alarmPreview.Text = "请选择提醒时间";
        addAlarm.IsEnabled = !busy && !store.ReadOnly && alarmTime.SelectedTime != null;
        foreach (var item in store.Data.Alarms)
            if (alarmLabels.TryGetValue(item.Id, out var label)) label.Text = item.State switch
            {
                ClockState.Stopped => "已关闭", ClockState.Finished => "已提醒 · 再次开启可重新设置",
                _ => ClockPolicy.IsDue(item, now, zone) ? "已到时间，等待提醒" : item.SnoozeUntil is { } snooze
                    ? "稍后提醒 · " + ClockPolicy.When(TimeZoneInfo.ConvertTime(snooze, zone).DateTime, now, zone) : ClockPolicy.When(item.LocalAlarm!.Value, now, zone)
            };
        bool active = ActiveCountdown; var current = store.Data.Countdown;
        if (current is null) loadedCountdown = null;
        else if (loadedCountdown != current.Id)
        {
            loadedCountdown = current.Id; syncingDuration = true;
            try { hours.Value = current.DurationSeconds / 3600; minutes.Value = current.DurationSeconds / 60 % 60; seconds.Value = current.DurationSeconds % 60; timerTitle.Text = current.Title; }
            finally { syncingDuration = false; }
        }
        foreach (var box in new[] { hours, minutes, seconds }) box.IsEnabled = !active && !store.ReadOnly;
        timerTitle.IsEnabled = !active && !store.ReadOnly;
        foreach (var button in timerPage.Children.OfType<StackPanel>().SelectMany(p => p.Children.OfType<Button>()).Where(b => Equals(b.Tag, "clock-preset"))) button.IsEnabled = !active && !store.ReadOnly;
        start.Visibility = active ? Visibility.Collapsed : Visibility.Visible;
        start.Content = current?.State == ClockState.Finished ? "开始新计时" : "开始计时";
        pause.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        cancel.Visibility = current != null ? Visibility.Visible : Visibility.Collapsed;
        repeat.Visibility = current?.State == ClockState.Finished ? Visibility.Visible : Visibility.Collapsed;
        start.IsEnabled = pause.IsEnabled = cancel.IsEnabled = repeat.IsEnabled = !busy && !store.ReadOnly;
        try { _ = Duration(); } catch (InvalidDataException) { start.IsEnabled = false; }
        pause.Content = current?.State == ClockState.Paused ? "继续" : "暂停";
        if (current != null)
        {
            var left = ClockPolicy.Remaining(current, now, zone); remaining.Text = ClockPolicy.Format(left);
            pause.IsEnabled &= current.State == ClockState.Paused || left > TimeSpan.Zero;
            timerDescription.Text = (current.Title.Length > 0 ? current.Title + " · " : "") + (current.State switch
            { ClockState.Paused => "已暂停", ClockState.Finished => "计时结束", _ => left == TimeSpan.Zero ? "已到时间，等待提醒" : "计时中，收起后继续运行" });
        }
        else
        {
            try { remaining.Text = ClockPolicy.Format(TimeSpan.FromSeconds(Duration())); }
            catch (InvalidDataException) { remaining.Text = "00:00"; start.IsEnabled = false; }
            timerDescription.Text = "设置一个倒计时";
        }
    }
    async Task Edit(ClockEntry item) => await Run(async () =>
    {
        var time = new TimePicker { Header = "提醒时间", ClockIdentifier = "24HourClock", SelectedTime = item.LocalAlarm!.Value.TimeOfDay };
        var title = new TextBox { Header = "备注", Text = item.Title, MaxLength = 120 };
        var enabled = new ToggleSwitch { Header = "启用闹钟", IsOn = item.State == ClockState.Running };
        var preview = Hint("");
        void Preview() { if (time.SelectedTime is { } selected) preview.Text = ClockPolicy.When(ClockPolicy.NextAlarm(new TimeOnly(selected.Hours, selected.Minutes), DateTimeOffset.Now, TimeZoneInfo.Local), DateTimeOffset.Now, TimeZoneInfo.Local); }
        time.SelectedTimeChanged += (_, _) => Preview(); Preview();
        var body = new StackPanel { Spacing = 12 }; body.Children.Add(time); body.Children.Add(preview); body.Children.Add(title); body.Children.Add(enabled);
        var dialog = new ContentDialog { XamlRoot = root.XamlRoot, Title = "编辑闹钟", Content = body, PrimaryButtonText = "保存", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Primary };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary && time.SelectedTime is { } selected)
            await store.EditAlarm(item.Id, new TimeOnly(selected.Hours, selected.Minutes), title.Text, enabled.IsOn, DateTimeOffset.Now);
    });
    async Task Run(Func<Task> action)
    {
        if (busy || destroyed || ModalActive) return;
        busy = true; using var lease = KeepOpen();
        try { await action(); status.Text = "已保存"; }
        catch (Exception ex) when (ex is not OutOfMemoryException) { status.Text = ex is InvalidDataException ? ex.Message : "未能保存，请重试。"; RefreshAlarms(); }
        finally { busy = false; UpdateTime(); }
    }
    internal async Task<bool> ConfirmQuit()
    {
        using var lease = KeepOpen();
        for (int i = 0; root.XamlRoot is null && i < 10; i++) await Task.Delay(20);
        if (root.XamlRoot is null) { status.Text = "界面尚未就绪，请稍后再试退出。"; return false; }
        var dialog = new ContentDialog { XamlRoot = root.XamlRoot, Title = "仍有闹钟或计时未结束", Content = "退出后不会响铃。记录会保留，下次启动时补提醒。要退出留白吗？",
            PrimaryButtonText = "退出留白", CloseButtonText = "继续运行", DefaultButton = ContentDialogButton.Close };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
    protected override void OnHiding() => timer.Stop();
}
