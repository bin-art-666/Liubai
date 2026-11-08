using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using System.Text.Json;

namespace Liubai;

internal sealed partial class StationWindow
{
    internal async Task<string[]> VerifyWorkbenchPanel()
    {
        var checks=new List<string>(); Open(display,display.Info.Work.Top); using var lease=KeepOpen();
        await store.Add([StationStore.Text("第一个测试项"),StationStore.Text("第二个测试项"),StationStore.Text("第三个测试项"),StationStore.Text("https://example.com/test","资料链接")]);
        Refresh(); var items=CurrentItems();
        SelectItem(items[0].Id,false,false);SelectItem(items[2].Id,false,true);
        if(selection.Count!=3||selectionBar.Visibility!=Visibility.Visible) throw new Exception("Shift range selection");
        SelectItem(items[1].Id,true,false);if(selection.Count!=2)throw new Exception("Ctrl toggle selection");
        var data=new DataPackage();await FillSelection(data,items[0]);
        var files=await data.GetView().GetStorageItemsAsync();if(files.Count!=2)throw new Exception("Multi-item text export");
        foreach(var file in files.OfType<Windows.Storage.StorageFile>()) { using var stream=await file.OpenReadAsync();using var reader=new StreamReader(stream.AsStreamForRead());string payload=await reader.ReadToEndAsync();if(payload.Length==0)throw new Exception("Empty exported content"); }
        var internalItems=await StationTransfer.InternalItems(data.GetView(),store);if(internalItems?.Length!=2)throw new Exception("Private internal identity lost");
        checks.Add("Ctrl toggle, Shift range, selection toolbar, batch text-to-file export and internal identity");
        await store.UpdateActions(new(Grid:true));
        var grid=(Grid)BuildItemActions(new(Guid.NewGuid(),StationKind.File,Path.Combine(App.DataDirectory,"fixture.txt"),"",DateTimeOffset.Now),out _,out _);
        if(grid.Children.Count!=6||grid.RowDefinitions.Count!=2||grid.ColumnDefinitions.Count!=3)throw new Exception("Six-grid layout");
        await store.UpdateActions(new(Text:[StationAction.Edit,StationAction.Remove]));
        var compact=(Grid)BuildItemActions(items.First(i=>i.Kind==StationKind.Text),out _,out _);
        if(compact.Children.Count!=3||compact.Children.OfType<Button>().Last().Flyout is not MenuFlyout menu||menu.Items.OfType<MenuFlyoutItem>().Any(e=>e.Text=="编辑"))throw new Exception("Custom compact actions and overflow");
        checks.Add("Native six-grid and configurable compact layout; hidden actions in overflow");
        Guid group=await App.Work.Store.Create("工作台测试分组");await App.Work.Store.Link(group,items.Take(2).Select(i=>new WorkRef(WorkKind.Station,i.Id)));OpenWorkspace(group);
        if(CurrentItems().Length!=2)throw new Exception("Group filter");
        await App.Planner.AddTask("验证共享任务","",null); Guid task=App.Planner.Data.Tasks.Single().Id;
        Guid note=Guid.NewGuid();await App.Planner.SaveNote(note,"验证共享笔记"); await App.Work.Store.Link(group,[new(WorkKind.Task,task),new(WorkKind.Note,note)]);
        workspacePage=1;Refresh();if(!cards.Children.OfType<Border>().Any())throw new Exception("Task page empty");
        await App.Planner.CompleteTask(task,true);if(!App.Planner.Data.Tasks.Single().Completed)throw new Exception("Shared completion");
        workspacePage=2;Refresh();if(!cards.Children.OfType<Border>().Any())throw new Exception("Note page empty");
        checks.Add("Group filtering, shared task completion and native note page");
        workspacePage=0;ClearSelection();workspaceId=null;Refresh();
        lease.Dispose();pin.IsChecked=true;ToggleDetached();await Task.Delay(200);if(categories.Visibility!=Visibility.Visible)throw new Exception("Group sidebar hidden");
        if(!categories.Items.OfType<ListViewItem>().Any(i=>i.Tag is WorkspaceChoice {Id:not null}))throw new Exception("Group navigation missing");
        using(KeepOpenForDrag()) {await HoverWorkspace(group);if(workspaceId!=group)throw new Exception("Drag-hover group opening failed");}
        workspaceId=null;Refresh();checks.Add("Group hover opens while source drag remains protected");
        expandedQuery=new(Search:"第一个");CommitQuery();if(CurrentItems().Length!=1||selection.Count!=0)throw new Exception("Filtered selection scope");
        ToggleDetached();if(CurrentItems().Length!=4)throw new Exception("Docked query isolation");
        checks.Add("Detached group navigation, filtered selection clearing and docked query isolation");
        await App.Work.Store.Remove(group);if(store.Items.Count!=4||App.Planner.Data.Notes.Length!=1)throw new Exception("Dissolve removed original data");
        checks.Add("Dissolve preserves original station, task and note records");
        checks.AddRange(await VerifyWorkspaceLabels());
        checks.AddRange(await VerifyEntryDialogs());
        return checks.ToArray();
    }
    async Task<string[]> VerifyWorkspaceLabels()
    {
        var groups=new List<Guid>();
        for(int i=0;i<8;i++)groups.Add(await App.Work.Store.Create("分组标签验证 · "+i));
        await SelectWorkspace(new(null,"全部"));
        var original=new Windows.Graphics.RectInt32(AppWindow.Position.X,AppWindow.Position.Y,AppWindow.Size.Width,AppWindow.Size.Height);
        AppWindow.MoveAndResize(new(original.X,original.Y,(int)(360*display.Dpi/96d),original.Height));
        await Task.Delay(250);RenderWorkspaceLabels();
        void Bounds()
        {
            if(workspaceScroller.ActualWidth+workspaceOverflow.ActualWidth+workspaceLabelHost.ColumnSpacing>workspaceLabelHost.ActualWidth+1)throw new Exception("Scroll viewport overlaps overflow or create/import");
        }
        Bounds();
        UpdateStationHeader();
        if(stationCommands.Visibility!=Visibility.Collapsed||stationCommandMore.Visibility!=Visibility.Visible||stationCommandMore.Flyout is not MenuFlyout {Items.Count:3})throw new Exception("Narrow header did not fold search/clear/archive");
        Guid? selectedBefore=workspaceId;double offsetBefore=workspaceScroller.HorizontalOffset;
        var origin=new Native.Point();ClientToScreen(WinRT.Interop.WindowNative.GetWindowHandle(this),ref origin);
        var local=workspaceScroller.TransformToVisual((UIElement)Content).TransformPoint(new());double scale=root.XamlRoot.RasterizationScale;
        var wheelPoint=new Native.Point {X=origin.X+(int)((local.X+workspaceScroller.ActualWidth/2)*scale),Y=origin.Y+(int)((local.Y+workspaceScroller.ActualHeight/2)*scale)};
        if(!HandlePanelWheel(wheelPoint,-120,false))throw new Exception("Panel wheel region rejected a group-area event");
        var outside=wheelPoint;outside.Y=origin.Y+(int)((local.Y+workspaceScroller.ActualHeight+10)*scale);
        if(HandlePanelWheel(outside,-120,false))throw new Exception("Group wheel consumed list-area input");
        await Task.Delay(80);
        if(workspaceScroller.HorizontalOffset<=offsetBefore||workspaceId!=selectedBefore)throw new Exception($"Group scrolling failed or changed selection: {offsetBefore}->{workspaceScroller.HorizontalOffset}, viewport={workspaceScroller.ViewportWidth}, scrollable={workspaceScroller.ScrollableWidth}");
        ScrollWorkspaceLabels(-10000);await Task.Delay(80);RefreshWorkspaceOverflow();
        if(workspaceScroller.HorizontalOffset!=0)throw new Exception("Group scroll boundary not clamped");
        var overflow=workspaceOverflow;
        if(overflow.Flyout is not MenuFlyout menu||!menu.Items.OfType<MenuFlyoutItem>().Any(e=>e.Tag is WorkspaceChoice {Id:not null}))throw new Exception("Overflow did not contain hidden groups");
        menu.ShowAt(overflow);await Task.Delay(150);
        var entry=menu.Items.OfType<MenuFlyoutItem>().Last(e=>e.Tag is WorkspaceChoice {Id:not null});
        Guid target=((WorkspaceChoice)entry.Tag).Id!.Value;
        var peer=new Microsoft.UI.Xaml.Automation.Peers.MenuFlyoutItemAutomationPeer(entry);
        ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)peer.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();
        for(int i=0;i<100&&(workspaceId!=target||busy);i++)await Task.Delay(30);
        if(workspaceId!=target||App.WorkStore.Data.Active!=target||!workspaceLabels.Children.OfType<Microsoft.UI.Xaml.Controls.Primitives.ToggleButton>().Any(b=>b.Tag is WorkspaceChoice c&&c.Id==target&&b.IsChecked==true))throw new Exception("Overflow click did not activate and promote selected group");
        menu.Hide();await Task.Delay(80);Bounds();
        var selected=workspaceLabels.Children.OfType<Microsoft.UI.Xaml.Controls.Primitives.ToggleButton>().Single(b=>b.Tag is WorkspaceChoice c&&c.Id==target);
        double selectedX=selected.TransformToVisual(workspaceLabels).TransformPoint(new()).X;
        if(selectedX<workspaceScroller.HorizontalOffset-.5||selectedX+selected.ActualWidth>workspaceScroller.HorizontalOffset+workspaceScroller.ViewportWidth+.5)throw new Exception("Selected group did not scroll into view");
        var order=workspaceLabels.Children.OfType<Microsoft.UI.Xaml.Controls.Primitives.ToggleButton>().Select(b=>((WorkspaceChoice)b.Tag).Id).Where(id=>id!=null).ToArray();
        if(!order.SequenceEqual(App.WorkStore.Data.Scenes.OrderByDescending(s=>s.Pinned).Select(s=>(Guid?)s.Id)))throw new Exception("Scrolling/selection reordered groups");
        if(workspaceLabels.Children.OfType<Microsoft.UI.Xaml.Controls.Primitives.ToggleButton>().Any(b=>b.ContextFlyout is not MenuFlyout))throw new Exception("Group management context menu missing");
        ToggleDetached();await Task.Delay(180);
        if(workspaceId!=target)throw new Exception("Detachment lost selected group");
        if(stationCommands.Visibility!=Visibility.Visible||workspaceTools.Visibility!=Visibility.Visible)throw new Exception("Wide header commands or group tools disappeared");
        ToggleDetached();await Task.Delay(180);
        if(workspaceId!=target)throw new Exception("Docking lost selected group");
        await SelectWorkspace(new(null,"全部"));
        foreach(var id in groups)await App.Work.Store.Remove(id);
        AppWindow.MoveAndResize(original);await Task.Delay(150);
        var buttons=stationCommands.Children.OfType<Button>().Concat(workspaceTools.Children.OfType<Button>()).Append(expand);
        if(buttons.Any(b=>b.Width!=PanelTheme.Value<double>("ActionSize")||b.Height!=b.Width||b.BorderThickness.Left!=1||b.Content is not Microsoft.UI.Xaml.Controls.Viewbox))throw new Exception("Unified framed/vector command style missing");
        if(pin.BorderThickness.Left!=1||pin.Width!=expand.Width)throw new Exception("Pin size/border differs from actions");
        return ["Narrow group labels fit; native overflow click activates and promotes selection; management menus and detach/dock preserve group", "Header, command row and pin share framed sizes and vector icons"];
    }
}

public sealed partial class BarWindow
{
    async Task VerifyWorkbench()
    {
        string result="PASS",error="";string[] checks=[];StationWindow? panel=null;
        try
        {
            await Task.Delay(450);if(SessionState.Locked==true)throw new IOException("LOCKED: unlock before verification");
            panel=new StationWindow(App.Station,Display);checks=await panel.VerifyWorkbenchPanel();
        }
        catch(Exception ex){result="FAIL";error=ex.ToString();}
        finally {panel?.Destroy();Directory.CreateDirectory(App.DataDirectory);File.WriteAllText(Path.Combine(App.DataDirectory,"workbench-check.json"),JsonSerializer.Serialize(new{result,error,checks}));Close();}
    }
}
