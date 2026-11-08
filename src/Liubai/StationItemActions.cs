using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.System;

namespace Liubai;

internal sealed partial class StationWindow
{
    FrameworkElement BuildItemActions(StationItem item, out Button copy, out Button open)
    {
        var all = StationActions.Available(item.Kind);
        var shown = store.Actions.Visible(item.Kind);
        Button Build(StationAction action)
        {
            var button = ActionIcon(action);
            if (action is StationAction.Remove or StationAction.Edit) button.IsEnabled = !store.ReadOnly;
            button.Click += async (_, _) => await Run(() => ItemCommand(item, action));
            return button;
        }
        var buttons = all.ToDictionary(a => a, Build); copy = buttons[StationAction.Copy]; open = buttons[StationAction.Open];
        var grid = new Grid { ColumnSpacing = 6, RowSpacing = 6, VerticalAlignment = VerticalAlignment.Center };
        for (int i = 0; i < (store.Actions.Grid ? 3 : shown.Length + 1); i++) grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        if (store.Actions.Grid) { grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); }
        for (int i = 0; i < shown.Length; i++) { var button = buttons[shown[i]]; Grid.SetColumn(button, store.Actions.Grid ? i % 3 : i); Grid.SetRow(button, store.Actions.Grid ? i / 3 : 0); grid.Children.Add(button); }
        var menu = new MenuFlyout();
        foreach (var action in all.Where(a => !shown.Contains(a)))
        {
            var entry = new MenuFlyoutItem { Text = StationActions.Label(action), IsEnabled = !store.ReadOnly || action is not (StationAction.Edit or StationAction.Remove) };
            entry.Click += async (_, _) => await Run(() => ItemCommand(item, action)); menu.Items.Add(entry);
        }
        AddLocateAction(menu,item);
        if (!store.Actions.Grid)
        { var more = IconButton("\uE712", "更多操作"); more.Style=PanelTheme.Style("ItemAction"); more.Flyout = menu; Grid.SetColumn(more, shown.Length); grid.Children.Add(more); }
        return grid;
    }
    internal static Button ActionIcon(StationAction action)
    {
        var button = action switch { StationAction.Open => ItemAction("打开", OpenPath), StationAction.Copy => ItemAction("复制内容", CopyPath), StationAction.Remove => ItemAction("移除记录", TrashPath), _ => IconButton(action switch { StationAction.Folder => "\uE8B7", StationAction.Path => "\uE946", StationAction.Edit => "\uE70F", _ => "\uE8F4" }, StationActions.Label(action)) };
        button.Style = PanelTheme.Style("ItemAction");
        return button;
    }
    async Task ItemCommand(StationItem item, StationAction action)
    {
        switch (action)
        {
            case StationAction.Open:
                if (item.Kind == StationKind.Text) await EditItem(item, false); else await OpenItem(item); break;
            case StationAction.Copy:
                var package = new DataPackage(); await StationTransfer.Fill(package, item); Clipboard.SetContent(package); Clipboard.Flush(); status.Text = "已复制"; break;
            case StationAction.Remove:
                if (workspaceId != null) await Remember("已移出分组",()=>App.Work.Store.Unlink(workspaceId.Value, new(WorkKind.Station, item.Id)));
                else await Remember("已移除记录",()=>store.Remove(item.Id));
                status.Text = workspaceId == null ? "已移除记录，原文件保留" : "已移出分组，原记录保留"; break;
            case StationAction.Folder:
                var path = item.Kind == StationKind.Folder ? item.Value : Path.GetDirectoryName(item.Value)!;
                var folder = await StorageFolder.GetFolderFromPathAsync(path);
                if (!await Launcher.LaunchFolderAsync(folder)) throw new IOException("无法打开所在文件夹"); break;
            case StationAction.Path:
                await new ContentDialog { XamlRoot = root.XamlRoot, Title = "原文件路径", Content = new TextBox { Text = item.Value, IsReadOnly = true, TextWrapping = TextWrapping.Wrap }, CloseButtonText = "关闭" }.ShowAsync(); break;
            case StationAction.Edit: await EditItem(item, true); break;
            case StationAction.Group: await ChooseWorkspace([item]); break;
        }
    }
    async Task EditItem(StationItem item, bool edit)
    {
        var text = new TextBox { AcceptsReturn = item.Kind == StationKind.Text, Text = item.Kind == StationKind.Text ? item.Value : item.Title, TextWrapping = TextWrapping.Wrap,
            MaxLength = item.Kind == StationKind.Text ? StationStore.MaximumText : 200, IsReadOnly = !edit, MaxHeight = 380 };
        if(!edit) {await new ContentDialog {XamlRoot=root.XamlRoot,Title="文字内容",Content=text,CloseButtonText="关闭"}.ShowAsync();return;}
        var dialog=new PanelEntryDialog(root.XamlRoot,item.Kind==StationKind.Text?"文字内容":"修改显示名称","保存",text,[text],
            ()=>string.IsNullOrWhiteSpace(text.Text)?"请输入内容":null,
            ()=>store.Edit(item.Id,item.Kind==StationKind.Text?text.Text:item.Value,item.Kind==StationKind.Text?item.Title:text.Text));
        await dialog.Show();
    }
    void AddLocateAction(MenuFlyout menu,StationItem item)
    {
        if(item.Kind is not (StationKind.File or StationKind.Folder)) return;
        var locate=new MenuFlyoutItem {Text="重新定位原文件",IsEnabled=!store.ReadOnly}; locate.Click+=async (_,_)=>await Run(async ()=> {
            string? path;
            if(item.Kind==StationKind.Folder) path=await StationArchivePicker.Pick(this);
            else {var picker=new Windows.Storage.Pickers.FileOpenPicker();picker.FileTypeFilter.Add("*");WinRT.Interop.InitializeWithWindow.Initialize(picker,WinRT.Interop.WindowNative.GetWindowHandle(this));path=(await picker.PickSingleFileAsync())?.Path;}
            if(path!=null) {await store.Relocate(item.Id,path);status.Text="原文件位置已更新，分组关联保留";}
        }); menu.Items.Add(locate);
    }
}
