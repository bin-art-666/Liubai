using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;

namespace Liubai;

internal sealed partial class SettingsWindow
{
    internal async Task VerifyLayoutControls()
    {
        using var visibility = KeepOpen();
        long end = Environment.TickCount64 + 5000;
        while (!interactive) { if (Environment.TickCount64 > end) throw new Exception("Settings did not load"); await Task.Delay(40); }
        layoutRows["ai"].Placement.SelectedIndex = (int)ToolbarPlacement.More;
        layoutRows["privacy"].Placement.SelectedIndex = (int)ToolbarPlacement.Hidden;
        layoutSpacing.Value = 8;
        end = Environment.TickCount64 + 5000;
        while (App.ToolbarLayout.Data.Entry("ai").Placement != ToolbarPlacement.More || App.ToolbarLayout.Data.Entry("privacy").Placement != ToolbarPlacement.Hidden || App.ToolbarLayout.Data.Spacing != 8)
        { if (Environment.TickCount64 > end) throw new Exception("Layout did not auto-save"); await Task.Delay(40); }
        var read = new ToolbarLayoutStore(Path.Combine(App.DataDirectory, "toolbar-layout.json"));
        if (read.Data.Entry("ai").Placement != ToolbarPlacement.More || read.Data.Entry("privacy").Placement != ToolbarPlacement.Hidden) throw new Exception("Layout reload lost placements");
        var before = preferences.ToolbarItems.ToArray(); int index = Array.IndexOf(before, "ai"); MoveLayoutTool("ai", -1);
        if (preferences.ToolbarItems[index - 1] != "ai") throw new Exception("Settings order action failed");
        layoutSpacing.Value = 7; visibility.Dispose(); Hide();
        if (App.ToolbarLayout.Data.Spacing != 7) throw new Exception("Hide lost pending spacing");
        ResetToolbarLayout();
        if (App.ToolbarLayout.Current.Entry("ai").Placement != ToolbarPlacement.Bar || App.ToolbarLayout.Current.Spacing != 2 || !preferences.ToolbarItems.SequenceEqual(ToolbarOrder.Defaults)) throw new Exception("Default layout restore failed");
    }
    internal async Task PreviewToolbarLayoutFixture()
    {
        using var lease = KeepOpen(); Open(display.Info.Work.Top);
        var section = StationControlsForStyle<Expander>((DependencyObject)Content).Single(e => Equals(e.Header, "顶栏布局")); section.IsExpanded = true;
        await Task.Delay(TimeSpan.FromSeconds(60));
    }
}

public sealed partial class BarWindow
{
    async Task VerifyToolbarLayout()
    {
        string result = "PASS", error = ""; var checks = new List<string>();
        try
        {
            await Task.Delay(600); if (SessionState.Locked == true) throw new IOException("LOCKED: unlock before toolbar verification");
            var profile = new ToolbarLayoutOptions(Entries: ToolbarOrder.Defaults.Where(id => id != "settings").Select(id => new ToolbarEntry(id, id == "privacy" ? ToolbarPlacement.Hidden : ToolbarPlacement.More)).ToArray());
            var menus = new[] { screenshot.ContextFlyout, aiButton.ContextFlyout, guardButton.ContextFlyout };
            ((MenuFlyout)screenshot.ContextFlyout).ShowAt(screenshot); await Task.Delay(120); screenshot.ContextFlyout.Hide();
            var sourceContent = todo.Content; bool wanted = App.Guard.Wanted; bool privacyState = App.Privacy.Journal.Active;
            await App.Clock.StartCountdown(120, "布局验证", DateTimeOffset.Now); var countdown = App.Clock.Data.Countdown!.Id;
            App.ToolbarLayout.Save(profile); ReflowToolbar(1000);
            if (!toolbarPlan.Visible.SequenceEqual(new[] { "settings" }) || !toolbarPlan.ShowMore || toolbarPlan.More.Contains("privacy")) throw new Exception("Explicit placement mismatch");
            EnsureMoreWindow().PreviewPinned = true; OpenMore(); await Task.Delay(160);
            if (moreWindow?.IsOpen != true || !moreWindow.Contains(todo) || ReferenceEquals(todo.Parent, actions) || !todo.AllowDrop || !aiButton.AllowDrop || !shelf.AllowDrop
                || screenshot.ContextFlyout == null || aiButton.ContextFlyout == null || guardButton.ContextFlyout == null) throw new Exception($"More did not retain tools: open={moreWindow?.IsOpen}, contains={moreWindow?.Contains(todo)}, barParent={ReferenceEquals(todo.Parent, actions)}, drops={todo.AllowDrop}/{aiButton.AllowDrop}/{shelf.AllowDrop}");
            checks.Add("More reuses original tools with menus and drag targets; Hidden entries excluded; settings retained");
            await App.Clock.PauseCountdown(DateTimeOffset.Now); await Task.Delay(100);
            if (!double.IsNaN(clockButton.Width) || clockButton.HorizontalContentAlignment != HorizontalAlignment.Stretch || App.Clock.Data.Countdown?.Id != countdown) throw new Exception("Clock state/layout lost inside More");
            if (App.Guard.Wanted != wanted || App.Privacy.Journal.Active != privacyState) throw new Exception("Opening More changed privacy hardware state");
            checks.Add("Moving tools does not stop a countdown, initialize guard hardware or toggle privacy");
            ((IInvokeProvider)new ButtonAutomationPeer(todo).GetPattern(PatternInterface.Invoke)).Invoke();
            long end = Environment.TickCount64 + 5000;
            while (plannerWindow?.IsOpen != true) { if (Environment.TickCount64 > end) throw new Exception("Todo action did not open original planner"); await Task.Delay(40); }
            if (moreWindow.IsOpen || !ReferenceEquals(todo.Parent, actions) || !ReferenceEquals(sourceContent, todo.Content) || plannerWindow.Page != PlannerPage.Tasks) throw new Exception("Opening tool lost content or did not dismiss More");
            if (!ReferenceEquals(menus[0], screenshot.ContextFlyout) || !ReferenceEquals(menus[1], aiButton.ContextFlyout) || !ReferenceEquals(menus[2], guardButton.ContextFlyout)) throw new Exception("Original menus not restored on return");
            checks.Add("Original click handler opens planner, dismisses More and restores original toolbar content");
            plannerWindow.Hide(); OpenMore(); await Task.Delay(100);
            var menu = (MenuFlyout)screenshot.ContextFlyout; menu.ShowAt(screenshot); await Task.Delay(180);
            if (!menu.Items.OfType<MenuFlyoutItem>().Any(i => i.Text == "打开截图文件夹")) throw new Exception("Screenshot context action missing");
            menu.Hide(); moreWindow.Hide(); await Task.Delay(100);
            if (toolbarButtons.Values.Any(b => !ReferenceEquals(b.Parent, actions))) throw new Exception("Closing More lost toolbar parents");
            checks.Add("Screenshot context menu remains attached and usable; closing More restores every tool");
            App.ToolbarLayout.Save(new()); ReflowToolbar(130);
            if (!toolbarPlan.Visible.Contains("settings") || !toolbarPlan.ShowMore || toolbarPlan.More.Length == 0) throw new Exception("Automatic overflow failed");
            ReflowToolbar(2000); if (toolbarPlan.More.Length != 0) throw new Exception("Wider monitor retained overflow");
            App.ToolbarLayout.Preview(new ToolbarLayoutOptions().WithEntry(new("station", GroupStart: true)) with { Spacing = 8 }); ReflowToolbar(2000);
            if (actions.Spacing != 8 || shelf.Margin.Left != 10) throw new Exception("Spacing/group preview failed");
            checks.Add("Narrow/wide overflow adapts without saving monitor-specific placement; grouping and spacing preview");
            App.ToolbarLayout.Save(new()); await App.Clock.CancelCountdown();
            await App.Station.Add([StationStore.Text("布局恢复保留的暂存")]); int count = App.Station.Items.Count;
            OpenSettings(); await settingsWindow!.VerifyLayoutControls();
            if (App.Station.Items.Count != count) throw new Exception("Reset deleted station data");
            checks.Add("Settings placements, ordering, debounced persistence, close flush and default reset preserve records");
            if (Environment.GetCommandLineArgs().Contains("--preview-toolbar-layout"))
            {
                OpenSettings(); await settingsWindow.PreviewToolbarLayoutFixture();
                App.ToolbarLayout.Save(profile); ReflowToolbar(1000); EnsureMoreWindow().PreviewPinned = true; OpenMore(); await Task.Delay(TimeSpan.FromSeconds(60));
            }
        }
        catch (Exception ex) { result = "FAIL"; error = ex.ToString(); }
        finally
        {
            moreWindow?.Hide(); await App.Clock.CancelCountdown(); App.ToolbarLayout.Save(new());
            File.WriteAllText(Path.Combine(App.DataDirectory, "toolbar-layout-check.json"), System.Text.Json.JsonSerializer.Serialize(new { result, error, checks })); Close();
        }
    }
}
