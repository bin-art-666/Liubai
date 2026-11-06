using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Liubai;

internal sealed partial class SettingsWindow
{
    internal event Action? MoreRequested;
    sealed record LayoutRow(Border Card, ComboBox Placement, CheckBox Group, Button Up, Button Down);
    readonly Dictionary<string, LayoutRow> layoutRows = new();
    StackPanel layoutList = null!;
    ComboBox layoutDensity = null!;
    Slider layoutSpacing = null!;
    TextBlock layoutStatus = null!;
    Microsoft.UI.Dispatching.DispatcherQueueTimer layoutSaveTimer = null!;
    bool layoutSync;
    Expander BuildToolbarLayoutSettings()
    {
        var body = new StackPanel { Spacing = 14 };
        body.Children.Add(Hint("安排工具入口的位置；隐藏或收进“更多”不会关闭功能，计时和提醒仍会继续。"));
        layoutDensity = Combo("顶栏图标间距", "紧凑", "标准", "宽松", "自定义"); body.Children.Add(layoutDensity);
        layoutSpacing = AddSlider(body, "图标间距", "仅调整顶栏，与面板的界面疏密独立。", 0, 16, " px", () =>
        { if (!layoutSync && interactive) PreviewToolbarLayout(App.ToolbarLayout.Current with { Spacing = (int)layoutSpacing.Value }); });
        body.Children.Add(Hint("顶栏按住 Ctrl 拖动排序；更多及隐藏项可用下方箭头排序。勾选“前方分组”会增加一段留白。"));
        layoutList = new StackPanel { Spacing = 8 }; body.Children.Add(layoutList);
        foreach (string id in ToolbarOrder.Defaults)
        {
            string key = id;
            var row = new StackPanel { Spacing = 8 };
            var arrows = PanelTheme.Actions();
            var up = PanelTheme.Icon("\uE70E", "上移" + ToolbarLayoutPolicy.Name(id));
            var down = PanelTheme.Icon("\uE70D", "下移" + ToolbarLayoutPolicy.Name(id)); arrows.Children.Add(up); arrows.Children.Add(down);
            row.Children.Add(PanelTheme.Header(new TextBlock { Text = ToolbarLayoutPolicy.Name(id), VerticalAlignment = VerticalAlignment.Center }, arrows));
            var placement = Combo("入口位置", "顶栏", "更多", "隐藏"); placement.IsEnabled = !ToolCatalog.BuiltIn[id].RequiredEntry; row.Children.Add(placement);
            var group = new CheckBox { Content = "前方分组" }; row.Children.Add(group);
            if (ToolCatalog.BuiltIn[id].RequiredEntry) row.Children.Add(Hint("设置入口始终保留；顶栏过低时可通过托盘打开。"));
            var card = PanelTheme.Card(row); layoutRows.Add(id, new(card, placement, group, up, down));
            placement.SelectionChanged += (_, _) =>
            {
                if (layoutSync || !interactive || placement.SelectedIndex < 0) return;
                var entry = App.ToolbarLayout.Current.Entry(key) with { Placement = (ToolbarPlacement)placement.SelectedIndex };
                PreviewToolbarLayout(App.ToolbarLayout.Current.WithEntry(entry));
            };
            group.Click += (_, _) => { if (!layoutSync && interactive) PreviewToolbarLayout(App.ToolbarLayout.Current.WithEntry(App.ToolbarLayout.Current.Entry(key) with { GroupStart = group.IsChecked == true })); };
            up.Click += (_, _) => MoveLayoutTool(key, -1); down.Click += (_, _) => MoveLayoutTool(key, 1);
        }
        var open = new Button { Content = "打开更多工具", HorizontalAlignment = HorizontalAlignment.Left }; open.Click += (_, _) => MoreRequested?.Invoke(); body.Children.Add(open);
        var reset = new Button { Content = "恢复默认布局", HorizontalAlignment = HorizontalAlignment.Left };
        reset.Click += (_, _) => ResetToolbarLayout(); body.Children.Add(reset);
        layoutStatus = Hint(App.ToolbarLayout.Warning ?? "调整后自动保存；不会移动或删除任何内容。"); body.Children.Add(layoutStatus);
        layoutSaveTimer = DispatcherQueue.CreateTimer(); layoutSaveTimer.IsRepeating = false; layoutSaveTimer.Interval = TimeSpan.FromMilliseconds(400);
        layoutSaveTimer.Tick += (_, _) => SaveToolbarLayout();
        layoutDensity.SelectionChanged += (_, _) =>
        { if (!layoutSync && interactive && layoutDensity.SelectedIndex is >= 0 and < 3) PreviewToolbarLayout(App.ToolbarLayout.Current with { Spacing = new[] { 0, 2, 8 }[layoutDensity.SelectedIndex] }); };
        App.ToolbarLayout.Changed += RefreshToolbarLayoutSettings; preferences.ToolbarOrderChanged += RefreshToolbarLayoutSettings;
        Closed += (_, _) => { layoutSaveTimer.Stop(); App.ToolbarLayout.Changed -= RefreshToolbarLayoutSettings; preferences.ToolbarOrderChanged -= RefreshToolbarLayoutSettings; };
        RefreshToolbarLayoutSettings();
        return new Expander { Header = "顶栏布局", Content = new ContentControl { Content = body, IsEnabled = !App.ToolbarLayout.ReadOnly, HorizontalContentAlignment = HorizontalAlignment.Stretch }, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    }
    void RefreshToolbarLayoutSettings()
    {
        layoutSync = true;
        try
        {
            var options = App.ToolbarLayout.Current; var order = preferences.ToolbarItems;
            layoutSpacing.Value = options.Spacing; layoutDensity.SelectedIndex = options.Spacing switch { 0 => 0, 2 => 1, 8 => 2, _ => 3 };
            for (int i = 0; i < order.Count; i++)
            {
                var row = layoutRows[order[i]]; var entry = options.Entry(order[i]);
                row.Placement.SelectedIndex = (int)entry.Placement; row.Group.IsChecked = entry.GroupStart;
                row.Up.IsEnabled = i > 0; row.Down.IsEnabled = i < order.Count - 1;
                if (!layoutList.Children.Contains(row.Card)) layoutList.Children.Add(row.Card);
                if (!ReferenceEquals(layoutList.Children[i], row.Card)) { layoutList.Children.Remove(row.Card); layoutList.Children.Insert(i, row.Card); }
            }
        }
        finally { layoutSync = false; }
    }
    void PreviewToolbarLayout(ToolbarLayoutOptions options)
    {
        if (App.ToolbarLayout.ReadOnly) return;
        App.ToolbarLayout.Preview(options); layoutStatus.Text = "正在预览，稍后自动保存…"; layoutSaveTimer.Stop(); layoutSaveTimer.Start();
    }
    bool SaveToolbarLayout()
    {
        layoutSaveTimer.Stop(); if (App.ToolbarLayout.ReadOnly) return true;
        try { if (App.ToolbarLayout.Data != App.ToolbarLayout.Current) App.ToolbarLayout.Save(App.ToolbarLayout.Current); layoutStatus.Text = $"顶栏布局已保存 · {DateTime.Now:HH:mm:ss}"; return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { layoutStatus.Text = "布局已预览，但保存失败；请重试或检查目录权限。"; return false; }
    }
    void MoveLayoutTool(string id, int delta)
    {
        var order = preferences.ToolbarItems.ToArray(); int index = Array.IndexOf(order, id);
        int target = index + delta; if (target < 0 || target >= order.Length || App.ToolbarLayout.ReadOnly) return;
        try { preferences.SaveToolbarOrder(ToolbarOrder.Move(order, id, target)); layoutStatus.Text = "排序已保存"; }
        catch { layoutStatus.Text = "排序保存失败，原顺序已保留。"; }
    }
    void ResetToolbarLayout()
    {
        PreviewToolbarLayout(new()); if (!SaveToolbarLayout()) return;
        try { preferences.SaveToolbarOrder(ToolbarOrder.Defaults); } catch { layoutStatus.Text = "入口布局已恢复，但排序保存失败，请重试。"; }
    }
}
