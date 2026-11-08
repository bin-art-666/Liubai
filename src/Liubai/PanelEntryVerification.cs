using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Liubai;

internal sealed partial class StationWindow
{
    internal async Task<string[]> VerifyEntryDialogs()
    {
        using var lease=KeepOpen();
        Guid fixture=await App.Work.Store.Create("已有分组验证",false);
        int before=App.WorkStore.Data.Scenes.Length;
        var pending=CreateWorkspace();await Task.Delay(180);
        var dialog=VisualTreeHelper.GetOpenPopupsForXamlRoot(root.XamlRoot).Select(p=>p.Child).Where(p=>p!=null).SelectMany(p=>EntryControls<ContentDialog>(p!)).Single();
        var panel=(StackPanel)dialog.Content;var name=panel.Children.OfType<TextBox>().Single();
        if(!ReferenceEquals(FocusManager.GetFocusedElement(root.XamlRoot),name)||dialog.DefaultButton!=ContentDialogButton.Primary||dialog.IsPrimaryButtonEnabled)throw new Exception("Create focus/default/empty validation");
        name.Text="   ";await Task.Delay(60);if(dialog.IsPrimaryButtonEnabled)throw new Exception("Whitespace enabled creation");
        name.Text=App.WorkStore.Data.Scenes.First().Name;
        await Task.Delay(80);
        if(dialog.IsPrimaryButtonEnabled||!panel.Children.OfType<TextBlock>().Any(t=>t.Text.Contains("同名")))throw new Exception("Duplicate name not shown inline");
        name.Text="回车创建验证";await Task.Delay(60);
        var primary=EntryControls<Button>(dialog).Single(b=>b.Name=="PrimaryButton"||Equals(b.Content,"创建"));
        ((IInvokeProvider)new ButtonAutomationPeer(primary).GetPattern(PatternInterface.Invoke)).Invoke();
        await pending;
        if(App.WorkStore.Data.Scenes.Length!=before+1||Scene()?.Name!="回车创建验证")throw new Exception("Primary submit did not create exactly one group");
        Guid created=workspaceId!.Value;
        var canceled=CreateWorkspace();await Task.Delay(120);
        var cancelDialog=VisualTreeHelper.GetOpenPopupsForXamlRoot(root.XamlRoot).Select(p=>p.Child).Where(p=>p!=null).SelectMany(p=>EntryControls<ContentDialog>(p!)).Single();
        cancelDialog.Hide();await canceled;
        if(App.WorkStore.Data.Scenes.Length!=before+1)throw new Exception("Cancel created a record");
        await App.Work.Store.Remove(created);await App.Work.Store.Remove(fixture);workspaceId=null;Refresh();
        var text=new TextBox {AcceptsReturn=true,Text="第一行\n第二行"};int saves=0;
        var editor=new PanelEntryDialog(root.XamlRoot,"多行验证","保存",text,[text],()=>null,async ()=>{await Task.Delay(60);saves++;});
        var shown=editor.Show();await Task.Delay(120);
        if(editor.Dialog.DefaultButton!=ContentDialogButton.None)throw new Exception("Multiline has a default Enter action");
        editor.Composition(text,true);await editor.Submit();
        if(editor.CanSubmit||saves!=0)throw new Exception("IME composition submitted a record");
        editor.Composition(text,false);await editor.Submit();
        if(saves!=0)throw new Exception("IME committing key submitted a record");
        await Task.Delay(100);await Task.WhenAll(editor.Submit(),editor.Submit(),editor.Submit());await shown;
        if(saves!=1||text.Text.Replace("\r\n","\n").Replace('\r','\n')!="第一行\n第二行")throw new Exception($"Repeated submit or multiline mismatch: saves={saves}, text={System.Text.Json.JsonSerializer.Serialize(text.Text)}");
        return ["Create dialog focuses name, defaults Enter to primary, blocks blank/duplicate names, creates once and cancels without a record", "Multiline preserves newline; composition and commit-key guards block submission; concurrent saves execute once"];
    }
    static IEnumerable<T> EntryControls<T>(DependencyObject node) where T:DependencyObject
    {
        if(node is T result)yield return result;
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)foreach(var child in EntryControls<T>(VisualTreeHelper.GetChild(node,i)))yield return child;
    }
}
