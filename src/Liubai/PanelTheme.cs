using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using System.Runtime.CompilerServices;

namespace Liubai;

// One resource vocabulary for native feature panels. Top-bar geometry is independent.
internal static class PanelTheme
{
    sealed record Target(WeakReference<FrameworkElement> Element, Action<FrameworkElement> Update);
    static readonly List<Target> targets = [];
    static readonly List<WeakReference<PanelWindow>> panels = [];
    static readonly ConditionalWeakTable<Control, object> automaticCorners = new();
    internal static UiStyleOptions Current { get; private set; } = new();
    internal static event Action? Changed;
    internal static T Track<T>(T element, Action<T> update) where T : FrameworkElement
    {
        if (targets.Count % 128 == 0) targets.RemoveAll(t => !t.Element.TryGetTarget(out _));
        targets.Add(new(new(element), e => update((T)e))); update(element); return element;
    }
    internal static void Register(PanelWindow panel)
    {
        panels.RemoveAll(p => !p.TryGetTarget(out _)); panels.Add(new(panel));
        if (panel.Content is FrameworkElement content) content.Loaded += (_, _) => Refresh(panel);
        Refresh(panel);
    }
    internal static void Unregister(PanelWindow panel) => panels.RemoveAll(p => !p.TryGetTarget(out var current) || ReferenceEquals(current, panel));
    internal static void Refresh(PanelWindow panel)
    {
        if (panel.SystemBackdrop is GlassBackdrop glass) glass.Update(Material());
        if (panel.Content is DependencyObject root) Controls(root, new HashSet<DependencyObject>());
    }
    static void Controls(DependencyObject node, HashSet<DependencyObject> visited)
    {
        if (!visited.Add(node) || node is WebView2) return;
        if (node is Control control && (automaticCorners.TryGetValue(control, out _) || control.ReadLocalValue(Control.CornerRadiusProperty) == DependencyProperty.UnsetValue))
        {
            automaticCorners.GetValue(control, _ => new object());
            control.CornerRadius = Value<CornerRadius>(control is Expander ? "CardRadius" : "ControlRadius");
        }
        if (node is Panel layout) foreach (var child in layout.Children) Controls(child, visited);
        if (node is Border border && border.Child != null) Controls(border.Child, visited);
        if (node is ContentControl host && host.Content is DependencyObject content) Controls(content, visited);
        if (node is Expander expander && expander.Content is DependencyObject expanded) Controls(expanded, visited);
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) Controls(VisualTreeHelper.GetChild(node, i), visited);
    }
    internal static void Apply(UiStyleOptions options)
    {
        UiStyleOptions.Validate(options); Current = options;
        double density = options.Density switch { UiDensity.Compact => .85, UiDensity.Spacious => 1.15, _ => 1 };
        var resources = Application.Current.Resources;
        void Set(string key, object value) => resources["Liubai" + key] = value;
        Set("PanelPadding", new Thickness(Math.Round(20 * density), Math.Round(16 * density), Math.Round(20 * density), Math.Round(16 * density)));
        Set("CardPadding", new Thickness(Math.Round(12 * density)));
        Set("PanelGap", Math.Round(12 * density)); Set("ActionGap", 4d);
        Set("ControlRadius", new CornerRadius(options.Roundness));
        Set("CardRadius", new CornerRadius(options.Roundness * 1.5)); Set("PanelRadius", new CornerRadius(options.Roundness * 2));
        Set("IconSize", options.Icons switch { UiIconSize.Small => 16d, UiIconSize.Large => 24d, _ => 20d });
        Set("ActionSize", options.Icons == UiIconSize.Large ? 40d : 36d);
        Set("PanelTintOpacity", options.Tint / 100d); Set("PanelLuminosityOpacity", options.Shade / 100d);
        Set("EnterMilliseconds", (double)options.Duration);
        targets.RemoveAll(t => !t.Element.TryGetTarget(out _));
        foreach (var target in targets.ToArray()) if (target.Element.TryGetTarget(out var element)) target.Update(element);
        panels.RemoveAll(p => !p.TryGetTarget(out _));
        foreach (var reference in panels.ToArray()) if (reference.TryGetTarget(out var panel)) Refresh(panel);
        Changed?.Invoke();
    }
    internal static T Value<T>(string key) => (T)Application.Current.Resources["Liubai" + key];
    internal static Style Style(string name) => Value<Style>(name + "Style");
    static MaterialOptions Material() => new(TintOpacity: (float)Value<double>("PanelTintOpacity"), LuminosityOpacity: (float)Value<double>("PanelLuminosityOpacity"));
    internal static GlassBackdrop Backdrop() => new(Material());
    internal static Grid Layout() => Track(new Grid { Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) }, static grid =>
    { grid.Padding = Value<Thickness>("PanelPadding"); grid.RowSpacing = Value<double>("PanelGap"); grid.CornerRadius = Value<CornerRadius>("PanelRadius"); });
    internal static Grid Header(UIElement title, FrameworkElement actions)
    {
        var header = Track(new Grid(), static grid => grid.ColumnSpacing = Value<double>("PanelGap"));
        header.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        header.Children.Add(title); Grid.SetColumn(actions, 1); header.Children.Add(actions); return header;
    }
    internal static StackPanel Actions() => Track(new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center }, static panel => panel.Spacing = Value<double>("ActionGap"));
    internal static TextBlock Title(string text) => new() { Text = text, FontSize = Value<double>("TitleSize"), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    internal static TextBlock Hint(string text) => new() { Text = text, FontSize = Value<double>("HintSize"), Opacity = .7, TextWrapping = TextWrapping.Wrap };
    internal static FontIcon Glyph(string glyph, double? size = null)
    {
        var icon = new FontIcon { Glyph = glyph, FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = size ?? Value<double>("IconSize") };
        return size != null ? icon : Track(icon, static value => value.FontSize = Value<double>("IconSize"));
    }
    internal static void IconStyle(ButtonBase button, string name)
    {
        button.Style = Style(button is ToggleButton ? "IconToggle" : "IconButton");
        Track(button, static value => { value.Width = value.Height = Value<double>("ActionSize"); value.CornerRadius = Value<CornerRadius>("ControlRadius"); });
        AutomationProperties.SetName(button, name); ToolTipService.SetToolTip(button, name);
    }
    internal static Button Icon(string glyph, string name, double? size = null)
    {
        var button = new Button { Content = Glyph(glyph, size) }; IconStyle(button, name); return button;
    }
    // Framed actions share native keyboard, pointer and checked states.
    internal static void FramedIconStyle(ButtonBase button, string name)
    {
        IconStyle(button,name);
        button.Style=Style(button is ToggleButton ? "FramedToggle" : "ItemAction");
        Track(button, static value => value.CornerRadius=new CornerRadius(Value<CornerRadius>("ControlRadius").TopLeft*1.5));
    }
    internal static Border Card(UIElement content) => Track(new Border { Child = content, Style = Style("Card") }, static card =>
    { card.CornerRadius = Value<CornerRadius>("CardRadius"); card.Padding = Value<Thickness>("CardPadding"); if (card.Child != null) Controls(card.Child, new HashSet<DependencyObject>()); });
}
