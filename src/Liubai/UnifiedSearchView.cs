using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.System;

namespace Liubai;

internal sealed partial class UnifiedSearchView : Grid
{
    readonly TextBox query=new() {PlaceholderText="搜索资料、分组、待办和笔记",MaxLength=200};
    readonly ComboBox scope = new() { HorizontalAlignment=HorizontalAlignment.Stretch, Visibility=Visibility.Collapsed };
    Func<SearchHit,bool>? scopeFilter;
    readonly ListView results=new() {SelectionMode=ListViewSelectionMode.Single,IsItemClickEnabled=true,MaxHeight=470,Background=null};
    readonly Func<SearchHit,Task> open;
    readonly Microsoft.UI.Dispatching.DispatcherQueueTimer debounce;
    CancellationTokenSource? cancellation;
    int version;
    bool subscribed,opening;
    readonly Grid Layout=new() {RowSpacing=12};
    readonly TextBlock StatusLine=PanelTheme.Hint("");
    internal bool IsOpen {get;private set;}
    internal UnifiedSearchView(Func<SearchHit,Task> open,Action back)
    {
        this.open=open;
        Layout.RowDefinitions.Add(new(){Height=GridLength.Auto});Layout.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});Layout.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var inputs=new StackPanel {Spacing=8};inputs.Children.Add(query);inputs.Children.Add(scope);
        scope.Items.Add("当前分类／分组");scope.Items.Add("全部内容");scope.SelectedIndex=0;scope.SelectionChanged+=(_,_)=>Schedule();
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(scope,"搜索范围");
        Layout.Children.Add(inputs);Grid.SetRow(results,1);results.MaxHeight=double.PositiveInfinity;Layout.Children.Add(results);Grid.SetRow(StatusLine,2);Layout.Children.Add(StatusLine);Children.Add(Layout);
        KeyDown+=(_,e)=>{if(e.Key==VirtualKey.Escape){e.Handled=true;back();}};
        debounce=DispatcherQueue.CreateTimer();debounce.IsRepeating=false;debounce.Interval=TimeSpan.FromMilliseconds(220);debounce.Tick+=async (_,_)=>await Search();
        query.TextChanged+=(_,_)=>Schedule();
        results.ItemClick+=async (_,e)=>{if(e.ClickedItem is FrameworkElement {Tag:SearchHit hit})await OpenResult(hit);};
        query.KeyDown+=async (_,e)=>{if(e.Key==VirtualKey.Enter&&results.IsEnabled&&results.SelectedItem is ListViewItem {Tag:SearchHit hit}){e.Handled=true;await OpenResult(hit);}else if(e.Key==VirtualKey.Down&&results.Items.Count>0){e.Handled=true;results.Focus(FocusState.Keyboard);}};
        results.KeyDown+=async (_,e)=>{if(e.Key==VirtualKey.Enter&&results.SelectedItem is ListViewItem {Tag:SearchHit hit}){e.Handled=true;await OpenResult(hit);}};

    }
    internal void Open()
    {
        if(!subscribed){App.Station.Changed+=SourceChanged;App.Planner.Changed+=SourceChanged;App.WorkStore.Changed+=SourceChanged;subscribed=true;}
        IsOpen=true;DispatcherQueue.TryEnqueue(()=>{if(IsOpen)query.Focus(FocusState.Programmatic);});Schedule();
    }
    internal void ConfigureScope(bool expanded,Func<SearchHit,bool>? filter)
    {scopeFilter=filter;scope.Visibility=expanded?Visibility.Visible:Visibility.Collapsed;scope.SelectedIndex=expanded?0:1;}
    void SourceChanged(){if(IsOpen)Schedule();}
    void Schedule(){version++;cancellation?.Cancel();results.IsEnabled=false;debounce.Stop();if(IsOpen){StatusLine.Text=query.Text.Trim().Length==0?"":"正在搜索…";debounce.Start();}}
    async Task Search()
    {
        if(!IsOpen)return;int current=version;
        cancellation?.Dispose();cancellation=new CancellationTokenSource();var token=cancellation.Token;
        string text=query.Text;var station=App.Station.Items.ToArray();var planner=App.Planner.Data;var work=App.WorkStore.Data;
        try {
            var filter=scope.SelectedIndex==0?scopeFilter:null;
            var hits=await Task.Run(()=>UnifiedSearch.Find(text,station,planner,work,token,filter),token);
            if(!IsOpen||current!=version||token.IsCancellationRequested)return;
            results.Items.Clear();
            foreach(var hit in hits){
                var row=new StackPanel {Spacing=4,Tag=hit};row.Children.Add(PanelTheme.Hint(UnifiedSearch.Label(hit.Kind)+(hit.Groups.Length>0?" · "+hit.Groups:"")));
                row.Children.Add(new TextBlock {Text=hit.Title,TextWrapping=TextWrapping.Wrap,MaxLines=2,FontWeight=Microsoft.UI.Text.FontWeights.SemiBold});
                if(hit.Detail.Length>0)row.Children.Add(new TextBlock {Text=hit.Detail,TextWrapping=TextWrapping.Wrap,MaxLines=2,Opacity=.7,FontSize=12});
                var item=new ListViewItem {Content=row,Tag=hit};Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(item,UnifiedSearch.Label(hit.Kind)+"："+hit.Title);results.Items.Add(item);
            }
            if(results.Items.Count>0)results.SelectedIndex=0;
            results.IsEnabled=true;StatusLine.Text=text.Trim().Length==0?"":hits.Length==0?"没有匹配内容":$"{hits.Length} 项结果"+(hits.Length==100?" · 最多显示 100 项":"");
        }
        catch(OperationCanceledException){}
        catch(Exception ex) when(ex is not OutOfMemoryException){if(IsOpen&&current==version){results.Items.Clear();StatusLine.Text="搜索未完成，请重试";}}
    }
    async Task OpenResult(SearchHit hit)
    {
        if(!IsOpen||opening||!results.IsEnabled)return;opening=true;
        try {await open(hit);}
        catch(Exception ex) when(ex is not OutOfMemoryException){StatusLine.Text="无法打开，记录可能已移除。请重新搜索。";Schedule();}
        finally {opening=false;}
    }
    internal void Stop()
    {IsOpen=false;debounce.Stop();version++;cancellation?.Cancel();cancellation?.Dispose();cancellation=null;scopeFilter=null;results.Items.Clear();if(subscribed){App.Station.Changed-=SourceChanged;App.Planner.Changed-=SourceChanged;App.WorkStore.Changed-=SourceChanged;subscribed=false;}}

}
