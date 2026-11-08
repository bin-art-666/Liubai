using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Liubai;

internal sealed partial class SettingsWindow
{
    StackPanel BuildStationActions()
    {
        var panel = new StackPanel { Spacing = 8 };
        var layout = new ComboBox { Header = "项目操作布局", HorizontalAlignment = HorizontalAlignment.Stretch, IsEnabled = !App.Station.ReadOnly };
        layout.Items.Add("精简模式 · 常用操作＋更多"); layout.Items.Add("六宫格 · 两行三列"); layout.SelectedIndex = App.Station.Actions.Grid ? 1 : 0;
        panel.Children.Add(layout);
        var choices = new StackPanel { Spacing = 8 }; panel.Children.Add(choices);
        var preview = new StackPanel { Spacing=6 }; panel.Children.Add(preview);
        void Render()
        {
            choices.Children.Clear();
            foreach (var kind in new[] { StationKind.File, StationKind.Link, StationKind.Text })
            {
                var values = App.Station.Actions.Visible(kind);
                var section = new Expander { Header = kind == StationKind.File ? "文件／图片" : kind == StationKind.Link ? "链接" : "文字", HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
                var list = new ListView { CanDragItems = true, CanReorderItems = true, AllowDrop = true, SelectionMode = ListViewSelectionMode.None, IsEnabled = !App.Station.Actions.Grid && !App.Station.ReadOnly }; section.Content = list;
                var ordered = values.Concat(StationActions.Available(kind).Except(values)).ToArray();
                foreach (var action in ordered)
                {
                    var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Tag = action };
                    var check = new CheckBox { Content = StationActions.Label(action), IsChecked = values.Contains(action), MinWidth = 130, IsEnabled = !App.Station.Actions.Grid && !App.Station.ReadOnly };
                    check.Click += async (_, _) => {
                        var current = App.Station.Actions.Visible(kind).ToList();
                        if (check.IsChecked == true && !current.Contains(action)) current.Add(action); else if (check.IsChecked != true) current.Remove(action);
                        if (current.Count > 3) { status.Text = "最多常驻三个操作，其余始终可在更多菜单找到"; Render(); return; }
                        await Save(kind, current.ToArray());
                    };
                    row.Children.Add(check);
                    foreach (int direction in new[] { -1, 1 })
                    {
                        var move = PanelTheme.Icon(direction < 0 ? "\uE70E" : "\uE70D", direction < 0 ? "前移" : "后移");
                        int index = Array.IndexOf(values, action); move.IsEnabled = check.IsEnabled && index >= 0 && index + direction >= 0 && index + direction < values.Length;
                        move.Click += async (_, _) => { var next = values.ToArray(); (next[index], next[index + direction]) = (next[index + direction], next[index]); await Save(kind, next); };
                        row.Children.Add(move);
                    }
                    list.Items.Add(row);
                }
                list.DragItemsCompleted += async (_,_) => { var enabled = App.Station.Actions.Visible(kind); var order = list.Items.Cast<StackPanel>().Select(row=>(StationAction)row.Tag).Where(enabled.Contains).ToArray(); await Save(kind,order); };
                choices.Children.Add(section);
            }
            preview.Children.Clear(); preview.Children.Add(PanelTheme.Hint("布局预览"));
            var body=new Grid {ColumnSpacing=8,IsHitTestVisible=false};body.ColumnDefinitions.Add(new() {Width=new GridLength(1,GridUnitType.Star)});body.ColumnDefinitions.Add(new() {Width=GridLength.Auto});
            body.Children.Add(new TextBlock {Text="论文修改.docx",TextWrapping=TextWrapping.Wrap,MaxLines=2,VerticalAlignment=VerticalAlignment.Center});
            var buttons=new Grid {ColumnSpacing=4,RowSpacing=5}; var shown=App.Station.Actions.Visible(StationKind.File);
            for(int i=0;i<(App.Station.Actions.Grid?3:shown.Length+1);i++) buttons.ColumnDefinitions.Add(new() {Width=GridLength.Auto});
            if(App.Station.Actions.Grid) {buttons.RowDefinitions.Add(new() {Height=GridLength.Auto});buttons.RowDefinitions.Add(new() {Height=GridLength.Auto});}
            for(int i=0;i<shown.Length;i++) {var button=StationWindow.ActionIcon(shown[i]);Grid.SetColumn(button,App.Station.Actions.Grid?i%3:i);Grid.SetRow(button,App.Station.Actions.Grid?i/3:0);buttons.Children.Add(button);}
            if(!App.Station.Actions.Grid) {var more=PanelTheme.Icon("\uE712","更多操作");more.Style=PanelTheme.Style("ItemAction");Grid.SetColumn(more,shown.Length);buttons.Children.Add(more);}
            Grid.SetColumn(buttons,1);body.Children.Add(buttons);preview.Children.Add(PanelTheme.Card(body));
        }
        async Task Save(StationKind kind, StationAction[] values)
        {
            var next = App.Station.Actions;
            next = kind == StationKind.File ? next with { Files = values } : kind == StationKind.Link ? next with { Links = values } : next with { Text = values };
            try { await App.Station.UpdateActions(next); status.Text = "项目操作已保存"; } catch { status.Text = "项目操作保存失败，请重试"; }
            Render();
        }
        layout.SelectionChanged += async (_, _) => { try { await App.Station.UpdateActions(App.Station.Actions with { Grid = layout.SelectedIndex == 1 }); Render(); } catch { status.Text = "布局保存失败"; } };
        var reset = new Button { Content = "恢复默认项目操作", IsEnabled = !App.Station.ReadOnly };
        reset.Click += async (_, _) => { try { await App.Station.UpdateActions(new()); layout.SelectedIndex = 0; Render(); } catch { status.Text = "恢复失败"; } }; panel.Children.Add(reset);
        Render(); return panel;
    }
}
