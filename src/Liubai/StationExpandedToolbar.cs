using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace Liubai;

internal sealed partial class StationWindow
{
    readonly StackPanel expandedCommands = new() { Orientation = Orientation.Horizontal, Spacing = 4, Visibility = Visibility.Collapsed };
    readonly Button timeFilterButton = IconButton("\uE787", "时间筛选：全部时间");
    readonly Grid expandedActionHost = new() { ColumnSpacing = 12 };
    readonly TextBlock expandedSelectionCount = Hint("已选 0 项");
    readonly Dictionary<StationAction, Button> expandedActions = [];
    bool? expandedHeaderMode;
    readonly Microsoft.UI.Xaml.Shapes.Ellipse timeFilterBadge = new() { Width=5,Height=5,Fill=new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.DodgerBlue),HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Bottom,Visibility=Visibility.Collapsed,IsHitTestVisible=false };

    void InitializeExpandedToolbar()
    {
        expandedActionHost.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        expandedActionHost.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        expandedSelectionCount.VerticalAlignment = VerticalAlignment.Center;
        expandedActionHost.Children.Add(expandedSelectionCount);
        var scroll = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalScrollMode = ScrollMode.Enabled, VerticalScrollMode = ScrollMode.Disabled };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        expandedProjectActions=actions;
        foreach (var action in new[] { StationAction.Open, StationAction.Copy, StationAction.Folder, StationAction.Path, StationAction.Group, StationAction.Remove })
        {
            var button = ActionIcon(action); expandedActions.Add(action, button); actions.Children.Add(button);
            if(action==StationAction.Group)AttachGroupButton(button,SelectedGroupItems);
            else button.Click += async (_, _) => await Run(() => ExpandedCommand(action));
        }
        scroll.Content = actions; Grid.SetColumn(scroll, 1); expandedActionHost.Children.Add(scroll);
        var calendar = new Grid(); var calendarIcon=(UIElement)timeFilterButton.Content;timeFilterButton.Content=null;calendar.Children.Add(calendarIcon);calendar.Children.Add(timeFilterBadge);timeFilterButton.Content=calendar;
        var menu = new MenuFlyout(); timeFilterButton.Flyout = menu;
        menu.Opening += (_, _) => PopulateTimeMenu(menu.Items);
        InitializeLeadingActions();
    }
    void PopulateTimeMenu(IList<MenuFlyoutItemBase> entries)
    {
        entries.Clear();
        for (int i = 0; i < StationQuery.PeriodNames.Length; i++)
        {
            int index = i;
            var item = new ToggleMenuFlyoutItem { Text = StationQuery.PeriodNames[i], IsChecked = (int)expandedQuery.Period == i };
            item.Click += (_, _) => periodFilter.SelectedIndex = index; entries.Add(item);
        }
    }
    void UpdateExpandedHeader()
    {
        if (createWorkspaceButton == null) return;
        if (expandedHeaderMode != Detached)
        {
            workspaceTools.Children.Remove(createWorkspaceButton); workspaceTools.Children.Remove(importWorkspaceButton);
            if(workspaceDrag!=null){workspaceTools.Children.Remove(workspaceDrag);stationLeading.Children.Remove(workspaceDrag);}
            expandedCommands.Children.Clear();
            if (Detached)
            {
                expandedCommands.Children.Add(timeFilterButton); expandedCommands.Children.Add(createWorkspaceButton); expandedCommands.Children.Add(importWorkspaceButton);
                if(workspaceDrag!=null){stationLeading.Children.Add(workspaceDrag);stationLeading.Children.Add(workspacePageButton);}
            }
            else {stationLeading.Children.Remove(workspacePageButton);workspaceTools.Children.Insert(0, createWorkspaceButton); workspaceTools.Children.Insert(1, importWorkspaceButton);if(workspaceDrag!=null)workspaceTools.Children.Add(workspaceDrag);}
            expandedHeaderMode = Detached;
        }
        workspaceGroupRow.Visibility = Detached && categories.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        workspacePageButton.Visibility=Detached&&Scene()!=null?Visibility.Visible:Visibility.Collapsed;
        workspacePageButton.Content=new[]{"资料 ▾","待办 ▾","笔记 ▾"}[workspacePage];
        UpdateStationHeader();
    }
    void UpdateExpandedActions()
    {
        if (expandedActions.Count == 0) return;
        expandedActionHost.Visibility = Detached && workspacePage == 0 ? Visibility.Visible : Visibility.Collapsed;
        expandedSelectionCount.Visibility=selection.Count>0?Visibility.Visible:Visibility.Collapsed;
        var selected = CurrentItems().Where(i => selection.Contains(i.Id)).ToArray();
        expandedSelectionCount.Text = $"已选 {selected.Length} 项";
        foreach (var (action, button) in expandedActions)
        {
            bool single = selected.Length == 1;
            var effective = action == StationAction.Path && single && selected[0].Kind is StationKind.Text or StationKind.Link ? StationAction.Edit : action;
            string label = action == StationAction.Path ? effective == StationAction.Edit ? "编辑内容／显示名称" : "查看路径" : StationActions.Label(action);
            SetStationIconForDetail(button, action, effective); AutomationProperties.SetName(button, label); ToolTipService.SetToolTip(button, label);
            button.IsEnabled = !busy && !ModalActive && selected.Length > 0
                && (action is StationAction.Copy or StationAction.Group or StationAction.Remove || single)
                && (action != StationAction.Folder || single && selected[0].Kind is StationKind.File or StationKind.Folder)
                && (!(effective is StationAction.Edit or StationAction.Group or StationAction.Remove) || !store.ReadOnly && !App.WorkStore.ReadOnly);
        }
        string period = StationQuery.PeriodNames[(int)expandedQuery.Period];
        AutomationProperties.SetName(timeFilterButton, "时间筛选：" + period); ToolTipService.SetToolTip(timeFilterButton, "时间筛选：" + period);
        timeFilterBadge.Visibility=expandedQuery.Period==StationPeriod.All?Visibility.Collapsed:Visibility.Visible;
        timeFilterButton.IsEnabled=!busy;
        if(Detached)UpdateStationHeader();
    }
    static void SetStationIconForDetail(Button button, StationAction action, StationAction effective)
    { if (action == StationAction.Path) SetStationIcon(button, effective == StationAction.Edit ? "\uE70F" : "\uE946"); }
    async Task ExpandedCommand(StationAction action)
    {
        var selected = CurrentItems().Where(i => selection.Contains(i.Id)).ToArray();
        if (selected.Length == 0) return;
        if (action == StationAction.Copy)
        { var data = new DataPackage(); await StationTransfer.FillMany(data, selected, store.AttachmentDirectory); Clipboard.SetContent(data); Clipboard.Flush(); status.Text = $"已复制 {selected.Length} 项"; }
        else if (action == StationAction.Group) await ChooseWorkspace(selected);
        else if (action == StationAction.Remove) await RemoveSelection();
        else if (selected.Length == 1)
        {
            var item = selected[0];
            if (action == StationAction.Folder && item.Kind is not (StationKind.File or StationKind.Folder)) return;
            await ItemCommand(item, action == StationAction.Path && item.Kind is StationKind.Text or StationKind.Link ? StationAction.Edit : action);
        }
    }
}
