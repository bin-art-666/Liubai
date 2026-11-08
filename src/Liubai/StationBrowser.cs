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
    readonly Button viewAllAfterDrop = new() { Content = "查看全部", Visibility = Visibility.Collapsed };
    Microsoft.UI.Dispatching.DispatcherQueueTimer dayTimer = null!;
    DateTime renderedDay;
    bool querySync;
    ScrollViewer browserScroll = null!;
    sealed record CategoryChoice(StationCategory Value, string Label) { public override string ToString() => Label; }
    StationQuery ActiveQuery => Detached ? expandedQuery : new();
    StationItem[] CurrentItems() => ActiveQuery.Apply(WorkspaceItems(), DateTimeOffset.Now, TimeZoneInfo.Local);
    void InitializeBrowser(ScrollViewer scroll)
    {
        expandedQuery = (panelLayout.Data.View ?? new()) with { Search = "" };
        browserScroll = scroll;
        AutomationProperties.SetName(categories, "中转站分类");
        AutomationProperties.SetName(compactCategory, "中转站分类筛选"); AutomationProperties.SetName(periodFilter, "中转站时间筛选");
        foreach (string name in StationQuery.PeriodNames) periodFilter.Items.Add(name);
        periodFilter.SelectedIndex = (int)expandedQuery.Period;
        var filters = new Grid { ColumnSpacing = 8 }; filters.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); filters.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        filters.Children.Add(compactCategory); browseTools.Children.Add(filters);
        InitializeExpandedToolbar();
        var main = new Grid { RowSpacing = 10 }; main.RowDefinitions.Add(new() { Height = GridLength.Auto }); main.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        main.Children.Add(browseTools); Grid.SetRow(scroll, 1); main.Children.Add(scroll);
        browserBody.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); browserBody.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        categories.Width = 150; categories.Margin = new Thickness(0, 0, 0, 0); browserBody.Children.Add(categories); Grid.SetColumn(main, 1); browserBody.Children.Add(main);
        Grid.SetRow(browserBody, 2); root.Children.Add(browserBody);
        browserBody.SizeChanged += (_, _) => UpdateBrowserMode();
        categories.SelectionChanged += async (_, _) => {
            if(querySync)return;
            if(categories.SelectedItem is ListViewItem {Tag:WorkspaceChoice group})await Run(()=>SelectWorkspace(group));
            else if(categories.SelectedItem is ListViewItem {Tag:StationCategory value})await Run(()=>SelectCategory(value));
        };
        compactCategory.PlaceholderText="选择分类";
        compactCategory.SelectionChanged += async (_, _) => { if (!querySync && compactCategory.SelectedItem is CategoryChoice value) await Run(()=>SelectCategory(value.Value)); };
        periodFilter.SelectionChanged += (_, _) => { if (!querySync && periodFilter.SelectedIndex >= 0) { expandedQuery = expandedQuery with { Period = (StationPeriod)periodFilter.SelectedIndex }; CommitQuery(); } };
        viewAllAfterDrop.Click += async (_, _) => await Run(ShowAllItems);
        dayTimer = DispatcherQueue.CreateTimer(); dayTimer.Interval = TimeSpan.FromMinutes(1);
        dayTimer.Tick += (_, _) => { if (IsOpen && renderedDay != DateTime.Now.Date) Refresh(); };
        Closed += (_, _) => dayTimer.Stop();
        UpdateBrowserMode();
    }
    void UpdateBrowserMode()
    {
        bool wide = Detached && browserBody.ActualWidth >= 600;
        categories.Visibility = wide ? Visibility.Visible : Visibility.Collapsed;
        categories.Margin = wide ? new Thickness(0, 0, 0, 0) : new Thickness(0);
        browserBody.ColumnSpacing = wide ? 14 : 0;
        compactCategory.Visibility = Detached && !wide ? Visibility.Visible : Visibility.Collapsed;
        browseTools.Visibility = Detached&&!wide ? Visibility.Visible : Visibility.Collapsed;
        UpdateExpandedHeader(); UpdateExpandedActions();
        workspaceLabelHost.Visibility=wide?Visibility.Collapsed:Visibility.Visible;
        if(workspaceDrag!=null)workspaceDrag.Visibility=Detached&&Scene()!=null&&workspacePage==0?Visibility.Visible:Visibility.Collapsed;
        if(Detached)workspaceTabs.Visibility=Visibility.Collapsed;
        UpdateStationHeader();
    }
    void SaveQuery()
    {
        if (panelLayout.ReadOnly) return;
        try { panelLayout.Save(panelLayout.Data with { View = expandedQuery }); dockLayout = dockLayout with { View = expandedQuery }; }
        catch { status.Text = "筛选已应用，但记忆保存失败，请重试。"; }
    }
    Task ShowAllItems()=>SelectCategory(StationCategory.All,true);
    async Task SelectCategory(StationCategory category,bool resetFilters=false)
    {
        workspaceHoverVersion++;workspaceId=null;ungrouped=false;workspacePage=0;
        expandedQuery=resetFilters?new():expandedQuery with {Category=category,Search=""};
        workspaceSync=true;querySync=true;
        try {workspaceTabs.SelectedIndex=0;periodFilter.SelectedIndex=(int)expandedQuery.Period;}
        finally {workspaceSync=false;querySync=false;}
        ClearSelection();viewAllAfterDrop.Visibility=Visibility.Collapsed;
        try {await App.WorkStore.Activate(null);}
        finally {CommitQuery();}
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
                if (category == expandedQuery.Category && workspaceId==null && !ungrouped) { categories.SelectedItem = item; compactCategory.SelectedItem = choice; }
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
