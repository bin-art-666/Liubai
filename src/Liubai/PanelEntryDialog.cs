using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Windows.UI.Core;

namespace Liubai;

// Shared entry behavior. Persistence finishes before closing; validation failures
// keep both the editor and its contents available for correction.
internal sealed class PanelEntryDialog
{
    internal ContentDialog Dialog { get; }
    internal TextBlock Error { get; } = PanelTheme.Hint("");
    readonly TextBox first;
    readonly IReadOnlyList<TextBox> editors;
    readonly Func<string?> validate;
    readonly Func<Task> save;
    readonly HashSet<TextBox> composing=[];
    readonly Microsoft.UI.Dispatching.DispatcherQueueTimer imeRelease;
    bool commitKey, saving, saved, closed;
    internal bool CanSubmit=>!saving&&!saved&&!closed&&composing.Count==0&&!commitKey&&validate()==null;
    internal PanelEntryDialog(XamlRoot root,string title,string primary,TextBox first,IReadOnlyList<TextBox> editors,
        Func<string?> validate,Func<Task> save,UIElement? extra=null)
    {
        this.first=first;this.editors=editors;this.validate=validate;this.save=save;
        var panel=new StackPanel {Spacing=8};panel.Children.Add(first);
        if(extra!=null)panel.Children.Add(extra);
        Error.Foreground=new SolidColorBrush(Windows.UI.Color.FromArgb(255,194,54,66));
        Error.Visibility=Visibility.Collapsed;panel.Children.Add(Error);
        Dialog=new ContentDialog {XamlRoot=root,Title=title,Content=panel,PrimaryButtonText=primary,CloseButtonText="取消",
            DefaultButton=first.AcceptsReturn?ContentDialogButton.None:ContentDialogButton.Primary};
        imeRelease=first.DispatcherQueue.CreateTimer();imeRelease.Interval=TimeSpan.FromMilliseconds(40);imeRelease.IsRepeating=true;
        imeRelease.Tick+=(_,_)=>{if(!Down(VirtualKey.Enter)&&!Down(VirtualKey.Space)){commitKey=false;imeRelease.Stop();Refresh();}};
        foreach(var editor in editors)
        {
            editor.TextChanged+=(_,_)=>Refresh();
            editor.TextCompositionStarted+=(_,_)=>Composition(editor,true);
            editor.TextCompositionEnded+=(_,_)=>Composition(editor,false);
            editor.KeyDown+=Key;
            editor.AddHandler(UIElement.KeyUpEvent,new KeyEventHandler((_,e)=>{
                if(e.Key is VirtualKey.Enter or VirtualKey.Space)editor.DispatcherQueue.TryEnqueue(()=>{
                    if(!closed&&!Down(VirtualKey.Enter)&&!Down(VirtualKey.Space)){commitKey=false;imeRelease.Stop();Refresh();}
                });
            }),true);
        }
        Dialog.Opened+=(_,_)=>{Refresh();first.Focus(FocusState.Programmatic);};
        Dialog.Closed+=(_,_)=>{closed=true;imeRelease.Stop();};
        Dialog.PrimaryButtonClick+=async (_,e)=>{
            e.Cancel=true;var deferral=e.GetDeferral();
            try{await Submit(false);}finally{deferral.Complete();}
            if(saved&&!closed)Dialog.Hide();
        };
        Dialog.Closing+=(_,e)=>{if(saving)e.Cancel=true;};
        Refresh();
    }
    static bool Down(VirtualKey key)=>(InputKeyboardSource.GetKeyStateForCurrentThread(key)&CoreVirtualKeyStates.Down)!=0;
    internal void Composition(TextBox editor,bool active)
    {
        if(active)composing.Add(editor);
        else {composing.Remove(editor);commitKey=true;imeRelease.Start();}
        Refresh();
    }
    void Refresh()
    {
        if(closed)return;
        string? error=validate();
        Dialog.IsPrimaryButtonEnabled=!saving&&!saved&&composing.Count==0&&!commitKey&&error==null;
        if(!saving){Error.Text=first.Text.Length==0?"":error??"";Error.Visibility=Error.Text.Length==0?Visibility.Collapsed:Visibility.Visible;}
    }
    async void Key(object sender,KeyRoutedEventArgs e)
    {
        if(e.Key!=VirtualKey.Enter)return;
        if(composing.Count>0||commitKey)return;
        if(Down(VirtualKey.Control)) {e.Handled=true;await Submit();}
        // Single-line fields use ContentDialog's native default Enter action;
        // multiline editors retain their normal newline behavior.
    }
    internal async Task Submit(bool hide=true)
    {
        if(!CanSubmit){Refresh();return;}
        saving=true;Dialog.IsPrimaryButtonEnabled=false;
        foreach(var editor in editors)editor.IsReadOnly=true;
        try{await save();saved=true;}
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {Error.Text=ex is InvalidDataException?ex.Message:"保存失败，请重试。";Error.Visibility=Visibility.Visible;}
        finally{foreach(var editor in editors)editor.IsReadOnly=false;saving=false;Dialog.IsPrimaryButtonEnabled=CanSubmit;}
        if(saved&&hide&&!closed)Dialog.Hide();
    }
    internal async Task<bool> Show() {await Dialog.ShowAsync();return saved;}
}
