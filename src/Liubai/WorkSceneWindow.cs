using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Liubai;

internal sealed class WorkSceneWindow : UtilityPanel
{
    readonly WorkHub hub;
    readonly Func<WorkRef, Task> open;
    readonly ComboBox scenes = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly TextBox name = new() { PlaceholderText = "新工作现场，例如：计算机网络第二章", MaxLength = 80 };
    readonly StackPanel items = new() { Spacing = 8 };
    readonly TextBox progress = new() { PlaceholderText = "记一句做到哪里了，下次接着做…", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxLength = 2000 };
    bool syncing;
    sealed record Choice(Guid? Id, string Name) { public override string ToString() => Name; }
    internal WorkSceneWindow(Func<WorkRef, Task> open) : base("工作现场")
    {
        hub = App.Work; this.open = open;
        Body.Children.Add(Hint("把资料、待办和笔记放在一起。选中现场后，新存入的内容会自动归入这里。")); Body.Children.Add(scenes);
        Body.Children.Add(name); var create = new Button { Content = "新建工作现场", HorizontalAlignment = HorizontalAlignment.Stretch };
        create.Click += async (_, _) => await Guard(async () => { if (string.IsNullOrWhiteSpace(name.Text)) throw new IOException("请先填写名称。"); await hub.Store.Create(name.Text); name.Text = ""; }); Body.Children.Add(create);
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var collect = new Button { Content = "加入已有内容" }; collect.Click += async (_, _) => await Guard(Collect); tools.Children.Add(collect);
        var rename = Icon("\uE70F", "重命名现场"); rename.Click += async (_, _) => await Guard(Rename); tools.Children.Add(rename);
        var remove = Icon("\uE74D", "移除现场分组"); remove.Click += async (_, _) => await Guard(Remove); tools.Children.Add(remove); Body.Children.Add(tools);
        Body.Children.Add(items); Body.Children.Add(progress);
        var save = new Button { Content = "保存接续记录", HorizontalAlignment = HorizontalAlignment.Stretch };
        save.Click += async (_, _) => await Guard(async () =>
        {
            var scene = hub.Store.Data.Active ?? throw new IOException("请先选择一个现场。");
            if (string.IsNullOrWhiteSpace(progress.Text)) return;
            Guid id = Guid.NewGuid(); await App.Planner.SaveNote(id, progress.Text); await hub.Store.Link(scene, [new(WorkKind.Note, id)]); progress.Text = ""; StatusLine.Text = "接续记录已保存到随手记和当前现场。";
        }); Body.Children.Add(save);
        scenes.SelectionChanged += async (_, _) => { if (!syncing && scenes.SelectedItem is Choice value) await Guard(() => hub.Store.Activate(value.Id)); };
        hub.Store.Changed += StoreChanged; App.Planner.Changed += StoreChanged; App.Station.Changed += StoreChanged;
        Closed += (_, _) => { hub.Store.Changed -= StoreChanged; App.Planner.Changed -= StoreChanged; App.Station.Changed -= StoreChanged; };
    }
    internal void Open(DesktopMonitor display, int top) { Refresh(); Present(display, top, 740); }
    void StoreChanged() { if (IsOpen) Refresh(); }
    void Refresh()
    {
        syncing = true; scenes.Items.Clear(); scenes.Items.Add(new Choice(null, "未分组 · 不自动收拢"));
        foreach (var scene in hub.Store.Data.Scenes) scenes.Items.Add(new Choice(scene.Id, scene.Name));
        scenes.SelectedItem = scenes.Items.Cast<Choice>().FirstOrDefault(c => c.Id == hub.Store.Data.Active); syncing = false;
        items.Children.Clear(); var active = hub.Store.Data.Scenes.FirstOrDefault(s => s.Id == hub.Store.Data.Active);
        if (active is null) items.Children.Add(Hint("选择或新建一个现场，随时回来继续。"));
        else
        {
            items.Children.Add(Hint($"{active.Items.Length} 项 · 引用原内容，不移动或删除原文件"));
            var available = hub.All().ToHashSet();
            foreach (var item in active.Items.Reverse().Take(200))
            {
                var row = new Grid { ColumnSpacing = 8 }; row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
                var button = new Button { Content = new TextBlock { Text = $"{item.Kind switch { WorkKind.Task => "待办", WorkKind.Note => "笔记", _ => "资料" }} · {hub.Title(item)}", TextTrimming = TextTrimming.CharacterEllipsis },
                    HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, IsEnabled = item.Asset != null || available.Contains(item) };
                button.Click += async (_, _) => await Inspect(item); row.Children.Add(button);
                var unlink = Icon("\uE74D", "从现场移除引用"); unlink.Click += async (_, _) => await Guard(() => hub.Store.Unlink(active.Id, item)); Grid.SetColumn(unlink, 1); row.Children.Add(unlink); items.Children.Add(row);
            }
            if (active.Items.Length > 200) items.Children.Add(Hint("当前显示最近 200 项；其余引用仍保留。"));
        }
        StatusLine.Text = hub.Store.Warning ?? hub.LastError ?? "全部保存在本机 · 再次打开恢复当前现场";
    }
    async Task Inspect(WorkRef item)
    {
        string? text = item.Asset?.Kind == "Text" ? item.Asset.Value : item.Kind == WorkKind.Station ? App.Station.Items.FirstOrDefault(i => i.Id == item.Id && i.Kind == StationKind.Text)?.Value : null;
        if (text != null)
        {
            await Guard(async () =>
            {
                var dialog = new ContentDialog { XamlRoot = Layout.XamlRoot, Title = "现场中的文字", Content = new TextBox { Text = text, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 380 }, PrimaryButtonText = "复制", CloseButtonText = "关闭", DefaultButton = ContentDialogButton.Close };
                if (await dialog.ShowAsync() == ContentDialogResult.Primary) { var data = new Windows.ApplicationModel.DataTransfer.DataPackage(); data.SetText(text); Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(data); }
            });
        }
        else { bool success = false; await Guard(async () => { await open(item); success = true; }); if (success) Hide(); }
    }
    async Task Collect()
    {
        Guid scene = hub.Store.Data.Active ?? throw new IOException("请先选择一个现场。");
        var existing = hub.Store.Data.Scenes.Single(s => s.Id == scene).Items.ToHashSet();
        var choices = hub.All().Where(r => !existing.Contains(r)).Take(200).Select(r => new CheckBox { Content = new TextBlock { Text = hub.Title(r), TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 300 }, Tag = r }).ToArray();
        var list = new StackPanel { Spacing = 6 }; foreach (var choice in choices) list.Children.Add(choice);
        var dialog = new ContentDialog { XamlRoot = Layout.XamlRoot, Title = "选择加入当前现场的内容", Content = new ScrollViewer { Content = list, MaxHeight = 380 }, PrimaryButtonText = "加入所选", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary) await hub.Store.Link(scene, choices.Where(c => c.IsChecked == true).Select(c => (WorkRef)c.Tag));
    }
    async Task Rename()
    {
        var scene = hub.Store.Data.Scenes.FirstOrDefault(s => s.Id == hub.Store.Data.Active) ?? throw new IOException("请先选择一个现场。");
        var edit = new TextBox { Text = scene.Name, MaxLength = 80 };
        var dialog = new ContentDialog { XamlRoot = Layout.XamlRoot, Title = "重命名工作现场", Content = edit, PrimaryButtonText = "保存", CloseButtonText = "取消" };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary) await hub.Store.Rename(scene.Id, edit.Text);
    }
    async Task Remove()
    {
        var scene = hub.Store.Data.Active ?? throw new IOException("请先选择一个现场。");
        var dialog = new ContentDialog { XamlRoot = Layout.XamlRoot, Title = "移除这个现场分组？", Content = "只移除分组与引用，原文件、待办和笔记均保留。", PrimaryButtonText = "移除分组", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary) await hub.Store.Remove(scene);
    }
}
