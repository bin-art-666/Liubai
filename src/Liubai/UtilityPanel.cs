using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Liubai;

internal abstract class UtilityPanel : PanelWindow
{
    protected readonly Grid Layout = PanelTheme.Layout();
    protected readonly StackPanel Body = new() { Spacing = 12 };
    protected readonly StackPanel HeaderActions = PanelTheme.Actions();
    protected readonly TextBlock StatusLine = Hint("");
    protected UtilityPanel(string title)
    {
        Title = "留白 · " + title;
        var presenter = (OverlappedPresenter)AppWindow.Presenter; presenter.SetBorderAndTitleBar(false, false);
        presenter.IsResizable = presenter.IsMaximizable = presenter.IsMinimizable = false; presenter.IsAlwaysOnTop = true;
        var handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        int corner = 2, border = unchecked((int)0xFFFFFFFE);
        Native.DwmSetWindowAttribute(handle, 33, ref corner, sizeof(int)); Native.DwmSetWindowAttribute(handle, 34, ref border, sizeof(int));
        AppWindow.IsShownInSwitchers = false; SystemBackdrop = PanelTheme.Backdrop();
        Layout.RowDefinitions.Add(new() { Height = GridLength.Auto }); Layout.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) }); Layout.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var close = Icon("\uE8BB", "收起" + title); close.Click += (_, _) => Hide(); HeaderActions.Children.Add(close);
        Layout.Children.Add(PanelTheme.Header(PanelTheme.Title(title), HeaderActions));
        var scroll = new ScrollViewer { Content = Body, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(scroll, 1); Layout.Children.Add(scroll); Grid.SetRow(StatusLine, 2); Layout.Children.Add(StatusLine); InitializePanel(Layout);
    }
    protected static TextBlock Hint(string text) => PanelTheme.Hint(text);
    internal static Button Icon(string glyph, string name) => PanelTheme.Icon(glyph, name);
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
