using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;

namespace Liubai;

internal sealed partial class StationWindow
{
    internal async Task<string[]> VerifyBrowser()
    {
        var checks = new List<string>(); Open(display, display.Info.Work.Top);
        int total = store.Items.Count;
        void Choose(StationCategory value) => categories.SelectedItem = categories.Items.OfType<ListViewItem>().Single(i => Equals(i.Tag, value));
        if (CurrentItems().Length != total || browseTools.Visibility != Visibility.Collapsed) throw new Exception("Docked view must start with all records");
        ToggleDetached(); await Task.Delay(150);
        Choose(StationCategory.Images);
        if (CurrentItems().Length != 1 || CurrentItems()[0].Kind != StationKind.File || expandedQuery.Category != StationCategory.Images) throw new Exception("Image category selection failed");
        if (categories.SelectedItem is not ListViewItem { Tag: StationCategory.Images }) throw new Exception("Sidebar selection not retained after count refresh");
        Choose(StationCategory.Files); if (CurrentItems().Length != 1) throw new Exception("Images leaked into files");
        checks.Add("Expanded sidebar selections filter images separately from files and retain selected category");
        Choose(StationCategory.Text); periodFilter.SelectedIndex = (int)StationPeriod.Today; search.Text = "备忘"; await Task.Delay(350);
        if (CurrentItems().Length != 1 || !Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(clear).Contains("1") || !Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(archive).Contains("1")) throw new Exception("Combined query or scope labels invalid");
        var clearPending = Clear(); await Task.Delay(150);
        ContentDialog Dialog() => VisualTreeHelper.GetOpenPopupsForXamlRoot(root.XamlRoot).SelectMany(p => p.Child is { } child ? FindDialogs(child) : []).Single();
        var dialog = Dialog();
        if (!Equals(dialog.Title, "清空当前筛选结果？") || !Convert.ToString(dialog.Content)!.Contains("1 项")) throw new Exception("Filtered clear confirmation scope missing");
        dialog.Hide(); await clearPending;
        if (store.Items.Count != total) throw new Exception("Canceled clear changed records");
        string directory = Path.Combine(Path.GetDirectoryName(store.WindowLayoutPath)!, "canceled-filter-archive");
        await store.UpdateOptions(store.Capacity, directory, true);
        var archivePending = Archive(); await Task.Delay(150); dialog = Dialog();
        if (!Equals(dialog.Title, "归档当前筛选结果？")) throw new Exception("Filtered archive confirmation scope missing");
        dialog.Hide(); await archivePending;
        if (Directory.Exists(directory) || store.Items.Count != total) throw new Exception("Canceled archive wrote or removed records");
        checks.Add("Search and local time combine with category; clear/archive show filtered counts; canceled confirmations leave items and files untouched");
        search.Text = ""; periodFilter.SelectedIndex = 0; Choose(StationCategory.Images); await Task.Delay(300);
        var package = new DataPackage(); package.SetText("筛选外拖入的文字");
        if (!await Receive(package.GetView()) || store.Items.Count != total + 1 || CurrentItems().Length != 1 || viewAllAfterDrop.Visibility != Visibility.Visible) throw new Exception("Hidden-type incoming drop was not retained or acknowledged");
        ShowAllItems(); if (CurrentItems().Length != total + 1 || ActiveQuery.Filtered) throw new Exception("View-all action failed");
        checks.Add("Drop outside selected category is saved, acknowledged and revealed by View all");
        Choose(StationCategory.Links); periodFilter.SelectedIndex = (int)StationPeriod.Last7Days;
        search.Text = "项目"; await Task.Delay(300); var selected = expandedQuery;
        if (CurrentItems().Length != 1) throw new Exception("Link title/date search failed");
        ToggleDetached();
        if (CurrentItems().Length != total + 1 || browseTools.Visibility != Visibility.Collapsed) throw new Exception("Docking retained hidden filters");
        ToggleDetached();
        if (expandedQuery != selected || CurrentItems().Length != 1 || new StationPanelLayout(store.WindowLayoutPath).Data.View != selected) throw new Exception("Expanded query did not survive docking and persistence");
        var work = display.Info.Work; double scale = display.Dpi / 96d;
        AppWindow.MoveAndResize(Clamp(new(work.Left, work.Top, (int)(480 * scale), (int)(650 * scale)))); await Task.Delay(180);
        if (categories.Visibility != Visibility.Collapsed || compactCategory.Visibility != Visibility.Visible) throw new Exception("Narrow mode missing compact category selector");
        AppWindow.MoveAndResize(Clamp(new(work.Left, work.Top, (int)(950 * scale), (int)(650 * scale)))); await Task.Delay(180);
        if (browserBody.ActualWidth >= 600 && categories.Visibility != Visibility.Visible) throw new Exception("Wide mode missing category sidebar");
        checks.Add("Docked view shows all; expanded query survives docking/restart; narrow and wide layouts adapt");
        return checks.ToArray();
    }
    static IEnumerable<ContentDialog> FindDialogs(DependencyObject parent)
    {
        if (parent is ContentDialog match) yield return match;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) foreach (var dialog in FindDialogs(VisualTreeHelper.GetChild(parent, i))) yield return dialog;
    }
}

public sealed partial class BarWindow
{
    async Task VerifyStationBrowser()
    {
        string result = "PASS", error = ""; string[] checks = []; StationWindow? panel = null;
        try
        {
            await Task.Delay(500); if (SessionState.Locked == true) throw new IOException("LOCKED: unlock before verification");
            Directory.CreateDirectory(App.DataDirectory); var now = DateTimeOffset.Now;
            StationItem Item(StationKind kind, string value, int days, string title = "") => new(Guid.NewGuid(), kind, value, title, now.AddDays(-days));
            var data = new StationDocument(1, [Item(StationKind.File, Path.Combine(App.DataDirectory,"fixture.JPG"),0), Item(StationKind.File,Path.Combine(App.DataDirectory,"fixture.pdf"),1),
                Item(StationKind.Folder,App.DataDirectory,0),Item(StationKind.Text,"今天的备忘",0),Item(StationKind.Text,"以前的备忘",8),Item(StationKind.Link,"https://example.com/",3,"项目链接")]);
            File.WriteAllText(Path.Combine(App.DataDirectory,"station.json"),System.Text.Json.JsonSerializer.Serialize(data));
            // The focus bar may already have initialized App.Station before these
            // fixture records were written. Load this fixture into its own store.
            panel = new StationWindow(new StationStore(Path.Combine(App.DataDirectory, "station.json")),Display); checks = await panel.VerifyBrowser();
        }
        catch (Exception ex) { result = "FAIL"; error = ex.ToString(); }
        finally
        {
            panel?.Destroy(); Directory.CreateDirectory(App.DataDirectory);
            File.WriteAllText(Path.Combine(App.DataDirectory,"station-browser-check.json"),System.Text.Json.JsonSerializer.Serialize(new { result,error,checks })); Close();
        }
    }
}
