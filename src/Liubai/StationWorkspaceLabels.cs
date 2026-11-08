using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Windows.Foundation;

namespace Liubai;

internal sealed partial class StationWindow
{
    void WorkspaceStyleChanged() {if(!destroyed){RenderWorkspaceLabels();UpdateStationHeader();}}
    WorkspaceChoice[] WorkspaceChoices() => new[]{new WorkspaceChoice(null,"全部"),new WorkspaceChoice(null,"未分组",true)}
        .Concat(App.WorkStore.Data.Scenes.OrderByDescending(s=>s.Pinned).Select(s=>new WorkspaceChoice(s.Id,s.Name))).ToArray();
    bool SelectedWorkspace(WorkspaceChoice choice)=>choice.Id==workspaceId&&choice.Ungrouped==ungrouped;
    async Task SelectWorkspace(WorkspaceChoice choice)
    {
        if(choice.Id is {} id&&!App.WorkStore.Data.Scenes.Any(s=>s.Id==id)) {status.Text="该分组已不存在";return;}
        revealWorkspaceLabel=true;workspaceId=choice.Id;ungrouped=choice.Ungrouped;workspacePage=0;
        expandedQuery=expandedQuery with {Category=StationCategory.All};ClearSelection();
        await App.Work.Store.Activate(choice.Id);workspaceTabs.SelectedIndex=0;Refresh();
    }
    MenuFlyout WorkspaceMenu(WorkspaceChoice choice)
    {
        var menu=new MenuFlyout();
        void Add(string text,Func<Task> action,bool needsGroup=true)
        {
            var entry=new MenuFlyoutItem {Text=text,IsEnabled=!needsGroup||choice.Id!=null};
            entry.Click+=async (_,_)=>await Run(async ()=> {await SelectWorkspace(choice);await action();});menu.Items.Add(entry);
        }
        Add("名称与说明",EditWorkspace);
        Add("固定／取消固定",async ()=>{if(Scene() is {} scene)await App.Work.Store.Describe(scene.Id,scene.Description,!scene.Pinned);});
        Add("分组前移",()=>choice.Id is {} id?App.Work.Store.Reorder(id,-1):Task.CompletedTask);
        Add("分组后移",()=>choice.Id is {} id?App.Work.Store.Reorder(id,1):Task.CompletedTask);
        Add("解散分组",DissolveWorkspace);
        menu.Items.Add(new MenuFlyoutSeparator());
        Add("撤销上次操作（Ctrl+Z）",async ()=>{if(history.CanUndo){await history.Undo();Refresh();status.Text="已撤销";}},false);
        return menu;
    }
    void RenderWorkspaceLabels()
    {
        if(destroyed)return;
        double offset=workspaceScroller.HorizontalOffset;int version=++workspaceLabelVersion,scrollVersion=workspaceScrollVersion;
        workspaceLabels.Children.Clear();
        foreach(var choice in WorkspaceChoices().Where(c=>!c.Ungrouped||SelectedWorkspace(c)))
        {
            var text=new TextBlock {Text=choice.Label,TextTrimming=TextTrimming.CharacterEllipsis,MaxLines=1,FontSize=14};
            text.Measure(new Size(double.PositiveInfinity,double.PositiveInfinity));
            double width=Math.Clamp(Math.Ceiling(text.DesiredSize.Width)+24,56,144);
            var button=new ToggleButton {Content=text,Height=PanelTheme.Value<double>("ActionSize"),MinWidth=0,Padding=new Thickness(10,0,10,0),IsChecked=SelectedWorkspace(choice),Tag=choice};
            PanelTheme.FramedIconStyle(button,choice.Label);button.Width=width;button.ContextFlyout=WorkspaceMenu(choice);
            AutomationProperties.SetItemStatus(button,SelectedWorkspace(choice)?"当前分组":"切换分组");
            button.Click+=async (_,_)=>{button.IsChecked=SelectedWorkspace(choice);await Run(()=>SelectWorkspace(choice));};workspaceLabels.Children.Add(button);
            button.GotFocus+=(_,_)=>RevealWorkspaceLabel(button);
        }
        RefreshWorkspaceOverflow();
        DispatcherQueue.TryEnqueue(()=>{
            if(destroyed||version!=workspaceLabelVersion||SearchActive||workspaceLabelHost.Visibility!=Visibility.Visible)return;
            workspaceScroller.UpdateLayout();
            workspaceLabels.MinWidth=workspaceScroller.ViewportWidth;
            if(scrollVersion!=workspaceScrollVersion){RefreshWorkspaceOverflow();return;}
            if(revealWorkspaceLabel){revealWorkspaceLabel=false;RevealWorkspaceLabel();}
            else workspaceScroller.ChangeView(Math.Clamp(offset,0,workspaceScroller.ScrollableWidth),null,null,true);
            RefreshWorkspaceOverflow();
        });
    }
    void ScrollWorkspaceLabels(double delta)
    {workspaceScrollVersion++;revealWorkspaceLabel=false;if(workspaceScroller.ViewportWidth>0)workspaceScroller.ChangeView(Math.Clamp(workspaceScroller.HorizontalOffset+delta,0,workspaceScroller.ScrollableWidth),null,null,true);}
    void RevealWorkspaceLabel(ToggleButton? target=null)
    {
        var button=target??workspaceLabels.Children.OfType<ToggleButton>().FirstOrDefault(b=>b.Tag is WorkspaceChoice c&&SelectedWorkspace(c));if(button==null)return;
        double x=button.TransformToVisual(workspaceLabels).TransformPoint(new()).X;
        double offset=workspaceScroller.HorizontalOffset;
        if(x<offset)offset=x;else if(x+button.ActualWidth>offset+workspaceScroller.ViewportWidth)offset=x+button.ActualWidth-workspaceScroller.ViewportWidth;
        workspaceScroller.ChangeView(Math.Clamp(offset,0,workspaceScroller.ScrollableWidth),null,null,true);
    }
    void RefreshWorkspaceOverflow()
    {
        if(workspaceOverflow.Flyout is not MenuFlyout menu)return;menu.Items.Clear();
        double left=workspaceScroller.HorizontalOffset,right=left+workspaceScroller.ViewportWidth;
        foreach(var choice in WorkspaceChoices())
        {
            var button=workspaceLabels.Children.OfType<ToggleButton>().FirstOrDefault(b=>Equals(b.Tag,choice));
            if(button!=null){double x=button.TransformToVisual(workspaceLabels).TransformPoint(new()).X;if(button.ActualWidth>0&&x>=left-.5&&x+button.ActualWidth<=right+.5)continue;}
            var entry=new MenuFlyoutItem {Text=choice.Label,Tag=choice};entry.Click+=async (_,_)=>await Run(()=>SelectWorkspace(choice));menu.Items.Add(entry);
        }
        if(menu.Items.Count>0)menu.Items.Add(new MenuFlyoutSeparator());
        var active=WorkspaceChoices().FirstOrDefault(SelectedWorkspace)??new(null,"全部");
        var manage=new MenuFlyoutSubItem {Text="管理当前分组",IsEnabled=Scene()!=null};var commands=WorkspaceMenu(active);
        foreach(var item in commands.Items.ToArray()){commands.Items.Remove(item);manage.Items.Add(item);}menu.Items.Add(manage);
    }
}
