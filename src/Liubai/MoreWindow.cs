using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace Liubai;

// The actual tools move temporarily into this panel, preserving menus and drop handlers.
internal sealed class MoreWindow : UtilityPanel
{
    sealed record Item(Button Button, object Content, double Width, double Height, double MinWidth, double MinHeight,
        Thickness Padding, Thickness Margin, HorizontalAlignment Alignment, HorizontalAlignment ContentAlignment, CornerRadius Radius, FlyoutBase? Menu, ToolTip? Tip);
    readonly List<Item> items = [];
    readonly Action<Button> returned;
    readonly Action released;
    readonly Func<Button, FlyoutBase?> createMenu;
    readonly Dictionary<Button, FlyoutBase> menus = new();
    internal bool PreviewPinned;
    protected override bool DismissOnOutside => !PreviewPinned;
    internal IEnumerable<Button> Buttons => items.Select(i => i.Button);
    internal MoreWindow(Action<Button> returned, Action released, Func<Button, FlyoutBase?> createMenu) : base("更多工具")
    {
        this.returned = returned; this.released = released; this.createMenu = createMenu;
        PanelToggleTargets.RegisterDynamic(this, () => Buttons);
        PanelTheme.Changed += Restyle;
        Closed += (_, _) => { Release(); PanelTheme.Changed -= Restyle; };
    }
    internal void Open(IEnumerable<(string Id, Button Button, bool Group)> tools, DesktopMonitor display, int top, bool activate = true)
    {
        Release();
        foreach (var (id, button, group) in tools)
        {
            if (button.Parent is Panel parent) parent.Children.Remove(button);
            var item = new Item(button, button.Content, button.Width, button.Height, button.MinWidth, button.MinHeight,
                button.Padding, button.Margin, button.HorizontalAlignment, button.HorizontalContentAlignment, button.CornerRadius, button.ContextFlyout, ToolTipService.GetToolTip(button) as ToolTip);
            items.Add(item); button.Content = null;
            item.Menu?.Hide();
            if (!menus.TryGetValue(button, out var menu) && createMenu(button) is { } created) { menu = created; menus.Add(button, menu); }
            button.ContextFlyout = menu;
            if (item.Tip is { } tip) { tip.IsOpen = false; ToolTipService.SetToolTip(button, new ToolTip { Content = tip.Content as string, PlacementTarget = button, MaxWidth = 340 }); }
            var row = new Grid { ColumnSpacing = 12 };
            row.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            if (item.Content is UIElement icon) row.Children.Add(icon);
            var label = new TextBlock { Text = ToolbarLayoutPolicy.Name(id), FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(label, 1); row.Children.Add(label); button.Content = row;
            button.RequestedTheme = ElementTheme.Default; button.ClearValue(Control.ForegroundProperty);
            if (item.Content is IconElement glyph) glyph.ClearValue(IconElement.ForegroundProperty);
            button.Margin = group && Body.Children.Count > 0 ? new Thickness(0, 8, 0, 0) : new Thickness(0);
            button.Visibility = Visibility.Visible; StyleButton(button); Body.Children.Add(button);
        }
        if (items.Count == 0) Body.Children.Add(Hint("工具目前都显示在顶栏。可以在“顶栏布局”中将不常用的入口收进这里。"));
        StatusLine.Text = "工具入口 · 不存放文件或记录";
        Present(display, top, Math.Min(700, 150 + items.Count * 58), activate);
    }
    internal bool Contains(Button button) => items.Any(i => ReferenceEquals(i.Button, button));
    internal void StyleButton(Button button)
    {
        button.Width = double.NaN; button.Height = double.NaN; button.MinWidth = 0; button.MinHeight = 44;
        button.Padding = new Thickness(12, 8, 12, 8); button.HorizontalAlignment = HorizontalAlignment.Stretch;
        button.HorizontalContentAlignment = HorizontalAlignment.Stretch; button.CornerRadius = PanelTheme.Value<CornerRadius>("ControlRadius");
    }
    void Restyle() { foreach (var item in items) StyleButton(item.Button); }
    void Release()
    {
        foreach (var item in items)
        {
            var button = item.Button;
            button.ContextFlyout?.Hide();
            if (ToolTipService.GetToolTip(button) is ToolTip tip) tip.IsOpen = false;
            Body.Children.Remove(button);
            if (button.Content is Grid row && item.Content is UIElement icon) row.Children.Remove(icon);
            button.Content = item.Content; button.Width = item.Width; button.Height = item.Height; button.MinWidth = item.MinWidth; button.MinHeight = item.MinHeight;
            button.Padding = item.Padding; button.Margin = item.Margin; button.HorizontalAlignment = item.Alignment;
            button.HorizontalContentAlignment = item.ContentAlignment; button.CornerRadius = item.Radius;
            returned(button);
            button.ContextFlyout = item.Menu;
            if (item.Tip != null) ToolTipService.SetToolTip(button, item.Tip);
        }
        items.Clear(); Body.Children.Clear(); released();
    }
    protected override void OnHiding() => Release();
}
