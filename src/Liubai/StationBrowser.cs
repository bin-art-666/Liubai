using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Liubai;

internal sealed partial class StationWindow
{
    StationQuery expandedQuery = new();
    readonly Grid browserBody = new() { ColumnSpacing = 14 };
    readonly ListView categories = new() { SelectionMode = ListViewSelectionMode.Single, Background = null };
    readonly StackPanel browseTools = new() { Spacing = 8 };
    readonly ComboBox compactCategory = new() { Width = 140 }, periodFilter = new() { MaxWidth = 180, HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly TextBox search = new() { PlaceholderText = "搜索文件名、文字或链接", MaxLength = 200 };
    readonly Button viewAllAfterDrop = new() { Content = "查看全部", Visibility = Visibility.Collapsed };
    Microsoft.UI.Dispatching.DispatcherQueueTimer searchTimer = null!, dayTimer = null!;
    DateTime renderedDay;
    bool querySync;
    ScrollViewer browserScroll = null!;
    sealed record CategoryChoice(StationCategory Value, string Label) { public override string ToString() => Label; }
    StationQuery ActiveQuery => Detached ? expandedQuery : new();
    StationItem[] CurrentItems() => ActiveQuery.Apply(WorkspaceItems(), DateTimeOffset.Now, TimeZoneInfo.Local);
    void InitializeBrowser(ScrollViewer scroll)
    {
        expandedQuery = panelLayout.Data.View ?? new();
        browserScroll = scroll;
        search.Text = expandedQuery.Search;
        AutomationProperties.SetName(search, "搜索中转站"); AutomationProperties.SetName(categories, "中转站分类");
        AutomationProperties.SetName(compactCategory, "中转站分类筛选"); AutomationProperties.SetName(periodFilter, "中转站时间筛选");
        foreach (string name in StationQuery.PeriodNames) periodFilter.Items.Add(name);
        periodFilter.SelectedIndex = (int)expandedQuery.Period;
        browseTools.Children.Add(search);
        var filters = new Grid { ColumnSpacing = 8 }; filters.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); filters.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        filters.Children.Add(compactCategory); Grid.SetColumn(periodFilter, 1); filters.Children.Add(periodFilter); browseTools.Children.Add(filters);
        var main = new Grid { RowSpacing = 10 }; main.RowDefinitions.Add(new() { Height = GridLength.Auto }); main.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        main.Children.Add(browseTools); Grid.SetRow(scroll, 1); main.Children.Add(scroll);
        browserBody.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); browserBody.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        categories.Width = 150; categories.Margin = new Thickness(0, 0, 0, 0); browserBody.Children.Add(categories); Grid.SetColumn(main, 1); browserBody.Children.Add(main);
        Grid.SetRow(browserBody, 2); root.Children.Add(browserBody);
        browserBody.SizeChanged += (_, _) => UpdateBrowserMode();
        searchTimer = DispatcherQueue.CreateTimer(); searchTimer.IsRepeating = false; searchTimer.Interval = TimeSpan.FromMilliseconds(250);
        searchTimer.Tick += (_, _) => CommitQuery();
        search.TextChanged += (_, _) => { if (querySync) return; expandedQuery = expandedQuery with { Search = search.Text }; searchTimer.Stop(); searchTimer.Start(); };
        categories.SelectionChanged += async (_, _) => { if (!querySync && categories.SelectedItem is ListViewItem { Tag: WorkspaceChoice group }) { await Run(async () => { workspaceId=group.Id; ungrouped=group.Ungrouped; workspacePage=0; expandedQuery=expandedQuery with {Category=StationCategory.All}; ClearSelection(); await App.Work.Store.Activate(group.Id); Refresh(); }); return; } if (!querySync && categories.SelectedItem is ListViewItem { Tag: StationCategory value }) { expandedQuery = expandedQuery with { Category = value }; CommitQuery(); } };
        compactCategory.SelectionChanged += (_, _) => { if (!querySync && compactCategory.SelectedItem is CategoryChoice value) { expandedQuery = expandedQuery with { Category = value.Value }; CommitQuery(); } };
        periodFilter.SelectionChanged += (_, _) => { if (!querySync && periodFilter.SelectedIndex >= 0) { expandedQuery = expandedQuery with { Period = (StationPeriod)periodFilter.SelectedIndex }; CommitQuery(); } };
        viewAllAfterDrop.Click += (_, _) => ShowAllItems();
        dayTimer = DispatcherQueue.CreateTimer(); dayTimer.Interval = TimeSpan.FromMinutes(1);
        dayTimer.Tick += (_, _) => { if (IsOpen && renderedDay != DateTime.Now.Date) Refresh(); };
        Closed += (_, _) => { searchTimer.Stop(); dayTimer.Stop(); };
        UpdateBrowserMode();
    }
    void UpdateBrowserMode()
    {
        bool wide = Detached && browserBody.ActualWidth >= 600;
        categories.Visibility = wide ? Visibility.Visible : Visibility.Collapsed;
        categories.Margin = wide ? new Thickness(0, 0, 0, 0) : new Thickness(0);
        browserBody.ColumnSpacing = wide ? 14 : 0;
        compactCategory.Visibility = Detached && !wide ? Visibility.Visible : Visibility.Collapsed;
        browseTools.Visibility = Detached ? Visibility.Visible : Visibility.Collapsed;
        workspaceLabelHost.Visibility=wide?Visibility.Collapsed:Visibility.Visible;
        if(workspaceDrag!=null)workspaceDrag.Visibility=Detached&&Scene()!=null?Visibility.Visible:Visibility.Collapsed;
    }
    void SaveQuery()
    {
        searchTimer.Stop(); if (panelLayout.ReadOnly) return;
        try { panelLayout.Save(panelLayout.Data with { View = expandedQuery }); dockLayout = dockLayout with { View = expandedQuery }; }
        catch { status.Text = "筛选已应用，但记忆保存失败，请重试。"; }
    }
    void ShowAllItems()
    {
        expandedQuery = new(); querySync = true; search.Text = ""; periodFilter.SelectedIndex = 0; querySync = false; viewAllAfterDrop.Visibility = Visibility.Collapsed; CommitQuery();
    }
    void CommitQuery() { ClearSelection(); SaveQuery(); visibleItems = 50; if (IsOpen) { Refresh(); browserScroll.ChangeView(null, 0, null, true); } }
    void RefreshCategories(DateTimeOffset now)
    {
        querySync = true;
        try
        {
            categories.Items.Clear(); compactCategory.Items.Clear();
            string[] glyphs = ["\uE71D", "\uE91B", "\uE8A5", "\uE8B7", "\uE8D2", "\uE71B"];
            var matches = store.Items.Where(i => expandedQuery.Matches(i, now, TimeZoneInfo.Local, category: false)).ToArray();
            foreach (StationCategory category in Enum.GetValues<StationCategory>())
            {
                int total = category == StationCategory.All ? matches.Length : matches.Count(i => StationQuery.Classify(i) == category);
                string label = StationQuery.CategoryNames[(int)category] + "（" + total + "）";
                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                row.Children.Add(new FontIcon { Glyph = glyphs[(int)category], FontSize = 15, FontFamily = new FontFamily("Segoe Fluent Icons") }); row.Children.Add(new TextBlock { Text = label, FontSize = 13 });
                var item = new ListViewItem { Tag = category, Content = row, Padding = new Thickness(8, 10, 6, 10) }; AutomationProperties.SetName(item, label); categories.Items.Add(item);
                var choice = new CategoryChoice(category, label); compactCategory.Items.Add(choice);
                if (category == expandedQuery.Category) { categories.SelectedItem = item; compactCategory.SelectedItem = choice; }
            }
            categories.Items.Add(new ListViewItem { Content = "我的分组", IsEnabled = false });
            foreach (var choice in new[] { new WorkspaceChoice(null,"未分组",true) }.Concat(App.WorkStore.Data.Scenes.OrderByDescending(s=>s.Pinned).Select(s=>new WorkspaceChoice(s.Id,(s.Pinned?"置顶 · ":"")+s.Name))))
            {
                var row = new ListViewItem { Tag=choice, Content=new TextBlock { Text=choice.Label,TextTrimming=TextTrimming.CharacterEllipsis }, AllowDrop=choice.Id!=null };
                row.ContextFlyout=WorkspaceMenu(choice);
                row.DragEnter+=async (_,_)=> {if(choice.Id is {} id) {row.BorderBrush=new SolidColorBrush(Microsoft.UI.Colors.DodgerBlue);row.BorderThickness=new Thickness(1);await HoverWorkspace(id);}};
                row.DragLeave+=(_,_)=> {workspaceHoverVersion++;row.ClearValue(Control.BorderBrushProperty);row.ClearValue(Control.BorderThicknessProperty);};
                row.Unloaded+=(_,_)=>workspaceHoverVersion++;
                row.DragOver += (_,e) => { e.Handled=true; e.AcceptedOperation=!busy&&StationTransfer.Supported(e.DataView)?Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy:Windows.ApplicationModel.DataTransfer.DataPackageOperation.None; };
                row.Drop += async (_,e) => { e.Handled=true; var deferral=e.GetDeferral(); try { if(choice.Id is {} id && await ReceiveForWorkspace(e.DataView,id)) e.AcceptedOperation=Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy; } finally { deferral.Complete(); } };
                categories.Items.Add(row);
                if(workspaceId==choice.Id&&ungrouped==choice.Ungrouped&&expandedQuery.Category==StationCategory.All) categories.SelectedItem=row;
            }
        }
        finally { querySync = false; }
    }
}
