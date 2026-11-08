using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Liubai;

internal sealed partial class StationWindow
{
    StackPanel expandedProjectActions=null!;
    void InitializeLeadingActions()
    {
        stationLeading.Children.Add(expandedActionHost);stationLeading.Children.Add(projectMore);
        var menu=new MenuFlyout();projectMore.Flyout=menu;
        foreach(var action in new[]{StationAction.Open,StationAction.Copy,StationAction.Folder,StationAction.Path,StationAction.Remove})
        {
            var item=new MenuFlyoutItem {Text=StationActions.Label(action),Tag=action};
            item.Click+=async(_,_)=>{activeGroupMenu?.Close();await Run(()=>ExpandedCommand(action));};menu.Items.Add(item);
        }
        AddGroupSubmenu(menu,SelectedGroupItems);
        menu.Opening+=(_,_)=>{foreach(var item in menu.Items.OfType<MenuFlyoutItem>()){var button=expandedActions[(StationAction)item.Tag];item.IsEnabled=button.IsEnabled;item.Text=AutomationProperties.GetName(button);}};
        var pages=new MenuFlyout();workspacePageButton.Flyout=pages;
        AutomationProperties.SetName(workspacePageButton,"分组内容：资料、待办或笔记");
        foreach(var page in Enumerable.Range(0,3))
        {
            int index=page;var item=new ToggleMenuFlyoutItem {Text=new[]{"资料","待办","笔记"}[page],Tag=page};
            item.Click+=(_,_)=>workspaceTabs.SelectedIndex=index;pages.Items.Add(item);
        }
        pages.Opening+=(_,_)=>{foreach(var item in pages.Items.OfType<ToggleMenuFlyoutItem>())item.IsChecked=(int)item.Tag==workspacePage;};
        PanelTheme.Track(workspacePageButton,static b=>{b.Height=PanelTheme.Value<double>("ActionSize");b.CornerRadius=PanelTheme.Value<CornerRadius>("ControlRadius");});
        workspacePageButton.Style=PanelTheme.Style("ItemAction");workspacePageButton.Padding=new Thickness(10,0,10,0);
    }
    void UpdateLeadingLayout(bool compact,double size)
    {
        bool material=workspacePage==0;
        expandedActionHost.Visibility=material?Visibility.Visible:Visibility.Collapsed;
        projectMore.Visibility=material&&compact?Visibility.Visible:Visibility.Collapsed;
        foreach(var (action,button) in expandedActions)
            button.Visibility=!compact||action==StationAction.Open||action==StationAction.Copy?Visibility.Visible:Visibility.Collapsed;
        if(stationHeader.ActualWidth<520&&compact)
        {expandedActionHost.Visibility=Visibility.Collapsed;stationTitle.MaxWidth=85;}
        expandedSelectionCount.Visibility=selection.Count>0&&!compact?Visibility.Visible:Visibility.Collapsed;
        if(workspaceDrag!=null)workspaceDrag.Visibility=Scene()!=null&&material&&!compact?Visibility.Visible:Visibility.Collapsed;
    }
}
