using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using Windows.UI.Core;

namespace Liubai;

internal sealed partial class StationWindow
{
    readonly HashSet<Guid> selection = [];
    readonly Dictionary<Guid, Border> selectionCards = [];
    readonly Dictionary<Guid, Control> selectionGrips = [];
    readonly StackPanel selectionBar = new() { Spacing = 6, Visibility = Visibility.Collapsed };
    readonly TextBlock selectionCount = Hint("");
    Guid? selectionAnchor;
    static bool KeyDown(VirtualKey key) => (InputKeyboardSource.GetKeyStateForCurrentThread(key) & CoreVirtualKeyStates.Down) != 0;
    void InitializeSelection(StackPanel footer)
    {
        selectionBar.Children.Add(selectionCount);
        var host = new ItemsControl { ItemsPanel = (ItemsPanelTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load("<ItemsPanelTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><WrapGrid Orientation='Horizontal' MaximumRowsOrColumns='5'/></ItemsPanelTemplate>") };
        void Add(Button button, Func<Task> run)
        { button.Margin = new Thickness(0, 0, 6, 5); button.Click += async (_, _) => await Run(run); host.Items.Add(button); }
        Add(IconButton("\uE8F4","将所选项目加入分组"), () => ChooseWorkspace(SelectionItems()));
        Add(ItemAction("复制所选项目",CopyPath), async () => { var data = new DataPackage(); await StationTransfer.FillMany(data, SelectionItems(), store.AttachmentDirectory); Clipboard.SetContent(data); Clipboard.Flush(); status.Text = "已复制所选项目"; });
        var archiveSelected = ItemAction("归档所选项目",ArchivePath);archiveSelected.Margin=new Thickness(0,0,6,5); archiveSelected.Click += async (_, _) => await Archive(); host.Items.Add(archiveSelected);
        Add(IconButton("\uE74D","移除所选项目"), RemoveSelection);
        Add(IconButton("\uE8BB","取消选择"), () => { ClearSelection(); return Task.CompletedTask; }); selectionBar.Children.Add(host); footer.Children.Add(selectionBar);
        root.KeyDown += SelectionKey;
    }
    async void SelectionKey(object sender, KeyRoutedEventArgs e)
    {
        if(SearchActive){if(e.Key==VirtualKey.Escape){e.Handled=true;CloseSearch();}return;}
        if(QuickLookOpen&&e.Key is VirtualKey.Space or VirtualKey.Escape){e.Handled=true;CloseQuickLook();return;}
        var focus = FocusManager.GetFocusedElement(root.XamlRoot);
        if (focus is TextBox or PasswordBox or RichEditBox || ModalActive || busy) return;
        if(e.Key==VirtualKey.F&&KeyDown(VirtualKey.Control)){e.Handled=true;SearchRequested?.Invoke();return;}
        if(e.Key==VirtualKey.Z&&KeyDown(VirtualKey.Control)&&!KeyDown(VirtualKey.Shift)&&!KeyDown(VirtualKey.Menu)) {e.Handled=true;await UndoLast();return;}
        if(workspacePage!=0)return;
        if(e.Key==VirtualKey.Space&&!KeyDown(VirtualKey.Control)&&!KeyDown(VirtualKey.Menu)) {if(selection.Count==1){e.Handled=true;await ToggleQuickLook();}return;}
        if (e.Key == VirtualKey.Escape && selection.Count > 0) { ClearSelection(); e.Handled = true; }
        if (e.Key == VirtualKey.A && KeyDown(VirtualKey.Control))
        { selection.UnionWith(CurrentItems().Select(i => i.Id)); PaintSelection(); e.Handled = true; }
    }
    void RegisterSelection(StationItem item, Border card, Control grip)
    {
        selectionCards[item.Id] = card;selectionGrips[item.Id]=grip;
        grip.PointerPressed += (_, e) => {
            if (!e.GetCurrentPoint(grip).Properties.IsLeftButtonPressed || busy || ModalActive) return;
            SelectItem(item.Id,KeyDown(VirtualKey.Control),KeyDown(VirtualKey.Shift));grip.Focus(FocusState.Pointer);
            PaintSelection();
        };
        grip.Tapped+=(_,_)=> { if(!KeyDown(VirtualKey.Control)&&!KeyDown(VirtualKey.Shift)&&!busy&&!ModalActive) { selection.Clear();selection.Add(item.Id);selectionAnchor=item.Id;PaintSelection(); } };
        var menu = new MenuFlyout();
        foreach (var action in StationActions.Available(item.Kind))
        { var entry = new MenuFlyoutItem { Text = StationActions.Label(action) }; entry.Click += async (_, _) => await Run(() => ItemCommand(item, action)); menu.Items.Add(entry); }
        AddLocateAction(menu,item); card.ContextFlyout = menu; PaintCard(item.Id, card);
    }
    void SelectItem(Guid id,bool control,bool shift)
    {
        if(shift && selectionAnchor is {} anchor) {
            var ids=CurrentItems().Select(i=>i.Id).ToArray(); int a=Array.IndexOf(ids,anchor), b=Array.IndexOf(ids,id);
            if(a>=0&&b>=0) selection.UnionWith(ids.Skip(Math.Min(a,b)).Take(Math.Abs(a-b)+1));
        }
        else if(control) {if(!selection.Add(id))selection.Remove(id);selectionAnchor=id;}
        else if(!selection.Contains(id)) {selection.Clear();selection.Add(id);selectionAnchor=id;}
        PaintSelection();
    }
    void PaintCard(Guid id, Border card)
    {
        if (selection.Contains(id)) { card.BorderBrush = new SolidColorBrush(Colors.DodgerBlue); card.BorderThickness = new Thickness(2); }
        else { card.ClearValue(Border.BorderBrushProperty); card.ClearValue(Border.BorderThicknessProperty); }
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetHelpText(card, selection.Contains(id) ? "已选择" : "按 Ctrl 添加选择，Shift 连选");
    }
    void PaintSelection()
    { foreach (var (id, card) in selectionCards) PaintCard(id, card); selectionCount.Text = $"已选 {selection.Count} 项"; selectionBar.Visibility = selection.Count == 0 ? Visibility.Collapsed : Visibility.Visible; }
    void ClearSelection() { selection.Clear(); selectionAnchor = null; PaintSelection(); }
    void PruneSelection(StationItem[] visible) { selection.IntersectWith(visible.Select(i => i.Id)); PaintSelection(); }
    StationItem[] SelectionItems() { var current = CurrentItems(); return selection.Count > 0 ? current.Where(i => selection.Contains(i.Id)).ToArray() : current; }
    async Task FillSelection(DataPackage package, StationItem item)
    {
        var items = selection.Contains(item.Id) ? SelectionItems() : [item];
        if (items.Length == 1) await StationTransfer.Fill(package, items[0]); else await StationTransfer.FillMany(package, items, store.AttachmentDirectory);
    }
    async Task RemoveSelection()
    {
        var snapshot = SelectionItems(); if (store.ReadOnly || snapshot.Length == 0) return;
        var dialog = new ContentDialog { XamlRoot = root.XamlRoot, Title = workspaceId == null ? "移除所选记录？" : "移出当前分组？", Content = $"共 {snapshot.Length} 项；原文件不会删除。", PrimaryButtonText = "移除", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        if (workspaceId is { } group) await Remember("已移出所选关联",()=>App.Work.Store.UnlinkMany(group, snapshot.Select(i => new WorkRef(WorkKind.Station,i.Id))));
        else await Remember("已移除所选记录",async ()=>{await store.RemoveArchived(snapshot);});
        ClearSelection(); Refresh();
    }
}
