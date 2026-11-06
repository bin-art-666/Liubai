using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace Liubai;

internal enum PlannerPage { Tasks, Notes }

internal sealed partial class PlannerWindow : PanelWindow
{
    readonly PlannerStore store;
    readonly StackPanel tasksPage = new() { Spacing = 12 }, notesPage = new() { Spacing = 12 };
    readonly StackPanel taskList = new() { Spacing = 7 }, noteList = new() { Spacing = 7 };
    readonly TextBox taskTitle = new() { PlaceholderText = "添加一件要做的事，回车保存", MaxLength = 200 };
    readonly TextBox taskDetail = new() { PlaceholderText = "备注（可选）", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxLength = 20000, MinHeight = 64 };
    readonly TaskScheduleEditor taskSchedule = new();
    CalendarDatePicker taskDate => taskSchedule.Date;
    readonly TextBox taskSearch = new() { PlaceholderText = "搜索任务与备注", MaxLength = 200 };
    readonly ComboBox taskFilter = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly TextBox noteEditor = new() { PlaceholderText = "随手记下想法……内容会自动保存", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxLength = 20000, Height = 180 };
    readonly TextBox noteSearch = new() { PlaceholderText = "搜索记录", MaxLength = 200 };
    readonly TextBlock status = Hint("内容保存在本机，自动保存");
    readonly Button saveButton = IconButton("\uE74E", "立即保存", fontSize: 17);
    readonly TextBlock reminderWarning = Hint("");
    readonly Button undoButton = new() { Content = "撤销删除", Visibility = Visibility.Collapsed };
    readonly ToggleButton tasksTab = new() { Content = "待办" }, notesTab = new() { Content = "随手记" };
    readonly Microsoft.UI.Dispatching.DispatcherQueueTimer saveTimer, dayTimer;
    readonly SemaphoreSlim flushGate = new(1, 1);
    DesktopMonitor display;
    PlannerPage page;
    Guid? noteId;
    bool syncing, draftDirty, noteDirty, busy, destroyed;
    int taskLimit = 50, noteLimit = 50;
    DateOnly shownDay = DateOnly.FromDateTime(DateTime.Now);
    Func<Task>? undo;

    internal PlannerWindow(PlannerStore store, DesktopMonitor display)
    {
        this.store = store; this.display = display;
        Title = "留白 · 待办与随手记";
        var presenter = (OverlappedPresenter)AppWindow.Presenter;
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsResizable = presenter.IsMinimizable = presenter.IsMaximizable = false;
        presenter.IsAlwaysOnTop = true;
        var handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        int corner = 2, border = unchecked((int)0xFFFFFFFE);
        Native.DwmSetWindowAttribute(handle, 33, ref corner, sizeof(int));
        Native.DwmSetWindowAttribute(handle, 34, ref border, sizeof(int));
        SystemBackdrop = new GlassBackdrop(GlassStyle.Frosted);
        saveTimer = DispatcherQueue.CreateTimer(); saveTimer.Interval = TimeSpan.FromMilliseconds(400); saveTimer.IsRepeating = false;
        saveTimer.Tick += async (_, _) => await FlushAsync();
        dayTimer = DispatcherQueue.CreateTimer(); dayTimer.Interval = TimeSpan.FromMinutes(1);
        dayTimer.Tick += (_, _) =>
        {
            var today = DateOnly.FromDateTime(DateTime.Now);
            if (shownDay != today) { shownDay = today; RefreshLists(); }
        };
        var root = new Grid { Padding = new Thickness(20, 16, 20, 16), RowSpacing = 12 };
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var header = new Grid { ColumnSpacing = 12 };
        header.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        tabs.Children.Add(tasksTab); tabs.Children.Add(notesTab); header.Children.Add(tabs);
        var close = IconButton("\uE8BB", "收起待办与随手记");
        var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        saveButton.Click += async (_, _) => await FlushAsync();
        controls.Children.Add(saveButton); controls.Children.Add(close);
        Grid.SetColumn(controls, 1); header.Children.Add(controls);
        close.Click += (_, _) => Hide(); root.Children.Add(header);
        var pages = new Grid(); pages.Children.Add(tasksPage); pages.Children.Add(notesPage);
        var scroll = new ScrollViewer { Content = pages, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(scroll, 1); root.Children.Add(scroll);
        var footer = new StackPanel { Spacing = 8 };
        footer.Children.Add(status);
        footer.Children.Add(reminderWarning);
        undoButton.Click += async (_, _) => await Run(async () =>
        {
            if (undo == null) return;
            await undo(); undo = null; undoButton.Visibility = Visibility.Collapsed; status.Text = "已恢复删除的内容";
        });
        footer.Children.Add(undoButton);
        Grid.SetRow(footer, 2); root.Children.Add(footer);
        BuildTasks(); BuildNotes(); InitializeDrops(root); InitializePanel(root);
        Name(taskTitle, "待办标题"); Name(taskDetail, "待办备注"); Name(taskDate, "待办日期"); Name(taskSearch, "搜索待办");
        Name(taskFilter, "待办分类"); Name(noteEditor, "随手记正文"); Name(noteSearch, "搜索随手记");
        tasksTab.Click += async (_, _) => await SwitchPage(PlannerPage.Tasks);
        notesTab.Click += async (_, _) => await SwitchPage(PlannerPage.Notes);
        taskTitle.TextChanged += (_, _) => DraftChanged(); taskDetail.TextChanged += (_, _) => DraftChanged();
        taskSchedule.Changed += DraftChanged;
        taskTitle.KeyDown += async (_, args) =>
        { if (args.Key == Windows.System.VirtualKey.Enter) { args.Handled = true; await Run(AddTask); } };
        noteEditor.TextChanged += (_, _) =>
        {
            if (syncing) return;
            if (noteId == null && string.IsNullOrWhiteSpace(noteEditor.Text)) return;
            if (store.Data.Notes.FirstOrDefault(n => n.Id == noteId)?.Text == noteEditor.Text) return;
            if (noteId == null && !string.IsNullOrWhiteSpace(noteEditor.Text)) noteId = Guid.NewGuid();
            noteDirty = true; QueueSave();
        };
        taskSearch.TextChanged += (_, _) => { taskLimit = 50; RefreshLists(); };
        noteSearch.TextChanged += (_, _) => { noteLimit = 50; RefreshLists(); };
        taskFilter.SelectionChanged += (_, _) => { taskLimit = 50; RefreshLists(); };
        store.Changed += StoreChanged;
        if (App.Reminders != null) App.Reminders.StatusChanged += ReminderStatus;
        Closed += (_, _) =>
        {
            destroyed = true; saveTimer.Stop(); dayTimer.Stop(); store.Changed -= StoreChanged;
            if (App.Reminders != null) App.Reminders.StatusChanged -= ReminderStatus;
        };
        ReminderStatus();
        RestoreEditors(); SetPage(PlannerPage.Tasks);
        if (store.LoadWarning != null) status.Text = store.LoadWarning;
        if (store.ReadOnly)
            foreach (var control in tasksPage.Children.Concat(notesPage.Children).OfType<Control>()) control.IsEnabled = false;
    }

    static TextBlock Hint(string text) => new() { Text = text, FontSize = 12, Opacity = .7, TextWrapping = TextWrapping.Wrap };
    static void Name(DependencyObject element, string name) => AutomationProperties.SetName(element, name);
    static Button IconButton(string glyph, string name, double fontSize = 13)
    {
        var button = new Button { Content = new FontIcon { Glyph = glyph, FontSize = fontSize }, Width = 30, Height = 30,
            MinWidth = 0, MinHeight = 0, Padding = new Thickness(0), BorderThickness = new Thickness(0), Background = new SolidColorBrush(Colors.Transparent) };
        Name(button, name); ToolTipService.SetToolTip(button, name); return button;
    }
    static DateOnly? DateOf(CalendarDatePicker picker) => picker.Date is { } value ? DateOnly.FromDateTime(value.Date) : null;
    static DateTimeOffset? CalendarDate(DateOnly? date) => date is { } value ? new DateTimeOffset(value.ToDateTime(TimeOnly.MinValue)) : null;

    void BuildTasks()
    {
        tasksPage.Children.Add(new TextBlock { Text = "把事情安排好", FontSize = 23, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        tasksPage.Children.Add(Hint("今天处理，或给之后的事情安排一个日期。"));
        tasksPage.Children.Add(taskTitle);
        var details = new Expander { Header = "日期、时间与提醒（可选）", HorizontalAlignment = HorizontalAlignment.Stretch };
        var fields = new StackPanel { Spacing = 8 }; fields.Children.Add(taskSchedule.Root);
        fields.Children.Add(taskDetail); details.Content = fields; tasksPage.Children.Add(details);
        var add = new Button { Content = "添加待办", HorizontalAlignment = HorizontalAlignment.Stretch };
        add.Click += async (_, _) => await Run(AddTask); tasksPage.Children.Add(add);
        tasksPage.Children.Add(taskSearch);
        foreach (string filter in new[] { "今天（含逾期）", "计划", "全部待办", "已完成" }) taskFilter.Items.Add(filter);
        taskFilter.SelectedIndex = 0; tasksPage.Children.Add(taskFilter); tasksPage.Children.Add(taskList);
    }
    void BuildNotes()
    {
        notesPage.Children.Add(new TextBlock { Text = "先记下来", FontSize = 23, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        notesPage.Children.Add(Hint("写下想法或片段，停笔后自动保存。"));
        notesPage.Children.Add(noteEditor);
        notesPage.Children.Add(noteImages);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var create = new Button { Content = "新建记录" }; create.Click += async (_, _) => await Run(async () =>
        {
            if (!await FlushAsync()) return;
            await store.ActivateNote(null); LoadNote(null); status.Text = "新记录：输入后自动保存"; noteEditor.Focus(FocusState.Programmatic);
        });
        var convert = new Button { Content = "转为待办" }; convert.Click += async (_, _) => await Run(ConvertCurrentNote);
        var copy = new Button { Content = "复制" }; copy.Click += (_, _) =>
        {
            var package = new Windows.ApplicationModel.DataTransfer.DataPackage(); package.SetText(noteEditor.Text);
            try { Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package); status.Text = "已复制记录"; }
            catch { status.Text = "复制失败，请重试"; }
        };
        actions.Children.Add(create); actions.Children.Add(convert); actions.Children.Add(copy); notesPage.Children.Add(actions);
        notesPage.Children.Add(noteSearch); notesPage.Children.Add(noteList);
    }
    void RestoreEditors()
    {
        syncing = true;
        taskTitle.Text = store.Data.Draft.Title; taskDetail.Text = store.Data.Draft.Detail;
        taskSchedule.Load(store.Data.Draft.Date, store.Data.Draft.Time, store.Data.Draft.RemindBefore);
        syncing = false; LoadNote(store.Data.ActiveNote);
    }
    void LoadNote(Guid? id)
    {
        var note = store.Data.Notes.FirstOrDefault(n => n.Id == id);
        syncing = true; noteId = note?.Id; noteEditor.Text = note?.Text ?? ""; syncing = false;
        noteDirty = false;
        RefreshNoteImages();
    }
    void StoreChanged() => DispatcherQueue.TryEnqueue(() => { if (!destroyed) RefreshLists(); });
    void ReminderStatus() => DispatcherQueue.TryEnqueue(() =>
    {
        if (destroyed) return;
        reminderWarning.Text = App.Reminders?.Warning ?? "";
        reminderWarning.Visibility = reminderWarning.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    });
    TodoDraft CurrentDraft() => new(taskTitle.Text, taskDetail.Text, taskSchedule.DateValue, taskSchedule.TimeValue, taskSchedule.LeadValue);
    void DraftChanged()
    {
        if (syncing || CurrentDraft() == store.Data.Draft) return;
        draftDirty = true; QueueSave();
    }
    void QueueSave() { status.Text = "正在保存…"; saveTimer.Stop(); saveTimer.Start(); }

    internal async Task<bool> FlushAsync()
    {
        saveTimer.Stop(); await flushGate.WaitAsync();
        saveButton.IsEnabled = false;
        try
        {
            // XAML can dispatch TextChanged after a programmatic edit or a fast
            // action. Flush the actual editors rather than trusting event flags.
            var draft = CurrentDraft();
            draftDirty |= draft != store.Data.Draft;
            if (noteId == null && !string.IsNullOrWhiteSpace(noteEditor.Text)) noteId = Guid.NewGuid();
            if (noteId is { } active)
                noteDirty |= store.Data.Notes.FirstOrDefault(n => n.Id == active)?.Text != noteEditor.Text;
            if (draftDirty)
            {
                await store.SaveDraft(draft);
                draftDirty = CurrentDraft() != draft;
            }
            if (noteDirty)
            {
                string text = noteEditor.Text; Guid? id = noteId;
                if (id is { } value) await store.SaveNote(value, text);
                noteDirty = noteId != id || noteEditor.Text != text;
            }
            if (draftDirty || noteDirty) { saveTimer.Stop(); saveTimer.Start(); }
            if (!destroyed) status.Text = store.ReadOnly ? store.LoadWarning : draftDirty || noteDirty ? "正在保存…" : $"已保存 · {DateTime.Now:HH:mm:ss}";
            return !draftDirty && !noteDirty;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        { if (!destroyed) status.Text = "保存失败，内容仍保留在面板中。请重试。"; return false; }
        finally { if (!destroyed) saveButton.IsEnabled = true; flushGate.Release(); }
    }
    async Task Run(Func<Task> action)
    {
        if (busy || destroyed) return; busy = true;
        try { await action(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or System.Runtime.InteropServices.COMException)
        { if (!destroyed) { RefreshLists(); status.Text = "操作未完成：" + ex.Message; } }
        finally { busy = false; }
    }
    async Task AddTask()
    {
        if (string.IsNullOrWhiteSpace(taskTitle.Text)) { status.Text = "先写下待办标题"; taskTitle.Focus(FocusState.Programmatic); return; }
        if (!await FlushAsync()) return;
        var submitted = CurrentDraft();
        await store.AddTask(submitted.Title, submitted.Detail, submitted.Date, submitted.Time, submitted.RemindBefore);
        if (CurrentDraft() == submitted)
        {
            syncing = true; taskTitle.Text = taskDetail.Text = ""; taskSchedule.Load(null, null, null); syncing = false; draftDirty = false;
        }
        else { draftDirty = true; QueueSave(); }
        // Undated tasks live in All rather than silently disappearing from Today.
        taskFilter.SelectedIndex = 2; taskSearch.Text = ""; RefreshLists();
        status.Text = "待办已添加"; taskTitle.Focus(FocusState.Programmatic);
    }
    async Task EditTask(TodoItem item)
    {
        using var lease = KeepOpen();
        var title = new TextBox { Header = "标题", Text = item.Title, MaxLength = 200 };
        var detail = new TextBox { Header = "备注", Text = item.Detail, MaxLength = 20000, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 140 };
        var schedule = new TaskScheduleEditor(); schedule.Load(item.Date, item.Time, item.RemindBefore);
        var error = Hint("");
        var fields = new StackPanel { Spacing = 12 }; fields.Children.Add(title); fields.Children.Add(schedule.Root); fields.Children.Add(detail);
        if (item.SourceNote != null) fields.Children.Add(Hint("由随手记转入；原记录仍保留。"));
        fields.Children.Add(error);
        var dialog = new ContentDialog { XamlRoot = ((FrameworkElement)Content).XamlRoot, Title = "编辑待办",
            Content = fields, PrimaryButtonText = "保存修改", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Primary };
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            if (string.IsNullOrWhiteSpace(title.Text)) { args.Cancel = true; error.Text = "标题不能为空"; return; }
            var deferral = args.GetDeferral();
            try { await store.EditTask(item.Id, title.Text, detail.Text, schedule.DateValue, schedule.TimeValue, schedule.LeadValue); status.Text = "修改已保存"; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            { args.Cancel = true; error.Text = "保存失败，请重试。"; }
            finally { deferral.Complete(); }
        };
        await dialog.ShowAsync();
    }
    async Task ConvertCurrentNote()
    {
        if (string.IsNullOrWhiteSpace(noteEditor.Text)) { status.Text = "先写下一些内容再转为待办"; return; }
        if (!await FlushAsync() || noteId == null) return;
        Guid id = await store.ConvertNote(noteId.Value);
        SetPage(PlannerPage.Tasks); taskFilter.SelectedIndex = 2; taskSearch.Text = "";
        status.Text = "已关联待办，原记录保留；可在下方安排日期";
        await EditTask(store.Data.Tasks.Single(t => t.Id == id));
    }
    void SetUndo(Func<Task> action) { undo = action; undoButton.Visibility = Visibility.Visible; status.Text = "已删除，可撤销"; }
    internal async Task<bool> SwitchPage(PlannerPage target)
    { if (!await FlushAsync()) return false; SetPage(target); return true; }
    void SetPage(PlannerPage target)
    {
        page = target; tasksTab.IsChecked = page == PlannerPage.Tasks; notesTab.IsChecked = page == PlannerPage.Notes;
        tasksPage.Visibility = page == PlannerPage.Tasks ? Visibility.Visible : Visibility.Collapsed;
        notesPage.Visibility = page == PlannerPage.Notes ? Visibility.Visible : Visibility.Collapsed;
        RefreshLists();
        NotifyPresentationChanged();
    }
    internal PlannerPage Page => page;
    internal async Task OpenWorkItem(WorkRef item, DesktopMonitor monitor, int top)
    {
        await Open(item.Kind == WorkKind.Note ? PlannerPage.Notes : PlannerPage.Tasks, monitor, top);
        if (item.Kind == WorkKind.Note)
        {
            if (!store.Data.Notes.Any(n => n.Id == item.Id)) throw new IOException("该笔记已移除。");
            await store.ActivateNote(item.Id); LoadNote(item.Id);
        }
        else
        {
            var task = store.Data.Tasks.FirstOrDefault(t => t.Id == item.Id) ?? throw new IOException("该待办已移除。");
            taskFilter.SelectedIndex = task.Completed ? 3 : 2; taskSearch.Text = task.Title;
        }
    }
    internal string DisplayDevice => display.Device;
    internal async Task OpenReminder(Guid id, DesktopMonitor monitor, int barBottom)
    {
        await Open(PlannerPage.Tasks, monitor, barBottom);
        if (!IsOpen) return;
        var item = store.Data.Tasks.FirstOrDefault(t => t.Id == id);
        if (item == null) { status.Text = "这项任务已被删除"; return; }
        taskFilter.SelectedIndex = item.Completed ? 3 : 2; taskSearch.Text = item.Title;
        await Run(() => EditTask(item));
    }
    internal async Task Open(PlannerPage target, DesktopMonitor monitor, int barBottom)
    {
        display = monitor;
        if (!await SwitchPage(target)) return;
        Reposition(barBottom); ShowPanel(); dayTimer.Start();
        DispatcherQueue.TryEnqueue(() => { if (IsOpen) (page == PlannerPage.Tasks ? taskTitle : noteEditor).Focus(FocusState.Programmatic); });
    }
    internal void UpdateDisplay(DesktopMonitor monitor) { display = monitor; if (IsOpen) Reposition(monitor.Info.Work.Top); }
    void Reposition(int barBottom)
    {
        double scale = display.Dpi / 96d; var work = display.Info.Work;
        int margin = (int)Math.Round(12 * scale);
        int width = Math.Min((int)Math.Round(430 * scale), work.Right - work.Left - margin * 2);
        int top = Math.Max(work.Top, barBottom) + (int)Math.Round(8 * scale);
        int height = Math.Max(120, Math.Min((int)Math.Round(700 * scale), work.Bottom - top - margin));
        AppWindow.MoveAndResize(new RectInt32(work.Right - width - margin, top, width, height));
    }
    protected override void OnHiding() { dayTimer.Stop(); _ = FlushAsync(); }
}
