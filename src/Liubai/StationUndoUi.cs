using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Liubai;

internal sealed partial class StationWindow
{
    StationHistory history=null!;
    readonly StackPanel undoToast=new() {Orientation=Orientation.Horizontal,Spacing=8,Visibility=Visibility.Collapsed};
    readonly TextBlock undoText=Hint("");
    readonly Button undoButton=IconButton("\uE7A7","撤销上次操作（Ctrl+Z）");
    Microsoft.UI.Dispatching.DispatcherQueueTimer undoToastTimer=null!;
    int historyVersion;
    void InitializeUndo(StackPanel footer)
    {
        history=App.HistoryFor(store);historyVersion=history.Version;
        undoToast.Children.Add(undoText);undoToast.Children.Add(undoButton);footer.Children.Add(undoToast);
        undoText.MaxWidth=230;undoText.VerticalAlignment=VerticalAlignment.Center;
        undoButton.Click+=async (_,_)=>await UndoLast();
        undoToastTimer=DispatcherQueue.CreateTimer();undoToastTimer.IsRepeating=false;undoToastTimer.Interval=TimeSpan.FromSeconds(7);
        undoToastTimer.Tick+=(_,_)=>undoToast.Visibility=Visibility.Collapsed;
        history.Changed+=HistoryChanged;
        Closed+=(_,_)=>{undoToastTimer.Stop();history.Changed-=HistoryChanged;};
    }
    void HistoryChanged()
    {
        if(destroyed||historyVersion==history.Version)return;
        historyVersion=history.Version;
        if(!IsOpen)return;
        undoText.Text=history.Notice??history.Feedback;undoButton.Visibility=history.CanUndo?Visibility.Visible:Visibility.Collapsed;
        undoToast.Visibility=Visibility.Visible;undoToastTimer.Stop();undoToastTimer.Start();
    }
    Task Remember(string label,Func<Task> action)=>history.Execute(label,action);
    async Task UndoLast()=>await Run(async ()=> {
        if(!history.CanUndo){status.Text="暂无可撤销的操作";return;}
        try {await history.Undo();Refresh();status.Text="已撤销，记录与关联已恢复，原文件未改动。";}
        catch(Exception ex) when(ex is IOException or InvalidDataException or InvalidOperationException) {status.Text="撤销未完成："+ex.Message;}
    });
}
