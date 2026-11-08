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
    Microsoft.UI.Dispatching.DispatcherQueueTimer feedbackTimer=null!;
    Grid InitializeStationHeader(FrameworkElement title,StackPanel windowActions)
    {
        stationCommands.Spacing=4;
        var commands=new Grid();stationHeaderCommands=commands;stationWindowActions=windowActions;commands.Children.Add(stationCommands);commands.Children.Add(stationCommandMore);
        var actions=PanelTheme.Actions();actions.Spacing=8;actions.Children.Add(commands);actions.Children.Add(windowActions);actions.Children.Add(searchReturn);searchReturn.Visibility=Visibility.Collapsed;searchReturn.Click+=(_,_)=>CloseSearch();
        var menu=new MenuFlyout();
        var search=new MenuFlyoutItem {Text="搜索"};search.Click+=(_,_)=>SearchRequested?.Invoke();menu.Items.Add(search);
        var empty=new MenuFlyoutItem {Text="清空"};empty.Click+=async (_,_)=>await Clear();menu.Items.Add(empty);
        var save=new MenuFlyoutItem {Text="归档"};save.Click+=async (_,_)=>await Archive();menu.Items.Add(save);
        menu.Opening+=(_,_)=>{empty.IsEnabled=clear.IsEnabled;save.IsEnabled=archive.IsEnabled;empty.Text=AutomationProperties.GetName(clear);save.Text=AutomationProperties.GetName(archive);};
        stationCommandMore.Flyout=menu;
        stationHeader=PanelTheme.Header(title,actions);
        stationHeader.SizeChanged+=(_,_)=>UpdateStationHeader();
        InitializeFloatingCaption(stationHeader,actions,()=>Detached);
        UpdateStationHeader();return stationHeader;
    }
    void UpdateStationHeader()
    {
        if(stationHeader==null||destroyed)return;
        stationTitle.Text=SearchActive?"统一搜索":$"中转站 · {store.Items.Count}";
        stationHeaderCommands.Visibility=stationWindowActions.Visibility=SearchActive?Visibility.Collapsed:Visibility.Visible;
        searchReturn.Visibility=SearchActive?Visibility.Visible:Visibility.Collapsed;
        if(SearchActive)return;
        stationTitle.Measure(new Size(double.PositiveInfinity,double.PositiveInfinity));
        double size=PanelTheme.Value<double>("ActionSize");
        double required=stationTitle.DesiredSize.Width+PanelTheme.Value<double>("PanelGap")+6*size+24;
        bool compact=stationHeader.ActualWidth>0&&stationHeader.ActualWidth<required;
        stationCommands.Visibility=compact?Visibility.Collapsed:Visibility.Visible;
        stationCommandMore.Visibility=compact?Visibility.Visible:Visibility.Collapsed;
    }
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
