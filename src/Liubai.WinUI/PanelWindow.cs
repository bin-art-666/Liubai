using System.Diagnostics;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Text;
using Windows.Graphics;
using Windows.System;

namespace Liubai.Modern;

public sealed class PanelWindow : Window
{
    public TodoStore Store { get; }
    public bool IsOpen { get; private set; }
    public bool IsSettings { get; private set; }
    public bool IsDialogOpen => dialogOpen || openMenus > 0;
    readonly Action changed;
    readonly Action exit;
    readonly Func<bool> getAutoHide;
    readonly Func<bool, bool> setAutoHide;
    readonly Border surface;
    readonly Grid layout;
    readonly TextBlock heading, subtitle, status;
    readonly ContentControl body;
    TextBox? input;
    StackPanel? tasks;
    ToggleButton? completed;
    bool showCompleted, dialogOpen;
    bool closed;
    int openMenus;
    string inputDraft = "";
    readonly bool inspect;
    public PanelWindow(TodoStore store, Action changed, Action exit, Func<bool> getAutoHide, Func<bool, bool> setAutoHide, bool inspect)
    {
        Store = store; this.changed = changed; this.exit = exit; this.inspect = inspect;
        this.getAutoHide = getAutoHide; this.setAutoHide = setAutoHide;
        Title = "留白待办"; Shell.Configure(this, inspect);
        layout = new Grid { Padding = new Thickness(22, 22, 22, 16), RowSpacing = 20 };
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var header = new Grid { ColumnSpacing = 12 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var titles = new StackPanel { Spacing = 6 };
        heading = UI.Text("待办", 24); heading.FontWeight = FontWeights.SemiBold;
        subtitle = UI.Text("写下要做的事，下次回来继续。", 12, .65);
        titles.Children.Add(heading); titles.Children.Add(subtitle); header.Children.Add(titles);
        var close = new Button { Content = Icons.Symbol("\uE8BB", 12), Background = new SolidColorBrush(Colors.Transparent), BorderThickness = new Thickness(0), Padding = new Thickness(8), VerticalAlignment = VerticalAlignment.Top };
        AutomationProperties.SetName(close, "收起"); ToolTipService.SetToolTip(close, "收起");
        close.Click += (_, _) => Hide(); Grid.SetColumn(close, 1); header.Children.Add(close);
        layout.Children.Add(header);
        body = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
        Grid.SetRow(body, 1); layout.Children.Add(body);
        status = UI.Text(store.Warning ?? "自动保存在此电脑", 11, .6);
        Grid.SetRow(status, 2); layout.Children.Add(status);
        surface = new Border { CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), BorderBrush = UI.Brush("SurfaceStrokeColorDefaultBrush", 45, 128, 128, 128), Child = layout };
        Content = surface; Shell.Backdrop(this);
        Activated += (_, e) =>
        {
            if (e.WindowActivationState == WindowActivationState.Deactivated && IsOpen && !dialogOpen && !inspect) Hide();
        };
        AppWindow.Hide();
        Closed += (_, _) => { closed = true; IsOpen = false; App.Trace("Todo window closed"); };
    }
    public void Open(bool settings, int barBottom)
    {
        if (closed || App.IsExiting) return;
        IsSettings = settings;
        if (settings) BuildSettings(); else BuildTodos();
        float scale = Native.GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96F;
        var monitor = Native.PrimaryMonitor();
        int width = (int)(388 * scale);
        int height = Math.Min((int)((settings ? 440 : 550) * scale), monitor.Work.Bottom - barBottom - (int)(20 * scale));
        AppWindow.MoveAndResize(new RectInt32(monitor.Monitor.Right - width - (int)(12 * scale), barBottom + (int)(8 * scale), width, height));
        IsOpen = true; Activate();
        if (!settings) DispatcherQueue.TryEnqueue(() => { if (!closed && !App.IsExiting) input?.Focus(FocusState.Programmatic); });
    }
    public void Hide() { if (closed || App.IsExiting || dialogOpen) return; IsOpen = false; AppWindow.Hide(); }
    bool Save()
    {
        try { Store.Save(); status.Text = "已保存到此电脑"; changed(); return true; }
        catch { status.Text = "保存失败，请检查磁盘空间和文件权限。"; return false; }
    }
    void BuildTodos()
    {
        Title = "留白待办"; heading.Text = "待办"; subtitle.Text = "写下要做的事，下次回来继续。";
        var grid = new Grid { RowSpacing = 16 };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var entry = new Grid { ColumnSpacing = 8 };
        entry.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        entry.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        input = new TextBox { Text = inputDraft, PlaceholderText = "记下一件要做的事…", MaxLength = 500, MinHeight = 40 };
        input.TextChanged += (_, _) => inputDraft = input.Text;
        AutomationProperties.SetName(input, "新增待办");
        input.KeyDown += (_, e) => { if (e.Key == VirtualKey.Enter) { e.Handled = true; Add(); } };
        entry.Children.Add(input);
        var add = new Button { Content = Icons.Symbol("\uE710"), MinWidth = 40, MinHeight = 40, Padding = new Thickness(10), Style = (Style)Application.Current.Resources["LiubaiPrimaryButtonStyle"] };
        AutomationProperties.SetName(add, "添加待办"); ToolTipService.SetToolTip(add, "添加待办");
        add.Click += (_, _) => Add(); Grid.SetColumn(add, 1); entry.Children.Add(add); grid.Children.Add(entry);
        tasks = new StackPanel { Spacing = 8 };
        var scroll = new ScrollViewer { Content = tasks, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(scroll, 1); grid.Children.Add(scroll);
        completed = new ToggleButton { IsChecked = showCompleted, HorizontalAlignment = HorizontalAlignment.Left, Background = new SolidColorBrush(Colors.Transparent), BorderThickness = new Thickness(0), Padding = new Thickness(0, 6, 8, 6), FontSize = 12 };
        completed.Click += (_, _) => { showCompleted = completed.IsChecked == true; RenderTasks(); };
        Grid.SetRow(completed, 2); grid.Children.Add(completed);
        body.Content = grid; RenderTasks();
    }
    void Add()
    {
        if (input == null || string.IsNullOrWhiteSpace(input.Text)) return;
        var task = new Todo { Text = input.Text.Trim() }; Store.Data.Items.Add(task);
        if (Save()) { input.Text = ""; RenderTasks(); } else Store.Data.Items.Remove(task);
    }
    void RenderTasks()
    {
        if (tasks == null || completed == null) return;
        tasks.Children.Clear();
        int pending = Store.Data.Items.Count(x => !x.Done);
        subtitle.Text = pending > 0 ? "还有 " + pending + " 件事，按自己的节奏来。" : "写下要做的事，下次回来继续。";
        int done = Store.Data.Items.Count(x => x.Done);
        completed.Content = (showCompleted ? "收起已完成" : "已完成") + "  ·  " + done;
        if (pending == 0)
        {
            var empty = new StackPanel { Spacing = 12, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 42, 0, 30), Opacity = .6 };
            empty.Children.Add(new Grid { Children = { Icons.SquareCheck(32) }, HorizontalAlignment = HorizontalAlignment.Center });
            empty.Children.Add(UI.Text(done > 0 ? "都完成了，给自己一点留白。" : "从一件小事开始吧。", 13));
            tasks.Children.Add(empty);
        }
        foreach (var task in Store.Data.Items.Where(x => !x.Done || showCompleted).OrderBy(x => x.Done))
        {
            var grid = new Grid { ColumnSpacing = 4, Padding = new Thickness(10, 6, 6, 6) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var label = UI.Text(task.Text, 14, task.Done ? .5 : 1);
            var check = new CheckBox { IsChecked = task.Done, Content = label, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, MinHeight = 42 };
            AutomationProperties.SetName(check, task.Text);
            check.Click += (_, _) =>
            {
                bool old = task.Done; task.Done = check.IsChecked == true;
                if (!Save()) task.Done = old;
                RenderTasks();
            };
            grid.Children.Add(check);
            var more = new Button { Content = Icons.Symbol("\uE712"), Padding = new Thickness(7), Background = new SolidColorBrush(Colors.Transparent), BorderThickness = new Thickness(0), VerticalAlignment = VerticalAlignment.Center };
            AutomationProperties.SetName(more, "更多操作：" + task.Text);
            var menu = new MenuFlyout();
            menu.Opened += (_, _) => openMenus++;
            menu.Closed += (_, _) => openMenus = Math.Max(0, openMenus - 1);
            var edit = new MenuFlyoutItem { Text = "编辑", Icon = new FontIcon { Glyph = "\uE70F" } };
            edit.Click += async (_, _) => await Edit(task);
            var delete = new MenuFlyoutItem { Text = "删除", Icon = new FontIcon { Glyph = "\uE74D" } };
            delete.Click += (_, _) =>
            {
                int index = Store.Data.Items.IndexOf(task); Store.Data.Items.Remove(task);
                if (!Save()) Store.Data.Items.Insert(index, task);
                RenderTasks();
            };
            menu.Items.Add(edit); menu.Items.Add(delete); more.Flyout = menu;
            Grid.SetColumn(more, 1); grid.Children.Add(more);
            tasks.Children.Add(UI.Card(grid));
        }
    }
    async Task Edit(Todo task)
    {
        dialogOpen = true;
        try
        {
            var editor = new TextBox { Text = task.Text, MaxLength = 500, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 80 };
            var dialog = new ContentDialog { Title = "编辑待办", Content = editor, PrimaryButtonText = "保存", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Primary, XamlRoot = layout.XamlRoot };
            dialog.IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(editor.Text);
            editor.TextChanged += (_, _) => dialog.IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(editor.Text);
            dialog.RequestedTheme = layout.ActualTheme;
            if (await dialog.ShowAsync() == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(editor.Text))
            {
                string old = task.Text; task.Text = editor.Text.Trim(); if (!Save()) task.Text = old;
                RenderTasks();
            }
        }
        finally { dialogOpen = false; }
    }
    void BuildSettings()
    {
        Title = "留白设置"; heading.Text = "设置"; subtitle.Text = "简单一点，专注要做的事。";
        var stack = new StackPanel { Spacing = 16 };
        var displayMode = new ComboBox { Header = "顶部栏显示方式", HorizontalAlignment = HorizontalAlignment.Stretch };
        displayMode.Items.Add("常驻（预留顶部空间）"); displayMode.Items.Add("自动隐藏（释放预留空间）");
        displayMode.SelectedIndex = getAutoHide() ? 1 : 0;
        AutomationProperties.SetName(displayMode, "顶部栏显示方式");
        bool resetting = false;
        displayMode.SelectionChanged += (_, _) =>
        {
            if (resetting) return;
            bool auto = displayMode.SelectedIndex == 1;
            if (setAutoHide(auto)) status.Text = auto ? "已保存：触顶显示，移出隐藏。" : "已保存：顶部栏常驻显示。";
            else
            {
                resetting = true; displayMode.SelectedIndex = getAutoHide() ? 1 : 0; resetting = false;
                status.Text = "保存失败，已保留原显示方式。";
            }
        };
        stack.Children.Add(displayMode);
        bool enabled = Shell.StartupEnabled();
        var startup = new CheckBox { Content = "登录 Windows 后自动启动", IsChecked = enabled };
        startup.Click += (_, _) =>
        {
            try { Shell.SetStartup(startup.IsChecked == true); enabled = startup.IsChecked == true; status.Text = "开机启动设置已保存。"; }
            catch { startup.IsChecked = enabled; status.Text = "无法修改启动项，请检查权限。"; }
        };
        stack.Children.Add(startup);
        var data = new Button { Content = "打开数据文件夹", HorizontalAlignment = HorizontalAlignment.Stretch };
        data.Click += (_, _) =>
        {
            try { string folder = Path.GetDirectoryName(Store.FilePath)!; Directory.CreateDirectory(folder); Process.Start(new ProcessStartInfo("explorer.exe", folder) { UseShellExecute = true }); }
            catch { status.Text = "暂时无法打开数据文件夹。"; }
        };
        stack.Children.Add(data);
        var quit = new Button { Content = "退出留白", HorizontalAlignment = HorizontalAlignment.Stretch };
        quit.Click += (_, _) => exit(); stack.Children.Add(quit);
        status.Text = "待办自动保存在此电脑。";
        body.Content = stack;
    }
}
