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
    readonly TextBlock introHint = Hint("拖入文件、文字或链接，切换窗口后再取走。");
    Microsoft.UI.Dispatching.DispatcherQueueTimer searchTimer = null!, dayTimer = null!;
    DateTime renderedDay;
    bool querySync;
    ScrollViewer browserScroll = null!;
    sealed record CategoryChoice(StationCategory Value, string Label) { public override string ToString() => Label; }
    StationQuery ActiveQuery => Detached ? expandedQuery : new();
    StationItem[] CurrentItems() => ActiveQuery.Apply(store.Items, DateTimeOffset.Now, TimeZoneInfo.Local);
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
        categories.SelectionChanged += (_, _) => { if (!querySync && categories.SelectedItem is ListViewItem { Tag: StationCategory value }) { expandedQuery = expandedQuery with { Category = value }; CommitQuery(); } };
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
        introHint.Visibility = Detached ? Visibility.Collapsed : Visibility.Visible;
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
    void CommitQuery() { SaveQuery(); visibleItems = 50; if (IsOpen) { Refresh(); browserScroll.ChangeView(null, 0, null, true); } }
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
        }
        finally { querySync = false; }
    }
}
