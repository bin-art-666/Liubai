using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;

namespace Liubai;

internal sealed class QrResultWindow : PanelWindow
{
    readonly StackPanel results = new() { Spacing = 16 };
    readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    readonly Action retry;
    internal QrResultWindow(Action retry)
    {
        this.retry = retry; Title = "留白 · 扫码结果";
        var presenter = (OverlappedPresenter)AppWindow.Presenter;
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsResizable = presenter.IsMinimizable = presenter.IsMaximizable = false; presenter.IsAlwaysOnTop = true;
        AppWindow.IsShownInSwitchers = false;
        int border = unchecked((int)0xFFFFFFFE);
        Native.DwmSetWindowAttribute(WinRT.Interop.WindowNative.GetWindowHandle(this), 34, ref border, sizeof(int));
        var root = PanelTheme.Layout();
        root.RowDefinitions.Add(new() { Height = GridLength.Auto }); root.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var close = PanelTheme.Icon("\uE8BB", "收起扫码结果"); close.Click += (_, _) => Hide();
        root.Children.Add(PanelTheme.Header(PanelTheme.Title("扫码结果"), close));
        var scroll = new ScrollViewer { Content = results, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(scroll, 1); root.Children.Add(scroll);
        var footer = new StackPanel { Spacing = 12 }; footer.Children.Add(status);
        var again = new Button { Content = "重新框选" }; again.Click += (_, _) => { Hide(); this.retry(); }; footer.Children.Add(again);
        Grid.SetRow(footer, 2); root.Children.Add(footer); InitializePanel(root);
        SystemBackdrop = PanelTheme.Backdrop();
    }
    internal void Open(string[] values, DesktopMonitor monitor, int top, string? message = null)
    {
        results.Children.Clear(); status.Text = message ?? (values.Length == 0 ? "没有识别到二维码，请将二维码完整框入后重试。" : values.Length > 1 ? "识别到多个二维码，请选择需要的结果。" : "已识别。可以复制内容或用 Edge 打开网址。");
        foreach (string value in values)
        {
            var card = new StackPanel { Spacing = 8 };
            card.Children.Add(new TextBox { Text = value, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 180 });
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            var copy = new Button { Content = "复制内容" };
            copy.Click += (_, _) =>
            {
                try { var package = new DataPackage(); package.SetText(value); Clipboard.SetContent(package); Clipboard.Flush(); status.Text = "已复制内容"; }
                catch { status.Text = "复制失败，请重试"; }
            };
            buttons.Children.Add(copy);
            if (QrDecoder.WebUri(value) is { } uri)
            {
                var open = new Button { Content = "用 Edge 打开" };
                open.Click += async (_, _) =>
                {
                    using var lease = KeepOpen(); open.IsEnabled = false;
                    try { status.Text = await BarWindow.OpenQrLink(uri) ? "已用 Edge 打开 " + uri.IdnHost : "未能启动 Edge，可以复制网址后手动打开。"; }
                    finally { open.IsEnabled = true; }
                };
                buttons.Children.Add(open);
            }
            card.Children.Add(buttons); results.Children.Add(PanelTheme.Card(card));
        }
        Position(monitor, top); ShowPanel();
    }
    internal void OpenMessage(string message, DesktopMonitor monitor, int top)
    { Open([], monitor, top); status.Text = message; }
    void Position(DesktopMonitor monitor, int top)
    {
        var work = monitor.Info.Work; double scale = monitor.Dpi / 96d;
        int width = Math.Min((int)(420 * scale), work.Right - work.Left);
        int height = Math.Min((int)((results.Children.Count == 0 ? 220 : 460) * scale), work.Bottom - work.Top);
        AppWindow.MoveAndResize(new RectInt32(work.Right - width, Math.Clamp(top, work.Top, work.Bottom - height), width, height));
    }
    protected override void OnHiding() { results.Children.Clear(); status.Text = ""; }
}
