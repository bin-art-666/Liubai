using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace Liubai;

internal sealed partial class StationWindow
{
    readonly Button stationCommandMore=IconButton("\uE712","中转站操作");
    Grid stationHeader=null!, stationHeaderCommands=null!;
    StackPanel stationWindowActions=null!;
    readonly Button searchReturn=IconButton("\uE8BB","返回中转站");
    readonly StackPanel stationLeading=new(){Orientation=Orientation.Horizontal,Spacing=6,VerticalAlignment=VerticalAlignment.Center};
    readonly Border stationDivider=new(){Width=1,Height=22,Margin=new Thickness(6,0,6,0),Opacity=.25,VerticalAlignment=VerticalAlignment.Center};
    readonly Button projectMore=IconButton("\uE712","所选项目操作");
    readonly Button workspacePageButton=new(){MinWidth=72,VerticalAlignment=VerticalAlignment.Center};
    Microsoft.UI.Dispatching.DispatcherQueueTimer feedbackTimer=null!;
    Grid InitializeStationHeader(FrameworkElement title,StackPanel windowActions)
    {
        stationCommands.Spacing=4;
        var commands=new Grid();stationHeaderCommands=commands;stationWindowActions=windowActions;
        commands.ColumnDefinitions.Add(new(){Width=GridLength.Auto});commands.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        commands.Children.Add(expandedCommands);Grid.SetColumn(stationCommands,1);Grid.SetColumn(stationCommandMore,1);commands.Children.Add(stationCommands);commands.Children.Add(stationCommandMore);
        var actions=PanelTheme.Actions();actions.Spacing=8;actions.Children.Add(commands);actions.Children.Add(windowActions);actions.Children.Add(searchReturn);searchReturn.Visibility=Visibility.Collapsed;searchReturn.Click+=(_,_)=>CloseSearch();
        var menu=new MenuFlyout();
        var time=new MenuFlyoutSubItem {Text="时间筛选"};var create=new MenuFlyoutItem {Text="新建工作分组"};var import=new MenuFlyoutItem {Text="导入分组归档"};
        create.Click+=async (_,_)=>await Run(CreateWorkspace);import.Click+=async (_,_)=>await Run(ImportWorkspace);
        menu.Items.Add(time);menu.Items.Add(create);menu.Items.Add(import);
        var search=new MenuFlyoutItem {Text="搜索"};search.Click+=(_,_)=>SearchRequested?.Invoke();menu.Items.Add(search);
        var empty=new MenuFlyoutItem {Text="清空"};empty.Click+=async (_,_)=>await Clear();menu.Items.Add(empty);
        var save=new MenuFlyoutItem {Text="归档"};save.Click+=async (_,_)=>await Archive();menu.Items.Add(save);
        menu.Opening+=(_,_)=>{time.Visibility=create.Visibility=import.Visibility=Detached?Visibility.Visible:Visibility.Collapsed;PopulateTimeMenu(time.Items);empty.IsEnabled=clear.IsEnabled;save.IsEnabled=archive.IsEnabled;empty.Text=AutomationProperties.GetName(clear);save.Text=AutomationProperties.GetName(archive);};
        stationCommandMore.Flyout=menu;
        var layout=new Grid();layout.ColumnDefinitions.Add(new(){Width=GridLength.Auto});layout.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});layout.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        layout.Children.Add(stationLeading);Grid.SetColumn(actions,2);layout.Children.Add(actions);
        actions.Children.Insert(0,stationDivider);stationDivider.Background=new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray);
        stationHeader=PanelTheme.Header(title,layout);
        stationHeader.SizeChanged+=(_,_)=>UpdateStationHeader();
        InitializeFloatingCaption(stationHeader,actions,()=>Detached,stationLeading);
        UpdateStationHeader();return stationHeader;
    }
    void UpdateStationHeader()
    {
        if(stationHeader==null||destroyed)return;
        stationTitle.Text=SearchActive?"统一搜索":Detached?ExpandedStationTitle():$"中转站 · {store.Items.Count}";
        stationTitle.MaxWidth=Detached?Math.Clamp(stationHeader.ActualWidth*.2,85,180):double.PositiveInfinity;
        ToolTipService.SetToolTip(stationTitle,stationTitle.Text);
        stationHeader.ColumnDefinitions[0].Width=Detached?GridLength.Auto:new GridLength(1,GridUnitType.Star);
        stationHeader.ColumnDefinitions[1].Width=Detached?new GridLength(1,GridUnitType.Star):GridLength.Auto;
        stationLeading.Visibility=Detached&&!SearchActive?Visibility.Visible:Visibility.Collapsed;
        stationDivider.Visibility=Detached&&!SearchActive?Visibility.Visible:Visibility.Collapsed;
        stationHeaderCommands.Visibility=stationWindowActions.Visibility=SearchActive?Visibility.Collapsed:Visibility.Visible;
        searchReturn.Visibility=SearchActive?Visibility.Visible:Visibility.Collapsed;
        if(SearchActive)return;
        stationTitle.Measure(new Size(double.PositiveInfinity,double.PositiveInfinity));
        double size=PanelTheme.Value<double>("ActionSize");
        double required=stationTitle.DesiredSize.Width+PanelTheme.Value<double>("PanelGap")+(Detached?16:6)*size+(Detached?180:24);
        bool compact=stationHeader.ActualWidth>0&&stationHeader.ActualWidth<required;
        stationCommands.Visibility=compact?Visibility.Collapsed:Visibility.Visible;
        stationCommandMore.Visibility=compact?Visibility.Visible:Visibility.Collapsed;
        expandedCommands.Visibility=Detached&&!compact?Visibility.Visible:Visibility.Collapsed;
        if(Detached)UpdateLeadingLayout(compact,size);
    }
    string ExpandedStationTitle()=>"中转站"+(expandedQuery.Category!=StationCategory.All?"·"+StationQuery.CategoryNames[(int)expandedQuery.Category]:Scene() is {} scene?"·"+scene.Name:ungrouped?"·未分组":"");
    void InitializeStationFeedback()
    {
        status.Visibility=Visibility.Collapsed;
        AutomationProperties.SetLiveSetting(status,Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        feedbackTimer=DispatcherQueue.CreateTimer();feedbackTimer.IsRepeating=false;feedbackTimer.Interval=TimeSpan.FromSeconds(6);
        feedbackTimer.Tick+=(_,_)=>{
            if(busy){feedbackTimer.Start();return;}
            if(status.Text==store.LoadWarning||status.Text==panelLayout.Warning)return;
            status.Text="";
        };
        status.RegisterPropertyChangedCallback(TextBlock.TextProperty,(_,_)=>{
            status.Visibility=string.IsNullOrWhiteSpace(status.Text)?Visibility.Collapsed:Visibility.Visible;
            feedbackTimer.Stop();if(status.Visibility==Visibility.Visible)feedbackTimer.Start();
        });
    }
}
