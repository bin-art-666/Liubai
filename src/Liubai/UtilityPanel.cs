using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Liubai;

internal abstract class UtilityPanel : PanelWindow
{
    protected readonly Grid Layout = new() { Padding = new Thickness(20, 16, 20, 16), RowSpacing = 12, Background = new SolidColorBrush(Colors.Transparent) };
    protected readonly StackPanel Body = new() { Spacing = 12 };
    protected readonly StackPanel HeaderActions = new() { Orientation = Orientation.Horizontal, Spacing = 4 };
    protected readonly TextBlock StatusLine = Hint("");
    protected UtilityPanel(string title)
    {
        Title = "留白 · " + title;
        var presenter = (OverlappedPresenter)AppWindow.Presenter; presenter.SetBorderAndTitleBar(false, false);
        presenter.IsResizable = presenter.IsMaximizable = presenter.IsMinimizable = false; presenter.IsAlwaysOnTop = true;
        var handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        int corner = 2, border = unchecked((int)0xFFFFFFFE);
        Native.DwmSetWindowAttribute(handle, 33, ref corner, sizeof(int)); Native.DwmSetWindowAttribute(handle, 34, ref border, sizeof(int));
        AppWindow.IsShownInSwitchers = false; SystemBackdrop = new GlassBackdrop(GlassStyle.Frosted);
        Layout.RowDefinitions.Add(new() { Height = GridLength.Auto }); Layout.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) }); Layout.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var header = new Grid { ColumnSpacing = 12 }; header.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        header.Children.Add(new TextBlock { Text = title, FontSize = 23, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        var close = Icon("\uE8BB", "收起" + title); close.Click += (_, _) => Hide(); HeaderActions.Children.Add(close);
        Grid.SetColumn(HeaderActions, 1); header.Children.Add(HeaderActions); Layout.Children.Add(header);
        var scroll = new ScrollViewer { Content = Body, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(scroll, 1); Layout.Children.Add(scroll); Grid.SetRow(StatusLine, 2); Layout.Children.Add(StatusLine); InitializePanel(Layout);
    }
    protected static TextBlock Hint(string text) => new() { Text = text, FontSize = 12, Opacity = .7, TextWrapping = TextWrapping.Wrap };
    internal static Button Icon(string glyph, string name)
    {
        var button = new Button { Content = new FontIcon { Glyph = glyph, FontSize = 15 }, Width = 32, Height = 32, MinWidth = 0, MinHeight = 0,
            Padding = new Thickness(0), BorderThickness = new Thickness(0), Background = new SolidColorBrush(Colors.Transparent) };
        AutomationProperties.SetName(button, name); ToolTipService.SetToolTip(button, name); return button;
    }
    protected void Present(DesktopMonitor display, int top, int height = 620, bool activate = true)
    {
        double scale = display.Dpi / 96d; var work = display.Info.Work; int margin = (int)Math.Round(12 * scale);
        int width = Math.Min((int)Math.Round(430 * scale), work.Right - work.Left - margin * 2);
        int y = Math.Clamp(top + (int)(8 * scale), work.Top, Math.Max(work.Top, work.Bottom - (int)(160 * scale)));
        AppWindow.MoveAndResize(new(work.Right - width - margin, y, width, Math.Max(100, Math.Min((int)(height * scale), work.Bottom - y - margin)))); ShowPanel(activate);
    }
    protected async Task Guard(Func<Task> action)
    {
        using var lease = KeepOpen();
        try { await action(); } catch (Exception ex) when (ex is not OutOfMemoryException) { StatusLine.Text = "操作未完成：" + ex.Message; }
    }
}
