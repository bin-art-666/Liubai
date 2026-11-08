using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Liubai;

internal sealed partial class StationWindow
{
    async Task<string[]> VerifyGroupQuickMenus()
    {
        var checks=new List<string>();var groups=new List<Guid>();
        workspaceId=null;ungrouped=false;workspacePage=0;expandedQuery=new();periodFilter.SelectedIndex=0;Refresh();pin.IsChecked=true;
        for(int i=0;i<14;i++)groups.Add(await App.Work.Store.Create("快捷菜单验证 "+i,false));
        var items=CurrentItems().Take(2).ToArray();
        foreach(bool detached in new[]{false,true})
        {
            if(Detached!=detached){ToggleDetached();await Task.Delay(150);}
            ClearSelection();SelectItem(items[0].Id,false,false);SelectItem(items[1].Id,true,false);
            Guid target=groups[detached?1:0];await App.Work.Store.Link(target,[new(WorkKind.Station,items[0].Id)]);
            await Task.Delay(100);
            var before=selection.ToHashSet();var originalQuery=expandedQuery;double offset=browserScroll.VerticalOffset;
            var button=detached?expandedActions[StationAction.Group]:EntryControls<Button>(selectionBar).Single(b=>Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(b)=="将所选项目加入分组");
            if(button.Flyout is not MenuFlyout menu)throw new Exception("Group button has no native flyout");
            menu.ShowAt(button);await Task.Delay(150);
            var entry=menu.Items.OfType<MenuFlyoutItem>().Single(e=>Equals(e.Tag,target));
            if(!entry.Text.Contains("已有 1/2 项")||!entry.IsEnabled||!ModalActive||!IsOpen)throw new Exception("Partial membership or menu visibility protection missing");
            var presenters=VisualTreeHelper.GetOpenPopupsForXamlRoot(root.XamlRoot).SelectMany(p=>p.Child is {} child?EntryControls<MenuFlyoutPresenter>(child):[]).ToArray();
            if(!presenters.Any(p=>p.ActualHeight<=361&&EntryControls<ScrollViewer>(p).Any(s=>s.ScrollableHeight>0)))throw new Exception("Group menu does not scroll within bounded height");
            ((IInvokeProvider)new MenuFlyoutItemAutomationPeer(entry).GetPattern(PatternInterface.Invoke)).Invoke();
            for(int i=0;i<100&&(busy||App.WorkStore.Data.Scenes.Single(s=>s.Id==target).Items.Count(r=>r.Kind==WorkKind.Station)!=2);i++)await Task.Delay(30);
            await Task.Delay(100);
            if(!selection.SetEquals(before)||workspaceId!=null||expandedQuery!=originalQuery||Math.Abs(browserScroll.VerticalOffset-offset)>1||!IsOpen||ModalActive||!history.CanUndo)throw new Exception("Quick join lost selection/navigation or failed to release menu lease");
            menu.ShowAt(button);await Task.Delay(100);entry=menu.Items.OfType<MenuFlyoutItem>().Single(e=>Equals(e.Tag,target));
            if(entry.IsEnabled||!entry.Text.EndsWith("已加入"))throw new Exception("Full membership not disabled");menu.Hide();await Task.Delay(80);
            await UndoLast();if(App.WorkStore.Data.Scenes.Single(s=>s.Id==target).Items.Count(r=>r.Kind==WorkKind.Station)!=1)throw new Exception("Quick join undo removed original membership");
            var context=(MenuFlyout)selectionCards[items[0].Id].ContextFlyout;context.ShowAt(selectionGrips[items[0].Id]);await Task.Delay(100);
            var submenu=context.Items.OfType<MenuFlyoutSubItem>().Single(e=>e.Text=="加入分组");
            if(submenu.Items.OfType<MenuFlyoutItem>().Count(e=>e.Tag is Guid)<14)throw new Exception("Context group submenu missing groups");context.Hide();await Task.Delay(80);
            if(ModalActive)throw new Exception("Closed group menu retains lease");
            checks.Add((detached?"Expanded":"Docked")+" group menu scrolls, shows partial/full membership, joins selected records immediately, retains selection and supports undo; context menu exposes native submenu");
        }
        var pending=CreateGroupWithItems(items);await Task.Delay(120);
        var dialog=VisualTreeHelper.GetOpenPopupsForXamlRoot(root.XamlRoot).SelectMany(p=>p.Child is {} child?EntryControls<ContentDialog>(child):[]).Single();
        if(!Equals(dialog.Title,"新建分组并加入")||EntryControls<ComboBox>(dialog).Any()||dialog.DefaultButton!=ContentDialogButton.Primary)throw new Exception("New-only dialog still asks for existing group");
        dialog.Hide();await pending;
        activeGroupMenu?.Close();if(Detached)ToggleDetached();foreach(var id in groups)await App.Work.Store.Remove(id);ClearSelection();
        checks.Add("Only new-group action opens a name dialog with Enter primary; cancel creates no group");return checks.ToArray();
    }
}
