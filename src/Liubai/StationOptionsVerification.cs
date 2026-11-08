using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Liubai;

public sealed partial class BarWindow
{
    async Task VerifyStationOptions(List<string> checks)
    {
        var store = App.Station;
        int original = store.Capacity;
        if (original != 200) throw new Exception("Station default not 200");
        OpenSettings(); await Task.Delay(250);
        var settingsRoot = (DependencyObject)settingsWindow!.Content;
        var section = StationControls<Expander>(settingsRoot).Single(e => Equals(e.Header, "中转站设置"));
        section.IsExpanded = true; await Task.Delay(150);
        var capacity = StationControls<NumberBox>(settingsRoot).Single(b => AutomationProperties.GetName(b) == "中转站最多保存项目数");
        var remove = StationControls<ToggleSwitch>(settingsRoot).Single(b => AutomationProperties.GetName(b) == "归档成功后移出中转站");
        capacity.Value = 321; remove.IsOn = false;
        if (!await settingsWindow.FlushStationOptions()) throw new Exception("Station settings save failed");
        var reload = new StationStore(Path.Combine(App.DataDirectory, "station.json"));
        if (reload.Capacity != 321 || reload.RemoveAfterArchive) throw new Exception("Native settings did not persist");
        int items = store.Items.Count;
        capacity.Value = 1; await settingsWindow.FlushStationOptions();
        if (store.Items.Count != items) throw new Exception("Lowering capacity deleted records");
        capacity.Value = original; remove.IsOn = true;
        var help = StationControls<Expander>(settingsRoot).Single(e=>Equals(e.Header,"帮助"));
        help.IsExpanded=true;await Task.Delay(100);
        if(help.Content is not Expander stationHelp||!Equals(stationHelp.Header,"中转站"))throw new Exception("Station help entry missing");
        stationHelp.IsExpanded=true;await Task.Delay(100);
        var instructions=string.Join("\n",StationControls<TextBlock>(stationHelp).Select(t=>t.Text));
        if(!instructions.Contains("Ctrl+Z")||!instructions.Contains("不会删除原文件")||!instructions.Contains("归档与导入"))throw new Exception("Station help content incomplete");
        help.IsExpanded=false;
        checks.Add("Settings help lazily exposes station instructions, archive safety and shortcuts");
        await settingsWindow.FlushStationOptions(); settingsWindow.Hide(); await settingsWindow.FlushStationOptions();
        checks.Add("Native capacity and archive toggles persist; lowering capacity preserves existing records");
        string directory = Path.Combine(App.DataDirectory, "archive-dialog-cancel");
        await store.UpdateOptions(original, directory, true);
        OpenStation();
        var stationRoot = (DependencyObject)stationWindow!.Content;
        var archive = StationControls<Button>(stationRoot).Single(b => AutomationProperties.GetName(b) == "归档到文件夹");
        if (archive.Parent is not StackPanel header || header.Children[1] is not Button clear || AutomationProperties.GetName(clear) != "清空中转站" || header.Children[2] != archive
            || header.Children.Count != 3 || header.Children[0] is not Button search || !AutomationProperties.GetName(search).StartsWith("统一搜索")) throw new Exception("Search, clear and archive must share header commands");
        var pending = stationWindow.Archive();
        ContentDialog? dialog = null;
        long deadline = Environment.TickCount64 + 3000;
        do
        {
            dialog = VisualTreeHelper.GetOpenPopupsForXamlRoot(((FrameworkElement)stationWindow.Content).XamlRoot)
                .SelectMany(p => p.Child is { } child ? StationControls<ContentDialog>(child) : []).SingleOrDefault();
            if (dialog != null || pending.IsCompleted) break;
            await Task.Delay(50);
        } while (Environment.TickCount64 < deadline);
        if (dialog?.Title as string != "归档全部项目？" || dialog.DefaultButton != ContentDialogButton.Close
            || !StationControls<CheckBox>((DependencyObject)dialog.Content).Single().IsChecked.GetValueOrDefault()) throw new Exception("Archive confirmation/default selection missing");
        dialog.Hide(); await pending;
        if (Directory.Exists(directory) || store.Items.Count != items) throw new Exception("Canceled archive dialog wrote files or removed content");
        await store.UpdateOptions(original, "", true);
        checks.Add("Header icons precede window controls; confirmation includes target and default removal; cancel leaves data and disk unchanged");
    }
}
