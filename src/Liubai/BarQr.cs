using Microsoft.UI.Xaml;
using Windows.System;

namespace Liubai;

public sealed partial class BarWindow
{
    static bool scanning;
    QrResultWindow? qrResults;
    Microsoft.UI.Xaml.Controls.ToolTip? qrTip;
    int qrFeedbackVersion;
    async Task ScanQr()
    {
        if (scanning || openingPlanner || closing || PanelModal || manager?.HasModal == true) return;
        scanning = true; qrScan.IsEnabled = false;
        try
        {
            HidePanels(); manager?.HideOtherPanels(this);
            qrFeedbackVersion++;
            qrTip ??= new Microsoft.UI.Xaml.Controls.ToolTip { Content = "框选屏幕识别二维码", PlacementTarget = qrScan,
                Placement = Microsoft.UI.Xaml.Controls.Primitives.PlacementMode.Bottom };
            Microsoft.UI.Xaml.Controls.ToolTipService.SetToolTip(qrScan, qrTip); qrTip.IsOpen = false; qrTip.Content = "框选屏幕识别二维码";
            screenshotTip.IsOpen = false;
            VisualStateManager.GoToState(qrScan, "Normal", false);
            await Task.Delay(160);
            if (closing || manager?.Suspended == true) return;
            var selection = await QrSelectionWindow.Select();
            if (selection == null || closing) return;
            ShowQrFeedback("正在识别二维码…");
            string[] results = await Task.Run(() => QrDecoder.Decode(selection.Pixels, selection.Width, selection.Height));
            qrFeedbackVersion++; if (qrTip != null) qrTip.IsOpen = false;
            if (closing || manager?.Suspended == true || !Native.MonitorExists(Display.Handle)) return;
            bool launchFailed = false;
            if (results.Length == 1 && QrDecoder.WebUri(results[0]) is { } uri)
            {
                if (await OpenQrLink(uri)) { ShowQrFeedback("已用 Edge 打开 " + uri.IdnHost); return; }
                launchFailed = true;
            }
            qrResults ??= new QrResultWindow(() => _ = ScanQr());
            qrResults.Open(results, Display, windowShown ? AppWindow.Position.Y + AppWindow.Size.Height : Display.Info.Work.Top,
                launchFailed ? "未能启动 Edge，可以复制网址或点击按钮重试。" : null);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            qrFeedbackVersion++; if (qrTip != null) qrTip.IsOpen = false;
            if (!closing) ShowQrResult(ex is InvalidOperationException ? ex.Message : "扫码未完成，请重试");
        }
        finally { scanning = false; if (!closing) qrScan.IsEnabled = true; }
    }
    async void ShowQrFeedback(string message)
    {
        if (closing || qrTip == null) return;
        int version = ++qrFeedbackVersion;
        qrTip.Content = new Microsoft.UI.Xaml.Controls.TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 300 };
        qrTip.IsOpen = true;
        await Task.Delay(3000);
        if (version == qrFeedbackVersion && !closing) { qrTip.IsOpen = false; qrTip.Content = "框选屏幕识别二维码"; }
    }
    void ShowQrResult(string message)
    {
        if (closing || !Native.MonitorExists(Display.Handle)) return;
        qrResults ??= new QrResultWindow(() => _ = ScanQr());
        qrResults.OpenMessage(message, Display, windowShown ? AppWindow.Position.Y + AppWindow.Size.Height : Display.Info.Work.Top);
    }
    internal static async Task<bool> OpenQrLink(Uri uri)
    {
        if (QrDecoder.WebUri(uri.AbsoluteUri) == null) return false;
        try { return await Launcher.LaunchUriAsync(new Uri("microsoft-edge:" + uri.AbsoluteUri)); }
        catch { return false; }
    }
}
